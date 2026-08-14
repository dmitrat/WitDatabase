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

    #region Fields

    /// <summary>
    /// The data key of the database this build opened, once its crypto header has been read.
    /// </summary>
    /// <remarks>
    /// A database is a set of files - the database itself and one per secondary index - and they
    /// share a key. Each carries its own crypto header with its own random salt and its own nonce
    /// sequence, but only the database's header wraps a key, because deriving one per index would
    /// cost a full PBKDF2 per index on every open. Null until the database's own storage is built,
    /// which is always before an index store is asked for.
    /// </remarks>
    private byte[]? m_dataKey;

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
        // Before validation, so that the configuration the file supplies is validated too rather than
        // arriving behind the check.
        RestoreStoredConfiguration();

        ValidateConfiguration();
        ValidateSyncBuildAllowed();

        // Taken BEFORE any database file is opened, so that a second engine is refused with
        // DatabaseAlreadyOpenException rather than with whichever raw IOException a share-mode
        // collision happens to produce first. Released by WitDatabase.Dispose.
        var databaseLock = AcquireExclusiveLock();
        IKeyValueStore? store = null;

        try
        {
            store = BuildStoreInternal();
            ValidateStoredConfiguration(store);
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
            //
            // And the store goes with it. Everything from here on can throw - the stored-configuration
            // check below does so deliberately - and the store is already holding the data file open by
            // then, so leaving it would lock the database out of its own process. That is the third
            // shape of handle leak this phase has found, so the disposal is unconditional rather than
            // attached to any one failure.
            store?.Dispose();
            databaseLock?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds the database asynchronously.
    /// </summary>
    public async ValueTask<WitDatabase> BuildAsync(CancellationToken cancellationToken = default)
    {
        RestoreStoredConfiguration();

        ValidateConfiguration();

        var databaseLock = AcquireExclusiveLock();
        IKeyValueStore? store = null;

        try
        {
            store = await BuildStoreInternalAsync(cancellationToken).ConfigureAwait(false);
            ValidateStoredConfiguration(store);
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
            store?.Dispose();
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
        ValidateJournalIsReachable();

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

    /// <summary>
    /// Refuses a transaction journal that nothing in this configuration can use.
    /// </summary>
    /// <remarks>
    /// <c>TransactionalStore</c> is the only consumer of an <see cref="ITransactionJournal"/>: the MVCC
    /// store keeps its own versions, and with transactions off there is no transactional store at all.
    /// Until 12.0.0 the journal was built anyway and dropped - and a write-ahead journal opens its file
    /// in its constructor, so `Journal=wal` with the default `MVCC=true` leaked the handle and the
    /// database could not be opened a second time in the process. Refusing at open is the decision:
    /// a setting that cannot be honoured is an error, not a silence.
    /// </remarks>
    private void ValidateJournalIsReachable()
    {
        var hasJournal = Options.CustomJournal != null || !string.IsNullOrEmpty(Options.JournalProviderKey);

        if (!hasJournal)
            return;

        if (!Options.EnableTransactions)
        {
            throw new InvalidOperationException(
                "A transaction journal was configured and transactions are disabled, so nothing would " +
                "use it. Enable transactions, or drop the journal setting.");
        }

        if (Options.EnableMvcc)
        {
            throw new InvalidOperationException(
                "A transaction journal cannot be combined with MVCC: the MVCC store keeps its own " +
                "versions and takes no journal, so the setting would be accepted and ignored. Use " +
                "MVCC=false to get a journalled transactional store, or drop the journal setting.");
        }
    }

    /// <summary>
    /// Refuses a database that was written under a different transaction model from the one now asking
    /// to open it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>MVCC changes the key layout, and until 12.0.0 nothing said so.</b> A database created with the
    /// default configuration and opened with <c>MVCC=false</c> or <c>Transactions=false</c> opened
    /// without a word of complaint and then answered <c>Table 'X' not found</c> - in both directions.
    /// The rows were still there: the configuration that wrote them read them back afterwards. So it was
    /// invisibility rather than loss, right up to the moment the consumer did the obvious thing with an
    /// apparently empty database and created the schema on top of one that was intact. Measured across
    /// the 8x8 grid in <c>ConfigurationMismatchTests</c>.
    /// </para>
    /// <para>
    /// The header has recorded <see cref="ProviderFeatures.Mvcc"/> and
    /// <see cref="ProviderFeatures.Transactions"/> since the metadata section existed, so this needs no
    /// format change - only a comparison, and a way to read the metadata back off a built store, which
    /// is <see cref="IProviderMetadataSource"/>.
    /// </para>
    /// <para>
    /// <b>What is compared is the layout, not the keywords.</b> The MVCC store writes every value under
    /// a versioned key and nothing else does, so the question is whether the MVCC layer is there at all:
    /// transactions enabled <i>and</i> MVCC enabled. <c>MVCC=false</c> and <c>Transactions=false</c>
    /// produce the same layout as each other and read each other's databases correctly - measured, and
    /// the grid pins it - so refusing that pair would be refusing something that works.
    /// </para>
    /// <para>
    /// A file whose metadata section was never written carries an empty store provider key. That is
    /// indistinguishable from "created by a version that did not record it", so it is left alone: this
    /// check exists to stop a silent wrong answer, not to make old databases unopenable.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Fills in the settings the caller did not name from the ones the database recorded when it was
    /// created. Does nothing unless <see cref="WitDatabaseBuilderOptions.RestoreStoredConfiguration"/>
    /// is on, and never overwrites a setting the caller named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 12 measured what a reopen used to recover: nothing. A database created with
    /// <c>Cache=lru;CacheSize=64</c> and opened with <c>Data Source=</c> alone built a sharded clock
    /// with the default capacity and said nothing; fourteen settings behaved that way and five were
    /// refused. The file had a place to record most of them - <c>ProviderMetadata</c> declared the cache
    /// and journal keys with the comment "Not persisted" - and now it does.
    /// </para>
    /// <para>
    /// <b>The order matters.</b> The store comes first, because it decides which of the rest apply, and
    /// restoring it turns a path that is a directory into an LSM database rather than a file the
    /// operating system refuses to open. The transaction model comes before the journal, because a
    /// journal is only legal without MVCC - restoring one into an MVCC configuration would produce a
    /// combination the validator refuses, which is a worse answer than the one this replaces.
    /// </para>
    /// <para>
    /// <b>What is deliberately not restored:</b> <c>Synchronous Commit</c>, <c>FileLocking</c> and
    /// <c>Isolation Level</c>. See <see cref="WitDatabaseBuilderOptions.RestoreStoredConfiguration"/>.
    /// </para>
    /// </remarks>
    private void RestoreStoredConfiguration()
    {
        if (!Options.RestoreStoredConfiguration)
            return;

        // A caller who supplied the store or the storage is not asking a file what to build.
        if (Options.CustomStore != null || Options.CustomStorage != null)
            return;

        var path = Options.FilePath ?? Options.LsmDirectory;

        if (string.IsNullOrEmpty(path))
            return;

        if (StorageDetector.ReadStoredConfiguration(path) is not { } stored)
            return;

        var metadata = stored.Metadata;

        RestoreStore(stored, metadata, path);
        RefuseNamedTransactionModelMismatch(metadata);
        RestoreTransactionModel(metadata);

        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.PAGE_SIZE) && stored.PageSize > 0)
            Options.StoreParameters.Set("pageSize", (int)stored.PageSize);

        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.CACHE) &&
            !string.IsNullOrEmpty(metadata.CacheProviderKey))
        {
            Options.CacheProviderKey = metadata.CacheProviderKey;
        }

        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.CACHE_SIZE) && metadata.CacheSize > 0)
            Options.CacheParameters.Set("size", metadata.CacheSize);

        // Only meaningful without MVCC, and the model above has already been restored, so this asks
        // the configuration as it now stands rather than as it arrived.
        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.JOURNAL) &&
            !string.IsNullOrEmpty(metadata.JournalProviderKey) &&
            Options.EnableTransactions && !Options.EnableMvcc)
        {
            Options.JournalProviderKey = metadata.JournalProviderKey;
        }

        RestoreLsmOptions(stored);
    }

    private void RestoreStore(StoredConfiguration stored, ProviderMetadata metadata, string path)
    {
        if (Options.IsNamed(WitDatabaseBuilderOptions.Setting.STORE))
            return;

        var storedKey = stored.IsDirectory ? StoreLsm.PROVIDER_KEY : metadata.StoreProviderKey;

        if (string.IsNullOrEmpty(storedKey) || storedKey == Options.StoreProviderKey)
            return;

        Options.StoreProviderKey = storedKey;

        // The LSM store is given a directory and the B+Tree store a file, and a connection string that
        // never said Store= has only set the one. Without this, restoring the store key alone left the
        // LSM store looking for a directory parameter nobody had filled in.
        if (storedKey == StoreLsm.PROVIDER_KEY && !Options.StoreParameters.Has("directory"))
            Options.StoreParameters.Set("directory", path);
    }

    /// <summary>
    /// Refuses a transaction model the caller named that the database was not created with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ValidateStoredConfiguration"/> does this for the B+Tree store, from the metadata the
    /// built store exposes - and the LSM store exposes none, so that check has never applied to it. It
    /// did not show, because opening an LSM directory without <c>Store=lsm</c> failed in the operating
    /// system: a directory is not a file. Restoring the store from the sidecar removed that accident and
    /// left the real gap visible, which <c>ConfigurationMismatchTests</c> reported immediately as
    /// <c>OpensAndDataIsGone</c> for <c>lsm -&gt; MVCC=false</c>.
    /// </para>
    /// <para>
    /// So the same question is asked here, where the answer is available for both stores, and only for a
    /// model the caller actually named - one they did not name is restored rather than refused.
    /// </para>
    /// </remarks>
    private void RefuseNamedTransactionModelMismatch(ProviderMetadata stored)
    {
        if (string.IsNullOrEmpty(stored.StoreProviderKey))
            return;

        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.TRANSACTIONS) &&
            !Options.IsNamed(WitDatabaseBuilderOptions.Setting.MVCC))
        {
            return;
        }

        var storedHasMvcc = stored.HasTransactions && stored.HasMvcc;

        if (storedHasMvcc == (Options.EnableTransactions && Options.EnableMvcc))
            return;

        throw new ConfigurationMismatchException(BuildProviderMetadata(), stored, [TransactionModelMismatch(storedHasMvcc)]);
    }

    /// <summary>
    /// The one wording for a transaction-model mismatch, so the two places that can detect it cannot
    /// explain it differently.
    /// </summary>
    private static string TransactionModelMismatch(bool storedHasMvcc)
    {
        return storedHasMvcc
            ? "The database was created with MVCC and this configuration opens it without. The MVCC "
              + "store keeps every value under a versioned key, so a store opened without it finds none "
              + "of them and reports every table as missing - the data is intact and invisible. Open it "
              + "with MVCC=true, or create a new database."
            : "The database was created without MVCC and this configuration opens it with MVCC. The MVCC "
              + "store looks for values under versioned keys, so it finds none of the ones already "
              + "written and reports every table as missing - the data is intact and invisible. Open it "
              + "with MVCC=false, or create a new database.";
    }

    private void RestoreTransactionModel(ProviderMetadata metadata)
    {
        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.TRANSACTIONS))
            Options.EnableTransactions = metadata.HasTransactions;

        if (!Options.IsNamed(WitDatabaseBuilderOptions.Setting.MVCC))
            Options.TransactionParameters.Set("mvcc", metadata.HasTransactions && metadata.HasMvcc);
    }

    private void RestoreLsmOptions(StoredConfiguration stored)
    {
        if (stored.Lsm is not { } lsm || Options.StoreProviderKey != StoreLsm.PROVIDER_KEY)
            return;

        // Each is restored on its own, so naming one in a connection string does not discard the rest.
        // Every spelling LsmOptions.FromParameters accepts has to be checked before a value is put back:
        // a connection string writes MemTableSize and this would otherwise add memTableSize beside it and
        // leave which one wins to the order of a lookup.
        Restore(lsm.MemTableSizeLimit, "MemTableSize", "memTableSize", "MemTableSizeLimit");
        Restore(lsm.BlockCacheSizeBytes, "BlockCacheSize", "blockCacheSize", "BlockCacheSizeBytes");
        Restore(lsm.BlockSize, "BlockSize", "blockSize");
        Restore(lsm.Level0CompactionTrigger, "CompactionTrigger", "compactionTrigger", "Level0CompactionTrigger");
        Restore(lsm.EnableWal, "EnableWal", "enableWal");
        Restore(lsm.SyncWrites, "SyncWrites", "syncWrites");
        Restore(lsm.EnableBlockCache, "EnableBlockCache", "enableBlockCache");
        Restore(lsm.BackgroundCompaction, "BackgroundCompaction", "backgroundCompaction");

        void Restore(object value, params string[] names)
        {
            if (names.Any(Options.StoreParameters.Has))
                return;

            Options.StoreParameters.Set(names[0], value);
        }
    }

    private void ValidateStoredConfiguration(IKeyValueStore store)
    {
        if (store is not IProviderMetadataSource source || source.StoredMetadata is not { } stored)
            return;

        if (string.IsNullOrEmpty(stored.StoreProviderKey))
            return;

        var storedHasMvcc = stored.HasTransactions && stored.HasMvcc;
        var currentHasMvcc = Options.EnableTransactions && Options.EnableMvcc;

        if (storedHasMvcc == currentHasMvcc)
            return;

        // A caller who did not name the transaction model gets the one the database was created with,
        // rather than a refusal for a disagreement they never expressed. This seam is used as well as
        // the pre-read in RestoreStoredConfiguration because it is the only one that works for an
        // ENCRYPTED database: the header is inside the encrypted page, so nothing can be read from the
        // file before the store is built - and the transactional layer is built after this point, so
        // there is still time to choose.
        if (Options.RestoreStoredConfiguration &&
            !Options.IsNamed(WitDatabaseBuilderOptions.Setting.TRANSACTIONS) &&
            !Options.IsNamed(WitDatabaseBuilderOptions.Setting.MVCC))
        {
            Options.EnableTransactions = stored.HasTransactions;
            Options.TransactionParameters.Set("mvcc", storedHasMvcc);
            return;
        }

        throw new ConfigurationMismatchException(BuildProviderMetadata(), stored, [TransactionModelMismatch(storedHasMvcc)]);
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
        var metadata = BuildProviderMetadata();

        // Deferred, so that a store which does not want storage never opens a file. StoreInMemory's
        // factory ignores every parameter, and this used to hand it a StorageFile that was already open:
        // nothing owned it, nothing disposed it, and Store=inmemory with a file Data Source left the
        // handle held until finalization - so the database could not be opened a second time in the same
        // process. ProviderParameters unwraps a Lazy on read, so the factories are unchanged.
        var storage = new Lazy<IStorage>(() => BuildStorage(cryptoProvider));

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

        // Only when one was actually chosen, so the default path builds exactly what it built before.
        // Deferred like the storage: a store that wants neither opens nothing.
        if (!parameters.Has("cache") && (Options.CustomCache != null || !string.IsNullOrEmpty(Options.CacheProviderKey)))
            parameters.Set("cache", new Lazy<IPageCache>(() => BuildPageCache(storage.Value)!));

        if (!parameters.Has("cacheSize"))
            parameters.Set("cacheSize", Options.CacheSize);
        if (!parameters.Has("ownsStorage"))
            parameters.Set("ownsStorage", true);
        if (!parameters.Has("providerMetadata"))
            parameters.Set("providerMetadata", metadata);

        IKeyValueStore store;

        try
        {
            store = ProviderRegistry.Instance.Create<IKeyValueStore>(Options.StoreProviderKey, parameters);
        }
        catch
        {
            // The store took ownership of the storage only if it was built. When it throws - a wrong
            // password, a page size the file was not written with - the storage is already open and
            // nothing owns it, so the handle was held for the life of the process and the database
            // could not be opened again AT ALL: the next attempt, with the right password, met "the
            // process cannot access the file". Measured in ConfigurationMismatchTests.
            if (storage.IsValueCreated)
                storage.Value.Dispose();

            throw;
        }
        
        // Serialise the store if its own implementation does not
        return WrapForConcurrency(store);
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
            lsmOptions.Encryptor = BuildLsmEncryptor(directory, cryptoProvider);

        // Written before the store, and only when the directory does not already carry one: the sidecar
        // records what the database was CREATED with, so reopening it under other settings must not
        // rewrite it. Same rule as the database header, which is only filled in by InitializeNewDatabase.
        if (LsmDirectoryMetadata.Read(directory) is null)
            LsmDirectoryMetadata.Write(directory, BuildProviderMetadata(), StoredOptionsOf(lsmOptions));

        var store = new StoreLsm(directory, lsmOptions);

        // Serialise the store if its own implementation does not
        return WrapForConcurrency(store);
    }

    /// <summary>
    /// Applies the concurrency wrapper each store needs, and the write buffering the caller asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Serialising the B+Tree store is not a mode.</b> <see cref="StoreBTree"/> has no locking of any
    /// kind, and secondary index stores have been wrapped unconditionally since 6.0.0 because a second
    /// <i>connection</i> is enough to walk into a leaf split someone else is halfway through. The main
    /// store was left conditional on a parallel mode, and phase 11 measured what that left open: with
    /// <c>Transactions=false</c> and no mode, two writers inside one split threw and lost an entry in
    /// <b>five runs out of five</b>. With transactions the layer above happens to serialise it - which
    /// is a property of that layer, not a guarantee this one may lean on.
    /// </para>
    /// <para>
    /// Wrapping unconditionally costs a single thread nothing: five interleaved passes of 20,000
    /// put+get gave a median ratio of <b>1.001</b> wrapped against bare. Both measurements are in
    /// <c>MainStoreConcurrencyProbeTests</c>.
    /// </para>
    /// <para>
    /// So what is left of <c>Parallel Mode</c> is the LSM store's write buffering, which is a throughput
    /// choice rather than a safety one - <see cref="StoreLsm"/> locks internally and is safe without it.
    /// </para>
    /// </remarks>
    private IKeyValueStore WrapForConcurrency(IKeyValueStore store)
    {
        if (store is StoreBTree btreeStore)
            return new Tree.BTreeConcurrentStore(btreeStore, options: null, ownsStore: true);

        // Every other store this builder can produce locks internally - StoreInMemory and StoreLsm both
        // do - so there is nothing to add and nothing to choose.
        return store;
    }

    /// <summary>
    /// The asynchronous half of <see cref="BuildStoreInternal"/>, which must build the same store it
    /// does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Until 12.0.0 this method built a <see cref="StoreBTree"/> for every configuration that was
    /// not LSM.</b> `Store=inmemory` therefore opened the data file it exists in order not to touch, a
    /// third-party store registered in the provider registry was ignored outright, and `Cache=lru`
    /// selected a cache that was then never constructed - measured three ways in
    /// <c>SyncAndAsyncBuildAgreeTests</c>, which compares the two routes' object graphs.
    /// </para>
    /// <para>
    /// So everything except the B+Tree store is built where the synchronous route builds it, in the
    /// registry. The B+Tree store keeps a route of its own here for one reason: its page manager reads
    /// the header while it is being constructed, and a storage that can only work asynchronously
    /// (Blazor WASM) cannot serve that. What it must not do is read anything else differently, which is
    /// why the parameters below are taken from the same bag the registry factory reads.
    /// </para>
    /// </remarks>
    private async ValueTask<IKeyValueStore> BuildStoreInternalAsync(CancellationToken cancellationToken)
    {
        // Use custom store directly
        if (Options.CustomStore != null)
            return Options.CustomStore;

        // LSM is sync
        if (Options.UseLsmTree)
            return BuildLsmStore();

        if (!string.Equals(Options.EffectiveStoreProviderKey, StoreBTree.PROVIDER_KEY, StringComparison.OrdinalIgnoreCase))
            return BuildStoreFromRegistry();

        var cryptoProvider = BuildCryptoProvider();
        var metadata = BuildProviderMetadata();

        var supplied = Options.StoreParameters.Get<IStorage?>("storage", null);
        var storage = supplied ?? BuildStorage(cryptoProvider);

        try
        {
            if (storage is IAsyncInitializable asyncInitializable)
                await asyncInitializable.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var cacheSize = Options.StoreParameters.Get("cacheSize", Options.CacheSize);
            var ownsStorage = Options.StoreParameters.Get("ownsStorage", true);
            var cache = Options.StoreParameters.Get<IPageCache?>("cache", null) ?? BuildPageCache(storage);

            var store = cache != null
                ? await StoreBTree.CreateAsync(storage, cache, ownsStorage, metadata, cancellationToken)
                    .ConfigureAwait(false)
                : await StoreBTree.CreateAsync(storage, cacheSize, ownsStorage, metadata, cancellationToken)
                    .ConfigureAwait(false);

            // Serialise the store if its own implementation does not
            return WrapForConcurrency(store);
        }
        catch when (supplied == null)
        {
            // Only what this method opened. The same shape as the synchronous route's catch, and the
            // same reason: a store that never took ownership leaves the file held for the life of the
            // process, and the next attempt meets "the process cannot access the file".
            storage.Dispose();
            throw;
        }
    }

    private IStorage BuildStorage(ICryptoProvider? cryptoProvider = null)
    {
        if (Options.CustomStorage != null)
            return BuildCustomStorage(Options.CustomStorage, cryptoProvider);

        int storagePageSize = CalculateStoragePageSize(cryptoProvider);
        var baseStorage = CreateBaseStorage(storagePageSize);

        if (cryptoProvider != null)
            return WrapEncrypted(baseStorage, cryptoProvider);

        return baseStorage;
    }

    /// <summary>
    /// Wraps a storage in encryption, choosing the format from what the file already says rather
    /// than from what this connection was configured with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three cases, told apart by the first sixteen bytes of the first physical page, and none of
    /// them needs the password to decide:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Nothing there</b> - a database being created. A preamble is drawn: a
    /// random salt, the iteration count, a nonce sequence and, when there is a password, a random
    /// data key wrapped under it.</description></item>
    /// <item><description><b>A preamble</b> - read it, unwrap the data key, and encrypt with a nonce
    /// that carries a sequence number the file remembers.</description></item>
    /// <item><description><b>Anything else</b> - a database written before the preamble existed.
    /// Its salt is a function of its password and its nonce counter restarts on every open, and
    /// nothing done here can change either: those are properties of bytes already on disk. It opens
    /// exactly as it always did. Studio's password change is what moves such a file into the new
    /// format, and it is the documented migration.</description></item>
    /// </list>
    /// </remarks>
    private IStorage WrapEncrypted(IStorage baseStorage, ICryptoProvider cryptoProvider, byte[]? sharedDataKey = null)
    {
        var shape = CryptoPreamble.Inspect(baseStorage, out var header);

        return shape switch
        {
            CryptoPreamble.Shape.Other => new StorageEncrypted(
                baseStorage, new EncryptorPage(cryptoProvider, GetEncryptionSalt())),

            CryptoPreamble.Shape.Empty => CreateWithPreamble(baseStorage, cryptoProvider, sharedDataKey),

            _ => OpenWithPreamble(baseStorage, cryptoProvider, header, sharedDataKey)
        };
    }

    /// <summary>
    /// The LSM store's half of the same choice. It has no page 0 to put a preamble in, so its
    /// header is a small plaintext file beside the SSTables.
    /// </summary>
    /// <remarks>
    /// A directory that already holds SSTables and carries no header file was written before the
    /// header existed, and keeps its old encryptor - the same rule the paged store applies, decided
    /// the only way a directory can decide it.
    /// </remarks>
    private IBlockEncryptor BuildLsmEncryptor(string directory, ICryptoProvider cryptoProvider, byte[]? sharedDataKey = null)
    {
        if (CryptoPreamble.InspectDirectory(directory, out var existing))
        {
            ICryptoProvider provider;

            if (existing.Kdf == CryptoKdf.Pbkdf2Sha256)
            {
                var password = EncryptionPassword()
                    ?? throw new InvalidOperationException(
                        "This store is encrypted under a password and none was supplied.");

                var dataKey = existing.UnwrapDataKey(password);
                m_dataKey ??= dataKey;
                provider = ProviderForKey(dataKey);
            }
            else
            {
                provider = sharedDataKey != null ? ProviderForKey(sharedDataKey) : cryptoProvider;
            }

            var opened = CryptoPreamble.OpenDirectory(directory, existing, create: false);

            return new EncryptorBlockSequenced(provider, opened, opened);
        }

        if (HasSstables(directory))
            return new EncryptorBlock(cryptoProvider, GetEncryptionSalt());

        var password2 = sharedDataKey == null ? EncryptionPassword() : null;

        CryptoHeader header;
        ICryptoProvider newProvider;

        if (password2 != null)
        {
            header = CryptoHeader.CreateWrapping(password2, NewDatabaseIterations(), out var created);
            newProvider = ProviderForKey(created);
            m_dataKey ??= created;
        }
        else
        {
            header = CryptoHeader.CreateUnwrapped();
            newProvider = sharedDataKey != null ? ProviderForKey(sharedDataKey) : cryptoProvider;
        }

        var preamble = CryptoPreamble.OpenDirectory(directory, header, create: true);

        return new EncryptorBlockSequenced(newProvider, preamble, preamble);
    }

    private static bool HasSstables(string directory)
    {
        return Directory.Exists(directory) && Directory.GetFiles(directory, "sst_*.sst").Length > 0;
    }

    private IStorage CreateWithPreamble(IStorage baseStorage, ICryptoProvider cryptoProvider, byte[]? sharedDataKey)
    {
        var password = sharedDataKey == null ? EncryptionPassword() : null;

        CryptoHeader header;
        ICryptoProvider provider;

        if (password != null)
        {
            header = CryptoHeader.CreateWrapping(password, NewDatabaseIterations(), out var dataKey);
            provider = ProviderForKey(dataKey);

            // Remembered so that the index files beside this database can be encrypted under the
            // same key without each of them paying for its own derivation - at the OWASP iteration
            // count that would be tens of milliseconds per index, per open.
            m_dataKey = dataKey;
        }
        else
        {
            // Either a caller who owns the key material - there is nothing to wrap and nothing this
            // build could improve about a key it did not choose - or a file in a set whose key was
            // already established. Both still gain the random salt and the sequence that survives
            // the file, which is what closes the nonce reuse.
            header = CryptoHeader.CreateUnwrapped();
            provider = sharedDataKey != null ? ProviderForKey(sharedDataKey) : cryptoProvider;
        }

        var preamble = CryptoPreamble.Create(baseStorage, header);

        return new StorageEncrypted(
            baseStorage,
            new EncryptorPageSequenced(provider, preamble),
            pageOffset: CryptoPreamble.PREAMBLE_PAGE + 1,
            preamble: preamble);
    }

    private IStorage OpenWithPreamble(
        IStorage baseStorage, ICryptoProvider cryptoProvider, CryptoHeader header, byte[]? sharedDataKey)
    {
        ICryptoProvider provider;

        if (header.Kdf == CryptoKdf.Pbkdf2Sha256)
        {
            var password = EncryptionPassword()
                ?? throw new InvalidOperationException(
                    "This database is encrypted under a password and none was supplied.");

            var dataKey = header.UnwrapDataKey(password);
            m_dataKey = dataKey;
            provider = ProviderForKey(dataKey);
        }
        else
        {
            provider = sharedDataKey != null ? ProviderForKey(sharedDataKey) : cryptoProvider;
        }

        var preamble = CryptoPreamble.Open(baseStorage, header);

        return new StorageEncrypted(
            baseStorage,
            new EncryptorPageSequenced(provider, preamble),
            pageOffset: CryptoPreamble.PREAMBLE_PAGE + 1,
            preamble: preamble);
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

        // Encryption spends Overhead bytes of every physical page on the nonce and tag, so the
        // logical page size the rest of the engine sees is PageSize - Overhead - and that has to
        // remain a legal page size. The built-in storages are sized as PageSize + Overhead for
        // exactly this reason; a caller-supplied one has to be too.
        var overhead = cryptoProvider.Overhead;
        var logicalPageSize = customStorage.PageSize - overhead;

        if (logicalPageSize < DatabaseConstants.MIN_PAGE_SIZE ||
            logicalPageSize > DatabaseConstants.MAX_PAGE_SIZE ||
            (logicalPageSize & (logicalPageSize - 1)) != 0)
        {
            throw new InvalidOperationException(
                $"The storage supplied to WithStorage() has a page size of {customStorage.PageSize} " +
                $"bytes, which leaves {logicalPageSize} usable after the {overhead} bytes " +
                $"encryption needs per page - not a valid page size. Construct it with " +
                $"{Options.PageSize + overhead} bytes (a power of two plus the overhead), " +
                $"or build the database without encryption.");
        }

        return WrapEncrypted(customStorage, cryptoProvider);
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

    /// <summary>
    /// Builds the page cache this configuration asked for, or null to leave the store its default.
    /// </summary>
    /// <remarks>
    /// A page cache is bound to one storage, so <c>WithCache(IPageCache)</c> - a single instance - can
    /// only serve the main store; an index store asks for its own instance of the same provider.
    /// </remarks>
    private IPageCache? BuildPageCache(IStorage storage, bool allowCustomInstance = true, int? capacity = null)
    {
        if (allowCustomInstance && Options.CustomCache != null)
            return Options.CustomCache;

        if (string.IsNullOrEmpty(Options.CacheProviderKey))
            return null;

        var parameters = new ProviderParameters();

        foreach (var (key, value) in Options.CacheParameters.GetAll())
            parameters.Set(key, value);

        parameters.Set("storage", storage);

        if (capacity.HasValue || !parameters.Has("capacity"))
            parameters.Set("capacity", capacity ?? Options.CacheSize);

        return ProviderRegistry.Instance.Create<IPageCache>(Options.CacheProviderKey, parameters);
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

    /// <summary>
    /// Builds a crypto provider over a key this build chose, rather than over the one derived from
    /// the password before any file was opened.
    /// </summary>
    private ICryptoProvider ProviderForKey(byte[] key)
    {
        if (string.IsNullOrEmpty(Options.EncryptionProviderKey))
            return new EncryptorProviderAesGcm(key);

        var parameters = new ProviderParameters();

        foreach (var (name, value) in Options.EncryptionParameters.GetAll())
            parameters.Set(name, value);

        parameters.Set("key", key);

        return ProviderRegistry.Instance.Create<ICryptoProvider>(Options.EncryptionProviderKey, parameters);
    }

    /// <summary>
    /// The secret a password-protected database is opened with, or null when the caller owns the key.
    /// </summary>
    private string? EncryptionPassword()
    {
        var password = Options.EncryptionParameters.Get<string?>("password", null);

        if (string.IsNullOrEmpty(password))
            return null;

        return CryptoHeader.CombineUserAndPassword(
            Options.EncryptionParameters.Get<string?>("user", null), password);
    }

    /// <summary>
    /// The iteration count to record in a database being created.
    /// </summary>
    /// <remarks>
    /// A count the caller named is the caller's - <c>WithEncryption(password, 250_000)</c> means it,
    /// and so does <c>WithEncryptionFast</c>. The DEFAULT is a different matter: 100,000 was chosen
    /// when the number could not be written into the file, so every build had to agree on it
    /// forever. It can be written down now, so a new database gets the current OWASP figure and old
    /// databases keep opening at the count they were written with.
    /// </remarks>
    private int NewDatabaseIterations()
    {
        return Options.EncryptionParameters.Get("iterationsExplicit", false)
            ? Options.EncryptionParameters.Get("iterations", CryptoHeader.DEFAULT_ITERATIONS)
            : CryptoHeader.DEFAULT_ITERATIONS;
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
            JournalProviderKey = Options.CustomJournal?.ProviderKey ?? Options.JournalProviderKey ?? "",
            CacheSize = Options.CacheSize
        };
    }

    /// <summary>
    /// The subset of the LSM options a connection string can select, for the directory's sidecar.
    /// </summary>
    private static LsmStoredOptions StoredOptionsOf(LsmOptions options)
    {
        return new LsmStoredOptions
        {
            MemTableSizeLimit = options.MemTableSizeLimit,
            BlockCacheSizeBytes = options.BlockCacheSizeBytes,
            BlockSize = options.BlockSize,
            Level0CompactionTrigger = options.Level0CompactionTrigger,
            EnableWal = options.EnableWal,
            SyncWrites = options.SyncWrites,
            EnableBlockCache = options.EnableBlockCache,
            BackgroundCompaction = options.BackgroundCompaction
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

        if (Options.EnableMvcc)
        {
            // No journal is built here, and that is deliberate rather than an omission: the MVCC store
            // keeps its own versions and takes no ITransactionJournal. BuildJournal used to run on this
            // path too, so `Journal=wal` with the default MVCC=true CONSTRUCTED a write-ahead log - which
            // opens its file in its constructor - and then dropped it: never referenced, never disposed,
            // the handle held for the life of the process, and reopening the database refused. The
            // keyword reaching nothing is a separate question, answered in WitSQL.md.
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
            BuildJournal(),
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
                    // Through the same three-way choice the LSM store itself makes, and under the
                    // database's own data key - an index directory is part of the same set.
                    lsmOptions.Encryptor = BuildLsmEncryptor(indexPath, cryptoProvider.Clone(), m_dataKey);
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

                    // Through the same three-way choice the database file makes, so an index written
                    // before the crypto header keeps opening and a new one gets the new format. The
                    // data key is the database's: an index sidecar is part of the same set, and
                    // deriving a separate one would cost a full PBKDF2 per index per open.
                    storage = WrapEncrypted(baseStorage, cryptoProvider.Clone(), m_dataKey);
                }
                else
                {
                    storage = new StorageFile(indexPath, storagePageSize);
                }

                // The indexes follow the database's own configuration here too - a cache provider chosen
                // for the database is the cache provider its indexes get, each with its own instance.
                var indexCache = BuildPageCache(storage, allowCustomInstance: false, capacity: cacheSize / 4);

                var indexStore = indexCache != null
                    ? new StoreBTree(storage, indexCache, ownsStorage: true, providerMetadata: null)
                    : new StoreBTree(storage, cacheSize / 4, ownsStorage: true);

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
