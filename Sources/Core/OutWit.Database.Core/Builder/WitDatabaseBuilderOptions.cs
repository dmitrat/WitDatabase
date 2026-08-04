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
    #region Stored Configuration

    /// <summary>
    /// Settings the caller named, as opposed to those left at their defaults. Only consulted when
    /// <see cref="RestoreStoredConfiguration"/> is on.
    /// </summary>
    private readonly HashSet<string> m_named = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a database that already exists may supply the settings the caller did not name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off by default, and deliberately: a builder written by hand states its configuration in full, so
    /// letting a file override any of it would change what existing code does. It is switched on by the
    /// routes where the caller is not spelling out a configuration - a connection string, and
    /// <see cref="WitDatabase.Open(string)"/> - which are the routes that meet a database somebody else
    /// created.
    /// </para>
    /// <para>
    /// <b>What is restored is layout and performance, never safety.</b> The store, page size, encryption,
    /// transaction model, journal, cache and cache size come back from the file;
    /// <c>Synchronous Commit</c>, <c>FileLocking</c> and <c>Isolation Level</c> do not. A file that
    /// could turn off durability or exclusive locking for a caller who said nothing about either would
    /// be a way to make a database quietly less safe than the defaults promise, and those three are
    /// properties of a session rather than of the data.
    /// </para>
    /// </remarks>
    public bool RestoreStoredConfiguration { get; set; }

    /// <summary>
    /// Records that the caller named a setting, so a stored value must not overwrite it.
    /// </summary>
    public void MarkNamed(string setting) => m_named.Add(setting);

    /// <summary>
    /// Whether the caller named a setting.
    /// </summary>
    public bool IsNamed(string setting) => m_named.Contains(setting);

    /// <summary>Names used by <see cref="MarkNamed"/>, kept together so they cannot drift apart.</summary>
    public static class Setting
    {
        public const string STORE = "Store";
        public const string PAGE_SIZE = "PageSize";
        public const string CACHE = "Cache";
        public const string CACHE_SIZE = "CacheSize";
        public const string JOURNAL = "Journal";
        public const string TRANSACTIONS = "Transactions";
        public const string MVCC = "MVCC";
        public const string ENCRYPTION = "Encryption";
    }

    #endregion

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
    public WitIsolationLevel DefaultIsolationLevel => 
        TransactionParameters.Get("isolationLevel", WitIsolationLevel.ReadCommitted);

    /// <summary>
    /// Gets whether a commit is flushed to storage before it returns. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Turning this off makes a successful COMMIT survivable only until the process exits cleanly.
    /// See <c>MvccTransactionalStore.SynchronousCommit</c>.
    /// </remarks>
    public bool SynchronousCommit => TransactionParameters.Get("synchronousCommit", true);

    /// <summary>
    /// Gets whether file locking is enabled.
    /// </summary>
    public bool EnableFileLocking => TransactionParameters.Get("fileLocking", true);

    /// <summary>
    /// Gets the lock timeout.
    /// </summary>
    public TimeSpan LockTimeout => TransactionParameters.Get("lockTimeout", TimeSpan.FromSeconds(30));

    /// <summary>
    /// How long <c>Build</c> waits for another engine to release this database before refusing.
    /// </summary>
    /// <remarks>
    /// Five seconds by default, and the number is about one scenario: a host restart, where the
    /// outgoing process is still flushing while the incoming one starts. One engine per database is a
    /// design limit, so the wait is short - long enough to cover a shutdown, short enough that a
    /// database somebody really is using is reported quickly. Zero means one attempt and no waiting.
    /// </remarks>
    public TimeSpan OpenTimeout => TransactionParameters.Get("openTimeout", TimeSpan.FromSeconds(5));

    #endregion

    #region Computed Properties - Index

    /// <summary>
    /// Gets the index directory from IndexParameters.
    /// </summary>
    public string? IndexDirectory => IndexParameters.Get<string>("directory");

    #endregion
}
