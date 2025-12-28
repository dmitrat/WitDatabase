using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Stores;
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
    public static bool RequiresAsyncBuild(this WitDatabaseBuilder builder)
    {
        return builder.Options.CustomStorage is IAsyncOnlyStorage asyncOnly && asyncOnly.RequiresAsyncOperations;
    }

    /// <summary>
    /// Checks if the configured storage supports async initialization.
    /// </summary>
    public static bool SupportsAsyncInitialization(this WitDatabaseBuilder builder)
    {
        return builder.Options.CustomStorage is IAsyncInitializable;
    }

    /// <summary>
    /// Gets the storage provider key, or null if no storage is configured.
    /// </summary>
    public static string? GetStorageProviderKey(this WitDatabaseBuilder builder)
    {
        return builder.Options.CustomStorage?.ProviderKey;
    }

    #endregion

    #region Storage Configuration

    /// <summary>
    /// Use file-based storage with the specified path.
    /// </summary>
    public static WitDatabaseBuilder WithFilePath(this WitDatabaseBuilder builder, string path)
    {
        builder.Options.StoreParameters.Set("filePath", path);
        builder.Options.StoreParameters.Remove("useMemory");
        return builder;
    }

    /// <summary>
    /// Use in-memory storage (data is not persisted).
    /// </summary>
    public static WitDatabaseBuilder WithMemoryStorage(this WitDatabaseBuilder builder)
    {
        builder.Options.StoreParameters.Set("useMemory", true);
        builder.Options.StoreParameters.Remove("filePath");
        return builder;
    }

    /// <summary>
    /// Use a custom storage implementation.
    /// </summary>
    public static WitDatabaseBuilder WithStorage(this WitDatabaseBuilder builder, IStorage storage)
    {
        builder.Options.CustomStorage = storage;
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
        builder.Options.StoreProviderKey = StoreBTree.PROVIDER_KEY;
        builder.Options.CustomStore = null;
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine.
    /// Best for write-heavy workloads with excellent sequential write performance.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder)
    {
        builder.Options.StoreProviderKey = StoreLsm.PROVIDER_KEY;
        builder.Options.CustomStore = null;
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine with custom options.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder, Action<LsmOptions> configure)
    {
        builder.Options.StoreProviderKey = StoreLsm.PROVIDER_KEY;
        builder.Options.CustomStore = null;
        
        var lsmOptions = new LsmOptions();
        configure(lsmOptions);
        builder.Options.StoreParameters.Set("options", lsmOptions);
        
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine with the specified directory.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder, string directory)
    {
        builder.Options.StoreProviderKey = StoreLsm.PROVIDER_KEY;
        builder.Options.CustomStore = null;
        builder.Options.StoreParameters.Set("directory", directory);
        return builder;
    }

    /// <summary>
    /// Use LSM-Tree storage engine with the specified directory and custom options.
    /// </summary>
    public static WitDatabaseBuilder WithLsmTree(this WitDatabaseBuilder builder, string directory, Action<LsmOptions> configure)
    {
        builder.Options.StoreProviderKey = StoreLsm.PROVIDER_KEY;
        builder.Options.CustomStore = null;
        builder.Options.StoreParameters.Set("directory", directory);
        
        var lsmOptions = new LsmOptions();
        configure(lsmOptions);
        builder.Options.StoreParameters.Set("options", lsmOptions);
        
        return builder;
    }

    /// <summary>
    /// Use a custom key-value store implementation.
    /// </summary>
    public static WitDatabaseBuilder WithStore(this WitDatabaseBuilder builder, IKeyValueStore store)
    {
        ArgumentNullException.ThrowIfNull(store, nameof(store));
        builder.Options.CustomStore = store;
        return builder;
    }

    /// <summary>
    /// Use a key-value store created via ProviderRegistry with the specified provider key.
    /// </summary>
    public static WitDatabaseBuilder WithStoreKey(this WitDatabaseBuilder builder, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        builder.Options.StoreProviderKey = providerKey;
        builder.Options.CustomStore = null;
        return builder;
    }

    /// <summary>
    /// Use a key-value store created via ProviderRegistry with the specified provider key and parameters.
    /// </summary>
    public static WitDatabaseBuilder WithStoreKey(this WitDatabaseBuilder builder, string providerKey, ProviderParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(parameters, nameof(parameters));
        
        builder.Options.StoreProviderKey = providerKey;
        builder.Options.CustomStore = null;
        
        foreach (var (key, value) in parameters.GetAll())
        {
            builder.Options.StoreParameters.Set(key, value);
        }
        
        return builder;
    }

    /// <summary>
    /// Use a key-value store created via ProviderRegistry with the specified provider key and parameter configuration.
    /// </summary>
    public static WitDatabaseBuilder WithStoreKey(this WitDatabaseBuilder builder, string providerKey, Action<ProviderParameters> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(configure, nameof(configure));
        
        builder.Options.StoreProviderKey = providerKey;
        builder.Options.CustomStore = null;
        configure(builder.Options.StoreParameters);
        
        return builder;
    }

    #endregion

    #region Secondary Indexes

    /// <summary>
    /// Use a custom secondary index factory.
    /// </summary>
    public static WitDatabaseBuilder WithSecondaryIndexFactory(this WitDatabaseBuilder builder, ISecondaryIndexFactory factory)
    {
        builder.Options.SecondaryIndexFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        return builder;
    }

    /// <summary>
    /// Set the directory for secondary index storage.
    /// </summary>
    public static WitDatabaseBuilder WithIndexDirectory(this WitDatabaseBuilder builder, string directory)
    {
        ArgumentNullException.ThrowIfNull(directory, nameof(directory));
        builder.Options.IndexParameters.Set("directory", directory);
        return builder;
    }

    #endregion

    #region Encryption - Password Based

    /// <summary>
    /// Enable AES-GCM encryption with password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string password)
    {
        return builder.WithEncryption(password, CryptoUtils.DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with password-based key derivation and custom iterations.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string password, int iterations)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        builder.Options.EncryptionParameters.Set("password", password);
        builder.Options.EncryptionParameters.Set("iterations", iterations);
        
        // Pre-derive key and salt for immediate use
        var salt = CryptoUtils.DerivePasswordSalt(password);
        var key = CryptoUtils.DeriveKey(password, salt, iterations);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.EncryptionParameters.Set("key", key);

        builder.Options.CustomCryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Enable AES-GCM encryption optimized for WASM/browser environments.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionFast(this WitDatabaseBuilder builder, string password)
    {
        return builder.WithEncryption(password, CryptoUtils.WASM_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with user and password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, string user, string password)
    {
        return builder.WithUserEncryption(user, password, CryptoUtils.DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable AES-GCM encryption with user and password-based key derivation and custom iterations.
    /// </summary>
    public static WitDatabaseBuilder WithUserEncryption(this WitDatabaseBuilder builder, string user, string password, int iterations)
    {
        if (string.IsNullOrEmpty(user))
            throw new ArgumentException("User cannot be empty", nameof(user));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        builder.Options.EncryptionParameters.Set("user", user);
        builder.Options.EncryptionParameters.Set("password", password);
        builder.Options.EncryptionParameters.Set("iterations", iterations);
        
        // Pre-derive key and salt for immediate use
        var salt = CryptoUtils.DeriveUserSalt(user);
        var key = CryptoUtils.DeriveKey(password, salt, iterations);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.EncryptionParameters.Set("key", key);

        builder.Options.CustomCryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Enable AES-GCM encryption with user/password optimized for WASM/browser environments.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionFast(this WitDatabaseBuilder builder, string user, string password)
    {
        return builder.WithUserEncryption(user, password, CryptoUtils.WASM_PBKDF2_ITERATIONS);
    }

    #endregion

    #region Encryption - Key Based

    /// <summary>
    /// Enable AES-GCM encryption with the specified 256-bit key.
    /// Salt is derived from the key for deterministic behavior.
    /// </summary>
    public static WitDatabaseBuilder WithAesEncryption(this WitDatabaseBuilder builder, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("AES-256 requires a 32-byte key", nameof(key));

        var salt = CryptoUtils.DeriveKeySalt(key);
        
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionProviderKey = null;
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

        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = new EncryptorProviderAesGcm(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    #endregion

    #region Encryption - Custom Provider

    /// <summary>
    /// Use a custom crypto provider for encryption.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, ICryptoProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider, nameof(provider));
        builder.Options.CustomCryptoProvider = provider;
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Use a custom crypto provider for encryption with the specified salt.
    /// </summary>
    public static WitDatabaseBuilder WithEncryption(this WitDatabaseBuilder builder, ICryptoProvider provider, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(provider, nameof(provider));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));

        builder.Options.CustomCryptoProvider = provider;
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    #endregion

    #region Encryption - Provider Registry

    /// <summary>
    /// Enable encryption using a registered provider key with password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKey(this WitDatabaseBuilder builder, string providerKey, string password)
    {
        return builder.WithEncryptionKey(providerKey, password, CryptoUtils.DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable encryption using a registered provider key with password-based key derivation and custom iterations.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKey(this WitDatabaseBuilder builder, string providerKey, string password, int iterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = CryptoUtils.DerivePasswordSalt(password);
        var key = CryptoUtils.DeriveKey(password, salt, iterations);

        builder.Options.EncryptionProviderKey = providerKey;
        builder.Options.EncryptionParameters.Set("password", password);
        builder.Options.EncryptionParameters.Set("iterations", iterations);
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = null;
        return builder;
    }

    /// <summary>
    /// Enable encryption using a registered provider key with user and password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKey(this WitDatabaseBuilder builder, string providerKey, string user, string password)
    {
        return builder.WithEncryptionKey(providerKey, user, password, CryptoUtils.DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable encryption using a registered provider key with user/password and custom iterations.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKey(this WitDatabaseBuilder builder, string providerKey, string user, string password, int iterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        if (string.IsNullOrEmpty(user))
            throw new ArgumentException("User cannot be empty", nameof(user));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = CryptoUtils.DeriveUserSalt(user);
        var key = CryptoUtils.DeriveKey(password, salt, iterations);

        builder.Options.EncryptionProviderKey = providerKey;
        builder.Options.EncryptionParameters.Set("user", user);
        builder.Options.EncryptionParameters.Set("password", password);
        builder.Options.EncryptionParameters.Set("iterations", iterations);
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = null;
        return builder;
    }

    /// <summary>
    /// Enable encryption using a registered provider key optimized for WASM/browser environments.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKeyFast(this WitDatabaseBuilder builder, string providerKey, string password)
    {
        return builder.WithEncryptionKey(providerKey, password, CryptoUtils.WASM_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable encryption using a registered provider key with user/password optimized for WASM.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKeyFast(this WitDatabaseBuilder builder, string providerKey, string user, string password)
    {
        return builder.WithEncryptionKey(providerKey, user, password, CryptoUtils.WASM_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable encryption using a registered provider key with custom parameters.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKey(this WitDatabaseBuilder builder, string providerKey, ProviderParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(parameters, nameof(parameters));

        builder.Options.EncryptionProviderKey = providerKey;
        builder.Options.CustomCryptoProvider = null;
        
        foreach (var (key, value) in parameters.GetAll())
        {
            builder.Options.EncryptionParameters.Set(key, value);
        }
        
        return builder;
    }

    /// <summary>
    /// Enable encryption using a registered provider key with custom parameters and salt.
    /// </summary>
    public static WitDatabaseBuilder WithEncryptionKey(this WitDatabaseBuilder builder, string providerKey, ProviderParameters parameters, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(parameters, nameof(parameters));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));

        builder.Options.EncryptionProviderKey = providerKey;
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = null;
        
        foreach (var (key, value) in parameters.GetAll())
        {
            builder.Options.EncryptionParameters.Set(key, value);
        }
        
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
        ArgumentNullException.ThrowIfNull(journal, nameof(journal));
        builder.Options.EnableTransactions = true;
        builder.Options.CustomJournal = journal;
        builder.Options.JournalProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Enable transaction support with a journal at the specified file path.
    /// Uses rollback journal by default.
    /// </summary>
    public static WitDatabaseBuilder WithJournal(this WitDatabaseBuilder builder, string journalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalPath, nameof(journalPath));
        builder.Options.EnableTransactions = true;
        builder.Options.JournalProviderKey = "rollback";
        builder.Options.JournalParameters.Set("filePath", journalPath);
        builder.Options.CustomJournal = null;
        return builder;
    }

    /// <summary>
    /// Enable transaction support with a journal created via ProviderRegistry.
    /// </summary>
    public static WitDatabaseBuilder WithJournalKey(this WitDatabaseBuilder builder, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        builder.Options.EnableTransactions = true;
        builder.Options.JournalProviderKey = providerKey;
        builder.Options.CustomJournal = null;
        return builder;
    }

    /// <summary>
    /// Enable transaction support with a journal created via ProviderRegistry.
    /// </summary>
    public static WitDatabaseBuilder WithJournalKey(this WitDatabaseBuilder builder, string providerKey, ProviderParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(parameters, nameof(parameters));
        
        builder.Options.EnableTransactions = true;
        builder.Options.JournalProviderKey = providerKey;
        builder.Options.CustomJournal = null;
        
        foreach (var (key, value) in parameters.GetAll())
        {
            builder.Options.JournalParameters.Set(key, value);
        }
        
        return builder;
    }

    /// <summary>
    /// Enable transaction support with a journal created via ProviderRegistry with parameter configuration.
    /// </summary>
    public static WitDatabaseBuilder WithJournalKey(this WitDatabaseBuilder builder, string providerKey, Action<ProviderParameters> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(configure, nameof(configure));
        
        builder.Options.EnableTransactions = true;
        builder.Options.JournalProviderKey = providerKey;
        builder.Options.CustomJournal = null;
        configure(builder.Options.JournalParameters);
        
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
    /// </summary>
    public static WitDatabaseBuilder WithMvcc(this WitDatabaseBuilder builder)
    {
        builder.Options.TransactionParameters.Set("mvcc", true);
        builder.Options.TransactionParameters.Set("isolationLevel", IsolationLevel.Snapshot);
        builder.Options.EnableTransactions = true;
        return builder;
    }

    /// <summary>
    /// Enable MVCC with a specific default isolation level.
    /// </summary>
    public static WitDatabaseBuilder WithMvcc(this WitDatabaseBuilder builder, IsolationLevel defaultIsolationLevel)
    {
        builder.Options.TransactionParameters.Set("mvcc", true);
        builder.Options.TransactionParameters.Set("isolationLevel", defaultIsolationLevel);
        builder.Options.EnableTransactions = true;
        return builder;
    }

    /// <summary>
    /// Set the default isolation level for transactions.
    /// </summary>
    public static WitDatabaseBuilder WithDefaultIsolationLevel(this WitDatabaseBuilder builder, IsolationLevel isolationLevel)
    {
        builder.Options.TransactionParameters.Set("isolationLevel", isolationLevel);
        return builder;
    }

    #endregion

    #region Cache

    /// <summary>
    /// Use a custom page cache.
    /// </summary>
    public static WitDatabaseBuilder WithCache(this WitDatabaseBuilder builder, IPageCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache, nameof(cache));
        builder.Options.CustomCache = cache;
        builder.Options.CacheProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Use a cache created via ProviderRegistry with the specified provider key.
    /// </summary>
    public static WitDatabaseBuilder WithCacheKey(this WitDatabaseBuilder builder, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        builder.Options.CacheProviderKey = providerKey;
        builder.Options.CustomCache = null;
        return builder;
    }

    /// <summary>
    /// Use a cache created via ProviderRegistry with the specified provider key and parameters.
    /// </summary>
    public static WitDatabaseBuilder WithCacheKey(this WitDatabaseBuilder builder, string providerKey, ProviderParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(parameters, nameof(parameters));
        
        builder.Options.CacheProviderKey = providerKey;
        builder.Options.CustomCache = null;
        
        foreach (var (key, value) in parameters.GetAll())
        {
            builder.Options.CacheParameters.Set(key, value);
        }
        
        return builder;
    }

    /// <summary>
    /// Use a cache created via ProviderRegistry with the specified provider key and parameter configuration.
    /// </summary>
    public static WitDatabaseBuilder WithCacheKey(this WitDatabaseBuilder builder, string providerKey, Action<ProviderParameters> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey, nameof(providerKey));
        ArgumentNullException.ThrowIfNull(configure, nameof(configure));
        
        builder.Options.CacheProviderKey = providerKey;
        builder.Options.CustomCache = null;
        configure(builder.Options.CacheParameters);
        
        return builder;
    }

    #endregion

    #region Locking

    /// <summary>
    /// Enable file locking for concurrent access (default).
    /// </summary>
    public static WitDatabaseBuilder WithFileLocking(this WitDatabaseBuilder builder)
    {
        builder.Options.TransactionParameters.Set("fileLocking", true);
        return builder;
    }

    /// <summary>
    /// Disable file locking (use only for single-process access).
    /// </summary>
    public static WitDatabaseBuilder WithoutFileLocking(this WitDatabaseBuilder builder)
    {
        builder.Options.TransactionParameters.Set("fileLocking", false);
        return builder;
    }

    /// <summary>
    /// Set the lock timeout for concurrent operations.
    /// </summary>
    public static WitDatabaseBuilder WithLockTimeout(this WitDatabaseBuilder builder, TimeSpan timeout)
    {
        builder.Options.TransactionParameters.Set("lockTimeout", timeout);
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

        builder.Options.StoreParameters.Set("pageSize", pageSize);
        return builder;
    }

    /// <summary>
    /// Set the number of pages to cache in memory.
    /// </summary>
    public static WitDatabaseBuilder WithCacheSize(this WitDatabaseBuilder builder, int pages)
    {
        if (pages < 1)
            throw new ArgumentOutOfRangeException(nameof(pages), "Cache size must be at least 1");

        builder.Options.CacheParameters.Set("size", pages);
        return builder;
    }

    #endregion
}
