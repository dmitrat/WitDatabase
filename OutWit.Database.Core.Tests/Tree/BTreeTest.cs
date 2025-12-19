using OutWit.Database.Core.Managers;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Tree;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Tree;

[TestFixture]
public class BTreeTest
{
    private MemoryStorage m_storage = null!;
    private PageManager m_pageManager = null!;

    [SetUp]
    public void SetUp()
    {
        m_storage = new MemoryStorage(4096, 1000);
        m_pageManager = new PageManager(m_storage);
    }

    [TearDown]
    public void TearDown()
    {
        m_pageManager?.Dispose();
        m_storage?.Dispose();
    }

    #region Basic Operations

    [Test]
    public void InsertAndSearchSingleKeyTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = TextEncoding.UTF8.GetBytes("hello");
        byte[] value = TextEncoding.UTF8.GetBytes("world");
        
        bool inserted = tree.Insert(key, value);
        Assert.That(inserted, Is.True);
        
        byte[]? result = tree.Search(key);
        Assert.That(result, Is.Not.Null);
        Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo("world"));
    }

    [Test]
    public void SearchNonExistentKeyReturnsNullTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = TextEncoding.UTF8.GetBytes("missing");
        byte[]? result = tree.Search(key);
        
        Assert.That(result, Is.Null);
    }

    [Test]
    public void InsertDuplicateKeyReturnsFalseTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = TextEncoding.UTF8.GetBytes("key");
        byte[] value1 = TextEncoding.UTF8.GetBytes("value1");
        byte[] value2 = TextEncoding.UTF8.GetBytes("value2");
        
        Assert.That(tree.Insert(key, value1), Is.True);
        Assert.That(tree.Insert(key, value2), Is.False);
        
        // Original value should be unchanged
        var result = tree.Search(key);
        Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo("value1"));
    }

    [Test]
    public void ContainsKeyTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = TextEncoding.UTF8.GetBytes("exists");
        tree.Insert(key, "value"u8.ToArray());
        
        Assert.That(tree.ContainsKey(key), Is.True);
        Assert.That(tree.ContainsKey("missing"u8.ToArray()), Is.False);
    }

    [Test]
    public void DeleteKeyTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = TextEncoding.UTF8.GetBytes("delete-me");
        tree.Insert(key, "value"u8.ToArray());
        
        Assert.That(tree.ContainsKey(key), Is.True);
        
        bool deleted = tree.Delete(key);
        Assert.That(deleted, Is.True);
        Assert.That(tree.ContainsKey(key), Is.False);
    }

    [Test]
    public void DeleteNonExistentKeyReturnsFalseTest()
    {
        using var tree = new BTree(m_pageManager);
        
        bool deleted = tree.Delete("missing"u8.ToArray());
        Assert.That(deleted, Is.False);
    }

    [Test]
    public void EmptyKeyThrowsTest()
    {
        using var tree = new BTree(m_pageManager);
        
        Assert.Throws<ArgumentException>(() => tree.Insert([], "value"u8.ToArray()));
        Assert.Throws<ArgumentException>(() => tree.Search([]));
        Assert.Throws<ArgumentException>(() => tree.ContainsKey([]));
        Assert.Throws<ArgumentException>(() => tree.Delete([]));
    }

    [Test]
    public void KeyTooLargeThrowsTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] largeKey = new byte[BTree.MAX_KEY_SIZE + 1];
        Assert.Throws<ArgumentException>(() => tree.Insert(largeKey, "value"u8.ToArray()));
    }

    #endregion

    #region Upsert Tests

    [Test]
    public void UpsertInsertsNewKeyTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = "key"u8.ToArray();
        byte[] value = "value"u8.ToArray();
        
        bool inserted = tree.Upsert(key, value);
        Assert.That(inserted, Is.True);
        Assert.That(tree.Count(), Is.EqualTo(1));
        
        var result = tree.Search(key);
        Assert.That(result, Is.EqualTo(value));
    }

    [Test]
    public void UpsertUpdatesExistingKeyTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = "key"u8.ToArray();
        tree.Insert(key, "value1"u8.ToArray());
        
        bool inserted = tree.Upsert(key, "value2"u8.ToArray());
        Assert.That(inserted, Is.False); // Was update, not insert
        Assert.That(tree.Count(), Is.EqualTo(1)); // Count unchanged
        
        var result = tree.Search(key);
        Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo("value2"));
    }

    #endregion

    #region Multiple Keys

    [Test]
    public void InsertMultipleKeysTest()
    {
        using var tree = new BTree(m_pageManager);
        
        for (int i = 0; i < 100; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = TextEncoding.UTF8.GetBytes($"value{i}");
            Assert.That(tree.Insert(key, value), Is.True);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(100));
        
        // Verify all keys
        for (int i = 0; i < 100; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            var result = tree.Search(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo($"value{i}"));
        }
    }

    [Test]
    public void InsertKeysInReverseOrderTest()
    {
        using var tree = new BTree(m_pageManager);
        
        for (int i = 99; i >= 0; i--)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = TextEncoding.UTF8.GetBytes($"value{i}");
            tree.Insert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(100));
        
        // Keys should still be searchable
        for (int i = 0; i < 100; i++)
        {
            Assert.That(tree.ContainsKey(TextEncoding.UTF8.GetBytes($"key{i:D3}")), Is.True);
        }
    }

    [Test]
    public void InsertRandomKeysTest()
    {
        using var tree = new BTree(m_pageManager);
        
        var random = new Random(42);
        var keys = Enumerable.Range(0, 500)
            .Select(i => TextEncoding.UTF8.GetBytes($"key{random.Next(10000):D5}"))
            .Distinct(new ByteArrayEqualityComparer())
            .ToList();
        
        foreach (var key in keys)
        {
            tree.Insert(key, key);
        }
        
        foreach (var key in keys)
        {
            Assert.That(tree.ContainsKey(key), Is.True, $"Key {TextEncoding.UTF8.GetString(key)} not found");
        }
    }

    #endregion

    #region Range Scan

    [Test]
    public void GetAllReturnsAllKeysInOrderTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert in random order
        var keys = new[] { "cherry", "apple", "date", "banana" };
        foreach (var k in keys)
        {
            tree.Insert(TextEncoding.UTF8.GetBytes(k), TextEncoding.UTF8.GetBytes(k.ToUpper()));
        }
        
        var all = tree.GetAll().ToList();
        Assert.That(all.Count, Is.EqualTo(4));
        
        // Should be in sorted order
        var sortedKeys = keys.OrderBy(k => k).ToArray();
        for (int i = 0; i < 4; i++)
        {
            Assert.That(TextEncoding.UTF8.GetString(all[i].Key), Is.EqualTo(sortedKeys[i]));
        }
    }

    [Test]
    public void GetRangeReturnsCorrectSubsetTest()
    {
        using var tree = new BTree(m_pageManager);
        
        for (int i = 0; i < 26; i++)
        {
            char c = (char)('a' + i);
            tree.Insert([(byte)c], [(byte)c]);
        }
        
        // Range from 'f' to 'j' (inclusive) - use GetRangeInclusive
        var range = tree.GetRangeInclusive([(byte)'f'], [(byte)'j']).ToList();
        
        Assert.That(range.Count, Is.EqualTo(5)); // f, g, h, i, j
        Assert.That(range[0].Key[0], Is.EqualTo((byte)'f'));
        Assert.That(range[4].Key[0], Is.EqualTo((byte)'j'));
    }

    [Test]
    public void GetRangeExclusiveEndTest()
    {
        using var tree = new BTree(m_pageManager);
        
        for (int i = 0; i < 26; i++)
        {
            char c = (char)('a' + i);
            tree.Insert([(byte)c], [(byte)c]);
        }
        
        // Range from 'f' to 'k' (exclusive end) - should get f, g, h, i, j
        var range = tree.GetRange([(byte)'f'], [(byte)'k']).ToList();
        
        Assert.That(range.Count, Is.EqualTo(5)); // f, g, h, i, j (k excluded)
        Assert.That(range[0].Key[0], Is.EqualTo((byte)'f'));
        Assert.That(range[4].Key[0], Is.EqualTo((byte)'j'));
    }

    #endregion

    #region Split Tests (Force Page Splits)

    [Test]
    public void InsertManyKeysForcesPageSplitTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert enough keys to force multiple page splits
        for (int i = 0; i < 500; i++)
        {
            byte[] key = new byte[20];
            byte[] value = new byte[30];
            BitConverter.TryWriteBytes(key, i);
            BitConverter.TryWriteBytes(value, i * 2);
            
            tree.Insert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(500));
        
        // Verify all keys are still findable
        for (int i = 0; i < 500; i++)
        {
            byte[] key = new byte[20];
            BitConverter.TryWriteBytes(key, i);
            Assert.That(tree.ContainsKey(key), Is.True, $"Key {i} not found after splits");
        }
    }

    [Test]
    public void InsertManyKeysForcesInternalNodeSplitTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert enough keys to force internal node splits
        // Need many keys with small values to create deep tree
        for (int i = 0; i < 2000; i++)
        {
            byte[] key = BitConverter.GetBytes(i);
            byte[] value = BitConverter.GetBytes(i);
            tree.Insert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(2000));
        
        // Verify all keys
        for (int i = 0; i < 2000; i++)
        {
            byte[] key = BitConverter.GetBytes(i);
            var result = tree.Search(key);
            Assert.That(result, Is.Not.Null, $"Key {i} not found");
            Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i));
        }
    }

    #endregion

    #region Overflow Tests

    [Test]
    public void LargeValueUsesOverflowTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = "large-value-key"u8.ToArray();
        byte[] largeValue = new byte[tree.MaxInlineValueSize + 100];
        Random.Shared.NextBytes(largeValue);
        
        Assert.That(tree.Insert(key, largeValue), Is.True);
        
        var result = tree.Search(key);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Length, Is.EqualTo(largeValue.Length));
        Assert.That(result.SequenceEqual(largeValue), Is.True);
    }

    [Test]
    public void MultipleLargeValuesTest()
    {
        using var tree = new BTree(m_pageManager);
        
        var entries = new Dictionary<string, byte[]>();
        
        for (int i = 0; i < 20; i++)
        {
            string keyStr = $"key{i:D3}";
            byte[] key = TextEncoding.UTF8.GetBytes(keyStr);
            byte[] value = new byte[tree.MaxInlineValueSize + i * 100];
            Random.Shared.NextBytes(value);
            
            tree.Insert(key, value);
            entries[keyStr] = value;
        }
        
        Assert.That(tree.Count(), Is.EqualTo(20));
        
        foreach (var (keyStr, expectedValue) in entries)
        {
            var result = tree.Search(TextEncoding.UTF8.GetBytes(keyStr));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SequenceEqual(expectedValue), Is.True, $"Value mismatch for {keyStr}");
        }
    }

    [Test]
    public void DeleteOverflowValueTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = "overflow-delete"u8.ToArray();
        byte[] largeValue = new byte[tree.MaxInlineValueSize + 500];
        Random.Shared.NextBytes(largeValue);
        
        tree.Insert(key, largeValue);
        Assert.That(tree.ContainsKey(key), Is.True);
        
        bool deleted = tree.Delete(key);
        Assert.That(deleted, Is.True);
        Assert.That(tree.ContainsKey(key), Is.False);
        Assert.That(tree.Count(), Is.EqualTo(0));
    }

    [Test]
    public void UpsertOverflowValueTest()
    {
        using var tree = new BTree(m_pageManager);
        
        byte[] key = "upsert-overflow"u8.ToArray();
        byte[] smallValue = "small"u8.ToArray();
        byte[] largeValue = new byte[tree.MaxInlineValueSize + 200];
        Random.Shared.NextBytes(largeValue);
        
        // Insert small value
        tree.Insert(key, smallValue);
        
        // Upsert to large value
        tree.Upsert(key, largeValue);
        
        var result = tree.Search(key);
        Assert.That(result!.SequenceEqual(largeValue), Is.True);
        
        // Upsert back to small value
        tree.Upsert(key, smallValue);
        
        result = tree.Search(key);
        Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo("small"));
    }

    [Test]
    public void RangeScanWithOverflowValuesTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Mix of small and large values
        for (int i = 0; i < 10; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D2}");
            byte[] value = i % 2 == 0 
                ? new byte[tree.MaxInlineValueSize + 100] 
                : TextEncoding.UTF8.GetBytes($"small{i}");
            
            if (i % 2 == 0)
                Random.Shared.NextBytes(value);
            
            tree.Insert(key, value);
        }
        
        var all = tree.GetAll().ToList();
        Assert.That(all.Count, Is.EqualTo(10));
        
        // Verify order
        for (int i = 0; i < 10; i++)
        {
            Assert.That(TextEncoding.UTF8.GetString(all[i].Key), Is.EqualTo($"key{i:D2}"));
        }
    }

    #endregion

    #region Integer Keys

    [Test]
    public void IntegerKeysTest()
    {
        using var tree = new BTree(m_pageManager);
        
        for (int i = 0; i < 100; i++)
        {
            byte[] key = BitConverter.GetBytes(i);
            byte[] value = BitConverter.GetBytes(i * 10);
            tree.Insert(key, value);
        }
        
        for (int i = 0; i < 100; i++)
        {
            byte[] key = BitConverter.GetBytes(i);
            var result = tree.Search(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(BitConverter.ToInt32(result!), Is.EqualTo(i * 10));
        }
    }

    #endregion

    #region Persistence

    [Test]
    public void ReopenTreeAfterFlushTest()
    {
        uint rootPage;
        
        // Create and populate tree
        using (var tree = new BTree(m_pageManager))
        {
            for (int i = 0; i < 50; i++)
            {
                byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
                byte[] value = TextEncoding.UTF8.GetBytes($"value{i}");
                tree.Insert(key, value);
            }
            
            rootPage = tree.RootPageNumber;
        }
        
        // Reopen with same root page
        using (var tree = new BTree(m_pageManager, rootPage))
        {
            Assert.That(tree.Count(), Is.EqualTo(50));
            
            // Verify keys
            for (int i = 0; i < 50; i++)
            {
                byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
                Assert.That(tree.ContainsKey(key), Is.True);
            }
        }
    }

    [Test]
    public void SchemaRootPageUpdatedTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert enough to cause root split
        for (int i = 0; i < 500; i++)
        {
            tree.Insert(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
        }
        
        // Schema root page should match tree root
        var header = m_pageManager.GetHeader();
        Assert.That(header.SchemaRootPage, Is.EqualTo(tree.RootPageNumber));
    }

    #endregion

    #region Count Tests

    [Test]
    public void CountIsAccurateAfterOperationsTest()
    {
        using var tree = new BTree(m_pageManager);
        
        Assert.That(tree.Count(), Is.EqualTo(0));
        
        // Insert
        for (int i = 0; i < 100; i++)
        {
            tree.Insert(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
        }
        Assert.That(tree.Count(), Is.EqualTo(100));
        
        // Delete some
        for (int i = 0; i < 30; i++)
        {
            tree.Delete(BitConverter.GetBytes(i));
        }
        Assert.That(tree.Count(), Is.EqualTo(70));
        
        // Upsert (update existing)
        tree.Upsert(BitConverter.GetBytes(50), "updated"u8.ToArray());
        Assert.That(tree.Count(), Is.EqualTo(70)); // No change
        
        // Upsert (insert new)
        tree.Upsert(BitConverter.GetBytes(1000), "new"u8.ToArray());
        Assert.That(tree.Count(), Is.EqualTo(71));
    }

    #endregion

    #region Stress Tests

    [Test]
    public void InsertDeleteStressTest()
    {
        using var tree = new BTree(m_pageManager);
        
        var random = new Random(42);
        var existingKeys = new HashSet<int>();
        
        for (int round = 0; round < 10; round++)
        {
            // Insert 100 random keys
            for (int i = 0; i < 100; i++)
            {
                int keyInt = random.Next(10000);
                if (existingKeys.Add(keyInt))
                {
                    tree.Insert(BitConverter.GetBytes(keyInt), BitConverter.GetBytes(keyInt));
                }
            }
            
            // Delete ~30% of keys
            var toDelete = existingKeys.Where(_ => random.Next(100) < 30).ToList();
            foreach (var keyInt in toDelete)
            {
                tree.Delete(BitConverter.GetBytes(keyInt));
                existingKeys.Remove(keyInt);
            }
            
            Assert.That(tree.Count(), Is.EqualTo(existingKeys.Count));
        }
        
        // Verify remaining keys
        foreach (var keyInt in existingKeys)
        {
            Assert.That(tree.ContainsKey(BitConverter.GetBytes(keyInt)), Is.True);
        }
    }

    [Test]
    public void LargeDatasetTest()
    {
        using var tree = new BTree(m_pageManager);
        
        const int count = 5000;
        
        for (int i = 0; i < count; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D6}");
            byte[] value = TextEncoding.UTF8.GetBytes($"value{i}");
            tree.Insert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(count));
        
        // Verify random sample
        var random = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            int idx = random.Next(count);
            byte[] key = TextEncoding.UTF8.GetBytes($"key{idx:D6}");
            var result = tree.Search(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo($"value{idx}"));
        }
    }

    #endregion

    #region Extended Stress Tests

    [Test]
    [Category("Stress")]
    public void HeavyInsertDeleteCycleTest()
    {
        using var storage = new MemoryStorage(4096, 5000);
        using var pageManager = new PageManager(storage);
        using var tree = new BTree(pageManager);
        
        var random = new Random(12345);
        var existingKeys = new Dictionary<int, byte[]>();
        
        const int totalOperations = 50000;
        int insertCount = 0;
        int deleteCount = 0;
        int updateCount = 0;
        
        // Track specific key for debugging
        const int trackedKey = 70983;
        int trackedKeyLastSeenOp = -1;
        string lastOperationDescription = "";
        
        for (int op = 0; op < totalOperations; op++)
        {
            int action = random.Next(100);
            
            // Check tracked key at every operation BEFORE we do anything
            if (existingKeys.ContainsKey(trackedKey))
            {
                if (tree.ContainsKey(BitConverter.GetBytes(trackedKey)))
                {
                    trackedKeyLastSeenOp = op;
                }
                else
                {
                    Assert.Fail($"Op {op}: Tracked key {trackedKey} disappeared! Last seen at op {trackedKeyLastSeenOp}.\n" +
                               $"Last operation was: {lastOperationDescription}");
                }
            }
            
            if (action < 60 || existingKeys.Count == 0) // 60% insert
            {
                int keyInt = random.Next(100000);
                byte[] key = BitConverter.GetBytes(keyInt);
                byte[] value = new byte[random.Next(10, 200)];
                random.NextBytes(value);
                
                if (!existingKeys.ContainsKey(keyInt))
                {
                    bool inserted = tree.Insert(key, value);
                    if (inserted)
                    {
                        existingKeys[keyInt] = value;
                        insertCount++;
                        lastOperationDescription = $"Insert key {keyInt}, value size {value.Length}";
                        
                        // Verify just-inserted key is searchable
                        if (!tree.ContainsKey(key))
                        {
                            Assert.Fail($"Op {op}: Just inserted key {keyInt} not found!");
                        }
                    }
                    else
                    {
                        lastOperationDescription = $"Insert key {keyInt} - FAILED (duplicate)";
                    }
                }
                else
                {
                    lastOperationDescription = $"Skip insert key {keyInt} - already exists in dict";
                }
            }
            else if (action < 80) // 20% delete
            {
                if (existingKeys.Count > 0)
                {
                    var keyToDelete = existingKeys.Keys.ElementAt(random.Next(existingKeys.Count));
                    byte[] keyBytes = BitConverter.GetBytes(keyToDelete);
                    
                    bool deleted = tree.Delete(keyBytes);
                    if (deleted)
                    {
                        existingKeys.Remove(keyToDelete);
                        deleteCount++;
                        lastOperationDescription = $"Delete key {keyToDelete}";
                    }
                    else
                    {
                        lastOperationDescription = $"Delete key {keyToDelete} - FAILED";
                    }
                }
                else
                {
                    lastOperationDescription = "Delete - no keys to delete";
                }
            }
            else // 20% upsert existing key
            {
                if (existingKeys.Count > 0)
                {
                    var keyToUpdate = existingKeys.Keys.ElementAt(random.Next(existingKeys.Count));
                    byte[] keyBytes = BitConverter.GetBytes(keyToUpdate);
                    byte[] newValue = new byte[random.Next(10, 200)];
                    random.NextBytes(newValue);
                    
                    bool inserted = tree.Upsert(keyBytes, newValue);
                    if (!inserted) // Was update, not insert
                    {
                        existingKeys[keyToUpdate] = newValue;
                        updateCount++;
                        lastOperationDescription = $"Upsert (update) key {keyToUpdate}, new value size {newValue.Length}";
                    }
                    else
                    {
                        Assert.Fail($"Op {op}: Upsert returned true (insert) for existing key {keyToUpdate}");
                    }
                }
                else
                {
                    lastOperationDescription = "Upsert - no keys to update";
                }
            }
        }
        
        // Verify final state
        Assert.That(tree.Count(), Is.EqualTo(existingKeys.Count), 
            $"Count mismatch after {insertCount} inserts, {deleteCount} deletes, {updateCount} updates");
        
        // Verify all existing keys
        foreach (var keyInt in existingKeys.Keys)
        {
            var result = tree.Search(BitConverter.GetBytes(keyInt));
            Assert.That(result, Is.Not.Null, $"Key {keyInt} not found");
        }
    }

    [Test]
    [Category("Stress")]
    public void SequentialBulkInsertTest()
    {
        using var storage = new MemoryStorage(4096, 10000);
        using var pageManager = new PageManager(storage);
        using var tree = new BTree(pageManager);
        
        const int count = 100000;
        
        // Sequential insert using string keys for proper lexicographic ordering
        for (int i = 0; i < count; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"{i:D8}");
            byte[] value = BitConverter.GetBytes(i);
            tree.Insert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(count));
        
        // Verify range scan returns all in order
        var all = tree.GetAll().ToList();
        Assert.That(all.Count, Is.EqualTo(count));
        
        for (int i = 0; i < count; i++)
        {
            int actualValue = BitConverter.ToInt32(all[i].Value);
            Assert.That(actualValue, Is.EqualTo(i), $"Entry at position {i} has wrong value");
        }
    }

    [Test]
    [Category("Stress")]
    public void ReverseSequentialBulkInsertTest()
    {
        using var storage = new MemoryStorage(4096, 10000);
        using var pageManager = new PageManager(storage);
        using var tree = new BTree(pageManager);
        
        const int count = 100000;
        
        // Reverse sequential insert using string keys
        for (int i = count - 1; i >= 0; i--)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"{i:D8}");
            byte[] value = BitConverter.GetBytes(i);
            tree.Insert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(count));
        
        // Verify all searchable
        for (int i = 0; i < count; i += 1000)
        {
            Assert.That(tree.ContainsKey(TextEncoding.UTF8.GetBytes($"{i:D8}")), Is.True);
        }
    }

    [Test]
    [Category("Stress")]
    public void MixedValueSizesStressTest()
    {
        using var storage = new MemoryStorage(4096, 5000);
        using var pageManager = new PageManager(storage);
        using var tree = new BTree(pageManager);
        
        var random = new Random(42);
        var entries = new Dictionary<int, byte[]>();
        
        const int count = 10000;
        
        for (int i = 0; i < count; i++)
        {
            byte[] key = BitConverter.GetBytes(i);
            
            // Mix of sizes: small, medium, large (overflow)
            int sizeCategory = random.Next(3);
            int valueSize = sizeCategory switch
            {
                0 => random.Next(10, 50),                           // small
                1 => random.Next(100, tree.MaxInlineValueSize),     // medium  
                _ => tree.MaxInlineValueSize + random.Next(100, 1000) // large (overflow)
            };
            
            byte[] value = new byte[valueSize];
            random.NextBytes(value);
            
            tree.Insert(key, value);
            entries[i] = value;
        }
        
        Assert.That(tree.Count(), Is.EqualTo(count));
        
        // Verify random sample
        for (int i = 0; i < 100; i++)
        {
            int idx = random.Next(count);
            var result = tree.Search(BitConverter.GetBytes(idx));
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.SequenceEqual(entries[idx]), Is.True, $"Value mismatch at index {idx}");
        }
    }

    [Test]
    [Category("Stress")]
    public void RangeScanStressTest()
    {
        using var storage = new MemoryStorage(4096, 5000);
        using var pageManager = new PageManager(storage);
        using var tree = new BTree(pageManager);
        
        const int count = 50000;
        
        // Use string keys for proper lexicographic ordering
        for (int i = 0; i < count; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"{i:D8}");
            byte[] value = BitConverter.GetBytes(i * 2);
            tree.Insert(key, value);
        }
        
        // Test multiple range scans
        var random = new Random(42);
        for (int scan = 0; scan < 100; scan++)
        {
            int start = random.Next(count - 1000);
            int end = start + random.Next(100, 1000);
            
            byte[] startKey = TextEncoding.UTF8.GetBytes($"{start:D8}");
            byte[] endKey = TextEncoding.UTF8.GetBytes($"{end:D8}");
            
            var range = tree.GetRange(startKey, endKey).ToList();
            
            Assert.That(range.Count, Is.EqualTo(end - start), $"Range [{start}, {end}) returned wrong count: {range.Count}");
            
            for (int i = 0; i < range.Count; i++)
            {
                int expectedKey = start + i;
                string actualKeyStr = TextEncoding.UTF8.GetString(range[i].Key);
                int actualKey = int.Parse(actualKeyStr);
                int actualValue = BitConverter.ToInt32(range[i].Value);
                
                Assert.That(actualKey, Is.EqualTo(expectedKey), $"Key mismatch at index {i}");
                Assert.That(actualValue, Is.EqualTo(expectedKey * 2), $"Value mismatch at index {i}");
            }
        }
    }

    #endregion

    #region Compaction Tests

    [Test]
    public void CompactionRecoversFreeSpaceAfterDeleteTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert multiple entries
        for (int i = 0; i < 50; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = TextEncoding.UTF8.GetBytes($"value{i:D3}");
            tree.Insert(key, value);
        }
        
        // Delete half of the entries (creates fragmentation)
        for (int i = 0; i < 25; i++)
        {
            tree.Delete(TextEncoding.UTF8.GetBytes($"key{i:D3}"));
        }
        
        Assert.That(tree.Count(), Is.EqualTo(25));
        
        // Insert more entries - should succeed due to compaction
        for (int i = 100; i < 150; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = TextEncoding.UTF8.GetBytes($"value{i:D3}");
            Assert.That(tree.Insert(key, value), Is.True, $"Failed to insert key{i:D3}");
        }
        
        Assert.That(tree.Count(), Is.EqualTo(75));
        
        // Verify all remaining entries
        for (int i = 25; i < 50; i++)
        {
            Assert.That(tree.ContainsKey(TextEncoding.UTF8.GetBytes($"key{i:D3}")), Is.True);
        }
        for (int i = 100; i < 150; i++)
        {
            Assert.That(tree.ContainsKey(TextEncoding.UTF8.GetBytes($"key{i:D3}")), Is.True);
        }
    }

    [Test]
    public void UpsertWithDifferentSizeTriggerCompactionTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert with small values
        for (int i = 0; i < 100; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = TextEncoding.UTF8.GetBytes("s"); // small value
            tree.Insert(key, value);
        }
        
        // Update to larger values - this creates fragmentation as old cells are abandoned
        for (int i = 0; i < 100; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = TextEncoding.UTF8.GetBytes($"larger_value_{i:D5}"); // larger value
            tree.Upsert(key, value);
        }
        
        Assert.That(tree.Count(), Is.EqualTo(100));
        
        // Verify all values are updated
        for (int i = 0; i < 100; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            var result = tree.Search(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(TextEncoding.UTF8.GetString(result!), Is.EqualTo($"larger_value_{i:D5}"));
        }
    }

    [Test]
    public void RepeatedDeleteInsertCyclesTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Multiple cycles of delete-insert to test compaction under stress
        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Insert 100 entries
            for (int i = 0; i < 100; i++)
            {
                byte[] key = TextEncoding.UTF8.GetBytes($"c{cycle}k{i:D3}");
                byte[] value = TextEncoding.UTF8.GetBytes($"v{cycle}{i:D3}");
                tree.Insert(key, value);
            }
            
            // Delete odd entries
            for (int i = 1; i < 100; i += 2)
            {
                tree.Delete(TextEncoding.UTF8.GetBytes($"c{cycle}k{i:D3}"));
            }
        }
        
        // Each cycle should have 50 remaining entries
        Assert.That(tree.Count(), Is.EqualTo(500));
        
        // Verify entries
        for (int cycle = 0; cycle < 10; cycle++)
        {
            for (int i = 0; i < 100; i += 2)
            {
                Assert.That(tree.ContainsKey(TextEncoding.UTF8.GetBytes($"c{cycle}k{i:D3}")), Is.True,
                    $"Missing key c{cycle}k{i:D3}");
            }
        }
    }

    [Test]
    public void UpdateToSmallerValueTest()
    {
        using var tree = new BTree(m_pageManager);
        
        // Insert with large inline values
        for (int i = 0; i < 50; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = new byte[200]; // large inline value
            Array.Fill(value, (byte)i);
            tree.Insert(key, value);
        }
        
        // Update to smaller values
        for (int i = 0; i < 50; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            byte[] value = new byte[10]; // much smaller
            Array.Fill(value, (byte)(i + 100));
            tree.Upsert(key, value);
        }
        
        // Verify updates
        for (int i = 0; i < 50; i++)
        {
            byte[] key = TextEncoding.UTF8.GetBytes($"key{i:D3}");
            var result = tree.Search(key);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Length, Is.EqualTo(10));
            Assert.That(result[0], Is.EqualTo((byte)(i + 100)));
        }
    }

    [Test]
    [Category("Stress")]
    public void FragmentationStressTest()
    {
        using var storage = new MemoryStorage(4096, 3000);
        using var pageManager = new PageManager(storage);
        using var tree = new BTree(pageManager);
        
        var random = new Random(42);
        var existingKeys = new Dictionary<int, int>(); // key -> expected value size
        
        // Perform many operations that cause fragmentation
        for (int op = 0; op < 20000; op++)
        {
            int action = random.Next(100);
            
            if (action < 40 || existingKeys.Count < 100) // 40% insert
            {
                int keyInt = random.Next(50000);
                if (!existingKeys.ContainsKey(keyInt))
                {
                    int valueSize = random.Next(10, 300);
                    byte[] value = new byte[valueSize];
                    random.NextBytes(value);
                    
                    if (tree.Insert(BitConverter.GetBytes(keyInt), value))
                    {
                        existingKeys[keyInt] = valueSize;
                    }
                }
            }
            else if (action < 70) // 30% delete
            {
                if (existingKeys.Count > 0)
                {
                    var keyToDelete = existingKeys.Keys.ElementAt(random.Next(existingKeys.Count));
                    if (tree.Delete(BitConverter.GetBytes(keyToDelete)))
                    {
                        existingKeys.Remove(keyToDelete);
                    }
                }
            }
            else // 30% update with different size
            {
                if (existingKeys.Count > 0)
                {
                    var keyToUpdate = existingKeys.Keys.ElementAt(random.Next(existingKeys.Count));
                    int newSize = random.Next(10, 300); // different size to cause fragmentation
                    byte[] newValue = new byte[newSize];
                    random.NextBytes(newValue);
                    
                    tree.Upsert(BitConverter.GetBytes(keyToUpdate), newValue);
                    existingKeys[keyToUpdate] = newSize;
                }
            }
        }
        
        Assert.That(tree.Count(), Is.EqualTo(existingKeys.Count));
        
        // Verify all keys
        foreach (var (keyInt, expectedSize) in existingKeys)
        {
            var result = tree.Search(BitConverter.GetBytes(keyInt));
            Assert.That(result, Is.Not.Null, $"Key {keyInt} not found");
            Assert.That(result!.Length, Is.EqualTo(expectedSize), $"Key {keyInt} has wrong value size");
        }
    }

    #endregion

    private class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.SequenceEqual(y);
        }
        public int GetHashCode(byte[] obj) => obj.Length > 0 ? obj[0] : 0;
    }
}
