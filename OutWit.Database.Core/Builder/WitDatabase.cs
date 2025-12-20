using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Builder;

/// <summary>
/// High-level database wrapper that provides a unified API 
/// regardless of the underlying storage engine.
/// </summary>
public sealed class WitDatabase : IDisposable
{
    #region Fields

    private readonly IKeyValueStore m_store;
    private readonly ITransactionalStore? m_transactionalStore;
    private readonly bool m_disposeStore;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new WitDatabase wrapping a key-value store.
    /// </summary>
    internal WitDatabase(IKeyValueStore store, bool disposeStore = true)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_transactionalStore = store as ITransactionalStore;
        m_disposeStore = disposeStore;
    }

    /// <summary>
    /// Creates a new WitDatabase wrapping a transactional store.
    /// </summary>
    internal WitDatabase(ITransactionalStore store, bool disposeStore = true)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_transactionalStore = store;
        m_disposeStore = disposeStore;
    }

    #endregion

    #region Static Factory Methods

    /// <summary>
    /// Creates a new database file with default settings (BTree, transactions enabled).
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <returns>A new WitDatabase instance.</returns>
    /// <exception cref="IOException">Thrown if the file already exists.</exception>
    public static WitDatabase Create(string path)
    {
        if (File.Exists(path))
            throw new IOException($"Database file already exists: {path}");

        return new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithTransactions()
            .Build();
    }

    /// <summary>
    /// Creates a new encrypted database file with default settings.
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <param name="password">Encryption password.</param>
    /// <returns>A new WitDatabase instance.</returns>
    /// <exception cref="IOException">Thrown if the file already exists.</exception>
    public static WitDatabase Create(string path, string password)
    {
        if (File.Exists(path))
            throw new IOException($"Database file already exists: {path}");

        return new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption(password)
            .WithTransactions()
            .Build();
    }

    /// <summary>
    /// Opens an existing database file with auto-detection of settings.
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <returns>A WitDatabase instance.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown if the database is encrypted (use overload with password).</exception>
    /// <remarks>
    /// This method reads the database header to auto-detect settings like store type and features.
    /// For encrypted databases, use the overload with password.
    /// </remarks>
    public static WitDatabase Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Database file not found", path);

        // Try to read metadata from header
        var hints = TryGetOpeningHints(path);
        
        if (hints?.RequiresEncryption == true)
        {
            throw new InvalidDataException(
                $"Database is encrypted with '{hints.EncryptionProvider}' provider. " +
                "Use WitDatabase.Open(path, password) or configure encryption in WitDatabaseBuilder.");
        }

        var builder = new WitDatabaseBuilder()
            .WithFilePath(path);

        // Configure based on hints
        if (hints != null)
        {
            ConfigureBuilderFromHints(builder, hints);
        }
        else
        {
            // Fallback to defaults
            builder.WithBTree().WithTransactions();
        }

        return builder.Build();
    }

    /// <summary>
    /// Opens an existing encrypted database file.
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <param name="password">Encryption password.</param>
    /// <returns>A WitDatabase instance.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <remarks>
    /// For encrypted databases, we cannot read settings from the header before decryption.
    /// The database is opened with default settings (BTree, Transactions enabled).
    /// After opening, the actual stored settings will be validated.
    /// </remarks>
    public static WitDatabase Open(string path, string password)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Database file not found", path);

        // For encrypted DBs, we can't read hints without the password.
        // Open with defaults - the stored metadata will be validated after decryption.
        return new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption(password)
            .WithTransactions()
            .Build();
    }

    /// <summary>
    /// Creates a new database or opens an existing one.
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <returns>A WitDatabase instance.</returns>
    public static WitDatabase CreateOrOpen(string path)
    {
        if (File.Exists(path))
        {
            return Open(path);
        }
        
        return Create(path);
    }

    /// <summary>
    /// Creates a new encrypted database or opens an existing one.
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <param name="password">Encryption password.</param>
    /// <returns>A WitDatabase instance.</returns>
    public static WitDatabase CreateOrOpen(string path, string password)
    {
        if (File.Exists(path))
        {
            return Open(path, password);
        }
        
        return Create(path, password);
    }

    /// <summary>
    /// Creates an in-memory database (data is lost on dispose).
    /// </summary>
    /// <returns>A WitDatabase instance.</returns>
    public static WitDatabase CreateInMemory()
    {
        return new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();
    }

    /// <summary>
    /// Creates an encrypted in-memory database.
    /// </summary>
    /// <param name="password">Encryption password.</param>
    /// <returns>A WitDatabase instance.</returns>
    public static WitDatabase CreateInMemory(string password)
    {
        return new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithEncryption(password)
            .WithTransactions()
            .Build();
    }

    /// <summary>
    /// Gets information about a database file without opening it.
    /// </summary>
    /// <param name="path">Path to the database file.</param>
    /// <returns>Opening hints, or null if the file cannot be read.</returns>
    public static OpeningHints? GetDatabaseInfo(string path)
    {
        return TryGetOpeningHints(path);
    }

    #endregion

    #region Private Helpers

    private static OpeningHints? TryGetOpeningHints(string path)
    {
        try
        {
            // Read just the header page to get metadata
            var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
            
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
                return null;

            stream.ReadExactly(buffer);

            // Check magic bytes
            if (!buffer.AsSpan(0, 16).SequenceEqual(DatabaseConstants.MAGIC_BYTES))
            {
                // File might be encrypted or corrupted
                return new OpeningHints
                {
                    RequiresEncryption = true,
                    EncryptionProvider = "unknown"
                };
            }

            // Read header
            var header = DatabaseHeader.ReadFrom(buffer);
            return ConfigurationValidator.GetOpeningHints(header.Providers);
        }
        catch
        {
            return null;
        }
    }

    private static void ConfigureBuilderFromHints(WitDatabaseBuilder builder, OpeningHints hints)
    {
        // Configure store type
        if (string.Equals(hints.StoreProvider, "lsm", StringComparison.OrdinalIgnoreCase))
        {
            // LSM requires directory, but Open() is for file path
            // Fall back to BTree for file-based opening
            builder.WithBTree();
        }
        else
        {
            builder.WithBTree();
        }

        // Configure transactions
        if (hints.HasTransactions)
        {
            builder.WithTransactions();
        }
        else
        {
            builder.WithoutTransactions();
        }

        // Configure file locking
        if (hints.HasFileLocking)
        {
            builder.WithFileLocking();
        }
        else
        {
            builder.WithoutFileLocking();
        }
    }

    #endregion

    #region Get

    /// <summary>
    /// Gets the value for the specified key.
    /// </summary>
    public byte[]? Get(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return m_store.Get(key);
    }

    /// <summary>
    /// Gets the value for the specified key.
    /// </summary>
    public byte[]? Get(byte[] key) => Get(key.AsSpan());

    /// <summary>
    /// Gets the value for the specified string key (UTF-8 encoded).
    /// </summary>
    public byte[]? Get(string key) => Get(System.Text.Encoding.UTF8.GetBytes(key));

    /// <summary>
    /// Gets the value for the specified key asynchronously.
    /// </summary>
    public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return m_store.GetAsync(key, cancellationToken);
    }

    #endregion

    #region Put

    /// <summary>
    /// Inserts or updates a key-value pair.
    /// </summary>
    public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        ThrowIfDisposed();
        m_store.Put(key, value);
    }

    /// <summary>
    /// Inserts or updates a key-value pair.
    /// </summary>
    public void Put(byte[] key, byte[] value) => Put(key.AsSpan(), value.AsSpan());

    /// <summary>
    /// Inserts or updates a key-value pair with string key (UTF-8 encoded).
    /// </summary>
    public void Put(string key, byte[] value) => Put(System.Text.Encoding.UTF8.GetBytes(key), value);

    /// <summary>
    /// Inserts or updates a key-value pair asynchronously.
    /// </summary>
    public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return m_store.PutAsync(key, value, cancellationToken);
    }

    #endregion

    #region Delete

    /// <summary>
    /// Deletes a key from the store.
    /// </summary>
    public bool Delete(ReadOnlySpan<byte> key)
    {
        ThrowIfDisposed();
        return m_store.Delete(key);
    }

    /// <summary>
    /// Deletes a key from the store.
    /// </summary>
    public bool Delete(byte[] key) => Delete(key.AsSpan());

    /// <summary>
    /// Deletes a key from the store (UTF-8 encoded).
    /// </summary>
    public bool Delete(string key) => Delete(System.Text.Encoding.UTF8.GetBytes(key));

    /// <summary>
    /// Deletes a key asynchronously.
    /// </summary>
    public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return m_store.DeleteAsync(key, cancellationToken);
    }

    #endregion

    #region Scan

    /// <summary>
    /// Scans key-value pairs in the specified range.
    /// </summary>
    public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey = null, byte[]? endKey = null)
    {
        ThrowIfDisposed();
        return m_store.Scan(startKey, endKey);
    }

    /// <summary>
    /// Scans key-value pairs asynchronously.
    /// </summary>
    public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? startKey = null, 
        byte[]? endKey = null, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return m_store.ScanAsync(startKey, endKey, cancellationToken);
    }

    #endregion

    #region Transactions

    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if transactions are not enabled.</exception>
    public ITransaction BeginTransaction()
    {
        ThrowIfDisposed();
        if (m_transactionalStore == null)
            throw new InvalidOperationException("Transactions are not enabled. Use WithTransactions() when building the database.");
        
        return m_transactionalStore.BeginTransaction();
    }

    /// <summary>
    /// Begins a new transaction asynchronously.
    /// </summary>
    public ValueTask<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (m_transactionalStore == null)
            throw new InvalidOperationException("Transactions are not enabled. Use WithTransactions() when building the database.");
        
        return m_transactionalStore.BeginTransactionAsync(cancellationToken);
    }

    #endregion

    #region Flush

    /// <summary>
    /// Flushes any pending writes to durable storage.
    /// </summary>
    public void Flush()
    {
        ThrowIfDisposed();
        m_store.Flush();
    }

    /// <summary>
    /// Flushes any pending writes asynchronously.
    /// </summary>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return m_store.FlushAsync(cancellationToken);
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;

        if (m_disposeStore)
        {
            m_store.Dispose();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether transaction support is enabled.
    /// </summary>
    public bool SupportsTransactions => m_transactionalStore != null;

    /// <summary>
    /// Gets the number of active transactions (if transactions are supported).
    /// </summary>
    public int ActiveTransactionCount => m_transactionalStore?.ActiveTransactionCount ?? 0;

    /// <summary>
    /// Gets the underlying key-value store.
    /// </summary>
    public IKeyValueStore Store => m_store;

    #endregion
}
