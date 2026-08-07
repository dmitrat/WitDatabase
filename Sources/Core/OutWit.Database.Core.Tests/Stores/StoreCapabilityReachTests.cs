using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Stores;

/// <summary>
/// What a database hands out is the outermost wrapper, and what the layers below it can do must be
/// findable from there.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this is about is not hypothetical, it happened twice.</b> <c>Checkpoint</c> was
/// forwarded by one wrapper and lost by the three above it, so a checkpoint asked of an LSM database
/// never moved the memtable. And <c>KeyValueStoreStatisticsExtensions.Count</c> tests
/// <c>store is IKeyValueStoreStatistics</c> - which is false for every wrapped store - and falls back
/// to scanning the entire database, correctly and expensively, with no way for the caller to tell.
/// </para>
/// <para>
/// Forwarding is per-capability and has to be remembered whenever either a capability or a wrapper is
/// added. <c>IStoreWrapper</c> is per-wrapper and is remembered once; a caller walks down and finds
/// what is there, including capabilities that did not exist when the wrapper was written.
/// </para>
/// <para>
/// <b>This fixture states the chain rather than assuming it</b>, and prints it, because "the
/// capability was found" says nothing about how many layers were actually walked - one wrapper that
/// stopped reporting its inner store would leave every case here green while hiding everything below
/// it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StoreCapabilityReachTests
{
    #region Setup

    private string m_root = null!;

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"capability_reach_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_root, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    [TestCase(Store.Lsm)]
    [TestCase(Store.BTree)]
    public void TheStoreThatDoesTheStoringIsReachableTest(Store store)
    {
        using var database = Build(store);

        var chain = database.Store.Chain();

        TestContext.Out.WriteLine(
            $"{store} chain: {string.Join(" -> ", chain.Select(link => link.GetType().Name))}");

        Assert.Multiple(() =>
        {
            // CONTROL: a chain of one is a wrapper that does not say what it wraps, and every
            // assertion below would then be about the outermost store talking to itself.
            Assert.That(chain, Has.Count.GreaterThan(1),
                "CONTROL: the database's store reports nothing underneath it, so this case cannot tell "
                + "'the capability is reachable' from 'the outermost store happens to have it'");

            Assert.That(chain[0], Is.Not.InstanceOf(Expected(store)),
                "the store that does the storing is the outermost one, so nothing here is being reached "
                + "THROUGH anything and the fixture is not measuring what it claims");

            Assert.That(database.Store.FindCapability<IKeyValueStore>(), Is.Not.Null);

            Assert.That(chain.Last(), Is.InstanceOf(Expected(store)),
                $"the bottom of the chain is not the {store} store");
        });
    }

    /// <summary>
    /// The capability the panel needs first, and the one that was unreachable.
    /// </summary>
    [TestCase(Store.Lsm)]
    [TestCase(Store.BTree)]
    public void StatisticsAreReachableThroughTheWrappersTest(Store store)
    {
        using var database = Build(store);

        Assert.That(database.Store, Is.Not.InstanceOf<IKeyValueStoreStatistics>(),
            "CONTROL: the outermost store implements the capability itself, so finding it proves "
            + "nothing about reaching through the chain");

        var statistics = database.Store.FindCapability<IKeyValueStoreStatistics>();

        Assert.That(statistics, Is.Not.Null,
            "no layer of the chain can be asked for statistics, so a report has nothing to read and "
            + "a count falls back to scanning the whole database");
    }

    /// <summary>
    /// And the walk answers "no" rather than guessing.
    /// </summary>
    [Test]
    public void ACapabilityNothingImplementsIsNotFoundTest()
    {
        using var database = Build(Store.BTree);

        Assert.That(database.Store.FindCapability<INothingImplementsThis>(), Is.Null,
            "the walk invented a capability, so a null from it means nothing");
    }

    #endregion

    #region Tools

    public enum Store
    {
        Lsm,
        BTree
    }

    private interface INothingImplementsThis;

    private static Type Expected(Store store) =>
        store == Store.Lsm ? typeof(StoreLsm) : typeof(StoreBTree);

    private WitDatabase Build(Store store)
    {
        var path = Path.Combine(m_root, store.ToString());

        return store == Store.Lsm
            ? new WitDatabaseBuilder().WithLsmTree(path).WithMvcc().Build()
            : new WitDatabaseBuilder().WithFilePath(path + ".witdb").WithBTree().WithMvcc().Build();
    }

    #endregion
}
