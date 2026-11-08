using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Indexes
{
    /// <summary>
    /// An index knows whether it holds writes its file does not: that is what lets a commit flush
    /// the indexes it changed and skip the ones it did not.
    /// </summary>
    [TestFixture]
    public class SecondaryIndexPendingWritesTests
    {
        #region Fields

        private string m_testDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "WitDB_IndexPending_" + Guid.NewGuid().ToString("N"));
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

        #region Pending Writes Tests

        [Test]
        public void AFreshIndexHasNothingPendingTest()
        {
            using var index = CreateIndex("fresh");

            Assert.That(index.HasPendingWrites, Is.False);
        }

        [Test]
        public void AnAddIsPendingUntilFlushedTest()
        {
            using var index = CreateIndex("add");

            index.Add([1, 2, 3], [0, 0, 0, 1]);
            Assert.That(index.HasPendingWrites, Is.True, "the entry is in memory and not yet in the file");

            index.Flush();
            Assert.That(index.HasPendingWrites, Is.False, "the flush wrote it");
        }

        [Test]
        public void ARemoveIsPendingUntilFlushedTest()
        {
            using var index = CreateIndex("remove");
            index.Add([1, 2, 3], [0, 0, 0, 1]);
            index.Flush();

            index.Remove([1, 2, 3], [0, 0, 0, 1]);
            Assert.That(index.HasPendingWrites, Is.True);

            index.Flush();
            Assert.That(index.HasPendingWrites, Is.False);
        }

        [Test]
        public void ARemoveThatFindsNothingLeavesNothingPendingTest()
        {
            using var index = CreateIndex("remove-miss");

            index.Remove([9, 9, 9], [0, 0, 0, 1]);

            Assert.That(index.HasPendingWrites, Is.False, "nothing changed, so nothing is owed to the file");
        }

        [Test]
        public async Task AnAsyncFlushClearsThePendingWritesTest()
        {
            using var index = CreateIndex("async");
            index.Add([1, 2, 3], [0, 0, 0, 1]);

            await index.FlushAsync();

            Assert.That(index.HasPendingWrites, Is.False);
        }

        [Test]
        public void FlushedEntriesAreInTheFileWhenTheStoreIsReopenedTest()
        {
            var path = Path.Combine(m_testDir, "reopen.idx");

            using (var index = new SecondaryIndexKeyValueStore("reopen", new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true), isUnique: false))
            {
                index.Add([1, 2, 3], [0, 0, 0, 1]);
                index.Add([1, 2, 3], [0, 0, 0, 2]);
                index.Flush();

                // No dispose-time flush is relied upon from here: a second store over the same file
                // cannot be opened while this one holds it, so the reopen below follows the using.
            }

            using var reopened = new SecondaryIndexKeyValueStore("reopen", new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true), isUnique: false);

            Assert.That(reopened.Find([1, 2, 3]).Count(), Is.EqualTo(2));
        }

        #endregion

        #region Tools

        private SecondaryIndexKeyValueStore CreateIndex(string name)
        {
            var path = Path.Combine(m_testDir, $"{name}.idx");
            return new SecondaryIndexKeyValueStore(name, new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true), isUnique: false);
        }

        #endregion
    }
}
