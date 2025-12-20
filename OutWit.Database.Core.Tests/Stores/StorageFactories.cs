using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Stores;

/// <summary>
/// Factory interface for creating storage instances in tests.
/// </summary>
public interface IStorageFactory : IDisposable
{
    string Name { get; }
    IStorage CreateStorage();
    IKeyValueStore CreateStore();
}

#region Base Storage Factories

/// <summary>
/// Factory for creating MemoryStorage-based stores.
/// </summary>
public class MemoryStorageFactory : IStorageFactory
{
    private readonly int m_pageSize;
    private readonly int m_maxPages;
    private readonly int m_cacheSize;
    private readonly List<IDisposable> m_disposables = [];

    public MemoryStorageFactory(int pageSize = 4096, int maxPages = 5000, int cacheSize = 1000)
    {
        m_pageSize = pageSize;
        m_maxPages = maxPages;
        m_cacheSize = cacheSize;
    }

    public string Name => "Memory";

    public IStorage CreateStorage()
    {
        var storage = new StorageMemory(m_pageSize, m_maxPages);
        m_disposables.Add(storage);
        return storage;
    }

    public IKeyValueStore CreateStore()
    {
        var storage = CreateStorage();
        var store = new StoreBTree(storage, m_cacheSize, ownsStorage: false);
        m_disposables.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var d in m_disposables.AsEnumerable().Reverse())
            d.Dispose();
        m_disposables.Clear();
    }

    public override string ToString() => Name;
}

/// <summary>
/// Factory for creating FileStorage-based stores.
/// </summary>
public class FileStorageFactory : IStorageFactory
{
    private readonly int m_pageSize;
    private readonly int m_cacheSize;
    private readonly string m_testDir;
    private readonly List<IDisposable> m_disposables = [];
    private readonly List<string> m_filesToDelete = [];

    public FileStorageFactory(int pageSize = 4096, int cacheSize = 1000)
    {
        m_pageSize = pageSize;
        m_cacheSize = cacheSize;
        m_testDir = Path.Combine(Path.GetTempPath(), $"StorageTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    public string Name => "File";

    public IStorage CreateStorage()
    {
        var filePath = Path.Combine(m_testDir, $"test_{Guid.NewGuid():N}.db");
        m_filesToDelete.Add(filePath);
        var storage = new StorageFile(filePath, m_pageSize);
        m_disposables.Add(storage);
        return storage;
    }

    public IKeyValueStore CreateStore()
    {
        var filePath = Path.Combine(m_testDir, $"test_{Guid.NewGuid():N}.db");
        m_filesToDelete.Add(filePath);
        var store = new StoreBTree(filePath, m_pageSize, m_cacheSize);
        m_disposables.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var d in m_disposables.AsEnumerable().Reverse())
            d.Dispose();
        m_disposables.Clear();

        foreach (var file in m_filesToDelete)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* Ignore */ }
        }
        m_filesToDelete.Clear();

        try { if (Directory.Exists(m_testDir)) Directory.Delete(m_testDir, true); }
        catch { /* Ignore */ }
    }

    public override string ToString() => Name;
}

#endregion

#region Encrypted Storage Factories

/// <summary>
/// Factory for creating encrypted MemoryStorage-based stores.
/// </summary>
public class EncryptedMemoryStorageFactory : IStorageFactory
{
    private readonly int m_pageSize;
    private readonly int m_maxPages;
    private readonly int m_cacheSize;
    private readonly byte[] m_key;
    private readonly byte[] m_salt;
    private readonly List<IDisposable> m_disposables = [];

    public EncryptedMemoryStorageFactory(int pageSize = 4096, int maxPages = 5000, int cacheSize = 1000)
    {
        m_pageSize = pageSize;
        m_maxPages = maxPages;
        m_cacheSize = cacheSize;
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
    }

    public string Name => "EncryptedMemory";

    public IStorage CreateStorage()
    {
        int physicalPageSize = m_pageSize + 28; // +overhead
        var innerStorage = new StorageMemory(physicalPageSize, m_maxPages);
        var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        var storage = new StorageEncrypted(innerStorage, encryptor);
        m_disposables.Add(storage);
        return storage;
    }

    public IKeyValueStore CreateStore()
    {
        var storage = CreateStorage();
        var store = new StoreBTree(storage, m_cacheSize, ownsStorage: false);
        m_disposables.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var d in m_disposables.AsEnumerable().Reverse())
            d.Dispose();
        m_disposables.Clear();
    }

    public override string ToString() => Name;
}

/// <summary>
/// Factory for creating encrypted FileStorage-based stores.
/// </summary>
public class EncryptedFileStorageFactory : IStorageFactory
{
    private readonly int m_pageSize;
    private readonly int m_cacheSize;
    private readonly string m_testDir;
    private readonly byte[] m_key;
    private readonly byte[] m_salt;
    private readonly List<IDisposable> m_disposables = [];
    private readonly List<string> m_filesToDelete = [];

    public EncryptedFileStorageFactory(int pageSize = 4096, int cacheSize = 1000)
    {
        m_pageSize = pageSize;
        m_cacheSize = cacheSize;
        m_testDir = Path.Combine(Path.GetTempPath(), $"EncryptedStorageTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
    }

    public string Name => "EncryptedFile";

    public IStorage CreateStorage()
    {
        int physicalPageSize = m_pageSize + 28;
        var filePath = Path.Combine(m_testDir, $"test_{Guid.NewGuid():N}.db");
        m_filesToDelete.Add(filePath);
        var innerStorage = new StorageFile(filePath, physicalPageSize);
        var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        var storage = new StorageEncrypted(innerStorage, encryptor);
        m_disposables.Add(storage);
        return storage;
    }

    public IKeyValueStore CreateStore()
    {
        var storage = CreateStorage();
        var store = new StoreBTree(storage, m_cacheSize, ownsStorage: false);
        m_disposables.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var d in m_disposables.AsEnumerable().Reverse())
            d.Dispose();
        m_disposables.Clear();

        foreach (var file in m_filesToDelete)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { /* Ignore */ }
        }
        m_filesToDelete.Clear();

        try { if (Directory.Exists(m_testDir)) Directory.Delete(m_testDir, true); }
        catch { /* Ignore */ }
    }

    public override string ToString() => Name;
}

#endregion

#region LSM Storage Factories

/// <summary>
/// Factory for creating LSM-Tree based stores.
/// </summary>
public class LsmStorageFactory : IStorageFactory
{
    private readonly string m_testDir;
    private readonly LsmOptions m_options;
    private readonly List<IDisposable> m_disposables = [];

    public LsmStorageFactory(LsmOptions? options = null)
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"LsmTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_options = options ?? new LsmOptions
        {
            EnableWal = true,
            SyncWrites = false,
            EnableBlockCache = true,
            BlockCacheSizeBytes = 10 * 1024 * 1024,
            MemTableSizeLimit = 256 * 1024,
            Level0CompactionTrigger = 4,
            BackgroundCompaction = false
        };
    }

    public string Name => "LSM";

    public IStorage CreateStorage()
    {
        // LSM doesn't use IStorage interface, throw NotSupported
        throw new NotSupportedException("LSM-Tree uses directory-based storage, not IStorage");
    }

    public IKeyValueStore CreateStore()
    {
        var storeDir = Path.Combine(m_testDir, $"store_{Guid.NewGuid():N}");
        var store = new StoreLsm(storeDir, m_options);
        m_disposables.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var d in m_disposables.AsEnumerable().Reverse())
            d.Dispose();
        m_disposables.Clear();

        try { if (Directory.Exists(m_testDir)) Directory.Delete(m_testDir, true); }
        catch { /* Ignore */ }
    }

    public override string ToString() => Name;
}

/// <summary>
/// Factory for creating encrypted LSM-Tree based stores.
/// </summary>
public class EncryptedLsmStorageFactory : IStorageFactory
{
    private readonly string m_testDir;
    private readonly LsmOptions m_options;
    private readonly List<IDisposable> m_disposables = [];

    public EncryptedLsmStorageFactory()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"EncryptedLsmTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        
        var key = RandomNumberGenerator.GetBytes(32);
        var salt = RandomNumberGenerator.GetBytes(16);
        var provider = new EncryptorProviderAesGcm(key);
        var encryptor = new EncryptorBlock(provider, salt);
        
        m_options = new LsmOptions
        {
            EnableWal = true,
            SyncWrites = false,
            EnableBlockCache = true,
            BlockCacheSizeBytes = 10 * 1024 * 1024,
            MemTableSizeLimit = 256 * 1024,
            Level0CompactionTrigger = 4,
            BackgroundCompaction = false,
            Encryptor = encryptor
        };
    }

    public string Name => "EncryptedLSM";

    public IStorage CreateStorage()
    {
        throw new NotSupportedException("LSM-Tree uses directory-based storage, not IStorage");
    }

    public IKeyValueStore CreateStore()
    {
        var storeDir = Path.Combine(m_testDir, $"store_{Guid.NewGuid():N}");
        var store = new StoreLsm(storeDir, m_options);
        m_disposables.Add(store);
        return store;
    }

    public void Dispose()
    {
        foreach (var d in m_disposables.AsEnumerable().Reverse())
            d.Dispose();
        m_disposables.Clear();

        try { if (Directory.Exists(m_testDir)) Directory.Delete(m_testDir, true); }
        catch { /* Ignore */ }
    }

    public override string ToString() => Name;
}

#endregion

#region Factory Sources

/// <summary>
/// Provides storage factory instances for parameterized tests.
/// </summary>
public static class StorageFactorySource
{
    /// <summary>
    /// All plain (non-encrypted) BTree storage factories.
    /// </summary>
    public static IEnumerable<TestCaseData> PlainBTreeStorages
    {
        get
        {
            yield return new TestCaseData(new MemoryStorageFactory()).SetName("{m}(BTree_Memory)");
            yield return new TestCaseData(new FileStorageFactory()).SetName("{m}(BTree_File)");
        }
    }

    /// <summary>
    /// All encrypted BTree storage factories.
    /// </summary>
    public static IEnumerable<TestCaseData> EncryptedBTreeStorages
    {
        get
        {
            yield return new TestCaseData(new EncryptedMemoryStorageFactory()).SetName("{m}(BTree_EncryptedMemory)");
            yield return new TestCaseData(new EncryptedFileStorageFactory()).SetName("{m}(BTree_EncryptedFile)");
        }
    }

    /// <summary>
    /// All BTree storage factories (plain + encrypted).
    /// </summary>
    public static IEnumerable<TestCaseData> AllBTreeStorages
    {
        get
        {
            foreach (var tc in PlainBTreeStorages) yield return tc;
            foreach (var tc in EncryptedBTreeStorages) yield return tc;
        }
    }

    /// <summary>
    /// All LSM storage factories.
    /// </summary>
    public static IEnumerable<TestCaseData> AllLsmStorages
    {
        get
        {
            yield return new TestCaseData(new LsmStorageFactory()).SetName("{m}(LSM)");
            yield return new TestCaseData(new EncryptedLsmStorageFactory()).SetName("{m}(LSM_Encrypted)");
        }
    }

    /// <summary>
    /// All storage factories (BTree + LSM, plain + encrypted).
    /// </summary>
    public static IEnumerable<TestCaseData> AllStorages
    {
        get
        {
            foreach (var tc in AllBTreeStorages) yield return tc;
            foreach (var tc in AllLsmStorages) yield return tc;
        }
    }

    // Legacy aliases for backward compatibility
    public static IEnumerable<TestCaseData> PlainStorages => PlainBTreeStorages;
    public static IEnumerable<TestCaseData> EncryptedStorages => EncryptedBTreeStorages;

    /// <summary>
    /// Storage factories for legacy compatibility.
    /// </summary>
    public static IEnumerable<IStorageFactory> AllStorageFactories
    {
        get
        {
            yield return new MemoryStorageFactory();
            yield return new FileStorageFactory();
            yield return new EncryptedMemoryStorageFactory();
            yield return new EncryptedFileStorageFactory();
            yield return new LsmStorageFactory();
            yield return new EncryptedLsmStorageFactory();
        }
    }
}

#endregion
