using System.Data;
using System.Data.Common;
using Transaction = System.Transactions.Transaction;
using OutWit.Database.AdoNet.Engines;
using OutWit.Database.AdoNet.Schema;
using OutWit.Database.AdoNet.Utils;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Utils;
using OutWit.Database.Engine;
using OutWit.Database.Schema;

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

    private readonly Lock m_lock = new();

    private string m_connectionString = string.Empty;
    private ConnectionState m_state = ConnectionState.Closed;
    private WitSqlEngine? m_engine;
    private WitDbEnlistment? m_enlistment;
    private WitDbDataReader? m_activeReader;
    private bool m_closePending;
    private WitDatabase? m_database;
    private WitDbTransaction? m_currentTransaction;

    /// <summary>
    /// This connection's share of a database held by <see cref="SharedDatabase"/>, for a file-backed
    /// database. Null for an in-memory one, which is private to its connection, and null when the
    /// connection was handed an engine directly.
    /// </summary>
    private SharedDatabaseLease? m_lease;

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

        var enlist = false;

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
                    var key = SharedDatabaseKey.TryResolve(options);

                    // Read-only is a property of this session, not of the storage: connections share a
                    // database, so one of them being read-only must not stop the others writing.
                    var readOnly = options.ReadOnly || options.Mode == WitDbConnectionMode.ReadOnly;

                    if (key == null)
                    {
                        // Nothing to share - an in-memory database, which is private to its connection
                        // exactly as it was before 5.0.0 and as SQLite's is without Cache=Shared.
                        m_database = BuildDatabase(options);
                        m_engine = new WitSqlEngine(m_database, new SchemaCatalog(m_database.Store),
                            ownsStore: true, readOnly);
                        OwnsEngine = true;
                    }
                    else
                    {
                        // One database and one schema catalog per file, shared by every connection in
                        // this process; the engine stays per-connection because it holds the current
                        // transaction. This is what makes several scoped DbContexts work in one host.
                        var signature = SharedDatabaseKey.BuildSignature(options);
                        m_lease = SharedDatabase.Acquire(key, signature, () => BuildDatabase(options));
                        m_database = m_lease.Database;
                        m_engine = new WitSqlEngine(m_database, m_lease.Schema, ownsStore: false, readOnly);
                        OwnsEngine = false;
                    }
                }

                m_state = ConnectionState.Open;
                enlist = new WitDbConnectionStringBuilder(m_connectionString).Enlist;
            }
            catch
            {
                m_state = ConnectionState.Closed;
                m_engine?.Dispose();
                m_engine = null;
                m_database = null;

                // The lease has to go back even when Open failed after taking it, or the shared database
                // keeps a reference nobody holds and never closes - which would leave the file locked for
                // the life of the process.
                m_lease?.Dispose();
                m_lease = null;
                throw;
            }
        }

        // Outside the lock, because enlisting runs SQL through the engine this method has just built.
        // At Open and only here: a connection opened BEFORE the scope began is not part of it - the same
        // rule SqlClient follows - and has to be enlisted by hand.
        if (enlist && Transaction.Current != null)
            EnlistTransaction(Transaction.Current);
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

        // The ordinary TransactionScope idiom disposes the connection INSIDE the scope and completes the
        // scope afterwards, so the engine has to outlive this call: the transaction's outcome is not
        // known yet and rolling back here would decide it. The real close happens in
        // OnEnlistmentFinished.
        if (m_enlistment != null)
        {
            m_closePending = true;
            return;
        }

        // The reader is closed BEFORE the engine goes, not left pointing at a disposed store. Every
        // ADO.NET provider closes its readers with the connection; this one used to hand out a reader,
        // forget about it, and dispose the storage underneath it - and the reader went on returning
        // rows, correctly, which is undefined behaviour that happens to work rather than a clean error.
        m_activeReader?.Close();
        m_activeReader = null;

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

            if (m_lease != null)
            {
                // The engine is this connection's own session and always goes; the database and its
                // schema catalog are shared, and the lease disposes them only when this was the last
                // connection using them.
                m_engine?.Dispose();
                m_engine = null;
                m_database = null;

                m_lease.Dispose();
                m_lease = null;
            }
            else if (OwnsEngine && m_engine != null)
            {
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
    /// <remarks>
    /// Enlisting in an ambient transaction and running a local one at the same time would give the
    /// connection two owners with different opinions about when to commit, so the second is refused -
    /// as it is by every provider that supports both.
    /// </remarks>
    public override void EnlistTransaction(Transaction? transaction)
    {
        if (transaction == null)
        {
            if (m_enlistment != null)
            {
                throw new InvalidOperationException(
                    "The connection is enlisted in a transaction that has not finished; it cannot be "
                    + "un-enlisted until that transaction commits or rolls back.");
            }

            return;
        }

        EnsureOpen();

        if (m_enlistment != null)
        {
            if (m_enlistment.Transaction.Equals(transaction))
                return;

            throw new InvalidOperationException(
                "The connection is already enlisted in a different transaction.");
        }

        if (m_currentTransaction != null)
        {
            throw new InvalidOperationException(
                "The connection has a local transaction in progress and cannot enlist in an ambient "
                + "one. Commit or roll the local transaction back first.");
        }

        var enlistment = new WitDbEnlistment(this, transaction);

        // Single resource manager, single machine: the transaction manager can hand the whole
        // transaction to this database and skip two-phase commit. False means somebody else already
        // owns it that way, and this engine has no durable prepare with which to join as a second
        // participant - so it says so instead of committing outside the caller's transaction.
        if (!transaction.EnlistPromotableSinglePhase(enlistment))
        {
            throw new NotSupportedException(
                "This transaction already has another resource manager that owns it, and WitDatabase "
                + "cannot join as a second durable participant - it has no two-phase prepare. Use one "
                + "database per TransactionScope.");
        }

        m_enlistment = enlistment;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Inherited until phase 6, which meant this reported the base class's 15 seconds - a number this
    /// provider had never heard of. It is the wait at <c>Open</c>, which is the only thing establishing
    /// a connection here can wait for.
    /// </remarks>
    public override int ConnectionTimeout =>
        string.IsNullOrEmpty(m_connectionString)
            ? 5
            : new WitDbConnectionStringBuilder(m_connectionString).ConnectionTimeout;

    /// <summary>
    /// Remembers the reader this connection handed out, so that closing the connection can close it.
    /// </summary>
    /// <remarks>
    /// One at a time, which is what ADO.NET assumes without multiple active result sets: a second
    /// reader replaces the first here, and the first is already finished as far as this connection is
    /// concerned.
    /// </remarks>
    internal void RegisterReader(WitDbDataReader reader)
    {
        m_activeReader = reader;
    }

    /// <summary>
    /// Called by a reader that has closed itself.
    /// </summary>
    internal void UnregisterReader(WitDbDataReader reader)
    {
        if (ReferenceEquals(m_activeReader, reader))
            m_activeReader = null;
    }

    /// <summary>
    /// Called by the enlistment once the ambient transaction has committed or rolled back.
    /// </summary>
    internal void OnEnlistmentFinished()
    {
        m_enlistment = null;

        if (!m_closePending)
            return;

        m_closePending = false;
        Close();
    }

    /// <inheritdoc/>
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        EnsureOpen();

        if (m_enlistment != null)
        {
            throw new InvalidOperationException(
                "The connection is enlisted in an ambient transaction. Complete the TransactionScope "
                + "instead of beginning a local transaction.");
        }

        if (m_currentTransaction != null)
            throw new InvalidOperationException("A transaction is already in progress.");

        var witIsolation = isolationLevel.ToIsolationLevel();
        
        // Use SQL command to start transaction - this properly coordinates with statement execution
        m_engine!.Execute("BEGIN TRANSACTION");

        if (witIsolation != WitIsolationLevel.ReadCommitted)
            m_engine.Execute($"SET TRANSACTION ISOLATION LEVEL {witIsolation.IsolationName()}");

        m_currentTransaction = new WitDbTransaction(this, isolationLevel);
        return m_currentTransaction;
    }

    /// <summary>
    /// Begins a database transaction asynchronously.
    /// </summary>
    public new async ValueTask<DbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => BeginDbTransaction(isolationLevel), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a database transaction asynchronously with default isolation level.
    /// </summary>
    public new async ValueTask<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await BeginTransactionAsync(IsolationLevel.Unspecified, cancellationToken).ConfigureAwait(false);
    }

    internal void ClearTransaction()
    {
        m_currentTransaction = null;
    }

    #endregion

    #region Command

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// The command's timeout starts at the connection string's <c>Default Timeout</c>, which is what
    /// ADO.NET means by that keyword. It was declared and read by nothing until phase 6 - the same family
    /// as <c>Read Only</c> and <c>Mode</c>: a keyword accepted and dropped.
    /// </remarks>
    protected override DbCommand CreateDbCommand()
    {
        var command = new WitDbCommand { Connection = this };

        if (!string.IsNullOrEmpty(m_connectionString))
            command.CommandTimeout = new WitDbConnectionStringBuilder(m_connectionString).DefaultTimeout;

        return command;
    }

    /// <summary>
    /// Creates a new command associated with this connection.
    /// </summary>
    public new WitDbCommand CreateCommand()
    {
        // Through the contract method, so the concrete surface and the base one cannot drift - which is
        // the whole subject of this phase.
        return (WitDbCommand)CreateDbCommand();
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

    #region Schema

    /// <inheritdoc/>
    public override DataTable GetSchema()
    {
        return GetSchema(null);
    }

    /// <inheritdoc/>
    public override DataTable GetSchema(string? collectionName)
    {
        return GetSchema(collectionName, null);
    }

    /// <inheritdoc/>
    public override DataTable GetSchema(string? collectionName, string?[]? restrictionValues)
    {
        EnsureOpen();
        var provider = new SchemaProvider(m_engine!);
        return provider.GetSchema(collectionName, restrictionValues);
    }

    #endregion

    #region Database Building

    private static WitDatabase BuildDatabase(WitDbConnectionStringBuilder options)
    {
        options.ThrowIfInvalid();
        
        var builder = new WitDatabaseBuilder();
        
        // Collect all provider parameters from connection string
        var providerParams = new ProviderParameters();
        foreach (var (key, value) in options.GetProviderParameters())
        {
            if (value != null)
                providerParams.Set(key, value);
        }

        // Configure storage
        builder.WithOpenTimeout(TimeSpan.FromSeconds(Math.Max(0, options.ConnectionTimeout)));

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

        // Configure file locking
        ConfigureFileLocking(builder, providerParams);

        // Configure parallel mode
        ConfigureParallelMode(builder, options);

        return builder.Build();
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
            RequireExistingDatabase(options);
            builder.WithFilePath(options.DataSource);
        }
        else
        {
            throw new ArgumentException("DataSource must be specified in connection string.");
        }
    }

    /// <summary>
    /// Enforces the half of <c>Mode</c> that says "open what is there", for the two values that mean it.
    /// </summary>
    /// <remarks>
    /// <c>ReadWriteCreate</c> - the default - creates a database that is not there, and
    /// <c>ReadWrite</c> and <c>ReadOnly</c> do not: they mean open an existing one and fail if it is
    /// absent. All three used to behave identically, because the only question asked of <c>Mode</c> was
    /// whether it was <c>Memory</c>. A mistyped path therefore produced an empty database rather than an
    /// error, which is the failure SQLite reports as "unable to open database file".
    ///
    /// A database is a file for the B+Tree store and a directory for the LSM one, so both count as
    /// present.
    /// </remarks>
    private static void RequireExistingDatabase(WitDbConnectionStringBuilder options)
    {
        if (options.Mode != WitDbConnectionMode.ReadWrite && options.Mode != WitDbConnectionMode.ReadOnly)
            return;

        var path = options.DataSource!;

        if (File.Exists(path) || Directory.Exists(path))
            return;

        throw new WitDbException(
            $"Unable to open database file '{path}': it does not exist, and Mode={options.Mode} means "
            + "open an existing database rather than create one. Use Mode=ReadWriteCreate to create it.",
            WitDbException.ERROR_IO);
    }

    private static void ConfigureStore(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options, ProviderParameters providerParams)
    {
        var storeKey = options.Store?.ToLowerInvariant();

        // Settings reach the store whether or not the store was named. Until 11.3.0 this method returned
        // here when Store was absent, taking the ENTIRE parameter bag with it: `Data Source=x;PageSize=16384`
        // built a database with the default page size, and `Data Source=x;Store=btree;PageSize=16384` -
        // which asks for the same engine, btree being the default - honoured it. What decided whether a
        // setting arrived was a different setting. Measured both ways in ConfigurationCensusTests.
        if (string.IsNullOrEmpty(storeKey))
        {
            builder.WithStoreParameters(providerParams);
            return;
        }

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
        // No encryption if no password
        if (string.IsNullOrEmpty(options.Password))
            return;

        // Check for fast encryption flag
        bool fastEncryption = providerParams.Get("FastEncryption", false) || 
                              providerParams.Get("Fast Encryption", false);

        var encryptionKey = options.Encryption?.ToLowerInvariant();

        // If encryption provider specified, use provider key
        if (!string.IsNullOrEmpty(encryptionKey))
        {
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
        else
        {
            // Default to AES-GCM
            if (fastEncryption)
            {
                if (!string.IsNullOrEmpty(options.User))
                    builder.WithEncryptionFast(options.User, options.Password);
                else
                    builder.WithEncryptionFast(options.Password);
            }
            else
            {
                if (!string.IsNullOrEmpty(options.User))
                    builder.WithEncryption(options.User, options.Password);
                else
                    builder.WithEncryption(options.Password);
            }
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
            // Same rule as the one deleting a database uses, so the journal cannot be missed there.
            var journalPath = DatabaseFiles.GetJournalPath(options.DataSource)!;

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

        if (options.Mvcc)
        {
            var coreIsolationLevel = options.IsolationLevel.ToWitIsolationLevel();
            builder.WithMvcc(coreIsolationLevel);

            // MVCC is the default here, so this is the path most consumers take; without the
            // keyword they would have no way to trade durability for throughput.
            if (!options.SynchronousCommit)
                builder.WithAsynchronousCommit();
        }
        else if (options.IsolationLevel != WitDbIsolationLevel.ReadCommitted)
        {
            var coreIsolationLevel = options.IsolationLevel.ToWitIsolationLevel();
            builder.WithDefaultIsolationLevel(coreIsolationLevel);
            builder.WithTransactions();
        }
        else
        {
            builder.WithTransactions();
        }
    }

    private static void ConfigureFileLocking(WitDatabaseBuilder builder, ProviderParameters providerParams)
    {
        // Check for explicit FileLocking=false
        var fileLocking = providerParams.Get<object?>("FileLocking");
        if (fileLocking is false || 
            fileLocking is string s && s.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            builder.WithoutFileLocking();
        }
        // else use default (with file locking)
    }

    private static void ConfigureParallelMode(WitDatabaseBuilder builder, WitDbConnectionStringBuilder options)
    {
        if (options.ParallelMode == WitDbParallelMode.None)
            return;

        var coreMode = options.ParallelMode switch
        {
            WitDbParallelMode.Auto => ParallelMode.Auto,
            WitDbParallelMode.Buffered => ParallelMode.Buffered,
            WitDbParallelMode.Latched => ParallelMode.Latched,
            WitDbParallelMode.Optimistic => ParallelMode.Optimistic,
            _ => ParallelMode.None
        };

        builder.WithParallelWrites(coreMode);

        if (options.MaxWriters != Environment.ProcessorCount)
        {
            builder.WithMaxWriters(options.MaxWriters);
        }
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

            m_connectionString = value;
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
            var dataSource = options.DataSource.Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFileNameWithoutExtension(dataSource) ?? DEFAULT_DATABASE_NAME;
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
    /// Available only when the connection is open.
    /// </summary>
    /// <remarks>
    /// This property provides direct access to the WitSqlEngine for advanced operations
    /// such as bulk operations and prepared statements.
    /// </remarks>
    public WitSqlEngine? Engine => m_engine;

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
