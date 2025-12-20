using System.Runtime.CompilerServices;
using OutWit.Database.Core.Cache;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;
using OutWit.Database.Core.Wal;

namespace OutWit.Database.Core.Providers;

/// <summary>
/// Registers all built-in providers with the ProviderRegistry.
/// Called automatically via ModuleInitializer.
/// </summary>
internal static class ProviderRegistration
{
    private static bool m_initialized;
    private static readonly Lock m_lock = new();

    /// <summary>
    /// Registers all built-in providers. Safe to call multiple times.
    /// </summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        if (m_initialized) return;

        lock (m_lock)
        {
            if (m_initialized) return;

            RegisterStorageProviders();
            RegisterStoreProviders();
            RegisterCryptoProviders();
            RegisterCacheProviders();
            RegisterJournalProviders();

            m_initialized = true;
        }
    }

    private static void RegisterStorageProviders()
    {
        // File storage
        ProviderRegistry.Instance.RegisterOrReplace<IStorage>(StorageFile.PROVIDER_KEY, p =>
        {
            var path = p.GetRequired<string>("path");
            var pageSize = p.Get("pageSize", DatabaseConstants.DEFAULT_PAGE_SIZE);
            var readOnly = p.Get("readOnly", false);
            return new StorageFile(path, pageSize, readOnly);
        });

        // Memory storage
        ProviderRegistry.Instance.RegisterOrReplace<IStorage>(StorageMemory.PROVIDER_KEY, p =>
        {
            var pageSize = p.Get("pageSize", DatabaseConstants.DEFAULT_PAGE_SIZE);
            var initialPageCount = p.Get("initialPageCount", 0);
            return new StorageMemory(pageSize, initialPageCount);
        });

        // Encrypted storage (requires base storage)
        ProviderRegistry.Instance.RegisterOrReplace<IStorage>(StorageEncrypted.PROVIDER_KEY, p =>
        {
            var baseStorage = p.GetRequired<IStorage>("baseStorage");
            var encryptor = p.GetRequired<IPageEncryptor>("encryptor");
            return new StorageEncrypted(baseStorage, encryptor);
        });
    }

    private static void RegisterStoreProviders()
    {
        // BTree store
        ProviderRegistry.Instance.RegisterOrReplace<IKeyValueStore>(StoreBTree.PROVIDER_KEY, p =>
        {
            // Check for PageManager first (preferred)
            if (p.Has("pageManager"))
            {
                var pageManager = p.GetRequired<Managers.PageManager>("pageManager");
                var rootPage = p.Get<uint>("rootPage", 0);
                return new StoreBTree(pageManager, rootPage);
            }

            // Otherwise use storage
            var storage = p.GetRequired<IStorage>("storage");
            var cacheSize = p.Get("cacheSize", DatabaseConstants.DEFAULT_CACHE_SIZE);
            var ownsStorage = p.Get("ownsStorage", true);
            var metadata = p.Get<ProviderMetadata?>("providerMetadata", null);
            return new StoreBTree(storage, cacheSize, ownsStorage, metadata);
        });

        // LSM store
        ProviderRegistry.Instance.RegisterOrReplace<IKeyValueStore>(StoreLsm.PROVIDER_KEY, p =>
        {
            var directory = p.GetRequired<string>("directory");
            var options = p.Get<LSM.LsmOptions?>("options", null) ?? new LSM.LsmOptions();
            return new StoreLsm(directory, options);
        });

        // In-memory store (simple)
        ProviderRegistry.Instance.RegisterOrReplace<IKeyValueStore>(StoreInMemory.PROVIDER_KEY, p =>
        {
            return new StoreInMemory();
        });
    }

    private static void RegisterCryptoProviders()
    {
        // AES-GCM
        ProviderRegistry.Instance.RegisterOrReplace<ICryptoProvider>(EncryptorProviderAesGcm.PROVIDER_KEY, p =>
        {
            var key = p.GetRequired<byte[]>("key");
            return new EncryptorProviderAesGcm(key);
        });
    }

    private static void RegisterCacheProviders()
    {
        // Sharded Clock cache (default)
        ProviderRegistry.Instance.RegisterOrReplace<IPageCache>(PageCacheShardedClock.PROVIDER_KEY, p =>
        {
            var storage = p.GetRequired<IStorage>("storage");
            var capacity = p.Get("capacity", DatabaseConstants.DEFAULT_CACHE_SIZE);
            return new PageCacheShardedClock(storage, capacity);
        });

        // LRU cache
        ProviderRegistry.Instance.RegisterOrReplace<IPageCache>(PageCacheLru.PROVIDER_KEY, p =>
        {
            var storage = p.GetRequired<IStorage>("storage");
            var capacity = p.Get("capacity", DatabaseConstants.DEFAULT_CACHE_SIZE);
            return new PageCacheLru(storage, capacity);
        });
    }

    private static void RegisterJournalProviders()
    {
        // Rollback journal
        ProviderRegistry.Instance.RegisterOrReplace<ITransactionJournal>(RollbackJournal.PROVIDER_KEY, p =>
        {
            var filePath = p.GetRequired<string>("filePath");
            return new RollbackJournal(filePath);
        });

        // WAL journal
        ProviderRegistry.Instance.RegisterOrReplace<ITransactionJournal>(WalTransactionJournal.PROVIDER_KEY, p =>
        {
            var walPath = p.GetRequired<string>("walPath");
            var pageSize = p.Get("pageSize", DatabaseConstants.DEFAULT_PAGE_SIZE);
            return new WalTransactionJournal(walPath, pageSize);
        });
    }
}
