using System.Collections.Concurrent;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions
{
    [TestFixture]
    public class TransactionTimestampManagerTests
    {
        #region Fields

        private TransactionTimestampManager m_manager = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_manager = new TransactionTimestampManager();
        }

        #endregion

        #region Timestamp Tests

        [Test]
        public void GetNextTimestampReturnsIncreasingValuesTest()
        {
            var ts1 = m_manager.GetNextTimestamp();
            var ts2 = m_manager.GetNextTimestamp();
            var ts3 = m_manager.GetNextTimestamp();

            Assert.That(ts2, Is.GreaterThan(ts1));
            Assert.That(ts3, Is.GreaterThan(ts2));
        }

        [Test]
        public void GetNextTimestampStartsFromOneByDefaultTest()
        {
            var ts = m_manager.GetNextTimestamp();
            Assert.That(ts, Is.EqualTo(1));
        }

        [Test]
        public void GetNextTimestampRespectsInitialTimestampTest()
        {
            var manager = new TransactionTimestampManager(100);
            var ts = manager.GetNextTimestamp();
            Assert.That(ts, Is.EqualTo(101));
        }

        [Test]
        public void CurrentTimestampReturnsLastAssignedTest()
        {
            m_manager.GetNextTimestamp();
            m_manager.GetNextTimestamp();
            var ts3 = m_manager.GetNextTimestamp();

            Assert.That(m_manager.CurrentTimestamp, Is.EqualTo(ts3));
        }

        [Test]
        public void GetNextTimestampIsThreadSafeTest()
        {
            const int threadCount = 10;
            const int iterationsPerThread = 1000;
            var timestamps = new ConcurrentBag<long>();

            Parallel.For(0, threadCount, _ =>
            {
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    timestamps.Add(m_manager.GetNextTimestamp());
                }
            });

            var list = timestamps.ToList();
            var distinct = list.Distinct().ToList();

            // All timestamps should be unique
            Assert.That(distinct.Count, Is.EqualTo(threadCount * iterationsPerThread));
        }

        #endregion

        #region Registration Tests

        [Test]
        public void RegisterTransactionIncreasesActiveCountTest()
        {
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(0));

            m_manager.RegisterTransaction(1, 100);
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(1));

            m_manager.RegisterTransaction(2, 101);
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(2));
        }

        [Test]
        public void RegisterTransactionThrowsOnDuplicateIdTest()
        {
            m_manager.RegisterTransaction(1, 100);

            Assert.Throws<InvalidOperationException>(() => 
                m_manager.RegisterTransaction(1, 101));
        }

        [Test]
        public void UnregisterTransactionDecreasesActiveCountTest()
        {
            m_manager.RegisterTransaction(1, 100);
            m_manager.RegisterTransaction(2, 101);
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(2));

            m_manager.UnregisterTransaction(1);
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(1));
        }

        [Test]
        public void UnregisterNonExistentTransactionDoesNotThrowTest()
        {
            Assert.DoesNotThrow(() => m_manager.UnregisterTransaction(999));
        }

        #endregion

        #region Commit Tests

        [Test]
        public void MarkCommittedRemovesFromActiveTest()
        {
            m_manager.RegisterTransaction(1, 100);
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(1));

            m_manager.MarkCommitted(1, 150);
            Assert.That(m_manager.ActiveTransactionCount, Is.EqualTo(0));
        }

        [Test]
        public void MarkCommittedAddsToCommittedTest()
        {
            m_manager.RegisterTransaction(1, 100);
            m_manager.MarkCommitted(1, 150);

            Assert.That(m_manager.IsCommitted(1), Is.True);
            Assert.That(m_manager.CommittedTransactionCount, Is.EqualTo(1));
        }

        [Test]
        public void GetCommitTimestampReturnsCorrectValueTest()
        {
            m_manager.RegisterTransaction(1, 100);
            m_manager.MarkCommitted(1, 150);

            Assert.That(m_manager.GetCommitTimestamp(1), Is.EqualTo(150));
        }

        [Test]
        public void GetCommitTimestampReturnsNullForUncommittedTest()
        {
            m_manager.RegisterTransaction(1, 100);
            Assert.That(m_manager.GetCommitTimestamp(1), Is.Null);
        }

        [Test]
        public void GetCommitTimestampReturnsNullForUnknownTest()
        {
            Assert.That(m_manager.GetCommitTimestamp(999), Is.Null);
        }

        [Test]
        public void IsCommittedReturnsFalseForActiveTest()
        {
            m_manager.RegisterTransaction(1, 100);
            Assert.That(m_manager.IsCommitted(1), Is.False);
        }

        [Test]
        public void IsCommittedReturnsFalseForUnknownTest()
        {
            Assert.That(m_manager.IsCommitted(999), Is.False);
        }

        #endregion

        #region Minimum Snapshot Tests

        [Test]
        public void GetMinimumActiveSnapshotTimestampReturnsCurrentWhenNoActiveTest()
        {
            m_manager.GetNextTimestamp(); // 1
            m_manager.GetNextTimestamp(); // 2
            var ts3 = m_manager.GetNextTimestamp(); // 3

            Assert.That(m_manager.GetMinimumActiveSnapshotTimestamp(), Is.EqualTo(ts3));
        }

        [Test]
        public void GetMinimumActiveSnapshotTimestampReturnsMinimumTest()
        {
            m_manager.RegisterTransaction(1, 100);
            m_manager.RegisterTransaction(2, 50);  // Earlier snapshot
            m_manager.RegisterTransaction(3, 200);

            Assert.That(m_manager.GetMinimumActiveSnapshotTimestamp(), Is.EqualTo(50));
        }

        [Test]
        public void GetMinimumActiveSnapshotTimestampUpdatesOnUnregisterTest()
        {
            m_manager.RegisterTransaction(1, 50);  // Earliest
            m_manager.RegisterTransaction(2, 100);

            Assert.That(m_manager.GetMinimumActiveSnapshotTimestamp(), Is.EqualTo(50));

            m_manager.UnregisterTransaction(1);
            Assert.That(m_manager.GetMinimumActiveSnapshotTimestamp(), Is.EqualTo(100));
        }

        #endregion

        #region Cleanup Tests

        [Test]
        public void CleanupCommittedTransactionsRemovesOldCommitsTest()
        {
            // Create some committed transactions
            m_manager.RegisterTransaction(1, 100);
            m_manager.MarkCommitted(1, 110);
            
            m_manager.RegisterTransaction(2, 100);
            m_manager.MarkCommitted(2, 120);

            // Register an active transaction with later snapshot
            m_manager.RegisterTransaction(3, 200);

            Assert.That(m_manager.CommittedTransactionCount, Is.EqualTo(2));

            // Cleanup should remove commits that are older than min active snapshot (200)
            var cleaned = m_manager.CleanupCommittedTransactions();

            Assert.That(cleaned, Is.EqualTo(2));
            Assert.That(m_manager.CommittedTransactionCount, Is.EqualTo(0));
        }

        [Test]
        public void CleanupCommittedTransactionsKeepsRecentCommitsTest()
        {
            // Register active transaction first
            m_manager.RegisterTransaction(1, 100);

            // Create committed transaction with later timestamp
            m_manager.RegisterTransaction(2, 100);
            m_manager.MarkCommitted(2, 150);  // After min snapshot (100)

            Assert.That(m_manager.CommittedTransactionCount, Is.EqualTo(1));

            // Cleanup should not remove commits newer than min active snapshot
            var cleaned = m_manager.CleanupCommittedTransactions();

            Assert.That(cleaned, Is.EqualTo(0));
            Assert.That(m_manager.CommittedTransactionCount, Is.EqualTo(1));
        }

        #endregion

        #region Active Transactions Info Tests

        [Test]
        public void ActiveTransactionsReturnsCorrectInfoTest()
        {
            m_manager.RegisterTransaction(1, 100);
            m_manager.RegisterTransaction(2, 200);

            var active = m_manager.ActiveTransactions;

            Assert.That(active.Count, Is.EqualTo(2));
            Assert.That(active.Any(t => t.TransactionId == 1 && t.SnapshotTimestamp == 100), Is.True);
            Assert.That(active.Any(t => t.TransactionId == 2 && t.SnapshotTimestamp == 200), Is.True);
        }

        [Test]
        public void TransactionInfoHasStartTimeTest()
        {
            var before = DateTime.UtcNow;
            m_manager.RegisterTransaction(1, 100);
            var after = DateTime.UtcNow;

            var info = m_manager.ActiveTransactions.First();

            Assert.That(info.StartTime, Is.GreaterThanOrEqualTo(before));
            Assert.That(info.StartTime, Is.LessThanOrEqualTo(after));
        }

        #endregion
    }
}
