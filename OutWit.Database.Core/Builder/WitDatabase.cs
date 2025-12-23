using OutWit.Database.Core.Indexes;
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
    private readonly IIndexManager? m_indexManager;
    private readonly IndexMetadataStore? m_indexMetadataStore;
    private readonly bool m_disposeStore;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new WitDatabase wrapping a key-value store.
    /// </summary>
    internal WitDatabase(IKeyValueStore store, IIndexManager? indexManager = null, bool disposeStore = true)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_transactionalStore = store as ITransactionalStore;
        m_indexManager = indexManager;
        m_disposeStore = disposeStore;
        
        // Create metadata store using the underlying store
        var underlyingStore = GetUnderlyingStore(store);
        m_indexMetadataStore = new IndexMetadataStore(underlyingStore);
        
        // Restore indexes from metadata
        RestoreIndexesFromMetadata();
    }

    /// <summary>
    /// Creates a new WitDatabase wrapping a transactional store.
    /// </summary>
    internal WitDatabase(ITransactionalStore store, IIndexManager? indexManager = null, bool disposeStore = true)
    {
        m_store = store ?? throw new ArgumentNullException(nameof(store));
        m_transactionalStore = store;
        m_indexManager = indexManager;
        m_disposeStore = disposeStore;
        
        // Create metadata store using the underlying store
        var underlyingStore = GetUnderlyingStore(store);
        m_indexMetadataStore = new IndexMetadataStore(underlyingStore);
        
        // Restore indexes from metadata
        RestoreIndexesFromMetadata();
    }

    #endregion

    #region Index Restoration

    private static IKeyValueStore GetUnderlyingStore(IKeyValueStore store)
    {
        // If it's a transactional store, get the wrapped store
        if (store is Transactions.TransactionalStore ts)
        {
            return ts.UnderlyingStore;
        }
        return store;
    }

    private void RestoreIndexesFromMetadata()
    {
        if (m_indexManager == null || m_indexMetadataStore == null)
            return;

        try
        {
            var savedIndexes = m_indexMetadataStore.LoadAllIndexes();
            
            foreach (var metadata in savedIndexes)
            {
                // Skip if index already exists (shouldn't happen normally)
                if (m_indexManager.HasIndex(metadata.Name))
                    continue;

                // Recreate the index
                m_indexManager.CreateIndex(metadata.Name, metadata.IsUnique);
            }
        }
        catch
        {
            // Ignore errors during restoration - index data might still be on disk
            // but metadata might be corrupted. User can recreate indexes manually.
        }
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
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException($"Database already exists: {path}");

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
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException($"Database already exists: {path}");

        return new WitDatabaseBuilder()
            .WithFilePath(path)
            .WithBTree()
            .WithEncryption(password)
            .WithTransactions()
            .Build();
    }

    /// <summary>
    /// Opens an existing database with auto-detection of settings.
    /// Works with both BTree (file) and LSM (directory) databases.
    /// </summary>
    /// <param name="path">Path to the database file or directory.</param>
    /// <returns>A WitDatabase instance.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the database does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown if the database is encrypted (use overload with password).</exception>
    public static WitDatabase Open(string path)
    {
        var detection = StorageDetector.Detect(path);
        
        if (!detection.Exists)
            throw new FileNotFoundException("Database not found", path);

        if (detection.RequiresPassword)
        {
            throw new InvalidDataException(
                $"Database is encrypted with '{detection.EncryptionProvider}' provider. " +
                "Use WitDatabase.Open(path, password) or configure encryption in WitDatabaseBuilder.");
        }

        var builder = new WitDatabaseBuilder();

        // Configure based on detected store type
        if (detection.StoreType == "lsm" || detection.IsDirectory)
        {
            builder.WithLsmTree(path);
        }
        else
        {
            builder.WithFilePath(path).WithBTree();
        }

        // Configure features from detection
        if (detection.HasTransactions)
            builder.WithTransactions();
        else
            builder.WithoutTransactions();

        if (detection.HasFileLocking)
            builder.WithFileLocking();
        else
            builder.WithoutFileLocking();

        return builder.Build();
    }

    /// <summary>
    /// Opens an existing encrypted database.
    /// Works with both BTree (file) and LSM (directory) databases.
    /// </summary>
    /// <param name="path">Path to the database file or directory.</param>
    /// <param name="password">Encryption password.</param>
    /// <returns>A WitDatabase instance.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the database does not exist.</exception>
    public static WitDatabase Open(string path, string password)
    {
        var detection = StorageDetector.Detect(path);
        
        if (!detection.Exists)
            throw new FileNotFoundException("Database not found", path);

        var builder = new WitDatabaseBuilder();

        // Configure based on detected store type
        if (detection.StoreType == "lsm" || detection.IsDirectory)
        {
            builder.WithLsmTree(path).WithEncryption(password);
        }
        else
        {
            builder.WithFilePath(path).WithBTree().WithEncryption(password);
        }

        // Default to transactions enabled for encrypted DBs (we can't read header)
        builder.WithTransactions();

        return builder.Build();
    }

    /// <summary>
    /// Creates a new database or opens an existing one.
    /// Auto-detects BTree vs LSM for existing databases.
    /// </summary>
    /// <param name="path">Path to the database file or directory.</param>
    /// <returns>A WitDatabase instance.</returns>
    public static WitDatabase CreateOrOpen(string path)
    {
        var detection = StorageDetector.Detect(path);
        
        if (detection.Exists)
        {
            return Open(path);
        }
        
        return Create(path);
    }

    /// <summary>
    /// Creates a new encrypted database or opens an existing one.
    /// Auto-detects BTree vs LSM for existing databases.
    /// </summary>
    /// <param name="path">Path to the database file or directory.</param>
    /// <param name="password">Encryption password.</param>
    /// <returns>A WitDatabase instance.</returns>
    public static WitDatabase CreateOrOpen(string path, string password)
    {
        var detection = StorageDetector.Detect(path);
        
        if (detection.Exists)
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
    /// Gets information about a database without opening it.
    /// Works with both BTree (file) and LSM (directory) databases.
    /// </summary>
    /// <param name="path">Path to the database file or directory.</param>
    /// <returns>Detection result with database info.</returns>
    public static StorageDetectionResult GetDatabaseInfo(string path)
    {
        return StorageDetector.Detect(path);
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
    /// Begins a new transaction with default isolation level.
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
    /// Begins a new transaction with specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <exception cref="InvalidOperationException">Thrown if transactions are not enabled.</exception>
    public ITransaction BeginTransaction(IsolationLevel isolationLevel)
    {
        ThrowIfDisposed();
        if (m_transactionalStore == null)
            throw new InvalidOperationException("Transactions are not enabled. Use WithTransactions() when building the database.");
        
        return m_transactionalStore.BeginTransaction(isolationLevel);
    }

    /// <summary>
    /// Begins a new transaction asynchronously with default isolation level.
    /// </summary>
    public ValueTask<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (m_transactionalStore == null)
            throw new InvalidOperationException("Transactions are not enabled. Use WithTransactions() when building the database.");
        
        return m_transactionalStore.BeginTransactionAsync(cancellationToken);
    }

    /// <summary>
    /// Begins a new transaction asynchronously with specified isolation level.
    /// </summary>
    /// <param name="isolationLevel">The isolation level for the transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask<ITransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (m_transactionalStore == null)
            throw new InvalidOperationException("Transactions are not enabled. Use WithTransactions() when building the database.");
        
        return m_transactionalStore.BeginTransactionAsync(isolationLevel, cancellationToken);
    }

    #endregion

    #region Indexes

    /// <summary>
    /// Creates a new secondary index.
    /// </summary>
    /// <param name="name">The unique name of the index.</param>
    /// <param name="isUnique">Whether the index should enforce uniqueness.</param>
    /// <returns>The created index.</returns>
    /// <exception cref="InvalidOperationException">Thrown if index manager is not available.</exception>
    public ISecondaryIndex CreateIndex(string name, bool isUnique = false)
    {
        ThrowIfDisposed();
        if (m_indexManager == null)
            throw new InvalidOperationException("Index manager is not available.");
        
        var index = m_indexManager.CreateIndex(name, isUnique);
        
        // Persist metadata
        m_indexMetadataStore?.SaveIndex(name, isUnique);
        
        return index;
    }

    /// <summary>
    /// Gets an existing index by name.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>The index, or null if not found.</returns>
    public ISecondaryIndex? GetIndex(string name)
    {
        ThrowIfDisposed();
        return m_indexManager?.GetIndex(name);
    }

    /// <summary>
    /// Drops (removes) an index.
    /// </summary>
    /// <param name="name">The name of the index to drop.</param>
    /// <returns>True if the index was dropped; false if it didn't exist.</returns>
    public bool DropIndex(string name)
    {
        ThrowIfDisposed();
        
        var dropped = m_indexManager?.DropIndex(name) ?? false;
        
        if (dropped)
        {
            // Remove metadata
            m_indexMetadataStore?.RemoveIndex(name);
        }
        
        return dropped;
    }

    /// <summary>
    /// Checks if an index with the specified name exists.
    /// </summary>
    /// <param name="name">The name of the index.</param>
    /// <returns>True if the index exists; otherwise false.</returns>
    public bool HasIndex(string name)
    {
        ThrowIfDisposed();
        return m_indexManager?.HasIndex(name) ?? false;
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
        m_indexManager?.Flush();
    }

    /// <summary>
    /// Flushes any pending writes asynchronously.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await m_store.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (m_indexManager != null)
            await m_indexManager.FlushAsync(cancellationToken).ConfigureAwait(false);
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

        m_indexManager?.Dispose();

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

    /// <summary>
    /// Gets the index manager, or null if not available.
    /// </summary>
    public IIndexManager? IndexManager => m_indexManager;

    /// <summary>
    /// Gets whether secondary index support is available.
    /// </summary>
    public bool SupportsIndexes => m_indexManager != null;

    /// <summary>
    /// Gets the names of all indexes.
    /// </summary>
    public IReadOnlyList<string> IndexNames => m_indexManager?.IndexNames ?? [];

    #endregion
}
