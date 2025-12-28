using System.Data;
using System.Data.Common;
using OutWit.Database.AdoNet.Schema;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Utils;
using OutWit.Database.Engine;
using WitDatabaseInstance = OutWit.Database.Core.Builder.WitDatabase;
using CoreIsolationLevel = OutWit.Database.Core.Interfaces.IsolationLevel;
using DataIsolationLevel = System.Data.IsolationLevel;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Represents a connection to a WitDatabase database.
/// </summary>
public sealed class WitDbConnection : DbConnection
{
    #region Constants

    private const string DEFAULT_DATABASE_NAME = "main";

    #endregion

    #region Fields

    private string m_connectionString = string.Empty;
    private ConnectionState m_state = ConnectionState.Closed;
    private WitSqlEngine? m_engine;
    private WitDatabaseInstance? m_database;
    private WitDbTransaction? m_currentTransaction;
    private readonly object m_lock = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbConnection"/> class.
    /// </summary>
    public WitDbConnection()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbConnection"/> class with the specified connection string.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    public WitDbConnection(string connectionString)
    {
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbConnection"/> class with an existing engine.
    /// </summary>
    /// <param name="engine">An existing WitSqlEngine instance.</param>
    /// <param name="ownsEngine">If true, the connection will dispose the engine when closed.</param>
    internal WitDbConnection(WitSqlEngine engine, bool ownsEngine = false)
    {
        m_engine = engine;
        OwnsEngine = ownsEngine;
        m_state = ConnectionState.Open;
    }

    #endregion

    #region Open/Close

    /// <inheritdoc/>
    public override void Open()
    {
        if (m_state == ConnectionState.Open)
            return;

        if (string.IsNullOrEmpty(m_connectionString) && m_engine == null)
            throw new InvalidOperationException("Connection string is not set.");

        lock (m_lock)
        {
            if (m_state == ConnectionState.Open)
                return;

            m_state = ConnectionState.Connecting;

            try
            {
                if (m_engine == null)
                {
                    var options = new WitDbConnectionStringBuilder(m_connectionString);
                    m_database = CreateDatabase(options);
                    m_engine = new WitSqlEngine(m_database, ownsStore: true);
                    OwnsEngine = true;
                }

                m_state = ConnectionState.Open;
            }
            catch
            {
                m_state = ConnectionState.Closed;
                m_engine?.Dispose();
                m_engine = null;
                m_database = null;
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        await Task.Run(Open, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Close()
    {
        if (m_state == ConnectionState.Closed)
            return;

        lock (m_lock)
        {
            if (m_state == ConnectionState.Closed)
                return;

            // Rollback any active transaction
            if (m_currentTransaction != null)
            {
                m_currentTransaction.Rollback();
                m_currentTransaction = null;
            }

            if (OwnsEngine && m_engine != null)
            {
                // Flush data to disk before disposing
                m_database?.Flush();
                
                m_engine.Dispose();
                m_engine = null;
                m_database = null;
            }

            m_state = ConnectionState.Closed;
        }
    }

    /// <inheritdoc/>
    public override async Task CloseAsync()
    {
        await Task.Run(Close).ConfigureAwait(false);
    }

    #endregion

    #region Transaction

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(DataIsolationLevel isolationLevel)
    {
        EnsureOpen();

        if (m_currentTransaction != null)
            throw new InvalidOperationException("A transaction is already in progress.");

        var witIsolation = MapIsolationLevel(isolationLevel);
        m_engine!.Execute("BEGIN TRANSACTION");

        if (witIsolation != CoreIsolationLevel.ReadCommitted)
        {
            var isolationName = witIsolation switch
            {
                CoreIsolationLevel.ReadUncommitted => "READ UNCOMMITTED",
                CoreIsolationLevel.ReadCommitted => "READ COMMITTED",
                CoreIsolationLevel.RepeatableRead => "REPEATABLE READ",
                CoreIsolationLevel.Serializable => "SERIALIZABLE",
                CoreIsolationLevel.Snapshot => "SNAPSHOT",
                _ => "READ COMMITTED"
            };
            m_engine.Execute($"SET TRANSACTION ISOLATION LEVEL {isolationName}");
        }

        m_currentTransaction = new WitDbTransaction(this, isolationLevel);
        return m_currentTransaction;
    }

    /// <summary>
    /// Begins a database transaction asynchronously.
    /// </summary>
    public new async ValueTask<DbTransaction> BeginTransactionAsync(DataIsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => BeginDbTransaction(isolationLevel), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a database transaction asynchronously with default isolation level.
    /// </summary>
    public new async ValueTask<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await BeginTransactionAsync(DataIsolationLevel.Unspecified, cancellationToken).ConfigureAwait(false);
    }

    internal void ClearTransaction()
    {
        m_currentTransaction = null;
    }

    #endregion

    #region Command

    /// <inheritdoc/>
    protected override DbCommand CreateDbCommand()
    {
        return new WitDbCommand { Connection = this };
    }

    /// <summary>
    /// Creates a new command associated with this connection.
    /// </summary>
    public new WitDbCommand CreateCommand()
    {
        return new WitDbCommand { Connection = this };
    }

    #endregion

    #region ChangeDatabase

    /// <inheritdoc/>
    public override void ChangeDatabase(string databaseName)
    {
        if (!string.Equals(databaseName, Database, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(databaseName, DEFAULT_DATABASE_NAME, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("WitDatabase does not support changing databases.");
        }
    }

    #endregion

    #region Database Creation

    private static WitDatabaseInstance CreateDatabase(WitDbConnectionStringBuilder options)
    {
        options.ThrowIfInvalid();
        
        // Check if this is a memory database
        if (options.Mode == WitDbConnectionMode.Memory ||
            string.Equals(options.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return CreateNewDatabase(options);
        }
        
        // For file-based databases, check if file exists to open vs create
        if (!string.IsNullOrEmpty(options.DataSource))
        {
            var exists = File.Exists(options.DataSource) || Directory.Exists(options.DataSource);
            
            if (exists)
            {
                // Open existing database
                return OpenExistingDatabase(options);
            }
        }
        
        // Create new database
        return CreateNewDatabase(options);
    }

    private static WitDatabaseInstance CreateNewDatabase(WitDbConnectionStringBuilder options)
    {
        var builder = new WitDatabaseBuilder();
        
        // Collect all provider parameters from connection string
        var providerParams = new ProviderParameters();
        foreach (var (key, value) in options.GetProviderParameters())
        {
            if (value != null)
                providerParams.Set(key, value);
        }

        // Configure storage
        ConfigureStorage(builder, options);

        // Configure store engine
        ConfigureStore(builder, options, providerParams);

        // Configure encryption
        ConfigureEncryption(builder, options, providerParams);

        // Configure cache
        ConfigureCache(builder, options, providerParams);

        // Configure journal
        ConfigureJournal(builder, options, providerParams);

        // Configure transactions
        ConfigureTransactions(builder, options);

        return builder.Build();
    }

    private static WitDatabaseInstance OpenExistingDatabase(WitDbConnectionStringBuilder options)
    {
        var path = options.DataSource!;
        
        // If encryption is specified, use CreateOrOpen with password
        // This handles both new and existing databases correctly
        if (!string.IsNullOrEmpty(options.Encryption) && !string.IsNullOrEmpty(options.Password))
        {
            // For encrypted databases, we need to use the builder approach
            // because WitDatabase.Open with password doesn't support all options
            var builder = new WitDatabaseBuilder();
            var providerParams = new ProviderParameters();
            
            foreach (var (key, value) in options.GetProviderParameters())
            {
                if (value != null)
                    providerParams.Set(key, value);
            }

            // Use StorageDetector to get database info
            var detection = StorageDetector.Detect(path);
            
            // Configure based on detected store type
            if (detection.StoreType == "lsm" || detection.IsDirectory)
            {
                builder.WithLsmTree(path);
            }
            else
            {
                builder.WithFilePath(path).WithBTree();
            }

            // Configure encryption
            ConfigureEncryption(builder, options, providerParams);

            // Configure transactions based on detection or connection string options
            if (detection.HasTransactions || options.Transactions)
            {
                // Use MVCC if detected or specified in connection string
                if (detection.HasMvcc || options.Mvcc)
                {
                    var coreIsolationLevel = MapDbIsolationLevel(options.IsolationLevel);
                    builder.WithMvcc(coreIsolationLevel);
                }
                else
                {
                    builder.WithTransactions();
                }
            }
            else
            {
                builder.WithoutTransactions();
            }

            // Configure file locking from detection
            if (detection.HasFileLocking)
                builder.WithFileLocking();
            else
                builder.WithoutFileLocking();

            return builder.Build();
        }
        
        // For non-encrypted databases, use WitDatabase.Open which auto-detects settings
        return WitDatabaseInstance.Open(path);
    }

    private static void ConfigureStorage(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options)
    {
        if (options.Mode == WitDbConnectionMode.Memory ||
            string.Equals(options.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            builder.WithMemoryStorage();
        }
        else if (!string.IsNullOrEmpty(options.DataSource))
        {
            builder.WithFilePath(options.DataSource);
        }
        else
        {
            throw new ArgumentException("DataSource must be specified in connection string.");
        }
    }

    private static void ConfigureStore(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options, ProviderParameters providerParams)
    {
        var storeKey = options.Store?.ToLowerInvariant();
        
        // No store specified - use default (btree)
        if (string.IsNullOrEmpty(storeKey))
            return;

        // Pass data source path to provider
        if (!string.IsNullOrEmpty(options.DataSource) && 
            !string.Equals(options.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            providerParams.Set("path", options.DataSource);
            providerParams.Set("filePath", options.DataSource);
            providerParams.Set("directory", options.DataSource);
        }

        builder.WithStoreKey(storeKey, providerParams);
    }

    private static void ConfigureEncryption(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options, ProviderParameters providerParams)
    {
        var encryptionKey = options.Encryption?.ToLowerInvariant();
        
        // No encryption if not specified or no password
        if (string.IsNullOrEmpty(encryptionKey) || string.IsNullOrEmpty(options.Password))
            return;

        // Check for fast encryption flag
        bool fastEncryption = providerParams.Get<bool>("FastEncryption", false) || 
                              providerParams.Get<bool>("Fast Encryption", false);
        int iterations = fastEncryption ? CryptoUtils.WASM_PBKDF2_ITERATIONS : CryptoUtils.DEFAULT_PBKDF2_ITERATIONS;

        if (!string.IsNullOrEmpty(options.User))
        {
            builder.WithEncryptionKey(encryptionKey, options.User, options.Password, iterations);
        }
        else
        {
            builder.WithEncryptionKey(encryptionKey, options.Password, iterations);
        }
    }

    private static void ConfigureCache(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options, ProviderParameters providerParams)
    {
        var cacheKey = options.Cache?.ToLowerInvariant();
        
        // No cache specified - use default
        if (string.IsNullOrEmpty(cacheKey))
            return;

        builder.WithCacheKey(cacheKey, providerParams);
    }

    private static void ConfigureJournal(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options, ProviderParameters providerParams)
    {
        var journalKey = options.Journal?.ToLowerInvariant();
        
        // No journal specified - use default
        if (string.IsNullOrEmpty(journalKey))
            return;

        // Add derived paths if not specified
        if (!string.IsNullOrEmpty(options.DataSource) && 
            !string.Equals(options.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            var journalPath = Path.Combine(
                Path.GetDirectoryName(options.DataSource) ?? ".",
                Path.GetFileNameWithoutExtension(options.DataSource) + ".journal");
            
            if (!providerParams.Has("filePath"))
                providerParams.Set("filePath", journalPath);
            if (!providerParams.Has("walPath"))
                providerParams.Set("walPath", journalPath);
        }

        builder.WithJournalKey(journalKey, providerParams);
    }

    private static void ConfigureTransactions(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options)
    {
        if (!options.Transactions)
        {
            builder.WithoutTransactions();
            return;
        }

        // MVCC provides snapshot isolation and is generally recommended.
        // However, MVCC stores data with version suffixes which changes the key format.
        // For file-based databases, we still use MVCC since the database will be
        // reopened with the same MVCC setting (stored in metadata).
        if (options.Mvcc)
        {
            var coreIsolationLevel = MapDbIsolationLevel(options.IsolationLevel);
            builder.WithMvcc(coreIsolationLevel);
        }
        else if (options.IsolationLevel != WitDbIsolationLevel.ReadCommitted)
        {
            var coreIsolationLevel = MapDbIsolationLevel(options.IsolationLevel);
            builder.WithDefaultIsolationLevel(coreIsolationLevel);
            builder.WithTransactions();
        }
        else
        {
            // Default: use regular transactions when Mvcc=false
            builder.WithTransactions();
        }
    }

    private static CoreIsolationLevel MapDbIsolationLevel(WitDbIsolationLevel level)
    {
        return level switch
        {
            WitDbIsolationLevel.ReadUncommitted => CoreIsolationLevel.ReadUncommitted,
            WitDbIsolationLevel.ReadCommitted => CoreIsolationLevel.ReadCommitted,
            WitDbIsolationLevel.RepeatableRead => CoreIsolationLevel.RepeatableRead,
            WitDbIsolationLevel.Serializable => CoreIsolationLevel.Serializable,
            WitDbIsolationLevel.Snapshot => CoreIsolationLevel.Snapshot,
            _ => CoreIsolationLevel.ReadCommitted
        };
    }

    private static CoreIsolationLevel MapIsolationLevel(DataIsolationLevel level)
    {
        return level switch
        {
            DataIsolationLevel.Unspecified => CoreIsolationLevel.ReadCommitted,
            DataIsolationLevel.Chaos => CoreIsolationLevel.ReadUncommitted,
            DataIsolationLevel.ReadUncommitted => CoreIsolationLevel.ReadUncommitted,
            DataIsolationLevel.ReadCommitted => CoreIsolationLevel.ReadCommitted,
            DataIsolationLevel.RepeatableRead => CoreIsolationLevel.RepeatableRead,
            DataIsolationLevel.Serializable => CoreIsolationLevel.Serializable,
            DataIsolationLevel.Snapshot => CoreIsolationLevel.Snapshot,
            _ => CoreIsolationLevel.ReadCommitted
        };
    }

    #endregion

    #region Schema

    /// <inheritdoc/>
    public override DataTable GetSchema()
    {
        return GetSchema(null);
    }

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName)
    {
        return GetSchema(collectionName, null);
    }

    /// <inheritdoc/>
    public override DataTable GetSchema(string collectionName, string?[]? restrictionValues)
    {
        EnsureOpen();
        var provider = new SchemaProvider(m_engine!);
        return provider.GetSchema(collectionName, restrictionValues);
    }

    #endregion

    #region Helpers

    private void EnsureOpen()
    {
        if (m_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override string ConnectionString
    {
        get => m_connectionString;
        set
        {
            if (m_state != ConnectionState.Closed)
                throw new InvalidOperationException("Cannot change connection string while connection is open.");
            m_connectionString = value ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public override string Database
    {
        get
        {
            if (string.IsNullOrEmpty(m_connectionString))
                return DEFAULT_DATABASE_NAME;

            var options = new WitDbConnectionStringBuilder(m_connectionString);
            return Path.GetFileNameWithoutExtension(options.DataSource) ?? DEFAULT_DATABASE_NAME;
        }
    }

    /// <inheritdoc/>
    public override string DataSource
    {
        get
        {
            if (string.IsNullOrEmpty(m_connectionString))
                return string.Empty;

            var options = new WitDbConnectionStringBuilder(m_connectionString);
            return options.DataSource ?? string.Empty;
        }
    }

    /// <inheritdoc/>
    public override string ServerVersion => "1.0.0";

    /// <inheritdoc/>
    public override ConnectionState State => m_state;

    /// <summary>
    /// Gets the underlying SQL engine.
    /// </summary>
    internal WitSqlEngine? Engine => m_engine;

    /// <summary>
    /// Gets whether this connection owns the engine.
    /// </summary>
    internal bool OwnsEngine { get; private set; }

    /// <summary>
    /// Gets the current transaction, if any.
    /// </summary>
    internal WitDbTransaction? CurrentTransaction => m_currentTransaction;

    #endregion
}
