using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Builder;

/// <summary>
/// Fluent builder for creating WitDatabase instances.
/// Use extension methods (WithFilePath, WithMemoryStorage, etc.) to configure.
/// </summary>
public sealed class WitDatabaseBuilder
{
    #region Events

    /// <summary>
    /// Event fired during validation, before building the database.
    /// </summary>
    public event Action<WitDatabaseBuilderOptions>? OnValidating;

    /// <summary>
    /// Event fired after the store is built but before creating the database.
    /// </summary>
    public event Action<IKeyValueStore>? OnStoreBuilt;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the configuration options.
    /// </summary>
    public WitDatabaseBuilderOptions Options { get; } = new();

    #endregion

    #region Build

    /// <summary>
    /// Builds the database with the configured options.
    /// </summary>
    public WitDatabase Build()
    {
        ValidateConfiguration();
        ValidateSyncBuildAllowed();

        var store = BuildStoreInternal();
        OnStoreBuilt?.Invoke(store);

        var indexManager = BuildIndexManagerInternal();

        if (Options.EnableTransactions)
        {
            var transactionalStore = BuildTransactionalStoreInternal(store);
            return new WitDatabase(transactionalStore, indexManager, disposeStore: true);
        }

        return new WitDatabase(store, indexManager, disposeStore: true);
    }

    /// <summary>
    /// Builds the database asynchronously.
    /// </summary>
    public async ValueTask<WitDatabase> BuildAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var store = await BuildStoreInternalAsync(cancellationToken).ConfigureAwait(false);
        OnStoreBuilt?.Invoke(store);

        var indexManager = BuildIndexManagerInternal();

        if (Options.EnableTransactions)
        {
            var transactionalStore = BuildTransactionalStoreInternal(store);
            return await WitDatabase.CreateAsync(transactionalStore, indexManager, disposeStore: true, cancellationToken)
                .ConfigureAwait(false);
        }

        return await WitDatabase.CreateAsync(store, indexManager, disposeStore: true, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds just the key-value store without transaction wrapper.
    /// </summary>
    public IKeyValueStore BuildStore()
    {
        ValidateConfiguration();
        ValidateSyncBuildAllowed();
        return BuildStoreInternal();
    }

    /// <summary>
    /// Builds just the key-value store asynchronously.
    /// </summary>
    public ValueTask<IKeyValueStore> BuildStoreAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        return BuildStoreInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a transactional store.
    /// </summary>
    public ITransactionalStore BuildTransactionalStore()
    {
        ValidateConfiguration();
        ValidateSyncBuildAllowed();
        var store = BuildStoreInternal();
        return BuildTransactionalStoreInternal(store);
    }

    /// <summary>
    /// Builds a transactional store asynchronously.
    /// </summary>
    public async ValueTask<ITransactionalStore> BuildTransactionalStoreAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        var store = await BuildStoreInternalAsync(cancellationToken).ConfigureAwait(false);
        return BuildTransactionalStoreInternal(store);
    }

    /// <summary>
    /// Builds an index manager with the configured factory.
    /// </summary>
    public IIndexManager BuildIndexManager()
    {
        ValidateConfiguration();
        return BuildIndexManagerInternal();
    }

    #endregion

    #region Validation

    private void ValidateConfiguration()
    {
        ValidateStorageCompatibility();
        ValidateCustomStoreCompatibility();
        ValidateProviderKeys();
        ValidateStorageConfigured();
        ValidatePageSize();
        ValidateEncryptionSalt();

        OnValidating?.Invoke(Options);
    }

    private void ValidateStorageCompatibility()
    {
        if (Options.UseLsmTree && Options.CustomStorage != null)
        {
            throw new InvalidOperationException(
                "LSM-Tree uses directory-based storage and cannot use WithStorage(). " +
                "Use WithFilePath(directory) instead, or use BTree with WithStorage().");
        }
    }

    private void ValidateCustomStoreCompatibility()
    {
        if (Options.CustomStore == null)
            return;

        if (Options.HasEncryption)
        {
            throw new InvalidOperationException(
                "Cannot use WithAesEncryption() or WithEncryption() with WithStore(IKeyValueStore). " +
                "Configure encryption in your custom store implementation.");
        }

        if (Options.CustomStorage != null)
        {
            throw new InvalidOperationException(
                "Cannot use WithStorage() with WithStore(IKeyValueStore). Choose one or the other.");
        }
    }

    private void ValidateProviderKeys()
    {
        // Validate store provider key (if not using custom store)
        if (Options.CustomStore == null && !ProviderRegistry.Instance.IsRegistered<IKeyValueStore>(Options.StoreProviderKey))
        {
            var available = ProviderRegistry.Instance.GetRegisteredKeys<IKeyValueStore>();
            throw new InvalidOperationException(
                $"Store provider '{Options.StoreProviderKey}' is not registered. " +
                $"Available: {string.Join(", ", available)}");
        }

        // Validate encryption provider key (if set)
        if (!string.IsNullOrEmpty(Options.EncryptionProviderKey) && 
            !ProviderRegistry.Instance.IsRegistered<ICryptoProvider>(Options.EncryptionProviderKey))
        {
            var available = ProviderRegistry.Instance.GetRegisteredKeys<ICryptoProvider>();
            throw new InvalidOperationException(
                $"Encryption provider '{Options.EncryptionProviderKey}' is not registered. " +
                $"Available: {string.Join(", ", available)}");
        }

        // Validate journal provider key (if set)
        if (!string.IsNullOrEmpty(Options.JournalProviderKey) && 
            !ProviderRegistry.Instance.IsRegistered<ITransactionJournal>(Options.JournalProviderKey))
        {
            var available = ProviderRegistry.Instance.GetRegisteredKeys<ITransactionJournal>();
            throw new InvalidOperationException(
                $"Journal provider '{Options.JournalProviderKey}' is not registered. " +
                $"Available: {string.Join(", ", available)}");
        }

        // Validate cache provider key (if set)
        if (!string.IsNullOrEmpty(Options.CacheProviderKey) && 
            !ProviderRegistry.Instance.IsRegistered<IPageCache>(Options.CacheProviderKey))
        {
            var available = ProviderRegistry.Instance.GetRegisteredKeys<IPageCache>();
            throw new InvalidOperationException(
                $"Cache provider '{Options.CacheProviderKey}' is not registered. " +
                $"Available: {string.Join(", ", available)}");
        }
    }

    private void ValidateStorageConfigured()
    {
        // Custom store doesn't need storage
        if (Options.CustomStore != null)
            return;

        bool hasStorage = Options.CustomStorage != null ||
                          Options.UseMemoryStorage ||
                          !string.IsNullOrEmpty(Options.FilePath) ||
                          !string.IsNullOrEmpty(Options.LsmDirectory);

        if (!hasStorage)
        {
            if (Options.UseLsmTree)
            {
                throw new InvalidOperationException(
                    "LSM-Tree requires a directory path. Use WithFilePath(path) or WithLsmTree(directory).");
            }
            throw new InvalidOperationException(
                "Storage not configured. Use WithFilePath(path), WithMemoryStorage(), or WithStorage(storage).");
        }

        if (Options.UseLsmTree &&
            string.IsNullOrEmpty(Options.LsmDirectory) &&
            string.IsNullOrEmpty(Options.FilePath))
        {
            throw new InvalidOperationException(
                "LSM-Tree requires a directory path. Use WithFilePath(path) or WithLsmTree(directory).");
        }
    }

    private void ValidatePageSize()
    {
        if (!IsPowerOfTwo(Options.PageSize))
        {
            throw new InvalidOperationException(
                $"Page size must be a power of 2. Got {Options.PageSize}.");
        }
    }

    private void ValidateEncryptionSalt()
    {
        if (Options.HasEncryption && Options.EncryptionParameters.Get<byte[]>("salt") == null)
        {
            throw new InvalidOperationException(
                "Encryption salt is required when encryption is enabled. " +
                "Use WithEncryption(password) or WithAesEncryption(key, salt) to configure encryption with salt.");
        }
    }

    private void ValidateSyncBuildAllowed()
    {
        if (Options.CustomStorage is IAsyncOnlyStorage asyncOnly && asyncOnly.RequiresAsyncOperations)
        {
            throw new InvalidOperationException(
                $"The configured storage ({Options.CustomStorage.ProviderKey}) requires asynchronous operations. " +
                "Use BuildAsync() instead of Build().");
        }
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    #endregion

    #region Store Building

    private IKeyValueStore BuildStoreInternal()
    {
        // Use custom store directly
        if (Options.CustomStore != null)
            return Options.CustomStore;

        // Build LSM-Tree
        if (Options.UseLsmTree)
            return BuildLsmStore();

        // Build via provider registry
        return BuildStoreFromRegistry();
    }

    private IKeyValueStore BuildStoreFromRegistry()
    {
        var cryptoProvider = BuildCryptoProvider();
        var storage = BuildStorage(cryptoProvider);
        var metadata = BuildProviderMetadata();

        // Prepare parameters
        var parameters = new ProviderParameters();
        
        // Copy user parameters
        foreach (var (key, value) in Options.StoreParameters.GetAll())
        {
            parameters.Set(key, value);
        }

        // Set defaults if not provided
        if (!parameters.Has("storage"))
            parameters.Set("storage", storage);
        if (!parameters.Has("cacheSize"))
            parameters.Set("cacheSize", Options.CacheSize);
        if (!parameters.Has("ownsStorage"))
            parameters.Set("ownsStorage", true);
        if (!parameters.Has("providerMetadata"))
            parameters.Set("providerMetadata", metadata);

        return ProviderRegistry.Instance.Create<IKeyValueStore>(Options.StoreProviderKey, parameters);
    }

    private IKeyValueStore BuildLsmStore()
    {
        var cryptoProvider = BuildCryptoProvider();
        var directory = Options.LsmDirectory ?? Options.FilePath!;
        var lsmOptions = Options.StoreParameters.Get<LsmOptions>("options") ?? new LsmOptions();

        if (cryptoProvider != null)
        {
            var salt = GetEncryptionSalt();
            lsmOptions.Encryptor = new EncryptorBlock(cryptoProvider, salt);
        }

        return new StoreLsm(directory, lsmOptions);
    }

    private async ValueTask<IKeyValueStore> BuildStoreInternalAsync(CancellationToken cancellationToken)
    {
        // Use custom store directly
        if (Options.CustomStore != null)
            return Options.CustomStore;

        // LSM is sync
        if (Options.UseLsmTree)
            return BuildLsmStore();

        // BTree with async init
        var cryptoProvider = BuildCryptoProvider();
        var storage = BuildStorage(cryptoProvider);
        var metadata = BuildProviderMetadata();

        if (storage is IAsyncInitializable asyncInitializable)
        {
            await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }

        return await StoreBTree.CreateAsync(storage, Options.CacheSize, ownsStorage: true, metadata, cancellationToken)
            .ConfigureAwait(false);
    }

    private IStorage BuildStorage(ICryptoProvider? cryptoProvider = null)
    {
        if (Options.CustomStorage != null)
            return Options.CustomStorage;

        int storagePageSize = CalculateStoragePageSize(cryptoProvider);
        var baseStorage = CreateBaseStorage(storagePageSize);

        if (cryptoProvider != null)
        {
            var salt = GetEncryptionSalt();
            var encryptor = new EncryptorPage(cryptoProvider, salt);
            return new StorageEncrypted(baseStorage, encryptor);
        }

        return baseStorage;
    }

    private int CalculateStoragePageSize(ICryptoProvider? cryptoProvider)
    {
        if (cryptoProvider == null)
            return Options.PageSize;

        return Options.PageSize + cryptoProvider.Overhead;
    }

    private IStorage CreateBaseStorage(int pageSize)
    {
        if (Options.UseMemoryStorage)
            return new StorageMemory(pageSize);

        if (!string.IsNullOrEmpty(Options.FilePath))
            return new StorageFile(Options.FilePath, pageSize);

        throw new InvalidOperationException("Storage not configured.");
    }

    private ICryptoProvider? BuildCryptoProvider()
    {
        // Use custom provider
        if (Options.CustomCryptoProvider != null)
            return Options.CustomCryptoProvider;

        // Use provider registry
        if (!string.IsNullOrEmpty(Options.EncryptionProviderKey))
        {
            return ProviderRegistry.Instance.Create<ICryptoProvider>(
                Options.EncryptionProviderKey, 
                Options.EncryptionParameters);
        }

        return null;
    }

    private byte[] GetEncryptionSalt()
    {
        return Options.EncryptionParameters.Get<byte[]>("salt") 
            ?? throw new InvalidOperationException("Encryption salt is required.");
    }

    private ITransactionJournal? BuildJournal()
    {
        // Use custom journal
        if (Options.CustomJournal != null)
            return Options.CustomJournal;

        // Use provider registry
        if (!string.IsNullOrEmpty(Options.JournalProviderKey))
        {
            var parameters = new ProviderParameters();
            
            // Copy user parameters
            foreach (var (key, value) in Options.JournalParameters.GetAll())
            {
                parameters.Set(key, value);
            }

            // Set defaults if not provided
            if (!parameters.Has("filePath") && !parameters.Has("walPath"))
            {
                var basePath = Options.FilePath ?? Options.LsmDirectory;
                if (!string.IsNullOrEmpty(basePath))
                {
                    var journalPath = Path.Combine(
                        Path.GetDirectoryName(basePath) ?? ".",
                        Path.GetFileNameWithoutExtension(basePath) + ".journal");
                    parameters.Set("filePath", journalPath);
                    parameters.Set("walPath", journalPath);
                }
            }

            if (!parameters.Has("pageSize"))
                parameters.Set("pageSize", Options.PageSize);

            return ProviderRegistry.Instance.Create<ITransactionJournal>(Options.JournalProviderKey, parameters);
        }

        return null;
    }

    private ProviderMetadata BuildProviderMetadata()
    {
        var features = ProviderFeatures.None;

        if (Options.HasEncryption)
            features |= ProviderFeatures.Encryption;

        if (Options.EnableTransactions)
            features |= ProviderFeatures.Transactions;

        if (Options.EnableFileLocking)
            features |= ProviderFeatures.FileLocking;

        if (Options.EnableMvcc)
            features |= ProviderFeatures.Mvcc;

        return new ProviderMetadata
        {
            Features = features,
            StoreProviderKey = Options.EffectiveStoreProviderKey,
            EncryptionProviderKey = Options.CustomCryptoProvider?.ProviderKey ?? Options.EncryptionProviderKey ?? "",
            CacheProviderKey = Options.CacheProviderKey ?? PageCacheShardedClock.PROVIDER_KEY,
            JournalProviderKey = Options.CustomJournal?.ProviderKey ?? Options.JournalProviderKey ?? ""
        };
    }

    #endregion

    #region Transaction Building

    private ITransactionalStore BuildTransactionalStoreInternal(IKeyValueStore store)
    {
        var lockManager = Options.EnableFileLocking
            ? new LockManager(Options.LockTimeout)
            : null;

        var journal = BuildJournal();

        if (Options.EnableMvcc)
        {
            return new MvccTransactionalStore(
                store,
                lockManager,
                Options.DefaultIsolationLevel,
                ownsStore: true);
        }

        return new TransactionalStore(
            store,
            journal,
            lockManager,
            ownsStore: true);
    }

    #endregion

    #region Index Building

    private IIndexManager BuildIndexManagerInternal()
    {
        if (Options.SecondaryIndexFactory != null)
            return new IndexManager(Options.SecondaryIndexFactory);

        var factory = BuildDefaultIndexFactory();
        return new IndexManager(factory);
    }

    private ISecondaryIndexFactory BuildDefaultIndexFactory()
    {
        // Custom store - use in-memory indexes
        if (Options.CustomStore != null)
            return CreateInMemoryIndexFactory();

        var baseDirectory = GetIndexBaseDirectory();

        // Memory storage or no directory - use in-memory
        if (Options.UseMemoryStorage || baseDirectory == null)
            return CreateInMemoryIndexFactory();

        if (Options.UseLsmTree)
            return CreateLsmIndexFactory(baseDirectory);

        return CreateBTreeIndexFactory(baseDirectory);
    }

    private string? GetIndexBaseDirectory()
    {
        if (!string.IsNullOrEmpty(Options.IndexDirectory))
            return Options.IndexDirectory;

        // For LSM-Tree, use directory + _indexes
        if (!string.IsNullOrEmpty(Options.LsmDirectory))
            return Path.Combine(Options.LsmDirectory, "_indexes");

        // For BTree file, create index directory based on database filename
        // e.g., "data.db" -> "data.db_indexes" (sibling to database file)
        if (!string.IsNullOrEmpty(Options.FilePath))
        {
            var directory = Path.GetDirectoryName(Options.FilePath);
            var filename = Path.GetFileName(Options.FilePath);
            
            // Create index directory named after the database file
            // e.g., /tmp/mydb.db -> /tmp/mydb.db_indexes/
            return directory != null 
                ? Path.Combine(directory, filename + "_indexes")
                : filename + "_indexes";
        }

        return null;
    }

    private static ISecondaryIndexFactory CreateInMemoryIndexFactory()
    {
        return new SecondaryIndexFactoryKeyValueStore(
            _ => new StoreInMemory(),
            StoreInMemory.PROVIDER_KEY);
    }

    private ISecondaryIndexFactory CreateLsmIndexFactory(string baseDirectory)
    {
        var cryptoProvider = BuildCryptoProvider();
        var encryptionSalt = Options.EncryptionParameters.Get<byte[]>("salt");

        return new SecondaryIndexFactoryKeyValueStore(
            indexName =>
            {
                var indexPath = Path.Combine(baseDirectory, indexName);
                Directory.CreateDirectory(indexPath);

                var lsmOptions = new LsmOptions();
                if (cryptoProvider != null && encryptionSalt != null)
                {
                    lsmOptions.Encryptor = new EncryptorBlock(cryptoProvider.Clone(), encryptionSalt);
                }

                return new StoreLsm(indexPath, lsmOptions);
            },
            StoreLsm.PROVIDER_KEY);
    }

    private ISecondaryIndexFactory CreateBTreeIndexFactory(string baseDirectory)
    {
        var pageSize = Options.PageSize;
        var cacheSize = Options.CacheSize;
        var cryptoProvider = BuildCryptoProvider();
        var encryptionSalt = Options.EncryptionParameters.Get<byte[]>("salt");

        return new SecondaryIndexFactoryKeyValueStore(
            indexName =>
            {
                Directory.CreateDirectory(baseDirectory);
                var indexPath = Path.Combine(baseDirectory, $"{indexName}.idx");

                IStorage storage;
                int storagePageSize = pageSize;

                if (cryptoProvider != null && encryptionSalt != null)
                {
                    storagePageSize = pageSize + cryptoProvider.Overhead;
                    var baseStorage = new StorageFile(indexPath, storagePageSize);
                    var encryptor = new EncryptorPage(cryptoProvider.Clone(), encryptionSalt);
                    storage = new StorageEncrypted(baseStorage, encryptor);
                }
                else
                {
                    storage = new StorageFile(indexPath, storagePageSize);
                }

                return new StoreBTree(storage, cacheSize / 4, ownsStorage: true);
            },
            StoreBTree.PROVIDER_KEY);
    }

    #endregion
}
