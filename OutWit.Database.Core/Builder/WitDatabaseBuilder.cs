using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Encryption;
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
        
        if (Options.EnableTransactions)
        {
            var transactionalStore = BuildTransactionalStoreInternal(store);
            return new WitDatabase(transactionalStore, disposeStore: true);
        }
        
        return new WitDatabase(store, disposeStore: true);
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

    private IKeyValueStore BuildStoreInternal()
    {
        // Use custom store if provided
        if (Options.KeyValueStore != null)
            return Options.KeyValueStore;

        // Build LSM-Tree store
        if (Options.UseLsmTree)
        {
            var directory = Options.LsmDirectory ?? Options.FilePath!;
            
            var lsmOptions = Options.LsmOptions ?? new LsmOptions();
            
            // Add encryption to LSM options if configured
            if (Options.CryptoProvider != null)
            {
                lsmOptions.Encryptor = new BlockEncryptor(Options.CryptoProvider, Options.EncryptionSalt);
            }
            
            return new LsmTreeStore(directory, lsmOptions);
        }

        // Build BTree store - use constructor that owns storage
        var storage = BuildStorage();
        return new BTreeStore(storage, Options.CacheSize, ownsStorage: true);
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
            baseStorage = new MemoryStorage(storagePageSize);
        }
        else if (!string.IsNullOrEmpty(Options.FilePath))
        {
            baseStorage = new FileStorage(Options.FilePath, storagePageSize);
        }
        else
        {
            throw new InvalidOperationException("Storage not configured. Use WithFilePath() or WithMemoryStorage().");
        }

        // Wrap with encryption if configured
        if (Options.CryptoProvider != null)
        {
            var encryptor = new PageEncryptor(Options.CryptoProvider, Options.EncryptionSalt);
            return new EncryptedStorage(baseStorage, encryptor);
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

    #endregion
}
