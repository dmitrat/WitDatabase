using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Integration;

/// <summary>
/// Integration tests for encrypted storage with BTreeStore.
/// Tests the full stack: EncryptedStorage -> PageManager -> BTree -> BTreeStore
/// </summary>
[TestFixture]
public class EncryptedStorageIntegrationTests
{
    private byte[] m_key = null!;
    private byte[] m_salt = null!;
    private string? m_testDir;

    [SetUp]
    public void SetUp()
    {
        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
        m_testDir = Path.Combine(Path.GetTempPath(), $"EncryptedIntegrationTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDir != null && Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private StorageEncrypted CreateEncryptedMemoryStorage(int logicalPageSize = 4096, int pageCount = 1000)
    {
        int physicalPageSize = logicalPageSize + 28;
        var innerStorage = new StorageMemory(physicalPageSize, pageCount);
        var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        return new StorageEncrypted(innerStorage, encryptor);
    }

    private StorageEncrypted CreateEncryptedFileStorage(string filename, int logicalPageSize = 4096)
    {
        int physicalPageSize = logicalPageSize + 28;
        var innerStorage = new StorageFile(filename, physicalPageSize);
        var provider = new EncryptorProviderAesGcm(m_key);
        var encryptor = new EncryptorPage(provider, m_salt);
        return new StorageEncrypted(innerStorage, encryptor);
    }

    #region Memory Storage Integration

    [Test]
    public void EncryptedMemoryStorageBasicCRUDTest()
    {
        using var storage = CreateEncryptedMemoryStorage();
        using var store = new StoreBTree(storage, ownsStorage: false);

        // Create
        for (int i = 0; i < 100; i++)
        {
            store.Put(BitConverter.GetBytes(i), TextEncoding.UTF8.GetBytes($"value_{i}"));
        }

        // Read
        for (int i = 0; i < 100; i++)
        {
            var result = store.Get(BitConverter.GetBytes(i));
            Assert.That(result, Is.Not.Null);
            Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo($"value_{i}"));
        }

        // Update
        for (int i = 0; i < 50; i++)
        {
            store.Put(BitConverter.GetBytes(i), TextEncoding.UTF8.GetBytes($"updated_{i}"));
        }

        // Delete
        for (int i = 50; i < 75; i++)
        {
            Assert.That(store.Delete(BitConverter.GetBytes(i)), Is.True);
        }

        // Verify final state
        Assert.That(store.Count(), Is.EqualTo(75));

        for (int i = 0; i < 50; i++)
        {
            var result = store.Get(BitConverter.GetBytes(i));
            Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo($"updated_{i}"));
        }
    }

    [Test]
    public void EncryptedMemoryStorageRangeScanTest()
    {
        using var storage = CreateEncryptedMemoryStorage();
        using var store = new StoreBTree(storage, ownsStorage: false);

        // Insert keys as formatted strings for proper ordering
        for (int i = 0; i < 1000; i++)
        {
            store.Put(TextEncoding.UTF8.GetBytes($"{i:D4}"), BitConverter.GetBytes(i * 10));
        }

        // Full scan
        var all = store.Scan(null, null).ToList();
        Assert.That(all.Count, Is.EqualTo(1000));

        // Range scan: "0100" to "0200" (exclusive end)
        var range = store.Scan(
            TextEncoding.UTF8.GetBytes("0100"), 
            TextEncoding.UTF8.GetBytes("0200")).ToList();
        Assert.That(range.Count, Is.EqualTo(100)); // 0100-0199
    }

    [Test]
    public void EncryptedMemoryStorageLargeValuesWithOverflowTest()
    {
        using var storage = CreateEncryptedMemoryStorage();
        using var store = new StoreBTree(storage, ownsStorage: false);

        var random = new Random(42);
        var largeValues = new Dictionary<int, byte[]>();

        for (int i = 0; i < 50; i++)
        {
            byte[] value = new byte[store.MaxInlineValueSize + 500 + i * 10];
            random.NextBytes(value);
            store.Put(BitConverter.GetBytes(i), value);
            largeValues[i] = value;
        }

        foreach (var (key, expectedValue) in largeValues)
        {
            var result = store.Get(BitConverter.GetBytes(key));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SequenceEqual(expectedValue), Is.True, $"Large value mismatch for key {key}");
        }
    }

    #endregion

    #region File Storage Integration

    [Test]
    public void EncryptedFileStoragePersistenceAcrossRestartsTest()
    {
        var dbPath = Path.Combine(m_testDir!, "encrypted.db");

        // Phase 1: Create and populate using storage constructor (saves header)
        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, ownsStorage: false))
        {
            for (int i = 0; i < 500; i++)
            {
                store.Put(BitConverter.GetBytes(i), TextEncoding.UTF8.GetBytes($"encrypted_value_{i}"));
            }
            store.Flush();
        }

        // Phase 2: Reopen with same key and verify
        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, ownsStorage: false))
        {
            Assert.That(store.Count(), Is.EqualTo(500));

            for (int i = 0; i < 500; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null);
                Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo($"encrypted_value_{i}"));
            }
        }
    }

    [Test]
    public void EncryptedFileStorageWrongKeyFailsToOpenTest()
    {
        var dbPath = Path.Combine(m_testDir!, "wrong_key.db");

        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, ownsStorage: false))
        {
            for (int i = 0; i < 100; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            store.Flush();
        }

        byte[] wrongKey = RandomNumberGenerator.GetBytes(32);
        var innerStorage = new StorageFile(dbPath, 4096 + 28);
        var provider = new EncryptorProviderAesGcm(wrongKey);
        var encryptor = new EncryptorPage(provider, m_salt);
        using var storage2 = new StorageEncrypted(innerStorage, encryptor);

        Assert.Throws<CryptographicException>(() =>
        {
            using var store = new StoreBTree(storage2, ownsStorage: false);
        });
    }

    [Test]
    public void EncryptedFileStorageModifyAndReopenTest()
    {
        var dbPath = Path.Combine(m_testDir!, "modify.db");

        // Phase 1: Create
        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, ownsStorage: false))
        {
            for (int i = 0; i < 1000; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            store.Flush();
        }

        // Phase 2: Modify
        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, ownsStorage: false))
        {
            // Delete half
            for (int i = 0; i < 500; i++)
            {
                store.Delete(BitConverter.GetBytes(i));
            }

            // Update remaining
            for (int i = 500; i < 1000; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 100));
            }

            // Add new
            for (int i = 1000; i < 1500; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }
            store.Flush();
        }

        // Phase 3: Verify
        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, ownsStorage: false))
        {
            Assert.That(store.Count(), Is.EqualTo(1000));

            for (int i = 0; i < 500; i++)
            {
                Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Null);
            }

            for (int i = 500; i < 1000; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 100));
            }

            for (int i = 1000; i < 1500; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i));
            }
        }
    }

    #endregion

    #region Async Operations

    [Test]
    public async Task EncryptedStorageAsyncOperationsTest()
    {
        using var storage = CreateEncryptedMemoryStorage();
        using var store = new StoreBTree(storage, ownsStorage: false);

        // Async put
        for (int i = 0; i < 100; i++)
        {
            await store.PutAsync(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
        }

        // Async get
        for (int i = 0; i < 100; i++)
        {
            var result = await store.GetAsync(BitConverter.GetBytes(i));
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i));
        }

        // Async scan
        var all = new List<(byte[] Key, byte[] Value)>();
        await foreach (var item in store.ScanAsync(null, null))
        {
            all.Add(item);
        }
        Assert.That(all.Count, Is.EqualTo(100));

        await store.FlushAsync();
    }

    #endregion

    #region Stress Tests

    [Test]
    [Category("Stress")]
    public void EncryptedStorageLargeDatasetTest()
    {
        var dbPath = Path.Combine(m_testDir!, "large.db");
        const int count = 10000;

        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, cacheSize: 200, ownsStorage: false))
        {
            for (int i = 0; i < count; i++)
            {
                store.Put(
                    TextEncoding.UTF8.GetBytes($"key_{i:D8}"),
                    TextEncoding.UTF8.GetBytes($"value_{i:D8}_with_extra_data"));
            }
            store.Flush();
        }

        using (var storage = CreateEncryptedFileStorage(dbPath))
        using (var store = new StoreBTree(storage, cacheSize: 200, ownsStorage: false))
        {
            Assert.That(store.Count(), Is.EqualTo(count));

            var random = new Random(42);
            for (int i = 0; i < 100; i++)
            {
                int idx = random.Next(count);
                var result = store.Get(TextEncoding.UTF8.GetBytes($"key_{idx:D8}"));
                Assert.That(result, Is.Not.Null);
                Assert.That(TextEncoding.UTF8.GetString(result!),
                    Is.EqualTo($"value_{idx:D8}_with_extra_data"));
            }
        }
    }

    [Test]
    [Category("Stress")]
    public void EncryptedStorageRandomAccessPatternTest()
    {
        using var storage = CreateEncryptedMemoryStorage(pageCount: 2000);
        using var store = new StoreBTree(storage, cacheSize: 100, ownsStorage: false);

        var random = new Random(42);
        var expectedState = new Dictionary<int, byte[]>();

        const int operationCount = 5000;
        const int keySpace = 1000;

        for (int op = 0; op < operationCount; op++)
        {
            int keyInt = random.Next(keySpace);
            byte[] key = BitConverter.GetBytes(keyInt);
            int action = random.Next(100);

            if (action < 50) // 50% put
            {
                byte[] value = new byte[random.Next(10, 100)];
                random.NextBytes(value);
                store.Put(key, value);
                expectedState[keyInt] = value;
            }
            else if (action < 80) // 30% get
            {
                var result = store.Get(key);
                if (expectedState.TryGetValue(keyInt, out var expected))
                {
                    Assert.That(result, Is.Not.Null);
                    Assert.That(result!.SequenceEqual(expected), Is.True);
                }
                else
                {
                    Assert.That(result, Is.Null);
                }
            }
            else // 20% delete
            {
                store.Delete(key);
                expectedState.Remove(keyInt);
            }
        }
    }

    #endregion
}
