using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Builder;

/// <summary>
/// Configuration options for WitDatabaseBuilder.
/// Stores provider keys and parameters for deferred creation during Build().
/// </summary>
/// <remarks>
/// <para>
/// This class follows a consistent pattern for each component:
/// - ProviderKey: identifies which provider to use (e.g., "btree", "aes-gcm")
/// - Parameters: ProviderParameters for that component
/// - Custom*: optional pre-built instance that bypasses registry
/// </para>
/// <para>
/// All component-specific settings (like file paths, cache sizes, etc.) 
/// are stored in the respective ProviderParameters, not as separate fields.
/// </para>
/// </remarks>
public sealed class WitDatabaseBuilderOptions
{
    #region Store Configuration

    /// <summary>
    /// Provider key for key-value store (e.g., "btree", "lsm", "inmemory").
    /// Default is "btree".
    /// </summary>
    public string StoreProviderKey { get; set; } = StoreBTree.PROVIDER_KEY;

    /// <summary>
    /// Parameters for creating the store via ProviderRegistry.
    /// Common parameters: "filePath", "directory", "cacheSize", "pageSize", "options" (LsmOptions).
    /// </summary>
    public ProviderParameters StoreParameters { get; } = new();

    /// <summary>
    /// Custom key-value store instance. If set, StoreProviderKey is ignored.
    /// </summary>
    public IKeyValueStore? CustomStore { get; set; }

    /// <summary>
    /// Custom storage implementation. Used when building BTree store.
    /// </summary>
    public IStorage? CustomStorage { get; set; }

    #endregion

    #region Encryption Configuration

    /// <summary>
    /// Provider key for encryption (e.g., "aes-gcm"). Null means no encryption.
    /// </summary>
    public string? EncryptionProviderKey { get; set; }

    /// <summary>
    /// Parameters for creating the crypto provider.
    /// Common parameters: "key" (byte[]), "salt" (byte[]), "password" (string), "user" (string), "iterations" (int).
    /// </summary>
    public ProviderParameters EncryptionParameters { get; } = new();

    /// <summary>
    /// Custom crypto provider. If set, EncryptionProviderKey is ignored.
    /// </summary>
    public ICryptoProvider? CustomCryptoProvider { get; set; }

    #endregion

    #region Transaction Configuration

    /// <summary>
    /// Whether to enable transaction support.
    /// </summary>
    public bool EnableTransactions { get; set; } = true;

    /// <summary>
    /// Provider key for transaction journal (e.g., "rollback", "wal"). Null means no journal.
    /// </summary>
    public string? JournalProviderKey { get; set; }

    /// <summary>
    /// Parameters for creating the journal.
    /// Common parameters: "filePath", "walPath", "pageSize".
    /// </summary>
    public ProviderParameters JournalParameters { get; } = new();

    /// <summary>
    /// Custom transaction journal. If set, JournalProviderKey is ignored.
    /// </summary>
    public ITransactionJournal? CustomJournal { get; set; }

    /// <summary>
    /// Parameters for transaction handling.
    /// Common parameters: "mvcc", "isolationLevel", "fileLocking", "lockTimeout".
    /// </summary>
    public ProviderParameters TransactionParameters { get; } = new();

    #endregion

    #region Cache Configuration

    /// <summary>
    /// Provider key for page cache (e.g., "clock", "lru"). Null means default cache.
    /// </summary>
    public string? CacheProviderKey { get; set; }

    /// <summary>
    /// Parameters for creating the cache.
    /// Common parameters: "size", "pageSize".
    /// </summary>
    public ProviderParameters CacheParameters { get; } = new();

    /// <summary>
    /// Custom page cache. If set, CacheProviderKey is ignored.
    /// </summary>
    public IPageCache? CustomCache { get; set; }

    #endregion

    #region Index Configuration

    /// <summary>
    /// Custom secondary index factory.
    /// </summary>
    public ISecondaryIndexFactory? SecondaryIndexFactory { get; set; }

    /// <summary>
    /// Parameters for secondary index configuration.
    /// Common parameters: "directory".
    /// </summary>
    public ProviderParameters IndexParameters { get; } = new();

    #endregion

    #region Computed Properties - Store

    /// <summary>
    /// Gets whether using LSM-Tree engine.
    /// </summary>
    public bool UseLsmTree => StoreProviderKey == StoreLsm.PROVIDER_KEY;

    /// <summary>
    /// Gets whether using BTree engine.
    /// </summary>
    public bool UseBTree => StoreProviderKey == StoreBTree.PROVIDER_KEY;

    /// <summary>
    /// Gets whether using in-memory storage.
    /// </summary>
    public bool UseMemoryStorage => StoreParameters.Get<bool>("useMemory");

    /// <summary>
    /// Gets the effective store provider key.
    /// </summary>
    public string EffectiveStoreProviderKey => CustomStore?.ProviderKey ?? StoreProviderKey;

    /// <summary>
    /// Gets the file path from StoreParameters.
    /// </summary>
    public string? FilePath => StoreParameters.Get<string>("filePath");

    /// <summary>
    /// Gets the LSM directory from StoreParameters.
    /// </summary>
    public string? LsmDirectory => StoreParameters.Get<string>("directory");

    /// <summary>
    /// Gets the page size from StoreParameters, or default.
    /// </summary>
    public int PageSize => StoreParameters.Get("pageSize", DatabaseConstants.DEFAULT_PAGE_SIZE);

    /// <summary>
    /// Gets the cache size from CacheParameters or StoreParameters, or default.
    /// </summary>
    public int CacheSize => CacheParameters.Get("size", 
        StoreParameters.Get("cacheSize", DatabaseConstants.DEFAULT_CACHE_SIZE));

    #endregion

    #region Computed Properties - Encryption

    /// <summary>
    /// Gets whether encryption is configured.
    /// </summary>
    public bool HasEncryption => CustomCryptoProvider != null || !string.IsNullOrEmpty(EncryptionProviderKey);

    #endregion

    #region Computed Properties - Transactions

    /// <summary>
    /// Gets whether MVCC is enabled.
    /// </summary>
    public bool EnableMvcc => TransactionParameters.Get<bool>("mvcc");

    /// <summary>
    /// Gets the default isolation level.
    /// </summary>
    public IsolationLevel DefaultIsolationLevel => 
        TransactionParameters.Get("isolationLevel", IsolationLevel.ReadCommitted);

    /// <summary>
    /// Gets whether file locking is enabled.
    /// </summary>
    public bool EnableFileLocking => TransactionParameters.Get("fileLocking", true);

    /// <summary>
    /// Gets the lock timeout.
    /// </summary>
    public TimeSpan LockTimeout => TransactionParameters.Get("lockTimeout", TimeSpan.FromSeconds(30));

    #endregion

    #region Computed Properties - Index

    /// <summary>
    /// Gets the index directory from IndexParameters.
    /// </summary>
    public string? IndexDirectory => IndexParameters.Get<string>("directory");

    #endregion
}
