using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using OutWit.Database.Core.Tree;

namespace OutWit.Database.Core.Tests.Indexes;

/// <summary>
/// The fact a secondary index is rebuilt on, asserted directly rather than through what it causes.
/// </summary>
/// <remarks>
/// <para>
/// An index that holds nothing is ambiguous: legitimately empty (every indexed value is NULL, or a
/// partial index matches no row) or empty because its content is gone. Answering from the second is
/// a wrong answer with no error; rebuilding the first on every open is a full table scan for ever.
/// <see cref="ISecondaryIndex.ContentWasFound"/> is what separates them, and this fixture asserts
/// the separation at the level where it is decided.
/// </para>
/// <para>
/// <b>Why here and not only through the engine.</b> The engine cases in
/// <c>MissingIndexContentTests</c> can tell that the answer came out right; they cannot tell that a
/// healthy database was left alone, because "did not rescan" has no observable effect on an answer.
/// This one asks the question itself, so the COST of the fix is guarded and not merely its benefit.
/// </para>
/// </remarks>
[TestFixture]
public class IndexContentOriginTests
{
    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"IndexOrigin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region The store knows, for one moment

    [Test]
    public void AStoreOverAFileThatDoesNotExistWasCreatedEmptyTest()
    {
        var path = Path.Combine(m_directory, "fresh.idx");

        using var store = new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true);

        Assert.That(store.WasCreatedEmpty, Is.True,
            "a store that had no file to load began this session with nothing in it");
    }

    /// <summary>
    /// The half that keeps the fix from being "rebuild anything empty": a store reopened over a file
    /// that exists reports content found, <b>even though it holds nothing</b>.
    /// </summary>
    [Test]
    public void AReopenedEmptyStoreDidNotBeginEmptyTest()
    {
        var path = Path.Combine(m_directory, "reopened.idx");

        using (var created = new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true))
        {
            Assert.That(created.WasCreatedEmpty, Is.True, "the first open makes the file");
            created.Flush();
        }

        using var reopened = new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true);

        Assert.Multiple(() =>
        {
            Assert.That(reopened.Count(), Is.Zero,
                "nothing was written, so the store is empty - which is exactly the ambiguity");
            Assert.That(reopened.WasCreatedEmpty, Is.False,
                "and its content WAS found; emptiness and absence are different things and this is "
                + "the case that tells them apart");
        });
    }

    #endregion

    #region And the index asks through the wrapper

    /// <summary>
    /// An index store is always wrapped for concurrency, so the question has to walk down. If it did
    /// not, the fix would silently never fire and every engine case would have to catch it.
    /// </summary>
    [Test]
    public void AnIndexOverAWrappedFreshStoreReportsItsContentMissingTest()
    {
        var path = Path.Combine(m_directory, "wrapped-fresh.idx");

        var store = new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true);
        var wrapped = new BTreeConcurrentStore(store, options: null, ownsStore: true);

        using var index = new SecondaryIndexKeyValueStore("IX", wrapped, isUnique: false);

        Assert.That(index.ContentWasFound, Is.False,
            "the capability must be found THROUGH the concurrency wrapper - this is the shape every "
            + "index store actually has");
    }

    [Test]
    public void AnIndexOverAWrappedReopenedStoreReportsItsContentFoundTest()
    {
        var path = Path.Combine(m_directory, "wrapped-reopened.idx");

        using (var created = new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true))
            created.Flush();

        var store = new StoreBTree(new StorageFile(path), cacheSize: 64, ownsStorage: true);
        var wrapped = new BTreeConcurrentStore(store, options: null, ownsStore: true);

        using var index = new SecondaryIndexKeyValueStore("IX", wrapped, isUnique: false);

        Assert.Multiple(() =>
        {
            Assert.That(index.Count, Is.Zero, "it holds nothing");
            Assert.That(index.ContentWasFound, Is.True, "and it must still be left alone");
        });
    }

    /// <summary>
    /// Control: a store that cannot answer the question is taken at its word, so nothing that
    /// existed before this capability changes behaviour.
    /// </summary>
    [Test]
    public void ControlAnIndexOverAStoreWithoutTheCapabilityReportsItsContentFoundTest()
    {
        using var index = new SecondaryIndexKeyValueStore("IX", new StoreWithoutOrigin(), isUnique: false);

        Assert.That(index.ContentWasFound, Is.True,
            "an implementation that does not publish the capability must keep the behaviour it had "
            + "before the capability existed");
    }

    /// <summary>
    /// A store that deliberately implements none of the capability interfaces, so the default path
    /// is exercised rather than assumed.
    /// </summary>
    private sealed class StoreWithoutOrigin : IKeyValueStore
    {
        private readonly StoreInMemory m_inner = new();

        public byte[]? Get(ReadOnlySpan<byte> key) => m_inner.Get(key);

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => m_inner.Put(key, value);

        public bool Delete(ReadOnlySpan<byte> key) => m_inner.Delete(key);

        public bool Contains(ReadOnlySpan<byte> key) => m_inner.Get(key) != null;

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey) =>
            m_inner.Scan(startKey, endKey);

        public void Flush() => m_inner.Flush();

        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.GetAsync(key, cancellationToken);

        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default) =>
            m_inner.PutAsync(key, value, cancellationToken);

        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.DeleteAsync(key, cancellationToken);

        public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(byte[]? startKey, byte[]? endKey,
            CancellationToken cancellationToken = default) =>
            m_inner.ScanAsync(startKey, endKey, cancellationToken);

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            m_inner.FlushAsync(cancellationToken);

        public void Dispose() => m_inner.Dispose();

        public string ProviderKey => "no-origin";
    }

    #endregion
}
