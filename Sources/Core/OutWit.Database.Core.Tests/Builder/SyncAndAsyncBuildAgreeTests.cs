using System.Collections;
using System.Reflection;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Providers;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Builder;

/// <summary>
/// Phase 11 follow-up - the two construction routes, asked to agree. For every configuration,
/// <see cref="WitDatabaseBuilder.Build"/> and <see cref="WitDatabaseBuilder.BuildAsync"/> must produce
/// the same engine.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two routes that disagree is the defect class of this whole phase</b>, found three times already:
/// the LSM options were parsed on a path the builder did not take, the ADO layer forwarded the
/// parameter bag only when <c>Store=</c> was written out, and the census then caught the same shape in
/// the cache. This is the fourth coordinate, and the only one where the two routes are two methods on
/// the same class - so "which one is right" is not a judgement call: they must be the same.
/// </para>
/// <para>
/// <b>The signature is structural, not behavioural.</b> Two builds of the same configuration are
/// compared by the runtime types of every store, page cache and storage reachable in their object
/// graphs. That is enough to see a route that silently substituted a different store, and it cannot
/// see whether the store then behaves - which is what the combination matrix is for.
/// </para>
/// <para>
/// <b>Controls in both directions.</b> The default configuration must agree, or the comparison is
/// broken rather than the builder; and two configurations that genuinely differ must produce different
/// signatures, or the signature is a constant and every agreement it reports is worthless.
/// </para>
/// </remarks>
[TestFixture]
public class SyncAndAsyncBuildAgreeTests
{
    #region Types

    public sealed record Configuration(string Label, Action<WitDatabaseBuilder, string> Configure)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// A third-party store, registered in the provider registry exactly as the construction kit
    /// documents - the central claim, in the smallest form that can be asserted. It delegates to
    /// <see cref="StoreInMemory"/>, so what is being tested is the registration rather than the
    /// storage.
    /// </summary>
    public sealed class ProbeStore : IKeyValueStore
    {
        public const string PROBE_KEY = "probe-store";

        private readonly StoreInMemory m_inner = new();

        public byte[]? Get(ReadOnlySpan<byte> key) => m_inner.Get(key);

        public ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.GetAsync(key, cancellationToken);

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) => m_inner.Put(key, value);

        public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default) =>
            m_inner.PutAsync(key, value, cancellationToken);

        public bool Delete(ReadOnlySpan<byte> key) => m_inner.Delete(key);

        public ValueTask<bool> DeleteAsync(byte[] key, CancellationToken cancellationToken = default) =>
            m_inner.DeleteAsync(key, cancellationToken);

        public IEnumerable<(byte[] Key, byte[] Value)> Scan(byte[]? startKey, byte[]? endKey) =>
            m_inner.Scan(startKey, endKey);

        public IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(byte[]? startKey, byte[]? endKey,
            CancellationToken cancellationToken = default) => m_inner.ScanAsync(startKey, endKey, cancellationToken);

        public void Flush() => m_inner.Flush();

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) =>
            m_inner.FlushAsync(cancellationToken);

        public string ProviderKey => PROBE_KEY;

        public void Dispose() => m_inner.Dispose();
    }

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [OneTimeSetUp]
    public void RegisterProbeStore()
    {
        ProviderRegistry.Instance.RegisterOrReplace<IKeyValueStore>(ProbeStore.PROBE_KEY, _ => new ProbeStore());
    }

    [OneTimeTearDown]
    public void UnregisterProbeStore()
    {
        ProviderRegistry.Instance.Unregister<IKeyValueStore>(ProbeStore.PROBE_KEY);
    }

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_routes_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_sequence = 0;
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region The configurations

    private static IEnumerable<Configuration> Configurations()
    {
        yield return new Configuration("btree (default)",
            (b, path) => b.WithFilePath(path).WithBTree());

        yield return new Configuration("btree + lru cache",
            (b, path) => b.WithFilePath(path).WithBTree().WithCacheKey("lru"));

        yield return new Configuration("btree + page size",
            (b, path) => b.WithFilePath(path).WithBTree().WithPageSize(8192));

        yield return new Configuration("inmemory store, file data source",
            (b, path) => b.WithFilePath(path).WithStoreKey(StoreInMemory.PROVIDER_KEY));

        yield return new Configuration("third-party store provider",
            (b, path) => b.WithFilePath(path).WithStoreKey(ProbeStore.PROBE_KEY));

        yield return new Configuration("lsm",
            (b, path) => b.WithLsmTree(path));

        yield return new Configuration("encrypted btree",
            (b, path) => b.WithFilePath(path).WithBTree().WithEncryption("routes-secret"));
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: the signature can tell two genuinely different configurations apart. Without it, a
    /// signature that returned the same constant everywhere would report perfect agreement.
    /// </summary>
    [Test]
    public void ControlTheSignatureSeesADifferenceTest()
    {
        using var btree = new WitDatabaseBuilder().WithFilePath(NewPath()).WithBTree().Build();
        using var inMemory = new WitDatabaseBuilder().WithMemoryStorage().WithStoreKey(StoreInMemory.PROVIDER_KEY).Build();

        Assert.That(Signature(btree.Store), Is.Not.EqualTo(Signature(inMemory.Store)),
            "a B+Tree database and an in-memory one produced the same structural signature - the " +
            "signature is blind, and every agreement this fixture reports means nothing");
    }

    /// <summary>
    /// Control: the same configuration built twice by the same route agrees with itself, so a
    /// disagreement below is between the routes rather than between two runs.
    /// </summary>
    [Test]
    public void ControlTheSameRouteAgreesWithItselfTest()
    {
        using var first = new WitDatabaseBuilder().WithFilePath(NewPath()).WithBTree().WithCacheKey("lru").Build();
        using var second = new WitDatabaseBuilder().WithFilePath(NewPath()).WithBTree().WithCacheKey("lru").Build();

        Assert.That(Signature(first.Store), Is.EqualTo(Signature(second.Store)),
            "two builds of one configuration by one route disagreed - the signature is picking up " +
            "run-to-run variation and cannot be used to compare routes");
    }

    #endregion

    #region The probe

    [Test]
    [TestCaseSource(nameof(Configurations))]
    public async Task BothRoutesBuildTheSameEngineTest(Configuration configuration)
    {
        var syncBuilder = new WitDatabaseBuilder();
        configuration.Configure(syncBuilder, NewPath());

        var asyncBuilder = new WitDatabaseBuilder();
        configuration.Configure(asyncBuilder, NewPath());

        using var synchronous = syncBuilder.Build();
        var asynchronous = await asyncBuilder.BuildAsync();

        try
        {
            var expected = Signature(synchronous.Store);
            var actual = Signature(asynchronous.Store);

            TestContext.Out.WriteLine($"ROUTES {configuration.Label,-34} sync [{expected}]  async [{actual}]");

            Assert.That(actual, Is.EqualTo(expected),
                $"{configuration.Label}: Build() and BuildAsync() produced different engines. Two " +
                "construction routes that disagree is the defect class this phase found three times; " +
                "the asynchronous one is the one that skips the provider registry.");
        }
        finally
        {
            asynchronous.Dispose();
        }
    }

    #endregion

    #region The signature

    /// <summary>
    /// The runtime types of every store, page cache and storage reachable from the built store.
    /// </summary>
    /// <remarks>
    /// Field-walked rather than property-walked, for the reason the census records: a property can
    /// compute, and a computed value is not what was built. Ordered collections are walked as well as
    /// counted - the census reported <c>CacheSize</c> as inert until it did, because the page cache
    /// keeps its shards in an array.
    /// </remarks>
    private static string Signature(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var found = new SortedSet<string>(StringComparer.Ordinal);

        Walk(root, depth: 0);

        return string.Join(" | ", found);

        void Walk(object? node, int depth)
        {
            if (node == null || depth > 6 || !seen.Add(node))
                return;

            var type = node.GetType();

            if (node is IKeyValueStore or IPageCache or IStorage)
                found.Add($"{Role(node)}:{type.Name}");

            if (node is string || type.IsPrimitive)
                return;

            if (node is IEnumerable enumerable and not IDictionary)
            {
                foreach (var item in enumerable)
                {
                    if (item != null && !item.GetType().IsPrimitive && item is not string)
                        Walk(item, depth + 1);
                }

                return;
            }

            foreach (var field in Fields(type))
            {
                if (field.FieldType.IsPrimitive || field.FieldType == typeof(string))
                    continue;

                object? value;

                try
                {
                    value = field.GetValue(node);
                }
                catch
                {
                    continue;
                }

                Walk(value, depth + 1);
            }
        }
    }

    private static string Role(object node)
    {
        return node switch
        {
            IKeyValueStore => "store",
            IPageCache => "cache",
            _ => "storage"
        };
    }

    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return field;
        }
    }

    #endregion

    #region Helpers

    private string NewPath()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "routes.witdb");
    }

    #endregion
}
