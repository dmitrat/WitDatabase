using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;

namespace OutWit.Database.Core.Builder;

/// <summary>
/// Configuration options for WitDatabaseBuilder.
/// </summary>
public sealed class WitDatabaseBuilderOptions
{
    #region Constants

    private static readonly byte[] DEFAULT_SALT = "WitDBSalt123"u8.ToArray();

    #endregion

    #region Properties

    /// <summary>
    /// Storage implementation to use.
    /// </summary>
    public IStorage? Storage { get; set; }
    
    /// <summary>
    /// Path for file-based storage.
    /// </summary>
    public string? FilePath { get; set; }
    
    /// <summary>
    /// Whether to use memory storage.
    /// </summary>
    public bool UseMemoryStorage { get; set; }
    
    /// <summary>
    /// Crypto provider for encryption.
    /// </summary>
    public ICryptoProvider? CryptoProvider { get; set; }
    
    /// <summary>
    /// Salt for encryption key derivation.
    /// </summary>
    public byte[] EncryptionSalt { get; set; } = DEFAULT_SALT;
    
    /// <summary>
    /// Custom key-value store implementation.
    /// </summary>
    public IKeyValueStore? KeyValueStore { get; set; }
    
    /// <summary>
    /// Transaction journal for durability.
    /// </summary>
    public ITransactionJournal? TransactionJournal { get; set; }
    
    /// <summary>
    /// LSM-Tree options (when using LSM engine).
    /// </summary>
    public LsmOptions? LsmOptions { get; set; }
    
    /// <summary>
    /// Page size in bytes.
    /// </summary>
    public int PageSize { get; set; } = DatabaseConstants.DEFAULT_PAGE_SIZE;
    
    /// <summary>
    /// Number of pages to cache.
    /// </summary>
    public int CacheSize { get; set; } = DatabaseConstants.DEFAULT_CACHE_SIZE;
    
    /// <summary>
    /// Lock timeout for concurrent access.
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Whether to enable transaction support.
    /// </summary>
    public bool EnableTransactions { get; set; } = true;
    
    /// <summary>
    /// Whether to enable file locking for concurrent access.
    /// </summary>
    public bool EnableFileLocking { get; set; } = true;
    
    /// <summary>
    /// Whether to use BTree engine (default).
    /// </summary>
    public bool UseBTree { get; set; } = true;
    
    /// <summary>
    /// Whether to use LSM-Tree engine.
    /// </summary>
    public bool UseLsmTree { get; set; }
    
    /// <summary>
    /// Directory for LSM-Tree storage.
    /// </summary>
    public string? LsmDirectory { get; set; }

    /// <summary>
    /// Custom secondary index factory.
    /// If not set, a default factory based on the storage engine will be used.
    /// </summary>
    public ISecondaryIndexFactory? SecondaryIndexFactory { get; set; }

    /// <summary>
    /// Directory for secondary index storage (used when separate index storage is needed).
    /// If not set, indexes are stored alongside the main data.
    /// </summary>
    public string? IndexDirectory { get; set; }

    /// <summary>
    /// Whether to enable MVCC (Multi-Version Concurrency Control).
    /// When enabled, the database supports snapshot isolation and concurrent transactions.
    /// </summary>
    public bool EnableMvcc { get; set; }

    /// <summary>
    /// Default isolation level for transactions.
    /// Only applies when MVCC is enabled.
    /// </summary>
    public IsolationLevel DefaultIsolationLevel { get; set; } = IsolationLevel.ReadCommitted;

    #endregion
}
