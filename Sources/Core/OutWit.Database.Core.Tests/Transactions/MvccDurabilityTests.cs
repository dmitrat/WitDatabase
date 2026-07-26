using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Transactions
{
    /// <summary>
    /// Regression tests for commit durability in MVCC mode.
    /// </summary>
    /// <remarks>
    /// MVCC is the default transactional mode behind the ADO.NET and EF Core providers, and its
    /// commit path applied the new versions and returned without flushing anything. A successful
    /// COMMIT was therefore lost by a process kill - and unlike the journalled path there is nothing
    /// to replay it from. Commit is now durable by default, with
    /// <c>WithAsynchronousCommit()</c> as the explicit opt-out.
    /// </remarks>
    [TestFixture]
    public class MvccDurabilityTests
    {
        #region Default Behaviour

        [Test]
        public void CommitFlushesToStorageByDefaultTest()
        {
            var storage = new CountingStorage(new StorageMemory(initialPageCount: 0));

            using var database = new WitDatabaseBuilder()
                .WithStorage(storage)
                .WithBTree()
                .WithMvcc()
                .Build();

            var flushesBefore = storage.FlushCount;

            using (var transaction = database.BeginTransaction())
            {
                transaction.Put("k"u8.ToArray(), "v"u8.ToArray());
                transaction.Commit();
            }

            Assert.That(storage.FlushCount, Is.GreaterThan(flushesBefore),
                "A successful COMMIT must reach durable storage before it returns");
        }

        [Test]
        public async Task CommitAsyncFlushesToStorageByDefaultTest()
        {
            var storage = new CountingStorage(new StorageMemory(initialPageCount: 0));

            using var database = new WitDatabaseBuilder()
                .WithStorage(storage)
                .WithBTree()
                .WithMvcc()
                .Build();

            var flushesBefore = storage.FlushCount;

            await using (var transaction = database.BeginTransaction())
            {
                transaction.Put("k"u8.ToArray(), "v"u8.ToArray());
                await transaction.CommitAsync();
            }

            Assert.That(storage.FlushCount, Is.GreaterThan(flushesBefore));
        }

        [Test]
        public void AsynchronousCommitIsOptInTest()
        {
            var storage = new CountingStorage(new StorageMemory(initialPageCount: 0));

            using var database = new WitDatabaseBuilder()
                .WithStorage(storage)
                .WithBTree()
                .WithMvcc()
                .WithAsynchronousCommit()
                .Build();

            var flushesBefore = storage.FlushCount;

            using (var transaction = database.BeginTransaction())
            {
                transaction.Put("k"u8.ToArray(), "v"u8.ToArray());
                transaction.Commit();
            }

            Assert.That(storage.FlushCount, Is.EqualTo(flushesBefore),
                "Opting out must actually skip the flush - otherwise the option is a lie");
        }

        [Test]
        public void CommittedDataIsStillReadableAfterASynchronousCommitTest()
        {
            var storage = new CountingStorage(new StorageMemory(initialPageCount: 0));

            using var database = new WitDatabaseBuilder()
                .WithStorage(storage)
                .WithBTree()
                .WithMvcc()
                .Build();

            using (var transaction = database.BeginTransaction())
            {
                transaction.Put("k"u8.ToArray(), "v"u8.ToArray());
                transaction.Commit();
            }

            Assert.That(database.Get("k"u8.ToArray()), Is.EqualTo("v"u8.ToArray()),
                "Flushing on commit must not disturb the value itself");
        }

        #endregion

        #region Test Doubles

        /// <summary>
        /// Passes everything through to an inner storage and counts flushes.
        /// </summary>
        private sealed class CountingStorage : IStorage
        {
            private readonly IStorage m_inner;

            public CountingStorage(IStorage inner) => m_inner = inner;

            public int FlushCount { get; private set; }

            public string ProviderKey => m_inner.ProviderKey;
            public int PageSize => m_inner.PageSize;
            public long PageCount => m_inner.PageCount;
            public bool IsReadOnly => m_inner.IsReadOnly;

            public void ReadPage(long pageNumber, Span<byte> buffer) => m_inner.ReadPage(pageNumber, buffer);

            public ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
                => m_inner.ReadPageAsync(pageNumber, buffer, cancellationToken);

            public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer) => m_inner.WritePage(pageNumber, buffer);

            public ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
                => m_inner.WritePageAsync(pageNumber, buffer, cancellationToken);

            public void Flush()
            {
                FlushCount++;
                m_inner.Flush();
            }

            public ValueTask FlushAsync(CancellationToken cancellationToken = default)
            {
                FlushCount++;
                return m_inner.FlushAsync(cancellationToken);
            }

            public void SetSize(long pageCount) => m_inner.SetSize(pageCount);

            public void Dispose() => m_inner.Dispose();
        }

        #endregion
    }
}
