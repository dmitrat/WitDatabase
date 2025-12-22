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
    public WitDatabase Build()
    {
        ValidateConfiguration();
        
        var store = BuildStoreInternal();
        var indexManager = BuildIndexManagerInternal();
        
        if (Options.EnableTransactions)
        {
            var transactionalStore = BuildTransactionalStoreInternal(store);
            return new WitDatabase(transactionalStore, indexManager, disposeStore: true);
        }
        
        return new WitDatabase(store, indexManager, disposeStore: true);
    }

    /// <summary>
    /// Builds just the key-value store without transaction wrapper.
    /// </summary>
    public IKeyValueStore BuildStore()
    {
        ValidateConfiguration();
        return BuildStoreInternal();
    }

    /// <summary>
    /// Builds a transactional store.
    /// </summary>
    public ITransactionalStore BuildTransactionalStore()
    {
        ValidateConfiguration();
        var store = BuildStoreInternal();
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

        // Determine the base directory for indexes
        string? baseDirectory = Options.IndexDirectory 
            ?? Options.LsmDirectory 
            ?? (Options.FilePath != null ? Path.GetDirectoryName(Options.FilePath) : null);

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
            var indexDir = Path.Combine(baseDirectory, "_indexes");
            
            // Capture encryption settings for lambda (create new instances per index)
            var cryptoProvider = Options.CryptoProvider;
            var encryptionSalt = Options.EncryptionSalt;
            
            return new SecondaryIndexFactoryKeyValueStore(
                indexName =>
                {
                    var indexPath = Path.Combine(indexDir, indexName);
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
        var btreeIndexDir = Path.Combine(baseDirectory, "_indexes");
        
        // Capture settings for lambda
        var pageSize = Options.PageSize;
        var cacheSize = Options.CacheSize;
        var btreeCryptoProvider = Options.CryptoProvider;
        var btreeEncryptionSalt = Options.EncryptionSalt;
        
        return new SecondaryIndexFactoryKeyValueStore(
            indexName =>
            {
                Directory.CreateDirectory(btreeIndexDir);
                var indexPath = Path.Combine(btreeIndexDir, $"{indexName}.idx");
                
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
