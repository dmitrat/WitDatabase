using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Tests.Indexes;

/// <summary>
/// Dropping an index must release it, whatever emptying it does.
/// </summary>
/// <remarks>
/// <para>
/// <c>DropIndex</c> takes the index out of the manager and then empties its backing store before
/// releasing it - emptying matters because a persistent store keeps its entries under the index's
/// own name, so the next index created with that name would reopen them. <c>ClearBackingStore</c>
/// says in its own comment that a drop must not fail because the store could not be emptied, and it
/// named two exception types. A third walked past it, and <c>Dispose</c> - the line after - was
/// never reached, so a file-backed index kept its file open for the life of the process.
/// </para>
/// <para>
/// Found 2026-08-09 through a <c>CREATE INDEX</c> whose build exhausted the page cache: the failure
/// arrives as an <c>InvalidOperationException</c>, which is exactly the type that was not named.
/// </para>
/// </remarks>
[TestFixture]
public class IndexManagerDropTests
{
    #region Tests

    /// <summary>
    /// The index is released even when emptying it throws something the clear does not expect.
    /// </summary>
    [Test]
    public void AnIndexIsDisposedEvenWhenClearingItThrowsTest()
    {
        var index = new ThrowingIndex(new InvalidOperationException("cache is full"));

        using var manager = new IndexManager(new SingleIndexFactory(index));

        manager.CreateIndex(index.Name, isUnique: false);

        Assert.That(() => manager.DropIndex(index.Name), Throws.InvalidOperationException,
            "the failure to empty the store must still reach the caller - a drop that swallows it "
            + "leaves stale entries under the index's name and says nothing");

        Assert.That(index.WasDisposed, Is.True,
            "the index was taken out of the manager and never disposed, so nothing will ever "
            + "release what it holds - on a file-backed index that is the file, for the life of "
            + "the process");
    }

    /// <summary>
    /// Control: the ordinary drop disposes too, so the case above is about the failure and not
    /// about disposal in general.
    /// </summary>
    [Test]
    public void ControlAnOrdinaryDropDisposesTheIndexTest()
    {
        var index = new ThrowingIndex(failure: null);

        using var manager = new IndexManager(new SingleIndexFactory(index));

        manager.CreateIndex(index.Name, isUnique: false);

        Assert.That(manager.DropIndex(index.Name), Is.True);
        Assert.That(index.WasDisposed, Is.True);
    }

    /// <summary>
    /// Control: the index really is emptied on the way out. Without it, "disposed" could be
    /// satisfied by a drop that skips the clear altogether - which is the defect the clear exists
    /// for, in the other direction.
    /// </summary>
    [Test]
    public void ControlAnOrdinaryDropEmptiesTheIndexTest()
    {
        var index = new ThrowingIndex(failure: null);

        using var manager = new IndexManager(new SingleIndexFactory(index));

        manager.CreateIndex(index.Name, isUnique: false);
        manager.DropIndex(index.Name);

        Assert.That(index.WasCleared, Is.True);
    }

    #endregion

    #region Doubles

    private sealed class SingleIndexFactory : ISecondaryIndexFactory
    {
        private readonly ISecondaryIndex m_index;

        public SingleIndexFactory(ISecondaryIndex index) => m_index = index;

        public ISecondaryIndex CreateIndex(string name, bool isUnique) => m_index;

        public string ProviderKey => "probe";
    }

    /// <summary>
    /// An index that can be told to fail when it is emptied, and that records what happened to it.
    /// </summary>
    private sealed class ThrowingIndex : ISecondaryIndex
    {
        private readonly Exception? m_failure;

        public ThrowingIndex(Exception? failure) => m_failure = failure;

        public string Name => "IX_Probe";

        public bool IsUnique => false;

        public long Count => 0;

        public bool WasCleared { get; private set; }

        public bool WasDisposed { get; private set; }

        public void Clear()
        {
            WasCleared = true;

            if (m_failure != null)
                throw m_failure;
        }

        public void Dispose() => WasDisposed = true;

        public IEnumerable<byte[]> Find(ReadOnlySpan<byte> indexKey) => [];

        public IEnumerable<(byte[] IndexKey, byte[] PrimaryKey)> FindRange(byte[]? startKey, byte[]? endKey) => [];

        public bool Contains(ReadOnlySpan<byte> indexKey) => false;

        public bool ContainsEntry(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey) => false;

        public void Add(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey)
        {
        }

        public bool Remove(ReadOnlySpan<byte> indexKey, ReadOnlySpan<byte> primaryKey) => false;

        public int RemoveAll(ReadOnlySpan<byte> indexKey) => 0;

        public (byte[] IndexKey, byte[] PrimaryKey)? GetFirstEntry() => null;

        public (byte[] IndexKey, byte[] PrimaryKey)? GetLastEntry() => null;

        public void Flush()
        {
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    #endregion
}
