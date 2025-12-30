using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Builder
{
    /// <summary>
    /// Tests for automatic secondary index factory creation.
    /// </summary>
    [TestFixture]
    public class AutomaticIndexFactoryTests
    {
        #region Fields

        private string m_testDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "WitDB_AutoIdx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            if (Directory.Exists(m_testDir))
            {
                try { Directory.Delete(m_testDir, true); } catch { }
            }
        }

        #endregion

        #region BTree Index Factory Tests

        [Test]
        public void BTreeDatabaseCreatesFileBasedIndexesTest()
        {
            var dbPath = Path.Combine(m_testDir, "btree.db");
            
            using (var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .WithTransactions()
                .Build())
            {
                // Create index - should create file in {filename}_indexes directory
                var index = db.CreateIndex("idx_test", isUnique: true);
                index.Add(GetBytes("key"), GetBytes("pk"));
                db.Flush();
            }
            
            // Verify index file was created
            // Index directory is now named after the database file: {filename}_indexes
            var indexDir = Path.Combine(m_testDir, "btree.db_indexes");
            Assert.That(Directory.Exists(indexDir), Is.True, "Index directory should be created");
            Assert.That(File.Exists(Path.Combine(indexDir, "idx_test.idx")), Is.True, 
                "Index file should be created");
        }

        [Test]
        public void BTreeIndexDataPersistsAcrossReopensTest()
        {
            var dbPath = Path.Combine(m_testDir, "btree_persist.db");
            
            // Create and populate index
            using (var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .Build())
            {
                var index = db.CreateIndex("idx_email", isUnique: true);
                index.Add(GetBytes("john@example.com"), GetBytes("user-1"));
                index.Add(GetBytes("jane@example.com"), GetBytes("user-2"));
                db.Flush();
            }
            
            // Reopen and verify data
            using (var db = WitDatabase.Open(dbPath))
            {
                Assert.That(db.HasIndex("idx_email"), Is.True);
                
                var index = db.GetIndex("idx_email")!;
                var results = index.Find(GetBytes("john@example.com")).ToList();
                
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0], Is.EqualTo(GetBytes("user-1")));
            }
        }

        [Test]
        public void EncryptedBTreeCreatesEncryptedIndexesTest()
        {
            var dbPath = Path.Combine(m_testDir, "encrypted.db");
            const string password = "test123";
            
            using (var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .WithEncryption(password)
                .Build())
            {
                var index = db.CreateIndex("idx_secure", isUnique: true);
                index.Add(GetBytes("secret-key"), GetBytes("secret-pk"));
                db.Flush();
            }
            
            // Verify data is recoverable with correct password
            using (var db = WitDatabase.Open(dbPath, password))
            {
                var index = db.GetIndex("idx_secure")!;
                var results = index.Find(GetBytes("secret-key")).ToList();
                
                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0], Is.EqualTo(GetBytes("secret-pk")));
            }
        }

        #endregion

        #region LSM Index Factory Tests

        [Test]
        public void LsmDatabaseCreatesLsmBasedIndexesTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_db");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .Build())
            {
                var index = db.CreateIndex("idx_tags", isUnique: false);
                for (int i = 0; i < 10; i++)
                {
                    index.Add(GetBytes("tag"), GetBytes($"item-{i}"));
                }
                db.Flush();
            }
            
            // Verify LSM index directory was created
            var indexDir = Path.Combine(lsmDir, "_indexes", "idx_tags");
            Assert.That(Directory.Exists(indexDir), Is.True, 
                "LSM index directory should be created");
        }

        [Test]
        public void LsmIndexDataPersistsAcrossReopensTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_persist");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .Build())
            {
                var index = db.CreateIndex("idx_cat", isUnique: false);
                index.Add(GetBytes("electronics"), GetBytes("product-1"));
                index.Add(GetBytes("electronics"), GetBytes("product-2"));
                db.Flush();
            }
            
            // Reopen using Open() auto-detection
            using (var db = WitDatabase.Open(lsmDir))
            {
                Assert.That(db.HasIndex("idx_cat"), Is.True);
                
                var index = db.GetIndex("idx_cat")!;
                var results = index.Find(GetBytes("electronics")).ToList();
                
                Assert.That(results, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void EncryptedLsmCreatesEncryptedIndexesTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_encrypted");
            const string password = "lsm_secret";
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .WithEncryption(password)
                .Build())
            {
                var index = db.CreateIndex("idx_secure", isUnique: true);
                index.Add(GetBytes("key1"), GetBytes("pk1"));
                db.Flush();
            }
            
            using (var db = WitDatabase.Open(lsmDir, password))
            {
                var index = db.GetIndex("idx_secure")!;
                Assert.That(index.Find(GetBytes("key1")).Any(), Is.True);
            }
        }

        #endregion

        #region Memory Storage Tests

        [Test]
        public void MemoryStorageUsesInMemoryIndexesTest()
        {
            using var db = new WitDatabaseBuilder()
                .WithMemoryStorage()
                .WithBTree()
                .Build();
            
            var index = db.CreateIndex("idx_mem", isUnique: true);
            index.Add(GetBytes("key"), GetBytes("pk"));
            
            // Verify it works (in-memory, no files created)
            var results = index.Find(GetBytes("key")).ToList();
            Assert.That(results, Has.Count.EqualTo(1));
        }

        #endregion

        #region Custom Store Tests

        [Test]
        public void CustomStoreUsesInMemoryIndexesByDefaultTest()
        {
            // Custom store without custom index factory
            using var customStore = new StoreInMemory();
            using var db = new WitDatabaseBuilder()
                .WithStore(customStore)
                .Build();
            
            var index = db.CreateIndex("idx_custom", isUnique: true);
            index.Add(GetBytes("key"), GetBytes("pk"));
            
            var results = index.Find(GetBytes("key")).ToList();
            Assert.That(results, Has.Count.EqualTo(1));
        }

        #endregion

        #region Custom Index Directory Tests

        [Test]
        public void CustomIndexDirectoryIsUsedTest()
        {
            var dbPath = Path.Combine(m_testDir, "main.db");
            var customIndexDir = Path.Combine(m_testDir, "custom_indexes");
            
            using (var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .WithIndexDirectory(customIndexDir)
                .Build())
            {
                db.CreateIndex("idx_custom_dir", isUnique: true);
                db.Flush();
            }
            
            // Verify index was created in custom directory
            Assert.That(Directory.Exists(customIndexDir), Is.True);
            Assert.That(File.Exists(Path.Combine(customIndexDir, "idx_custom_dir.idx")), Is.True);
        }

        #endregion

        #region Multiple Indexes Tests

        [Test]
        public void MultipleIndexesCanBeCreatedTest()
        {
            var dbPath = Path.Combine(m_testDir, "multi.db");
            
            using var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .Build();
            
            db.CreateIndex("idx_email", isUnique: true);
            db.CreateIndex("idx_name", isUnique: false);
            db.CreateIndex("idx_category", isUnique: false);
            
            Assert.That(db.IndexNames, Has.Count.EqualTo(3));
            Assert.That(db.HasIndex("idx_email"), Is.True);
            Assert.That(db.HasIndex("idx_name"), Is.True);
            Assert.That(db.HasIndex("idx_category"), Is.True);
        }

        [Test]
        public void DroppedIndexFilesAreNotRecreatedOnReopenTest()
        {
            var dbPath = Path.Combine(m_testDir, "drop_index.db");
            
            using (var db = new WitDatabaseBuilder()
                .WithFilePath(dbPath)
                .WithBTree()
                .Build())
            {
                db.CreateIndex("idx_keep", isUnique: true);
                db.CreateIndex("idx_drop", isUnique: false);
                db.DropIndex("idx_drop");
                db.Flush();
            }
            
            using (var db = WitDatabase.Open(dbPath))
            {
                Assert.That(db.HasIndex("idx_keep"), Is.True);
                Assert.That(db.HasIndex("idx_drop"), Is.False);
                Assert.That(db.IndexNames, Has.Count.EqualTo(1));
            }
        }

        #endregion

        #region Helpers

        private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #endregion
    }
}
