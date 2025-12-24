using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Managers;
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
    /// External packages can subscribe to perform additional validation.
    /// </summary>
    /// <remarks>
    /// Subscribers should throw <see cref="InvalidOperationException"/> to fail validation.
    /// This allows extension packages (e.g., IndexedDb) to validate their specific requirements
    /// without modifying the core builder.
    /// </remarks>
    public event Action<WitDatabaseBuilderOptions>? OnValidating;

    /// <summary>
    /// Event fired after the store is built but before creating the database.
    /// Can be used for post-build configuration or logging.
    /// </summary>
    public event Action<IKeyValueStore>? OnStoreBuilt;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the configuration options. Can be modified by extension methods.
    /// </summary>
    public WitDatabaseBuilderOptions Options { get; } = new();

    #endregion

    #region Build

    /// <summary>
    /// Builds the database with the configured options.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the configured storage requires async operations (e.g., IndexedDB).
    /// Use <see cref="BuildAsync"/> instead.
    /// </exception>
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
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An initialized WitDatabase.</returns>
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
    /// <exception cref="InvalidOperationException">
    /// Thrown if the configured storage requires async operations.
    /// Use <see cref="BuildStoreAsync"/> instead.
    /// </exception>
    public IKeyValueStore BuildStore()
    {
        ValidateConfiguration();
        ValidateSyncBuildAllowed();
        return BuildStoreInternal();
    }

    /// <summary>
    /// Builds just the key-value store asynchronously without transaction wrapper.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask<IKeyValueStore> BuildStoreAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        return BuildStoreInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a transactional store.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the configured storage requires async operations.
    /// Use <see cref="BuildTransactionalStoreAsync"/> instead.
    /// </exception>
    public ITransactionalStore BuildTransactionalStore()
    {
        ValidateConfiguration();
        ValidateSyncBuildAllowed();
        var store = BuildStoreInternal();
        return BuildTransactionalStoreInternal(store);
    }

    /// <summary>
    /// Builds a transactional store asynchronously.
    /// Use this in environments where synchronous I/O is not available (e.g., Blazor WASM).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
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
        // Validate incompatible combinations FIRST (before checking if storage is configured)
        if (Options.UseLsmTree && Options.Storage != null)
        {
            throw new InvalidOperationException(
                "LSM-Tree uses directory-based storage and cannot use WithStorage(). " +
                "Use WithFilePath(directory) instead, or use BTree with WithStorage().");
        }

        // Validate custom store doesn't conflict with other settings
        if (Options.KeyValueStore != null)
        {
            if (Options.CryptoProvider != null)
            {
                throw new InvalidOperationException(
                    "Cannot use WithAesEncryption() or WithEncryption() with WithStore(). " +
                    "Configure encryption in your custom store implementation.");
            }
            if (Options.Storage != null)
            {
                throw new InvalidOperationException(
                    "Cannot use WithStorage() with WithStore(). Choose one or the other.");
            }
        }

        // Validate storage is configured
        if (Options.Storage == null && 
            Options.KeyValueStore == null && 
            !Options.UseMemoryStorage && 
            string.IsNullOrEmpty(Options.FilePath) &&
            string.IsNullOrEmpty(Options.LsmDirectory))
        {
            if (Options.UseLsmTree)
            {
                throw new InvalidOperationException(
                    "LSM-Tree requires a directory path. Use WithFilePath(path) or WithLsmTree(directory).");
            }
            throw new InvalidOperationException(
                "Storage not configured. Use WithFilePath(path), WithMemoryStorage(), or WithStorage(storage).");
        }

        // Validate LSM-Tree has directory
        if (Options.UseLsmTree && 
            Options.KeyValueStore == null &&
            string.IsNullOrEmpty(Options.LsmDirectory) && 
            string.IsNullOrEmpty(Options.FilePath))
        {
            throw new InvalidOperationException(
                "LSM-Tree requires a directory path. Use WithFilePath(path) or WithLsmTree(directory).");
        }

        // Validate page size is power of 2
        if (!IsPowerOfTwo(Options.PageSize))
        {
            throw new InvalidOperationException(
                $"Page size must be a power of 2. Got {Options.PageSize}.");
        }

        // Fire validation event for external validators
        OnValidating?.Invoke(Options);
    }

    private void ValidateSyncBuildAllowed()
    {
        // Check if storage requires async-only operations
        if (Options.Storage is IAsyncOnlyStorage asyncOnly && asyncOnly.RequiresAsyncOperations)
        {
            throw new InvalidOperationException(
                $"The configured storage ({Options.Storage.ProviderKey}) requires asynchronous operations. " +
                "Use BuildAsync() instead of Build(). " +
                "This is required for browser-based storage like IndexedDB in Blazor WASM.");
        }
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    #endregion

    #region Internal Build Methods

    private ProviderMetadata BuildProviderMetadata()
    {
        var features = ProviderFeatures.None;
        
        if (Options.CryptoProvider != null)
            features |= ProviderFeatures.Encryption;
        
        if (Options.EnableTransactions)
            features |= ProviderFeatures.Transactions;
        
        if (Options.EnableFileLocking)
            features |= ProviderFeatures.FileLocking;

        return new ProviderMetadata
        {
            Features = features,
            StoreProviderKey = Options.UseLsmTree ? StoreLsm.PROVIDER_KEY : StoreBTree.PROVIDER_KEY,
            EncryptionProviderKey = Options.CryptoProvider?.ProviderKey ?? "",
            CacheProviderKey = "clock", // Default cache
            JournalProviderKey = Options.TransactionJournal?.ProviderKey ?? ""
        };
    }

    private IKeyValueStore BuildStoreInternal()
    {
        // Use custom store if provided
        if (Options.KeyValueStore != null)
            return Options.KeyValueStore;

        // Build provider metadata for new databases
        var metadata = BuildProviderMetadata();

        // Build LSM-Tree store
        if (Options.UseLsmTree)
        {
            var directory = Options.LsmDirectory ?? Options.FilePath!;
            
            var lsmOptions = Options.LsmOptions ?? new LsmOptions();
            
            // Add encryption to LSM options if configured
            if (Options.CryptoProvider != null)
            {
                lsmOptions.Encryptor = new EncryptorBlock(Options.CryptoProvider, Options.EncryptionSalt);
            }
            
            return new StoreLsm(directory, lsmOptions);
        }

        // Build BTree store with provider metadata
        var storage = BuildStorage();
        return new StoreBTree(storage, Options.CacheSize, ownsStorage: true, metadata);
    }

    private async ValueTask<IKeyValueStore> BuildStoreInternalAsync(CancellationToken cancellationToken)
    {
        // Use custom store if provided
        if (Options.KeyValueStore != null)
            return Options.KeyValueStore;

        // Build provider metadata for new databases
        var metadata = BuildProviderMetadata();

        // Build LSM-Tree store (LSM is inherently file-based, sync is OK)
        if (Options.UseLsmTree)
        {
            var directory = Options.LsmDirectory ?? Options.FilePath!;
            
            var lsmOptions = Options.LsmOptions ?? new LsmOptions();
            
            // Add encryption to LSM options if configured
            if (Options.CryptoProvider != null)
            {
                lsmOptions.Encryptor = new EncryptorBlock(Options.CryptoProvider, Options.EncryptionSalt);
            }
            
            return new StoreLsm(directory, lsmOptions);
        }

        // Build BTree store with async initialization
        var storage = BuildStorage();
        
        // Initialize storage if it supports async initialization (e.g., IndexedDB)
        if (storage is IAsyncInitializable asyncInitializable)
        {
            await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        
        return await StoreBTree.CreateAsync(storage, Options.CacheSize, ownsStorage: true, metadata, cancellationToken)
            .ConfigureAwait(false);
    }

    private IStorage BuildStorage()
    {
        // Use custom storage if provided
        if (Options.Storage != null)
            return Options.Storage;

        IStorage baseStorage;
        
        // Calculate actual storage page size (including encryption overhead if needed)
        int storagePageSize = Options.PageSize;
        if (Options.CryptoProvider != null)
        {
            // PageEncryptor adds overhead: nonce + tag
            var overhead = Options.CryptoProvider.Overhead;
            storagePageSize = Options.PageSize + overhead;
        }
        
        if (Options.UseMemoryStorage)
        {
            baseStorage = new StorageMemory(storagePageSize);
        }
        else if (!string.IsNullOrEmpty(Options.FilePath))
        {
            baseStorage = new StorageFile(Options.FilePath, storagePageSize);
        }
        else
        {
            throw new InvalidOperationException("Storage not configured. Use WithFilePath() or WithMemoryStorage().");
        }

        // Wrap with encryption if configured
        if (Options.CryptoProvider != null)
        {
            var encryptor = new EncryptorPage(Options.CryptoProvider, Options.EncryptionSalt);
            return new StorageEncrypted(baseStorage, encryptor);
        }

        return baseStorage;
    }

    private ITransactionalStore BuildTransactionalStoreInternal(IKeyValueStore store)
    {
        LockManager? lockManager = null;
        
        if (Options.EnableFileLocking)
        {
            lockManager = new LockManager(Options.LockTimeout);
        }

        // Use MVCC transactional store when enabled
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
            Options.TransactionJournal, 
            lockManager,
            ownsStore: true);
    }

    private IIndexManager BuildIndexManagerInternal()
    {
        // Use custom factory if provided
        if (Options.SecondaryIndexFactory != null)
        {
            return new IndexManager(Options.SecondaryIndexFactory);
        }

        // Build factory based on storage engine
        var factory = BuildDefaultIndexFactory();
        return new IndexManager(factory);
    }

    private ISecondaryIndexFactory BuildDefaultIndexFactory()
    {
        // If custom store is provided without custom index factory,
        // use in-memory indexes (safest default)
        if (Options.KeyValueStore != null)
        {
            return new SecondaryIndexFactoryKeyValueStore(
                indexName => new StoreInMemory(),
                "inmemory"
            );
        }

        // Determine if user explicitly specified index directory
        bool hasExplicitIndexDir = !string.IsNullOrEmpty(Options.IndexDirectory);
        
        // Determine the base directory for indexes
        string? baseDirectory;
        if (hasExplicitIndexDir)
        {
            // User explicitly specified - use as-is
            baseDirectory = Options.IndexDirectory;
        }
        else
        {
            // Auto-derive from main storage path
            var mainDir = Options.LsmDirectory 
                ?? (Options.FilePath != null ? Path.GetDirectoryName(Options.FilePath) : null);
            
            // Add _indexes subdirectory for auto-derived paths
            baseDirectory = mainDir != null ? Path.Combine(mainDir, "_indexes") : null;
        }

        // For memory storage, use in-memory indexes
        if (Options.UseMemoryStorage || baseDirectory == null)
        {
            return new SecondaryIndexFactoryKeyValueStore(
                indexName => new StoreInMemory(),
                "inmemory"
            );
        }

        // For LSM-Tree, use LSM-based indexes
        if (Options.UseLsmTree)
        {
            var indexDir = baseDirectory;
            
            // Capture encryption settings for lambda (create new instances per index)
            var cryptoProvider = Options.CryptoProvider;
            var encryptionSalt = Options.EncryptionSalt;
            
            return new SecondaryIndexFactoryKeyValueStore(
                indexName =>
                {
                    var indexPath = Path.Combine(indexDir!, indexName);
                    Directory.CreateDirectory(indexPath);
                    
                    var lsmOptions = new LsmOptions();
                    if (cryptoProvider != null)
                    {
                        // Create new encryptor instance for each index
                        lsmOptions.Encryptor = new EncryptorBlock(cryptoProvider.Clone(), encryptionSalt);
                    }
                    
                    return new StoreLsm(indexPath, lsmOptions);
                },
                StoreLsm.PROVIDER_KEY
            );
        }

        // For BTree, use BTree-based indexes (each index gets its own file)
        var btreeIndexDir = baseDirectory;
        
        // Capture settings for lambda
        var pageSize = Options.PageSize;
        var cacheSize = Options.CacheSize;
        var btreeCryptoProvider = Options.CryptoProvider;
        var btreeEncryptionSalt = Options.EncryptionSalt;
        
        return new SecondaryIndexFactoryKeyValueStore(
            indexName =>
            {
                Directory.CreateDirectory(btreeIndexDir!);
                var indexPath = Path.Combine(btreeIndexDir!, $"{indexName}.idx");
                
                IStorage storage;
                int storagePageSize = pageSize;
                
                if (btreeCryptoProvider != null)
                {
                    var overhead = btreeCryptoProvider.Overhead;
                    storagePageSize = pageSize + overhead;
                    var baseStorage = new StorageFile(indexPath, storagePageSize);
                    // Create new crypto provider instance for each index to avoid shared disposal
                    var encryptor = new EncryptorPage(btreeCryptoProvider.Clone(), btreeEncryptionSalt);
                    storage = new StorageEncrypted(baseStorage, encryptor);
                }
                else
                {
                    storage = new StorageFile(indexPath, storagePageSize);
                }
                
                return new StoreBTree(storage, cacheSize / 4, ownsStorage: true);
            },
            StoreBTree.PROVIDER_KEY
        );
    }

    #endregion
}
