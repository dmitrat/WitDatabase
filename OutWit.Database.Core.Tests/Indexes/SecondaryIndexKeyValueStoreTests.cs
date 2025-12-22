using NUnit.Framework;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Indexes
{
    [TestFixture]
    public class SecondaryIndexKeyValueStoreTests
    {
        #region Fields

        private string m_testDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "WitDB_IndexKVTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_testDir))
            {
                try { Directory.Delete(m_testDir, true); } catch { }
            }
        }

        #endregion

        #region Unique Index Tests

        [Test]
        public void UniqueIndexAddAndFindSucceedsTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: true, ownsStore: false);
            var indexKey = GetBytes("email@example.com");
            var primaryKey = GetBytes("user-123");

            // Act
            index.Add(indexKey, primaryKey);
            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(primaryKey));
        }

        [Test]
        public void UniqueIndexRejectsDuplicateKeyTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: true, ownsStore: false);
            var indexKey = GetBytes("email@example.com");
            var pk1 = GetBytes("user-1");
            var pk2 = GetBytes("user-2");

            index.Add(indexKey, pk1);

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => index.Add(indexKey, pk2));
        }

        [Test]
        public void UniqueIndexAllowsSameKeyAfterRemoveTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: true, ownsStore: false);
            var indexKey = GetBytes("email@example.com");
            var pk1 = GetBytes("user-1");
            var pk2 = GetBytes("user-2");

            index.Add(indexKey, pk1);
            index.Remove(indexKey, pk1);

            // Act
            index.Add(indexKey, pk2);
            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(pk2));
        }

        #endregion

        #region Non-Unique Index Tests

        [Test]
        public void NonUniqueIndexAllowsDuplicateKeysTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: false, ownsStore: false);
            var indexKey = GetBytes("category-electronics");
            var pk1 = GetBytes("product-1");
            var pk2 = GetBytes("product-2");
            var pk3 = GetBytes("product-3");

            // Act
            index.Add(indexKey, pk1);
            index.Add(indexKey, pk2);
            index.Add(indexKey, pk3);

            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(results, Contains.Item(pk1));
            Assert.That(results, Contains.Item(pk2));
            Assert.That(results, Contains.Item(pk3));
        }

        [Test]
        public void NonUniqueIndexRemovesSingleEntryTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: false, ownsStore: false);
            var indexKey = GetBytes("category-electronics");
            var pk1 = GetBytes("product-1");
            var pk2 = GetBytes("product-2");

            index.Add(indexKey, pk1);
            index.Add(indexKey, pk2);

            // Act
            bool removed = index.Remove(indexKey, pk1);
            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(removed, Is.True);
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(pk2));
        }

        [Test]
        public void NonUniqueIndexRemoveAllRemovesAllEntriesTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: false, ownsStore: false);
            var indexKey = GetBytes("category-electronics");
            var pk1 = GetBytes("product-1");
            var pk2 = GetBytes("product-2");
            var pk3 = GetBytes("product-3");

            index.Add(indexKey, pk1);
            index.Add(indexKey, pk2);
            index.Add(indexKey, pk3);

            // Act
            int removed = index.RemoveAll(indexKey);
            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(removed, Is.EqualTo(3));
            Assert.That(results, Is.Empty);
        }

        #endregion

        #region Count Tests

        [Test]
        public void CountReturnsCorrectValueTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: true, ownsStore: false);

            // Act
            index.Add(GetBytes("key1"), GetBytes("pk-1"));
            index.Add(GetBytes("key2"), GetBytes("pk-2"));
            index.Add(GetBytes("key3"), GetBytes("pk-3"));

            // Assert
            Assert.That(index.Count, Is.EqualTo(3));
        }

        [Test]
        public void ClearRemovesAllEntriesTest()
        {
            // Arrange
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test_idx", store, isUnique: true, ownsStore: false);

            index.Add(GetBytes("key1"), GetBytes("pk-1"));
            index.Add(GetBytes("key2"), GetBytes("pk-2"));
            index.Add(GetBytes("key3"), GetBytes("pk-3"));

            // Act
            index.Clear();

            // Assert
            Assert.That(index.Count, Is.EqualTo(0));
            Assert.That(index.Find(GetBytes("key1")).Any(), Is.False);
        }

        #endregion

        #region Properties Tests

        [Test]
        public void NamePropertyReturnsCorrectValueTest()
        {
            // Arrange & Act
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("my_index", store, isUnique: true, ownsStore: false);

            // Assert
            Assert.That(index.Name, Is.EqualTo("my_index"));
        }

        [Test]
        public void IsUniquePropertyReturnsCorrectValueTest()
        {
            // Arrange & Act
            using var store1 = new StoreInMemory();
            using var store2 = new StoreInMemory();
            using var uniqueIndex = new SecondaryIndexKeyValueStore("unique", store1, isUnique: true, ownsStore: false);
            using var nonUniqueIndex = new SecondaryIndexKeyValueStore("non_unique", store2, isUnique: false, ownsStore: false);

            // Assert
            Assert.That(uniqueIndex.IsUnique, Is.True);
            Assert.That(nonUniqueIndex.IsUnique, Is.False);
        }

        [Test]
        public void ImplementsISecondaryIndexTest()
        {
            // Arrange & Act
            using var store = new StoreInMemory();
            using var index = new SecondaryIndexKeyValueStore("test", store, isUnique: true, ownsStore: false);

            // Assert
            Assert.That(index, Is.InstanceOf<ISecondaryIndex>());
        }

        #endregion

        #region With LSM Store Tests

        [Test]
        public void WorksWithLsmStoreTest()
        {
            // Arrange
            var lsmDir = Path.Combine(m_testDir, "lsm_index");
            using var store = new StoreLsm(lsmDir);
            using var index = new SecondaryIndexKeyValueStore("lsm_idx", store, isUnique: false, ownsStore: false);
            var indexKey = GetBytes("category");

            // Act
            for (int i = 0; i < 10; i++)
            {
                index.Add(indexKey, GetBytes($"pk-{i}"));
            }

            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(10));
            Assert.That(index.Count, Is.EqualTo(10));
        }

        #endregion

        #region Helpers

        private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #endregion
    }
}
