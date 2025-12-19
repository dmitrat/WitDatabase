using OutWit.Database.Core.Interfaces;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Stores;

/// <summary>
/// Parameterized tests for IKeyValueStore that run against all storage implementations.
/// Uses TestCaseSource to run each test with Memory, File, EncryptedMemory, EncryptedFile, 
/// LSM, and EncryptedLSM storage.
/// </summary>
[TestFixture]
public class KeyValueStoreParameterizedTests
{
    #region Basic CRUD

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void PutAndGetTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key = "test-key"u8.ToArray();
            byte[] value = "test-value"u8.ToArray();

            store.Put(key, value);
            var result = store.Get(key);

            Assert.That(result, Is.EqualTo(value));
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void GetNonExistentReturnsNullTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            var result = store.Get("missing"u8);
            
            Assert.That(result, Is.Null);
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void PutUpdatesExistingKeyTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key = "key"u8.ToArray();
            store.Put(key, "value1"u8.ToArray());
            store.Put(key, "value2"u8.ToArray());

            var result = store.Get(key);
            
            Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo("value2"));
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void DeleteExistingKeySucceedsTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key = "to-delete"u8.ToArray();
            store.Put(key, "value"u8.ToArray());

            var deleted = store.Delete(key);

            Assert.That(deleted, Is.True);
            Assert.That(store.Get(key), Is.Null);
        }
    }

    /// <summary>
    /// Tests delete behavior for non-existent keys.
    /// Note: LSM-Tree always returns true (writes tombstone), BTree returns false.
    /// </summary>
    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllBTreeStorages))]
    public void DeleteNonExistentReturnsFalseTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            var deleted = store.Delete("missing"u8);
            
            Assert.That(deleted, Is.False);
        }
    }

    /// <summary>
    /// LSM-Tree specific: Delete always returns true because it writes a tombstone.
    /// </summary>
    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllLsmStorages))]
    public void DeleteNonExistentReturnsTrueForLsmTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            var deleted = store.Delete("missing"u8);
            
            // LSM writes tombstone regardless of key existence
            Assert.That(deleted, Is.True);
        }
    }

    #endregion

    #region Scan Operations

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void ScanAllReturnsInOrderTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            store.Put("c"u8.ToArray(), "3"u8.ToArray());
            store.Put("a"u8.ToArray(), "1"u8.ToArray());
            store.Put("b"u8.ToArray(), "2"u8.ToArray());

            var results = store.Scan(null, null).ToList();

            Assert.That(results.Count, Is.EqualTo(3));
            Assert.That(results[0].Key[0], Is.EqualTo((byte)'a'));
            Assert.That(results[1].Key[0], Is.EqualTo((byte)'b'));
            Assert.That(results[2].Key[0], Is.EqualTo((byte)'c'));
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void ScanWithRangeReturnsCorrectSubsetTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            for (int i = 0; i < 100; i++)
            {
                store.Put(TextEncoding.UTF8.GetBytes($"key{i:D3}"), BitConverter.GetBytes(i));
            }

            // Range from key020 to key050 (exclusive)
            var results = store.Scan(
                TextEncoding.UTF8.GetBytes("key020"),
                TextEncoding.UTF8.GetBytes("key050")
            ).ToList();

            Assert.That(results.Count, Is.EqualTo(30)); // key020 to key049
            Assert.That(TextEncoding.UTF8.GetString(results[0].Key), Is.EqualTo("key020"));
            Assert.That(TextEncoding.UTF8.GetString(results[29].Key), Is.EqualTo("key049"));
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void ScanEmptyReturnsEmptyTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            var results = store.Scan(null, null).ToList();
            
            Assert.That(results, Is.Empty);
        }
    }

    #endregion

    #region Multiple Operations

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void BulkInsertAllRetrievableTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            const int count = 1000;

            for (int i = 0; i < count; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i * 10));
            }

            for (int i = 0; i < count; i++)
            {
                var result = store.Get(BitConverter.GetBytes(i));
                Assert.That(result, Is.Not.Null, $"Key {i} not found");
                Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10));
            }
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void InsertDeleteMixedOperationsTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            // Insert 100
            for (int i = 0; i < 100; i++)
            {
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            // Delete even numbers
            for (int i = 0; i < 100; i += 2)
            {
                store.Delete(BitConverter.GetBytes(i));
            }

            // Verify odd remain
            for (int i = 1; i < 100; i += 2)
            {
                Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Not.Null);
            }

            // Verify even deleted
            for (int i = 0; i < 100; i += 2)
            {
                Assert.That(store.Get(BitConverter.GetBytes(i)), Is.Null);
            }
        }
    }

    #endregion

    #region Async Operations

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public async Task AsyncPutGetDeleteTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key = "async-key"u8.ToArray();
            byte[] value = "async-value"u8.ToArray();

            await store.PutAsync(key, value);
            var result = await store.GetAsync(key);
            Assert.That(result, Is.EqualTo(value));

            var deleted = await store.DeleteAsync(key);
            Assert.That(deleted, Is.True);

            result = await store.GetAsync(key);
            Assert.That(result, Is.Null);
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public async Task ScanAsyncTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            for (int i = 0; i < 50; i++)
            {
                await store.PutAsync(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            var results = new List<(byte[], byte[])>();
            await foreach (var item in store.ScanAsync(null, null))
            {
                results.Add(item);
            }

            Assert.That(results.Count, Is.EqualTo(50));
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void BinaryKeysWithNullBytesTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key1 = [0x00, 0x01, 0x02];
            byte[] key2 = [0x00, 0x00, 0x00];
            byte[] key3 = [0xFF, 0xFE, 0xFD];

            store.Put(key1, "value1"u8.ToArray());
            store.Put(key2, "value2"u8.ToArray());
            store.Put(key3, "value3"u8.ToArray());

            Assert.That(TextEncoding.UTF8.GetString(store.Get(key1)!), Is.EqualTo("value1"));
            Assert.That(TextEncoding.UTF8.GetString(store.Get(key2)!), Is.EqualTo("value2"));
            Assert.That(TextEncoding.UTF8.GetString(store.Get(key3)!), Is.EqualTo("value3"));
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void EmptyValueTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key = "empty-value-key"u8.ToArray();
            byte[] value = [];

            store.Put(key, value);
            var result = store.Get(key);

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Length, Is.EqualTo(0));
        }
    }

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    public void LargeKeyTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            byte[] key = new byte[500];
            Random.Shared.NextBytes(key);
            byte[] value = "large-key-value"u8.ToArray();

            store.Put(key, value);
            var result = store.Get(key);

            Assert.That(result, Is.EqualTo(value));
        }
    }

    #endregion

    #region Stress Tests

    [Test]
    [TestCaseSource(typeof(StorageFactorySource), nameof(StorageFactorySource.AllStorages))]
    [Category("Stress")]
    public void RandomOperationsMaintainsConsistencyTest(IStorageFactory factory)
    {
        using (factory)
        {
            using var store = factory.CreateStore();
            
            var random = new Random(42);
            var expectedState = new Dictionary<int, byte[]>();
            const int operationCount = 5000;
            const int keySpace = 500;

            for (int op = 0; op < operationCount; op++)
            {
                int keyInt = random.Next(keySpace);
                byte[] key = BitConverter.GetBytes(keyInt);
                int action = random.Next(100);

                if (action < 50)
                {
                    byte[] value = new byte[random.Next(10, 100)];
                    random.NextBytes(value);
                    store.Put(key, value);
                    expectedState[keyInt] = value;
                }
                else if (action < 80)
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
                else
                {
                    store.Delete(key);
                    expectedState.Remove(keyInt);
                }
            }

            foreach (var (keyInt, expectedValue) in expectedState)
            {
                var result = store.Get(BitConverter.GetBytes(keyInt));
                Assert.That(result, Is.Not.Null);
                Assert.That(result!.SequenceEqual(expectedValue), Is.True);
            }
        }
    }

    #endregion
}
