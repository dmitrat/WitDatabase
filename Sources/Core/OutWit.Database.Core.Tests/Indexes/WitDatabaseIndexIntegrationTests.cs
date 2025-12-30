using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Indexes
{
    [TestFixture]
    public class WitDatabaseIndexIntegrationTests
    {
        #region Fields

        private string m_testDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "WitDB_IndexIntegration_" + Guid.NewGuid().ToString("N"));
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

        #region InMemory Database Tests

        [Test]
        public void InMemoryDatabaseHasIndexManagerTest()
        {
            // Act
            using var db = WitDatabase.CreateInMemory();

            // Assert
            Assert.That(db.SupportsIndexes, Is.True);
            Assert.That(db.IndexManager, Is.Not.Null);
        }

        [Test]
        public void InMemoryDatabaseCanCreateIndexTest()
        {
            // Arrange
            using var db = WitDatabase.CreateInMemory();

            // Act
            var index = db.CreateIndex("idx_test", isUnique: true);

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index.Name, Is.EqualTo("idx_test"));
            Assert.That(db.HasIndex("idx_test"), Is.True);
        }

        [Test]
        public void InMemoryDatabaseIndexOperationsWorkTest()
        {
            // Arrange
            using var db = WitDatabase.CreateInMemory();
            var index = db.CreateIndex("idx_email", isUnique: true);

            var indexKey = GetBytes("test@example.com");
            var primaryKey = GetBytes("user-123");

            // Act
            index.Add(indexKey, primaryKey);
            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(primaryKey));
        }

        [Test]
        public void InMemoryDatabaseCanDropIndexTest()
        {
            // Arrange
            using var db = WitDatabase.CreateInMemory();
            db.CreateIndex("idx_test", isUnique: true);

            // Act
            bool dropped = db.DropIndex("idx_test");

            // Assert
            Assert.That(dropped, Is.True);
            Assert.That(db.HasIndex("idx_test"), Is.False);
        }

        [Test]
        public void InMemoryDatabaseIndexNamesReturnsAllIndexesTest()
        {
            // Arrange
            using var db = WitDatabase.CreateInMemory();
            db.CreateIndex("idx_a", isUnique: true);
            db.CreateIndex("idx_b", isUnique: false);
            db.CreateIndex("idx_c", isUnique: true);

            // Act
            var names = db.IndexNames;

            // Assert
            Assert.That(names, Has.Count.EqualTo(3));
            Assert.That(names, Contains.Item("idx_a"));
            Assert.That(names, Contains.Item("idx_b"));
            Assert.That(names, Contains.Item("idx_c"));
        }

        #endregion

        #region File-Based Database Tests

        [Test]
        public void FileDatabaseHasIndexManagerTest()
        {
            // Arrange
            var dbPath = Path.Combine(m_testDir, "test.db");

            // Act
            using var db = WitDatabase.Create(dbPath);

            // Assert
            Assert.That(db.SupportsIndexes, Is.True);
            Assert.That(db.IndexManager, Is.Not.Null);
        }

        [Test]
        public void FileDatabaseCanCreateAndUseIndexTest()
        {
            // Arrange
            var dbPath = Path.Combine(m_testDir, "test.db");
            using var db = WitDatabase.Create(dbPath);

            // Act
            var index = db.CreateIndex("idx_category", isUnique: false);
            
            var category = GetBytes("electronics");
            index.Add(category, GetBytes("product-1"));
            index.Add(category, GetBytes("product-2"));
            index.Add(category, GetBytes("product-3"));

            var results = index.Find(category).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(3));
        }

        [Test]
        public void FileDatabaseCreatesIndexDirectoryTest()
        {
            // Arrange
            var dbPath = Path.Combine(m_testDir, "test.db");
            using var db = WitDatabase.Create(dbPath);

            // Act
            db.CreateIndex("idx_test", isUnique: true);
            db.Flush();

            // Assert
            // Index directory is now named after the database file: {filename}_indexes
            var indexDir = Path.Combine(m_testDir, "test.db_indexes");
            Assert.That(Directory.Exists(indexDir), Is.True);
        }

        #endregion

        #region LSM Database Tests

        [Test]
        public void LsmDatabaseHasIndexManagerTest()
        {
            // Arrange
            var dbDir = Path.Combine(m_testDir, "lsm_db");

            // Act
            using var db = new WitDatabaseBuilder()
                .WithLsmTree(dbDir)
                .WithTransactions()
                .Build();

            // Assert
            Assert.That(db.SupportsIndexes, Is.True);
            Assert.That(db.IndexManager, Is.Not.Null);
        }

        [Test]
        public void LsmDatabaseCanCreateAndUseIndexTest()
        {
            // Arrange
            var dbDir = Path.Combine(m_testDir, "lsm_db");
            using var db = new WitDatabaseBuilder()
                .WithLsmTree(dbDir)
                .WithTransactions()
                .Build();

            // Act
            var index = db.CreateIndex("idx_tag", isUnique: false);
            
            var tag = GetBytes("important");
            for (int i = 0; i < 10; i++)
            {
                index.Add(tag, GetBytes($"doc-{i}"));
            }

            var results = index.Find(tag).ToList();

            // Assert
            Assert.That(results, Has.Count.EqualTo(10));
        }

        #endregion

        #region Custom Store Tests

        [Test]
        public void CustomStoreWithCustomIndexFactoryWorksTest()
        {
            // Arrange - user provides both custom store and custom index factory
            var customStore = new StoreInMemory();
            var customFactory = new SecondaryIndexFactoryKeyValueStore(
                _ => new StoreInMemory(),
                "custom"
            );

            // Act
            using var db = new WitDatabaseBuilder()
                .WithStore(customStore)
                .WithSecondaryIndexFactory(customFactory)
                .WithTransactions()
                .Build();

            var index = db.CreateIndex("idx_test", isUnique: true);
            index.Add(GetBytes("key"), GetBytes("pk"));

            // Assert
            Assert.That(index.Find(GetBytes("key")).Any(), Is.True);
        }

        [Test]
        public void CustomStoreWithoutIndexFactoryUsesInMemoryIndexesTest()
        {
            // Arrange - user provides custom store but no index factory
            var customStore = new StoreInMemory();

            // Act
            using var db = new WitDatabaseBuilder()
                .WithStore(customStore)
                .WithTransactions()
                .Build();

            var index = db.CreateIndex("idx_test", isUnique: true);
            index.Add(GetBytes("key"), GetBytes("pk"));

            // Assert - should work with default in-memory indexes
            Assert.That(index.Find(GetBytes("key")).Any(), Is.True);
        }

        #endregion

        #region Custom Factory Tests

        [Test]
        public void CustomFactoryIsUsedTest()
        {
            // Arrange
            var factoryUsed = false;
            var customFactory = new SecondaryIndexFactoryKeyValueStore(
                name =>
                {
                    factoryUsed = true;
                    return new StoreInMemory();
                },
                "custom"
            );

            // Act
            using var db = new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithSecondaryIndexFactory(customFactory)
                .WithTransactions()
                .Build();

            db.CreateIndex("idx_test", isUnique: true);

            // Assert
            Assert.That(factoryUsed, Is.True);
        }

        [Test]
        public void CustomIndexDirectoryIsUsedTest()
        {
            // Arrange
            var dbPath = Path.Combine(m_testDir, "test.db");
            var customIndexDir = Path.Combine(m_testDir, "custom_indexes");

            // Act
            using var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .WithIndexDirectory(customIndexDir)
                .WithTransactions()
                .Build();

            db.CreateIndex("idx_test", isUnique: true);
            db.Flush();

            // Assert
            Assert.That(Directory.Exists(customIndexDir), Is.True);
        }

        #endregion

        #region Encryption Tests

        [Test]
        public void EncryptedDatabaseHasIndexManagerTest()
        {
            // Arrange
            var dbPath = Path.Combine(m_testDir, "encrypted.db");

            // Act
            using var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .WithEncryption("password123")
                .WithTransactions()
                .Build();

            // Assert
            Assert.That(db.SupportsIndexes, Is.True);
        }

        [Test]
        public void EncryptedDatabaseCanUseIndexesTest()
        {
            // Arrange
            var dbPath = Path.Combine(m_testDir, "encrypted.db");
            using var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .WithEncryption("password123")
                .WithTransactions()
                .Build();

            // Act
            var index = db.CreateIndex("idx_secret", isUnique: true);
            index.Add(GetBytes("secret-key"), GetBytes("secret-pk"));
            db.Flush();

            // Assert
            var results = index.Find(GetBytes("secret-key")).ToList();
            Assert.That(results, Has.Count.EqualTo(1));
        }

        [Test]
        public void EncryptedLsmDatabaseCanUseIndexesTest()
        {
            // Arrange
            var dbDir = Path.Combine(m_testDir, "encrypted_lsm");
            using var db = new WitDatabaseBuilder()
                .WithLsmTree(dbDir)
                .WithEncryption("password123")
                .WithTransactions()
                .Build();

            // Act
            var index = db.CreateIndex("idx_secret", isUnique: false);
            for (int i = 0; i < 5; i++)
            {
                index.Add(GetBytes("tag"), GetBytes($"pk-{i}"));
            }

            // Assert
            var results = index.Find(GetBytes("tag")).ToList();
            Assert.That(results, Has.Count.EqualTo(5));
        }

        #endregion

        #region Without Transactions Tests

        [Test]
        public void DatabaseWithoutTransactionsHasIndexManagerTest()
        {
            // Arrange & Act
            using var db = new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithoutTransactions()
                .Build();

            // Assert
            Assert.That(db.SupportsIndexes, Is.True);
            Assert.That(db.SupportsTransactions, Is.False);
        }

        [Test]
        public void DatabaseWithoutTransactionsCanUseIndexesTest()
        {
            // Arrange
            using var db = new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .WithoutTransactions()
                .Build();

            // Act
            var index = db.CreateIndex("idx_test", isUnique: true);
            index.Add(GetBytes("key"), GetBytes("pk"));

            // Assert
            Assert.That(index.Find(GetBytes("key")).Any(), Is.True);
        }

        #endregion

        #region Fluent API Tests

        [Test]
        public void WithSecondaryIndexFactoryThrowsOnNullTest()
        {
            // Arrange
            var builder = new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.WithSecondaryIndexFactory(null!));
        }

        [Test]
        public void WithIndexDirectoryThrowsOnNullTest()
        {
            // Arrange
            var builder = new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.WithIndexDirectory(null!));
        }

        #endregion

        #region GetIndex Tests

        [Test]
        public void GetIndexReturnsExistingIndexTest()
        {
            // Arrange
            using var db = WitDatabase.CreateInMemory();
            db.CreateIndex("idx_test", isUnique: true);

            // Act
            var index = db.GetIndex("idx_test");

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index!.Name, Is.EqualTo("idx_test"));
        }

        [Test]
        public void GetIndexReturnsNullForNonExistentTest()
        {
            // Arrange
            using var db = WitDatabase.CreateInMemory();

            // Act
            var index = db.GetIndex("nonexistent");

            // Assert
            Assert.That(index, Is.Null);
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void DisposeDisposesIndexManagerTest()
        {
            // Arrange
            var db = WitDatabase.CreateInMemory();
            var index = db.CreateIndex("idx_test", isUnique: true);

            // Act
            db.Dispose();

            // Assert - accessing disposed index should throw
            Assert.Throws<ObjectDisposedException>(() => index.Add(GetBytes("key"), GetBytes("pk")));
        }

        #endregion

        #region All Combinations Tests

        [Test]
        [TestCase(true, true, false)]   // BTree + Memory + NoEncryption
        [TestCase(true, false, false)]  // BTree + File + NoEncryption
        [TestCase(false, false, false)] // LSM + Directory + NoEncryption
        [TestCase(true, true, true)]    // BTree + Memory + Encryption
        [TestCase(true, false, true)]   // BTree + File + Encryption
        [TestCase(false, false, true)]  // LSM + Directory + Encryption
        public void AllStorageCombinationsWorkWithIndexesTest(bool useBTree, bool useMemory, bool useEncryption)
        {
            // Arrange
            var builder = new WitDatabaseBuilder();

            if (useBTree)
            {
                builder.WithBTree();
                if (useMemory)
                {
                    builder.WithMemoryStorage();
                }
                else
                {
                    builder.WithFilePath(Path.Combine(m_testDir, "test.db"));
                }
            }
            else
            {
                builder.WithLsmTree(Path.Combine(m_testDir, "lsm_db"));
            }

            if (useEncryption)
            {
                builder.WithEncryption("test_password");
            }

            builder.WithTransactions();

            // Act
            using var db = builder.Build();
            var index = db.CreateIndex("idx_test", isUnique: false);

            var testKey = GetBytes("test_key");
            for (int i = 0; i < 5; i++)
            {
                index.Add(testKey, GetBytes($"pk_{i}"));
            }

            var results = index.Find(testKey).ToList();

            // Assert
            Assert.That(db.SupportsIndexes, Is.True);
            Assert.That(results, Has.Count.EqualTo(5));
        }

        #endregion

        #region Helpers

        private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #endregion
    }
}
