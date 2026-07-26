using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Transactions;

namespace OutWit.Database.Core.Tests.Transactions
{
    /// <summary>
    /// A snapshot must never observe a transaction's writes partially applied.
    /// </summary>
    /// <remarks>
    /// The commit timestamp is allocated before any version is installed, and snapshots used to come
    /// from the same counter, so a reader could take a snapshot above a commit that had only partly
    /// landed and see some of its keys updated and others not.
    /// <c>SnapshotIsolationConsistencyUnderLoadTest</c> catches that, but only about one run in five -
    /// useless as an oracle. These tests park the writer *inside* its commit and read from another
    /// thread while it is stopped there, so the window is not a race to win but a state to inspect.
    /// </remarks>
    [TestFixture]
    public class MvccCommitAtomicityTests
    {
        #region Constants

        private const long StartingBalance = 100;
        private const long ExpectedTotal = StartingBalance * 2;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        #endregion

        #region Tests

        [Test]
        public void SnapshotTakenMidCommitSeesNoneOfItTest()
        {
            var gate = new CommitGate();
            using var store = new MvccTransactionalStore(new GatedStore(new StoreInMemory(), gate), ownsStore: true);

            Seed(store);
            gate.ArmAfter(1);

            var writer = Task.Run(() =>
            {
                using var tx = store.BeginTransaction();
                tx.Put(Key("a"), BitConverter.GetBytes(0L));
                tx.Put(Key("b"), BitConverter.GetBytes(ExpectedTotal));
                tx.Commit();
            });

            Assert.That(gate.WaitUntilParked(Timeout), Is.True,
                "The writer never reached the gate - the test would prove nothing");

            try
            {
                // The committing transaction is stopped part way through installing its versions,
                // holding the commit lock. A reader must neither block on it nor see half of it.
                using var reader = store.BeginReadOnlyTransaction();

                var a = BitConverter.ToInt64(reader.Get(Key("a"))!);
                var b = BitConverter.ToInt64(reader.Get(Key("b"))!);

                Assert.That(a + b, Is.EqualTo(ExpectedTotal),
                    $"A snapshot taken mid-commit saw a={a}, b={b} - a state that was never committed");
            }
            finally
            {
                gate.Release();
            }

            writer.Wait(Timeout);
        }

        [Test]
        public void SnapshotTakenAfterCommitSeesAllOfItTest()
        {
            using var store = new MvccTransactionalStore(new StoreInMemory(), ownsStore: true);

            Seed(store);

            using (var tx = store.BeginTransaction())
            {
                tx.Put(Key("a"), BitConverter.GetBytes(0L));
                tx.Put(Key("b"), BitConverter.GetBytes(ExpectedTotal));
                tx.Commit();
            }

            using var reader = store.BeginReadOnlyTransaction();

            Assert.Multiple(() =>
            {
                Assert.That(BitConverter.ToInt64(reader.Get(Key("a"))!), Is.Zero);
                Assert.That(BitConverter.ToInt64(reader.Get(Key("b"))!), Is.EqualTo(ExpectedTotal));
            });
        }

        [Test]
        public void ReaderDoesNotBlockBehindACommitTest()
        {
            var gate = new CommitGate();
            using var store = new MvccTransactionalStore(new GatedStore(new StoreInMemory(), gate), ownsStore: true);

            Seed(store);
            gate.ArmAfter(1);

            var writer = Task.Run(() =>
            {
                using var tx = store.BeginTransaction();
                tx.Put(Key("a"), BitConverter.GetBytes(1L));
                tx.Put(Key("b"), BitConverter.GetBytes(2L));
                tx.Commit();
            });

            Assert.That(gate.WaitUntilParked(Timeout), Is.True);

            try
            {
                // The commit lock must not be on the read path: if BeginReadOnlyTransaction took it,
                // this would hang until the gate is released.
                var read = Task.Run(() =>
                {
                    using var reader = store.BeginReadOnlyTransaction();
                    return reader.Get(Key("a"));
                });

                Assert.That(read.Wait(TimeSpan.FromSeconds(5)), Is.True,
                    "A reader blocked behind an in-flight commit - the commit lock has leaked onto " +
                    "the read path, which is also a deadlock risk via Dispose -> Rollback");
            }
            finally
            {
                gate.Release();
            }

            writer.Wait(Timeout);
        }

        [Test]
        public void NonTransactionalWriteIsVisibleToALaterSnapshotTest()
        {
            using var store = new MvccTransactionalStore(new StoreInMemory(), ownsStore: true);

            // Snapshots read a published watermark; a write that never published would be invisible
            // to every transaction for ever, which is how this fix could quietly break the
            // non-transactional API.
            store.Put(Key("a"), BitConverter.GetBytes(42L));

            using var reader = store.BeginReadOnlyTransaction();

            Assert.That(BitConverter.ToInt64(reader.Get(Key("a"))!), Is.EqualTo(42L));
        }

        [Test]
        public void CommittedValueIsVisibleToTheCommittingConnectionImmediatelyTest()
        {
            using var store = new MvccTransactionalStore(new StoreInMemory(), ownsStore: true);

            using (var tx = store.BeginTransaction())
            {
                tx.Put(Key("a"), BitConverter.GetBytes(7L));
                tx.Commit();
            }

            using var next = store.BeginTransaction();

            Assert.That(BitConverter.ToInt64(next.Get(Key("a"))!), Is.EqualTo(7L),
                "Read-your-own-writes: a transaction started after a commit must see it");
        }

        #endregion

        #region Helper Methods

        private static void Seed(MvccTransactionalStore store)
        {
            using var tx = store.BeginTransaction();
            tx.Put(Key("a"), BitConverter.GetBytes(StartingBalance));
            tx.Put(Key("b"), BitConverter.GetBytes(StartingBalance));
            tx.Commit();
        }

        private static byte[] Key(string name) => System.Text.Encoding.UTF8.GetBytes(name);

        #endregion

        #region Test Doubles

        /// <summary>
        /// Parks the writing thread after a chosen number of writes, so another thread can inspect
        /// the store from exactly inside a commit.
        /// </summary>
        private sealed class CommitGate
        {
            private readonly ManualResetEventSlim m_parked = new(false);
            private readonly ManualResetEventSlim m_release = new(false);
            private int m_armAfter = -1;
            private int m_writes;

            public void ArmAfter(int writes)
            {
                m_armAfter = writes;
                m_writes = 0;
            }

            public bool WaitUntilParked(TimeSpan timeout) => m_parked.Wait(timeout);

            public void Release() => m_release.Set();

            public void OnWrite()
            {
                if (m_armAfter < 0)
                    return;

                if (Interlocked.Increment(ref m_writes) != m_armAfter)
                    return;

                m_parked.Set();
                m_release.Wait(TimeSpan.FromSeconds(60));
            }
        }

        /// <summary>
        /// Passes everything through, giving the gate a chance to park on each write.
        /// </summary>
        private sealed class GatedStore : IKeyValueStore
        {
            private readonly IKeyValueStore m_inner;
            private readonly CommitGate m_gate;

            public GatedStore(IKeyValueStore inner, CommitGate gate)
            {
                m_inner = inner;
                m_gate = gate;
            }

            public string ProviderKey => m_inner.ProviderKey;

            public long Count() => m_inner.Count();

            public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
            {
                m_inner.Put(key, value);
                m_gate.OnWrite();
            }

            public byte[]? Get(ReadOnlySpan<byte> key) => m_inner.Get(key);

            public bool Delete(ReadOnlySpan<byte> key) => m_inner.Delete(key);

            public bool ContainsKey(ReadOnlySpan<byte> key) => m_inner.ContainsKey(key);

            public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey)
                => m_inner.Scan(startKey, endKey);

            public void Flush() => m_inner.Flush();

            public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
                => m_inner.PutAsync(key, value, cancellationToken);

            public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
                => m_inner.GetAsync(key, cancellationToken);

            public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
                => m_inner.DeleteAsync(key, cancellationToken);

            public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
                byte[]? startKey, byte[]? endKey, CancellationToken cancellationToken = default)
                => m_inner.ScanAsync(startKey, endKey, cancellationToken);

            public ValueTask FlushAsync(CancellationToken cancellationToken = default)
                => m_inner.FlushAsync(cancellationToken);

            public void Dispose() => m_inner.Dispose();
        }

        #endregion
    }
}
