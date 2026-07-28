using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.AuditVerification
{
    /// <summary>
    /// Disposing an LSM store returned before its background compaction had finished, so the next
    /// store opened on the same directory met a half-written SSTable or a file the departing
    /// compaction still held.
    ///
    /// The order was the whole of it: Dispose waited for compaction and *then* flushed the memtable,
    /// and that flush scheduled a fresh compaction which nobody waited for.
    ///
    /// Found from CI, not from the audit: RapidOpenCloseTest had been failing intermittently on the
    /// runner and 9 runs in 10 on Windows.
    /// </summary>
    [TestFixture]
    public class LsmDisposalFindingsTests
    {
        #region Fields

        private string m_directory = null!;

        #endregion

        #region Setup

        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), $"lsm_disposal_{Guid.NewGuid():N}");
            Directory.CreateDirectory(m_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (!Directory.Exists(m_directory))
                return;

            try
            {
                Directory.Delete(m_directory, recursive: true);
            }
            catch (IOException)
            {
                // Cleanup only.
            }
        }

        #endregion

        #region Tests

        /// <summary>
        /// Writes enough SSTables to put a compaction over the trigger, then reopens the store
        /// immediately. Reopening is the assertion: recovery reads every SSTable in the directory,
        /// so anything left half-written or locked shows up here.
        /// </summary>
        [Test]
        public void StoreReopensImmediatelyAfterDisposeTest()
        {
            var options = CreateOptions();

            for (int i = 0; i < 20; i++)
            {
                using var store = new StoreLsm(m_directory, options);
                store.Put(BitConverter.GetBytes(i), BitConverter.GetBytes(i));
            }

            using var reopened = new StoreLsm(m_directory, options);

            for (int i = 0; i < 20; i++)
            {
                Assert.That(reopened.Get(BitConverter.GetBytes(i)), Is.Not.Null,
                    $"key {i} is missing after the store was closed and opened again");
            }
        }

        /// <summary>
        /// States the mechanism rather than the symptom, and states it without a race: the store
        /// reports whether a compaction is outstanding, and after Dispose the answer must be no.
        /// Reading the flag needs no timing - the defect is that Dispose *starts* one, so the flag
        /// is set by the time Dispose returns whatever the scheduler then does.
        /// </summary>
        [Test]
        public void DisposeLeavesNoCompactionOutstandingTest()
        {
            var options = CreateOptions();

            // Enough SSTables to put the final flush over the compaction trigger, which is what
            // used to schedule work nobody waited for.
            for (int i = 0; i < 20; i++)
            {
                using var seed = new StoreLsm(m_directory, options);
                seed.Put(BitConverter.GetBytes(i), new byte[256]);
            }

            var store = new StoreLsm(m_directory, options);
            store.Put(BitConverter.GetBytes(99), new byte[256]);

            store.Dispose();

            Assert.That(store.IsCompacting, Is.False,
                "Dispose returned with a compaction still outstanding - it waits for compaction "
                + "before the final flush, and the flush is what schedules the next one");
        }

        #endregion

        #region Helpers

        private static LsmOptions CreateOptions() => new()
        {
            EnableWal = true,
            EnableBlockCache = true,
            MemTableSizeLimit = 64 * 1024,
        };

        #endregion
    }
}
