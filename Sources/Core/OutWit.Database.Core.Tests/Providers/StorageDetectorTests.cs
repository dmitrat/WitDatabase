using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Core.Tests.Providers
{
    /// <summary>
    /// Tests for automatic storage type detection.
    /// </summary>
    [TestFixture]
    public class StorageDetectorTests
    {
        #region Fields

        private string m_testDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "WitDB_Detector_" + Guid.NewGuid().ToString("N"));
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

        #region BTree Detection Tests

        [Test]
        public void DetectsBTreeFileTest()
        {
            var path = Path.Combine(m_testDir, "btree.db");
            
            using (var db = WitDatabase.Create(path))
            {
                db.Put("key"u8, "value"u8);
            }

            var result = StorageDetector.Detect(path);
            
            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.False);
            Assert.That(result.StoreType, Is.EqualTo("btree"));
            Assert.That(result.RequiresPassword, Is.False);
        }

        [Test]
        public void DetectsEncryptedBTreeTest()
        {
            var path = Path.Combine(m_testDir, "encrypted.db");
            
            using (var db = WitDatabase.Create(path, "password"))
            {
                db.Put("key"u8, "value"u8);
            }

            var result = StorageDetector.Detect(path);
            
            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.False);
            Assert.That(result.RequiresPassword, Is.True);
            Assert.That(result.StoreType, Is.EqualTo("btree")); // Assume BTree for encrypted files
        }

        #endregion

        #region LSM Detection Tests

        [Test]
        public void DetectsLsmDirectoryTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_db");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .WithTransactions()
                .Build())
            {
                db.Put("key"u8, "value"u8);
            }

            var result = StorageDetector.Detect(lsmDir);
            
            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.True);
            Assert.That(result.StoreType, Is.EqualTo("lsm"));
        }

        [Test]
        public void DetectsLsmWithWalOnlyTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_wal");
            
            // Create LSM with small memtable so data stays in WAL
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir, opts =>
                {
                    opts.MemTableSizeLimit = 10 * 1024 * 1024; // Large so no flush
                })
                .Build())
            {
                db.Put("key"u8, "value"u8);
                // Don't flush - data stays in WAL
            }

            var result = StorageDetector.Detect(lsmDir);
            
            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.True);
            Assert.That(result.StoreType, Is.EqualTo("lsm"));
        }

        [Test]
        public void DetectsLsmWithSstFilesTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_sst");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir, opts =>
                {
                    opts.MemTableSizeLimit = 1024; // Small so triggers flush
                })
                .Build())
            {
                // Write enough to trigger flush
                for (int i = 0; i < 100; i++)
                {
                    db.Put(System.Text.Encoding.UTF8.GetBytes($"key{i}"), new byte[100]);
                }
            }

            var result = StorageDetector.Detect(lsmDir);

            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.True);
            Assert.That(result.StoreType, Is.EqualTo("lsm"));
        }

        /// <summary>
        /// An LSM directory says what it was built with, and detection reads it.
        ///
        /// <para>
        /// The directory branch used to answer the store type and nothing else, so
        /// <c>HasTransactions</c>, <c>HasMvcc</c> and <c>HasFileLocking</c> were <c>false</c> for
        /// EVERY LSM database - which is not "unknown", it is the wrong answer, and it is the answer a
        /// consumer prints. Studio's Open dialog said "no MVCC" about every LSM database in existence,
        /// found on 2026-08-08 by opening one.
        /// </para>
        /// <para>
        /// <b>Two databases, not one.</b> A case that only builds the MVCC one passes for an
        /// implementation that answers <c>true</c> without reading anything, which is the same defect
        /// with the sign flipped. The pair is the control.
        /// </para>
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DetectsTheTransactionModelOfAnLsmDirectoryTest(bool mvcc)
        {
            var lsmDir = Path.Combine(m_testDir, $"lsm_model_{mvcc}");

            var builder = new WitDatabaseBuilder().WithLsmTree(lsmDir).WithFileLocking();

            using (var db = (mvcc ? builder.WithMvcc() : builder.WithTransactions()).Build())
            {
                db.Put("key"u8, "value"u8);
            }

            var result = StorageDetector.Detect(lsmDir);

            Assert.Multiple(() =>
            {
                Assert.That(result.StoreType, Is.EqualTo("lsm"));
                Assert.That(result.HasTransactions, Is.True);
                Assert.That(result.HasMvcc, Is.EqualTo(mvcc),
                    "the transaction model is in the sidecar and detection has to read it");
                Assert.That(result.HasFileLocking, Is.True);
            });
        }

        /// <summary>
        /// And an encrypted LSM directory says so before anything tries to open it.
        ///
        /// <para>
        /// The sidecar is written in clear - it has to be, because it is what says which encryption
        /// provider to build - so "cannot detect encryption without opening" was true of the SSTables
        /// and not of the directory. Reported as no password needed, an encrypted LSM database was
        /// opened without one and the failure arrived as a wrong-password error from the engine.
        /// </para>
        /// </summary>
        [Test]
        public void DetectsThatAnLsmDirectoryIsEncryptedTest()
        {
            var plain = Path.Combine(m_testDir, "lsm_plain");
            var secret = Path.Combine(m_testDir, "lsm_secret");

            using (var db = new WitDatabaseBuilder().WithLsmTree(plain).Build())
                db.Put("key"u8, "value"u8);

            using (var db = new WitDatabaseBuilder().WithLsmTree(secret).WithEncryption("secret").Build())
                db.Put("key"u8, "value"u8);

            Assert.Multiple(() =>
            {
                // The control, and it is the half that fails for a detector that simply says "yes":
                // a database with no encryption must still be openable without a password.
                Assert.That(StorageDetector.Detect(plain).RequiresPassword, Is.False);
                Assert.That(StorageDetector.Detect(plain).EncryptionProvider, Is.Empty,
                    "an unencrypted database names no provider - the hints answer with an empty "
                    + "string rather than null, which is what the file branch has always returned");

                Assert.That(StorageDetector.Detect(secret).RequiresPassword, Is.True);
                Assert.That(StorageDetector.Detect(secret).EncryptionProvider, Is.Not.Empty);
            });
        }

        #endregion

        #region Open Auto-Detection Tests

        [Test]
        public void OpenAutoDetectsLsmTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_open");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .Build())
            {
                db.Put("key"u8, "value"u8);
            }

            // Open should auto-detect LSM
            using (var db = WitDatabase.Open(lsmDir))
            {
                Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
            }
        }

        [Test]
        public void OpenAutoDetectsBTreeTest()
        {
            var path = Path.Combine(m_testDir, "btree_open.db");
            
            using (var db = WitDatabase.Create(path))
            {
                db.Put("key"u8, "value"u8);
            }

            using (var db = WitDatabase.Open(path))
            {
                Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
            }
        }

        [Test]
        public void OpenEncryptedLsmWithPasswordTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_encrypted");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .WithEncryption("secret")
                .Build())
            {
                db.Put("key"u8, "value"u8);
            }

            using (var db = WitDatabase.Open(lsmDir, "secret"))
            {
                Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
            }
        }

        [Test]
        public void CreateOrOpenDetectsExistingLsmTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_createopen");
            
            // First call creates LSM
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .Build())
            {
                db.Put("key"u8, "value"u8);
            }

            // CreateOrOpen should detect and open LSM
            using (var db = WitDatabase.CreateOrOpen(lsmDir))
            {
                Assert.That(db.Get("key"u8), Is.EqualTo("value"u8.ToArray()));
            }
        }

        #endregion

        #region Edge Cases

        [Test]
        public void DetectNonExistentPathTest()
        {
            var result = StorageDetector.Detect(Path.Combine(m_testDir, "nonexistent"));
            
            Assert.That(result.Exists, Is.False);
        }

        [Test]
        public void DetectEmptyDirectoryTest()
        {
            var emptyDir = Path.Combine(m_testDir, "empty");
            Directory.CreateDirectory(emptyDir);

            var result = StorageDetector.Detect(emptyDir);
            
            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.True);
            Assert.That(result.StoreType, Is.Null); // Unknown - empty directory
        }

        [Test]
        public void DetectSmallFileTest()
        {
            var path = Path.Combine(m_testDir, "small.db");
            File.WriteAllBytes(path, new byte[10]); // Too small to be valid

            var result = StorageDetector.Detect(path);
            
            Assert.That(result.Exists, Is.True);
            Assert.That(result.IsDirectory, Is.False);
            Assert.That(result.StoreType, Is.Null); // Unknown - too small
        }

        [Test]
        public void OpenNonExistentThrowsTest()
        {
            var path = Path.Combine(m_testDir, "nonexistent.db");
            
            Assert.Throws<FileNotFoundException>(() => WitDatabase.Open(path));
        }

        #endregion

        #region GetDatabaseInfo Tests

        [Test]
        public void GetDatabaseInfoForLsmTest()
        {
            var lsmDir = Path.Combine(m_testDir, "lsm_info");
            
            using (var db = new WitDatabaseBuilder()
                .WithLsmTree(lsmDir)
                .Build())
            {
                db.Put("key"u8, "value"u8);
            }

            var info = WitDatabase.GetDatabaseInfo(lsmDir);
            
            Assert.That(info.Exists, Is.True);
            Assert.That(info.IsDirectory, Is.True);
            Assert.That(info.StoreType, Is.EqualTo("lsm"));
        }

        [Test]
        public void GetDatabaseInfoForBTreeTest()
        {
            var path = Path.Combine(m_testDir, "btree_info.db");
            
            using (var db = WitDatabase.Create(path))
            {
                db.Put("key"u8, "value"u8);
            }

            var info = WitDatabase.GetDatabaseInfo(path);
            
            Assert.That(info.Exists, Is.True);
            Assert.That(info.IsDirectory, Is.False);
            Assert.That(info.StoreType, Is.EqualTo("btree"));
            Assert.That(info.HasTransactions, Is.True);
        }

        #endregion
    }
}
