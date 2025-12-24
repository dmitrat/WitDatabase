using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Utils;

namespace OutWit.Database.Core.Builder;

/// <summary>
/// Extension methods for configuring WitDatabaseBuilder.
/// </summary>
public static class WitDatabaseBuilderExtensions
{
    #region Storage Capability Detection

    /// <summary>
    /// Checks if the configured storage requires async-only operations.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <returns>True if storage requires async operations (e.g., IndexedDB in WASM).</returns>
    /// <remarks>
    /// When this returns true, you must use <see cref="WitDatabaseBuilder.BuildAsync"/>
    /// instead of <see cref="WitDatabaseBuilder.Build"/>.
    /// </remarks>
    public static bool RequiresAsyncBuild(this WitDatabaseBuilder builder)
    {
        return builder.Options.Storage is IAsyncOnlyStorage asyncOnly && asyncOnly.RequiresAsyncOperations;
    }

    /// <summary>
    /// Checks if the configured storage supports async initialization.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <returns>True if storage implements IAsyncInitializable.</returns>
    public static bool SupportsAsyncInitialization(this WitDatabaseBuilder builder)
    {
        return builder.Options.Storage is IAsyncInitializable;
    }

    /// <summary>
    /// Gets the storage provider key, or null if no storage is configured.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <returns>The provider key or null.</returns>
    public static string? GetStorageProviderKey(this WitDatabaseBuilder builder)
    {
        return builder.Options.Storage?.ProviderKey;
    }

    #endregion

    #region Storage Configuration

    /// <summary>
    /// Use file-based storage with the specified path.
    /// </summary>
    public static WitDatabaseBuilder WithFilePath(this WitDatabaseBuilder builder, string path)
    {
        builder.Options.FilePath = path;
        builder.Options.UseMemoryStorage = false;
        return builder;
    }

    /// <summary>
    /// Use in-memory storage (data is not persisted).
    /// </summary>
    public static WitDatabaseBuilder WithMemoryStorage(this WitDatabaseBuilder builder)
    {
        builder.Options.UseMemoryStorage = true;
        builder.Options.FilePath = null;
        return builder;
    }

    /// <summary>
    /// Use a custom storage implementation.
    /// </summary>
    public static WitDatabaseBuilder WithStorage(this WitDatabaseBuilder builder, IStorage storage)
    {
        builder.Options.Storage = storage;
        return builder;
    }

    #endregion

    #region Engine Selection

    /// <summary>
    /// Use B-Tree storage engine (default).
    /// Best for read-heavy workloads with good random access performance.
    /// </summary>
    public static WitDatabaseBuilder WithBTree(this WitDatabaseBuilder builder)
    {
        builder.Options.UseBTree = true;
        builder.Options.UseLsmTree = false;
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine.
    /// Best for write-heavy workloads with excellent sequential write performance.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder)
    {
        builder.Options.UseLsmTree = true;
        builder.Options.UseBTree = false;
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine with custom options.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder, Action<LsmOptions> configure)
    {
        builder.Options.UseLsmTree = true;
        builder.Options.UseBTree = false;
        builder.Options.LsmOptions = new LsmOptions();
        configure(builder.Options.LsmOptions);
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine with the specified directory.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder, string directory)
    {
        builder.Options.UseLsmTree = true;
        builder.Options.UseBTree = false;
        builder.Options.LsmDirectory = directory;
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine with the specified directory and custom options.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder, string directory, Action<LsmOptions> configure)
    {
        builder.Options.UseLsmTree = true;
        builder.Options.UseBTree = false;
        builder.Options.LsmDirectory = directory;
        builder.Options.LsmOptions = new LsmOptions();
        configure(builder.Options.LsmOptions);
        return builder;
    }

    /// <summary>
    /// Use a custom key-value store implementation.
    /// </summary>
    public static WitDatabaseBuilder WithStore(this WitDatabaseBuilder builder, IKeyValueStore store)
    {
        builder.Options.KeyValueStore = store;
        return builder;
    }

    #endregion

    #region Secondary Indexes

    /// <summary>
    /// Use a custom secondary index factory.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="factory">The factory for creating secondary indexes.</param>
    public static WitDatabaseBuilder WithSecondaryIndexFactory(this WitDatabaseBuilder builder, ISecondaryIndexFactory factory)
    {
        builder.Options.SecondaryIndexFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return builder;
    }

    /// <summary>
    /// Set the directory for secondary index storage.
    /// Indexes will be stored separately from the main data.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="directory">Directory path for index storage.</param>
    public static WitDatabaseBuilder WithIndexDirectory(this WitDatabaseBuilder builder, string directory)
    {
        builder.Options.IndexDirectory = directory ?? throw new ArgumentNullException(nameof(directory));
        return builder;
    }

    #endregion

    #region Encryption

    /// <summary>
    /// Enable AES-GCM encryption with password-based key derivation.
    /// Uses PBKDF2 with SHA-256 for key derivation.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="password">Password to derive encryption key from.</param>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string password)
    {
        return builder.WithEncryption(password, CryptoUtils.DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with password-based key derivation and custom iterations.
    /// Uses PBKDF2 with SHA-256 for key derivation.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="password">Password to derive encryption key from.</param>
    /// <param name="iterations">Number of PBKDF2 iterations. Use <see cref="CryptoUtils.WASM_PBKDF2_ITERATIONS"/> for WASM.</param>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string password, int iterations)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = CryptoUtils.DerivePasswordSalt(password);
        var key = CryptoUtils.DeriveKey(password, salt, iterations);
        
        builder.Options.CryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionSalt = salt;
        return builder;
    }

    /// <summary>
    /// Enable AES-GCM encryption optimized for WASM/browser environments.
    /// Uses reduced PBKDF2 iterations (10,000) for faster key derivation.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="password">Password to derive encryption key from.</param>
    /// <remarks>
    /// Use this method in Blazor WASM applications for faster initialization.
    /// For native applications where security is paramount, use <see cref="WithEncryption(WitDatabaseBuilder, string)"/>.
    /// </remarks>
    public static WitDatabaseBuilder WithEncryptionFast(this WitDatabaseBuilder builder, string password)
    {
        return builder.WithEncryption(password, CryptoUtils.WASM_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with user and password-based key derivation.
    /// Uses user as salt basis and password for key derivation via PBKDF2.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="user">Username (used as salt basis).</param>
    /// <param name="password">Password to derive encryption key from.</param>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string user, string password)
    {
        return builder.WithEncryption(user, password, CryptoUtils.DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with user and password-based key derivation and custom iterations.
    /// Uses user as salt basis and password for key derivation via PBKDF2.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="user">Username (used as salt basis).</param>
    /// <param name="password">Password to derive encryption key from.</param>
    /// <param name="iterations">Number of PBKDF2 iterations. Use <see cref="CryptoUtils.WASM_PBKDF2_ITERATIONS"/> for WASM.</param>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string user, string password, int iterations)
    {
        if (string.IsNullOrEmpty(user))
            throw new ArgumentException("User cannot be empty", nameof(user));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = CryptoUtils.DeriveUserSalt(user);
        var key = CryptoUtils.DeriveKey(password, salt, iterations);
        
        builder.Options.CryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionSalt = salt;
        return builder;
    }

    /// <summary>
    /// Enable AES-GCM encryption with user/password optimized for WASM/browser environments.
    /// Uses reduced PBKDF2 iterations (10,000) for faster key derivation.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="user">Username (used as salt basis).</param>
    /// <param name="password">Password to derive encryption key from.</param>
    /// <remarks>
    /// Use this method in Blazor WASM applications for faster initialization.
    /// For native applications where security is paramount, use <see cref="WithEncryption(WitDatabaseBuilder, string, string)"/>
    /// </remarks>
    public static WitDatabaseBuilder WithEncryptionFast(this WitDatabaseBuilder builder, string user, string password)
    {
        return builder.WithEncryption(user, password, CryptoUtils.WASM_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with the specified 256-bit key.
    /// </summary>
    public static WitDatabaseBuilder WithAesEncryption(this WitDatabaseBuilder builder, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 requires a 32-byte key", nameof(key));
        
        builder.Options.CryptoProvider = new EncryptorProviderAesGcm(key);
        return builder;
    }

    /// <summary>
    /// Enable AES-GCM encryption with the specified 256-bit key and salt.
    /// </summary>
    public static WitDatabaseBuilder WithAesEncryption(this WitDatabaseBuilder builder, byte[] key, byte[] salt)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 requires a 32-byte key", nameof(key));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        
        builder.Options.CryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionSalt = salt;
        return builder;
    }

    /// <summary>
    /// Use a custom crypto provider for encryption.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, ICryptoProvider provider)
    {
        builder.Options.CryptoProvider = provider;
        return builder;
    }

    /// <summary>
    /// Use a custom crypto provider for encryption with the specified salt.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, ICryptoProvider provider, byte[] salt)
    {
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        
        builder.Options.CryptoProvider = provider;
        builder.Options.EncryptionSalt = salt;
        return builder;
    }

    #endregion

    #region Transactions

    /// <summary>
    /// Enable transaction support (default).
    /// </summary>
    public static WitDatabaseBuilder WithTransactions(this WitDatabaseBuilder builder)
    {
        builder.Options.EnableTransactions = true;
        return builder;
    }

    /// <summary>
    /// Enable transaction support with a custom journal.
    /// </summary>
    public static WitDatabaseBuilder WithTransactions(this WitDatabaseBuilder builder, ITransactionJournal journal)
    {
        builder.Options.EnableTransactions = true;
        builder.Options.TransactionJournal = journal;
        return builder;
    }

    /// <summary>
    /// Disable transaction support (faster but no atomicity guarantees).
    /// </summary>
    public static WitDatabaseBuilder WithoutTransactions(this WitDatabaseBuilder builder)
    {
        builder.Options.EnableTransactions = false;
        return builder;
    }

    /// <summary>
    /// Enable MVCC (Multi-Version Concurrency Control) for snapshot isolation.
    /// This enables concurrent read transactions and read-during-write support.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static WitDatabaseBuilder WithMvcc(this WitDatabaseBuilder builder)
    {
        builder.Options.EnableMvcc = true;
        builder.Options.EnableTransactions = true;
        builder.Options.DefaultIsolationLevel = IsolationLevel.Snapshot;
        return builder;
    }

    /// <summary>
    /// Enable MVCC with a specific default isolation level.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="defaultIsolationLevel">The default isolation level for transactions.</param>
    /// <returns>The builder for chaining.</returns>
    public static WitDatabaseBuilder WithMvcc(this WitDatabaseBuilder builder, IsolationLevel defaultIsolationLevel)
    {
        builder.Options.EnableMvcc = true;
        builder.Options.EnableTransactions = true;
        builder.Options.DefaultIsolationLevel = defaultIsolationLevel;
        return builder;
    }

    /// <summary>
    /// Set the default isolation level for transactions.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="isolationLevel">The default isolation level.</param>
    /// <returns>The builder for chaining.</returns>
    public static WitDatabaseBuilder WithDefaultIsolationLevel(this WitDatabaseBuilder builder, IsolationLevel isolationLevel)
    {
        builder.Options.DefaultIsolationLevel = isolationLevel;
        return builder;
    }

    #endregion

    #region Locking

    /// <summary>
    /// Enable file locking for concurrent access (default).
    /// </summary>
    public static WitDatabaseBuilder WithFileLocking(this WitDatabaseBuilder builder)
    {
        builder.Options.EnableFileLocking = true;
        return builder;
    }

    /// <summary>
    /// Disable file locking (use only for single-process access).
    /// </summary>
    public static WitDatabaseBuilder WithoutFileLocking(this WitDatabaseBuilder builder)
    {
        builder.Options.EnableFileLocking = false;
        return builder;
    }

    /// <summary>
    /// Set the lock timeout for concurrent operations.
    /// </summary>
    public static WitDatabaseBuilder WithLockTimeout(this WitDatabaseBuilder builder, TimeSpan timeout)
    {
        builder.Options.LockTimeout = timeout;
        return builder;
    }

    #endregion

    #region Page/Cache Settings

    /// <summary>
    /// Set the page size (default: 4096 bytes).
    /// </summary>
    public static WitDatabaseBuilder WithPageSize(this WitDatabaseBuilder builder, int pageSize)
    {
        if (pageSize < DatabaseConstants.MIN_PAGE_SIZE || pageSize > DatabaseConstants.MAX_PAGE_SIZE)
            throw new ArgumentOutOfRangeException(nameof(pageSize), 
                $"Page size must be between {DatabaseConstants.MIN_PAGE_SIZE} and {DatabaseConstants.MAX_PAGE_SIZE}");
        
        builder.Options.PageSize = pageSize;
        return builder;
    }

    /// <summary>
    /// Set the number of pages to cache in memory.
    /// </summary>
    public static WitDatabaseBuilder WithCacheSize(this WitDatabaseBuilder builder, int pages)
    {
        if (pages < 1)
            throw new ArgumentOutOfRangeException(nameof(pages), "Cache size must be at least 1");
        
        builder.Options.CacheSize = pages;
        return builder;
    }

    #endregion
}
