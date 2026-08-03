using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Utils;

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

        // Taken BEFORE any database file is opened, so that a second engine is refused with
        // DatabaseAlreadyOpenException rather than with whichever raw IOException a share-mode
        // collision happens to produce first. Released by WitDatabase.Dispose.
        var databaseLock = AcquireExclusiveLock();

        try
        {
            var store = BuildStoreInternal();
            OnStoreBuilt?.Invoke(store);

            var indexManager = BuildIndexManagerInternal();

            if (Options.EnableTransactions)
            {
                var transactionalStore = BuildTransactionalStoreInternal(store);
                return new WitDatabase(transactionalStore, indexManager, disposeStore: true, databaseLock);
            }

            return new WitDatabase(store, indexManager, disposeStore: true, databaseLock);
        }
        catch
        {
            // A Build that fails half way must not leave the database locked - nothing would ever
            // release it. ProbeRefusedOpenLeavesNothingBehindTest is the test for this shape.
            databaseLock?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds the database asynchronously.
    /// </summary>
    public async ValueTask<WitDatabase> BuildAsync(CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();

        var databaseLock = AcquireExclusiveLock();

        try
        {
            var store = await BuildStoreInternalAsync(cancellationToken).ConfigureAwait(false);
            OnStoreBuilt?.Invoke(store);

            var indexManager = BuildIndexManagerInternal();

            if (Options.EnableTransactions)
            {
                var transactionalStore = BuildTransactionalStoreInternal(store);
                return await WitDatabase.CreateAsync(transactionalStore, indexManager, disposeStore: true,
                        databaseLock, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await WitDatabase.CreateAsync(store, indexManager, disposeStore: true, databaseLock,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            databaseLock?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes the exclusive database lock that enforces one engine per database.
    /// </summary>
    /// <remarks>
    /// The mechanism is a <c>.lock</c> sidecar opened with <see cref="FileShare.None"/>, which is
    /// exclusive on Windows and an exclusive <c>flock</c> on Unix - so unlike the share modes of the
    /// database's own files it behaves the same on both platforms, and it does not depend on which
    /// files a given configuration happens to create. Before 5.0.0 exclusivity was a side effect of
    /// those share modes, and an LSM database with the write-ahead log switched off had none at all.
    ///
    /// The operating system releases the handle when the owning process exits, so a process that dies
    /// without running <c>Dispose</c> does not leave the database permanently locked. That matters
    /// here: phase 4 established that a crash runs no cleanup.
    ///
    /// Returns null when there is nothing to lock - an in-memory database, or a caller-supplied store
    /// whose path this builder does not know.
    /// </remarks>
    private FileLock? AcquireExclusiveLock()
    {
        if (!Options.EnableFileLocking)
            return null;

        var databasePath = Options.FilePath ?? Options.LsmDirectory;

        if (string.IsNullOrEmpty(databasePath))
            return null;

        var fileLock = new FileLock(databasePath);

        // A bounded wait, not a stall and not a single attempt. One engine per database is a design
        // limit, so an engine that is really open is refused - but a HOST RESTART overlaps the outgoing
        // process with the incoming one, and refusing on the first attempt turned that window into a
        // startup crash. SQLite covers the same window with busy_timeout. Options.OpenTimeout is five
        // seconds by default and zero means one attempt, which is what this used to do.
        if (fileLock.TryAcquireExclusiveLock(Options.OpenTimeout))
            return fileLock;

        fileLock.Dispose();
        throw new DatabaseAlreadyOpenException(databasePath);
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

        var store = ProviderRegistry.Instance.Create<IKeyValueStore>(Options.StoreProviderKey, parameters);
        
        // Wrap with parallel store if enabled
        return WrapWithParallelIfNeeded(store);
    }

    private IKeyValueStore BuildLsmStore()
    {
        var cryptoProvider = BuildCryptoProvider();
        var directory = Options.LsmDirectory ?? Options.FilePath!;
        // Without FromParameters this was new LsmOptions(), so every LSM setting in a
        // connection string was dropped for the main store. See LsmOptions.FromParameters.
        var lsmOptions = Options.StoreParameters.Get<LsmOptions>("options")
                         ?? LsmOptions.FromParameters(Options.StoreParameters);

        if (cryptoProvider != null)
        {
            var salt = GetEncryptionSalt();
            lsmOptions.Encryptor = new EncryptorBlock(cryptoProvider, salt);
        }

        var store = new StoreLsm(directory, lsmOptions);
        
        // Wrap with parallel store if enabled
        return WrapWithParallelIfNeeded(store);
    }

    private IKeyValueStore WrapWithParallelIfNeeded(IKeyValueStore store)
    {
        var parallelMode = Options.StoreParameters.Get("parallelMode", ParallelMode.None);
        
        if (parallelMode == ParallelMode.None)
            return store;

        var parallelOptions = Options.StoreParameters.Get<ParallelModeOptions>("parallelOptions");
        if (parallelOptions == null)
        {
            parallelOptions = new ParallelModeOptions { Mode = parallelMode };
            
            // Apply maxWriters if set
            var maxWriters = Options.StoreParameters.Get<int?>("maxWriters");
            if (maxWriters.HasValue)
                parallelOptions.MaxWriters = maxWriters.Value;
        }

        // Use factory to create appropriate parallel store
        if (store is StoreLsm lsmStore)
        {
            var lsmOptions = new LsmParallelStoreOptions
            {
                MaxWriters = parallelOptions.MaxWriters,
                BufferSizeThreshold = parallelOptions.BufferSizeThreshold,
                TrackStatistics = parallelOptions.TrackStatistics
            };
            return new LsmParallelStore(lsmStore, lsmOptions, ownsStore: true);
        }

        if (store is StoreBTree btreeStore)
        {
            var concurrencyOptions = new Tree.BTreeConcurrencyOptions
            {
                TrackStatistics = parallelOptions.TrackStatistics
            };
            return new Tree.BTreeConcurrentStore(btreeStore, concurrencyOptions, ownsStore: true);
        }

        // For other stores, return as-is (no parallel wrapper available)
        return store;
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

        var store = await StoreBTree.CreateAsync(storage, Options.CacheSize, ownsStorage: true, metadata, cancellationToken)
            .ConfigureAwait(false);
        
        // Wrap with parallel store if enabled
        return WrapWithParallelIfNeeded(store);
    }

    private IStorage BuildStorage(ICryptoProvider? cryptoProvider = null)
    {
        if (Options.CustomStorage != null)
            return BuildCustomStorage(Options.CustomStorage, cryptoProvider);

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

    /// <summary>
    /// Applies encryption to a caller-supplied storage, or refuses the combination.
    /// </summary>
    /// <remarks>
    /// A custom storage - <c>WithStorage()</c>, and therefore <c>WithIndexedDbStorage()</c> too -
    /// used to be returned as-is, bypassing the encryptor entirely, while the header still recorded
    /// <c>ProviderFeatures.Encryption</c>. The database reported itself as encrypted and wrote
    /// plaintext. It is now wrapped like any other storage; when its pages cannot hold the per-page
    /// overhead the build fails rather than producing something unreadable.
    /// </remarks>
    private IStorage BuildCustomStorage(IStorage customStorage, ICryptoProvider? cryptoProvider)
    {
        if (cryptoProvider == null)
            return customStorage;

        var encryptor = new EncryptorPage(cryptoProvider, GetEncryptionSalt());

        // Encryption spends Overhead bytes of every physical page on the nonce and tag, so the
        // logical page size the rest of the engine sees is PageSize - Overhead - and that has to
        // remain a legal page size. The built-in storages are sized as PageSize + Overhead for
        // exactly this reason; a caller-supplied one has to be too.
        var logicalPageSize = customStorage.PageSize - encryptor.Overhead;

        if (logicalPageSize < DatabaseConstants.MIN_PAGE_SIZE ||
            logicalPageSize > DatabaseConstants.MAX_PAGE_SIZE ||
            (logicalPageSize & (logicalPageSize - 1)) != 0)
        {
            throw new InvalidOperationException(
                $"The storage supplied to WithStorage() has a page size of {customStorage.PageSize} " +
                $"bytes, which leaves {logicalPageSize} usable after the {encryptor.Overhead} bytes " +
                $"encryption needs per page - not a valid page size. Construct it with " +
                $"{Options.PageSize + encryptor.Overhead} bytes (a power of two plus the overhead), " +
                $"or build the database without encryption.");
        }

        return new StorageEncrypted(customStorage, encryptor);
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
        // ALWAYS a lock manager, whatever EnableFileLocking says. Until 5.0.0 this was
        // `Options.EnableFileLocking ? new LockManager(...) : null`, and both transactional stores
        // treat null as "no locking" - so `FileLocking=false`, which reads as "do not coordinate
        // across processes", silently removed the mutual exclusion between two threads writing the
        // same store. Measured: two writers inside the store at once, on two distinct threads.
        //
        // The two jobs are now separate. In-process write serialisation is not optional; what
        // EnableFileLocking controls is the exclusive database lock in AcquireExclusiveLock, which is
        // the cross-process guard the name was always describing.
        var lockManager = new LockManager(Options.LockTimeout);

        var journal = BuildJournal();

        if (Options.EnableMvcc)
        {
            return new MvccTransactionalStore(
                store,
                lockManager,
                Options.DefaultIsolationLevel,
                ownsStore: true)
            {
                SynchronousCommit = Options.SynchronousCommit
            };
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
        // Only use LsmDirectory if we're actually using LSM-Tree
        if (Options.UseLsmTree && !string.IsNullOrEmpty(Options.LsmDirectory))
            return Path.Combine(Options.LsmDirectory, "_indexes");

        // For BTree file, the index directory is a sibling of the database file
        // e.g., /tmp/mydb.db -> /tmp/mydb.db_indexes/. The rule lives in DatabaseFiles so that
        // deleting a database removes exactly what creating it produced.
        return DatabaseFiles.GetIndexDirectory(Options.FilePath);
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
                var safeIndexName = SanitizeIndexName(indexName);
                var indexPath = Path.Combine(baseDirectory, safeIndexName);
                
                // Ensure index directory exists - don't throw if already exists
                if (!Directory.Exists(indexPath))
                    Directory.CreateDirectory(indexPath);

                // The indexes follow the database's own configuration. They used to get
                // plain defaults, so a connection string tuning the LSM store tuned only
                // half of it and the indexes quietly did something else.
                var lsmOptions = LsmOptions.FromParameters(Options.StoreParameters);
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
                // Ensure index directory exists
                // Check both Directory.Exists and !File.Exists to handle case where
                // a file with same name exists (which would cause CreateDirectory to fail)
                if (!Directory.Exists(baseDirectory) && !File.Exists(baseDirectory))
                    Directory.CreateDirectory(baseDirectory);
                
                var safeIndexName = SanitizeIndexName(indexName);
                var indexPath = Path.Combine(baseDirectory, $"{safeIndexName}.idx");

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

                var indexStore = new StoreBTree(storage, cacheSize / 4, ownsStorage: true);

                // Serialised, and unconditionally. One index store is shared by every connection to
                // the database and by every thread inside a connection, and StoreBTree is the only
                // store this factory can hand out that has no locking of its own - StoreInMemory and
                // StoreLsm both lock internally. Left bare, two writers walk into the same leaf split
                // and rewrite it from two snapshots: measured, one of them throws out of
                // BTreeNode.CollectLeafEntries, and when it does not, entries are simply gone.
                // Not conditional on a parallel mode, because a second CONNECTION is enough - which
                // is the shape 5.0.0 exists for.
                return new Tree.BTreeConcurrentStore(indexStore, options: null, ownsStore: true);
            },
            Tree.BTreeConcurrentStore.PROVIDER_KEY);
    }

    /// <summary>
    /// Sanitizes an index name by removing characters that are invalid for file system paths.
    /// </summary>
    /// <param name="indexName">The original index name.</param>
    /// <returns>A sanitized index name safe for use in file paths.</returns>
    private static string SanitizeIndexName(string indexName)
    {
        // Remove quotes (EF Core often generates names like "IX_Table_Column")
        var sanitized = indexName.Trim('"', '\'', '[', ']', '`');
        
        // Replace any remaining invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        
        return sanitized;
    }

    #endregion
}
