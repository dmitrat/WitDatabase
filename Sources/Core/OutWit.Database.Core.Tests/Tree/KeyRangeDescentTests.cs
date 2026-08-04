using System.Diagnostics;
using System.Text;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Tree;

/// <summary>
/// The smallest and the largest key, by descending the tree rather than by walking it.
/// </summary>
/// <remarks>
/// <para>
/// <c>ISecondaryIndex.GetLastEntry</c> was <c>Scan(null, null).LastOrDefault()</c> - a full pass over an
/// index to read one key, in a public API - and it is why the query optimizer could not ask what range
/// a column covers and estimated every range predicate at a flat 20% of the table.
/// </para>
/// <para>
/// <b>Two things have to be true, and the second is the one that bites.</b> The keys must be right, and
/// finding them must not read the store. An internal node holds <c>KeyCount</c> keys and
/// <c>KeyCount + 1</c> children, so a rightmost descent that stops at <c>KeyCount - 1</c> walks into the
/// second-largest subtree and returns a key that exists, is plausible, and is not the largest - which no
/// test that only checks "a key came back" would catch. The sizes below are chosen to build a tree of
/// more than one level, so the descent has somewhere to go wrong.
/// </para>
/// </remarks>
[TestFixture]
public class KeyRangeDescentTests
{
    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_keyrange_{Guid.NewGuid():N}");
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

    #region The shapes

    [Test]
    public void AnEmptyStoreHasNoFirstOrLastKeyTest()
    {
        using var store = NewStore();

        Assert.Multiple(() =>
        {
            Assert.That(store.GetFirstKey(), Is.Null);
            Assert.That(store.GetLastKey(), Is.Null);
        });
    }

    [Test]
    public void AStoreOfOneKeyReportsItAsBothTest()
    {
        using var store = NewStore();
        store.Put(Key(42), Value(42));

        Assert.Multiple(() =>
        {
            Assert.That(store.GetFirstKey(), Is.EqualTo(Key(42)));
            Assert.That(store.GetLastKey(), Is.EqualTo(Key(42)));
        });
    }

    /// <param name="count">
    /// 10 fits in one leaf; 1,000 and 20,000 do not, and the last one builds a tree deep enough that a
    /// wrong child index at an internal node lands in a different subtree.
    /// </param>
    [TestCase(10)]
    [TestCase(1000)]
    [TestCase(20000)]
    public void TheFirstAndLastKeyAreTheSmallestAndLargestTest(int count)
    {
        using var store = NewStore();

        // Inserted in an order that is neither ascending nor descending, so a tree that happens to keep
        // insertion order cannot pass by accident.
        foreach (var i in Shuffled(count))
            store.Put(Key(i), Value(i));

        Assert.Multiple(() =>
        {
            Assert.That(store.GetFirstKey(), Is.EqualTo(Key(0)),
                $"the smallest of {count} keys");

            Assert.That(store.GetLastKey(), Is.EqualTo(Key(count - 1)),
                $"the largest of {count} keys - a descent that takes the wrong child at an internal " +
                "node returns a key that exists but is not the largest");
        });
    }

    /// <summary>
    /// Control: the answers agree with a full scan, which is the definition being implemented.
    /// </summary>
    [Test]
    public void ControlTheyAgreeWithAFullScanTest()
    {
        using var store = NewStore();

        foreach (var i in Shuffled(5000))
            store.Put(Key(i), Value(i));

        var scanned = store.Scan(null, null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(store.GetFirstKey(), Is.EqualTo(scanned.First().Key));
            Assert.That(store.GetLastKey(), Is.EqualTo(scanned.Last().Key));
        });
    }

    #endregion

    #region The cost

    /// <summary>
    /// The point of the exercise: finding the largest key must cost the depth of the tree, not its size.
    /// </summary>
    /// <remarks>
    /// Asserted as a ratio against a full scan of the same store rather than as an absolute time, so the
    /// measurement means the same thing on a slow machine as on a fast one. A descent that had quietly
    /// become a scan again would come out at about 1.
    /// </remarks>
    [Test]
    public void FindingTheLargestKeyDoesNotReadTheStoreTest()
    {
        using var store = NewStore();

        foreach (var i in Shuffled(20000))
            store.Put(Key(i), Value(i));

        // Warm: the first of anything pays for page cache misses that say nothing about the algorithm.
        store.GetLastKey();
        store.Scan(null, null).Count();

        var descent = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++)
            store.GetLastKey();
        descent.Stop();

        var scan = Stopwatch.StartNew();
        store.Scan(null, null).Count();
        scan.Stop();

        var perDescent = descent.Elapsed.TotalMilliseconds / 50;
        var perScan = scan.Elapsed.TotalMilliseconds;

        TestContext.Out.WriteLine(
            $"KEY RANGE  20,000 keys: descent {perDescent:0.000} ms, full scan {perScan:0.000} ms, " +
            $"ratio {perScan / Math.Max(0.0001, perDescent):0} x");

        Assert.That(perDescent, Is.LessThan(perScan / 10),
            $"reading the largest key took {perDescent:0.000} ms against {perScan:0.000} ms for a full " +
            "scan of the same store - that is not a descent");
    }

    #endregion

    #region Tools

    private StoreBTree NewStore()
    {
        var path = Path.Combine(m_root, $"keyrange_{Interlocked.Increment(ref m_sequence):D3}.witdb");
        return new StoreBTree(path);
    }

    /// <summary>
    /// Deterministically shuffled, so a failure repeats.
    /// </summary>
    /// <remarks>
    /// The first version multiplied by a large constant to avoid a seeded <c>Random</c>, overflowed
    /// <c>int</c>, and produced a negative index - so every case here failed on the helper before the
    /// descent was asked anything. A fixed seed says the same thing and cannot do that.
    /// </remarks>
    private static IEnumerable<int> Shuffled(int count)
    {
        var values = Enumerable.Range(0, count).ToArray();
        var random = new Random(20260803);

        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }

        return values;
    }

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"key{i:D7}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i}");

    #endregion
}
