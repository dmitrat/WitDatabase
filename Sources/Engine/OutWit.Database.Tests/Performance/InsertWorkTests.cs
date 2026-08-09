using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Storage;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// What an INSERT costs in WORK, counted rather than timed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately NOT in the <c>Performance</c> category, unlike everything else in this folder.</b>
/// CI excludes that category because a wall clock on a shared runner measures the runner, and these
/// cases carry no clock at all: they count the pages an insert reads and writes and compare two
/// arrangements of the same work. A count does not move with the machine, the load or the cache, so
/// there is no reason for the one guard that would have caught 12.3.0's quadratic insert to be the
/// one thing nobody runs.
/// </para>
/// <para>
/// These replace three tests that bounded the wall-clock cost of a single row on a file-backed
/// database - 2.2 to 3.3 ms here against bounds of 1 and 2, while the same engine in memory does
/// 0.05 - and had therefore been red on this machine for as long as anyone remembers. Three
/// permanently red tests teach a team to stop reading red, which is the reason to fix them even
/// though nothing was ever wrong with the engine underneath.
/// </para>
/// </remarks>
[TestFixture]
public class InsertWorkTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDb_InsertWork_{Guid.NewGuid():N}");
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

    #region Cases

    #region INSERT Performance Tests

    /// <summary>
    /// The work an INSERT does must not grow with the rows already in the table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to be a wall clock and it measured the disk.</b> "Less than 1 ms per row" on a
    /// file-backed database where every statement is its own committed transaction is a statement
    /// about the machine's storage - the same engine in memory does the same work in 0.05 ms - so
    /// the bound was permanently red here and would be vacuous on a faster disk. Three tests were
    /// written that way and all three had been red for as long as anyone remembers, which teaches a
    /// team to stop reading red.
    /// </para>
    /// <para>
    /// What they were about is <b>work</b>, and work can be counted. The count does not move with
    /// the machine, the load or the cache, and this one has a defect behind it: the quadratic insert
    /// fixed in 12.3.0 was exactly this - a `UNIQUE` column with no index made every insert scan the
    /// whole table, so the cost per row followed the table's size.
    /// </para>
    /// <para>
    /// The assertion is a RELATION rather than a number. Measured here, 500 inserts cost 1,027 pages
    /// written at both sizes - but the absolute figure belongs to this page size and this row width,
    /// while "the same at 1,000 rows as at 2,500" is a property of the engine.
    /// </para>
    /// </remarks>
    [Test]
    public void InsertWorkPerRowDoesNotGrowWithTheTableTest()
    {
        using var db = OpenCounted("insert-growth.witdb", out var counter);
        using var engine = new WitSqlEngine(db);

        // A payload column, so the table outgrows the cache. Without it 2,500 of these rows fit in
        // 64 pages, every read is answered from memory, and a defect that scans the table once per
        // inserted row would leave the counter at zero reads - measured, and it is why the row is
        // this wide rather than two numbers.
        engine.Execute(
            "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE, Payload VARCHAR(200))");

        InsertGenerated(engine, 500);

        counter.Reset();
        var small = Measure(counter, () => InsertGenerated(engine, MEASURED_ROWS));

        InsertGenerated(engine, 1000);

        counter.Reset();
        var large = Measure(counter, () => InsertGenerated(engine, MEASURED_ROWS));

        TestContext.WriteLine($"{MEASURED_ROWS} inserts into a table of ~500: {small}");
        TestContext.WriteLine($"{MEASURED_ROWS} inserts into a table of ~2,000: {large}");

        // READS are where a scan per inserted row would show, and they are the sharp half: an insert
        // appends at the right edge of the tree and re-reads almost nothing, so the baseline is a
        // handful of pages for 500 rows. A defect that reads the table once per row would put
        // thousands here. ControlAScanIsVisibleToTheCounterTest is what says a scan is countable at
        // all under this cache.
        Assert.That(large.PagesRead, Is.LessThan(MEASURED_ROWS / 10),
            $"500 inserts into the larger table read {large.PagesRead} pages against "
            + $"{small.PagesRead} into the smaller one - an insert is reading the table");

        // WRITES are not asserted equal, and that is measured rather than conceded: 1,169 against
        // 1,168 across the two sizes, which is where a leaf happened to split, not a cost that
        // follows the table. A tenth is far tighter than any size-dependent cost could hide in.
        Assert.That(large.PagesWritten, Is.LessThan((int)(small.PagesWritten * 1.1)),
            $"500 inserts cost {large.PagesWritten} pages in the larger table against "
            + $"{small.PagesWritten} in the smaller one, so the cost follows the table");

        Assert.That(large.Flushes, Is.EqualTo(small.Flushes),
            "an insert reaches storage more often once the table is bigger");
    }

    /// <summary>
    /// Control: a full scan IS visible to the counter, so "the inserts read almost nothing" above is
    /// a statement about the inserts rather than about a cache that hides everything.
    /// </summary>
    /// <remarks>
    /// The first version of the growth test ran on a table of two numeric columns and reported
    /// <b>zero</b> reads at both sizes - 2,500 such rows fit in the cache, so a scan per inserted
    /// row would have left no trace at all. This is the case that would have caught that.
    /// </remarks>
    [Test]
    public void ControlAScanIsVisibleToTheCounterTest()
    {
        using var db = OpenCounted("scan-control.witdb", out var counter);
        using var engine = new WitSqlEngine(db);

        engine.Execute(
            "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE, Payload VARCHAR(200))");

        InsertGenerated(engine, MEASURED_ROWS * 4);

        counter.Reset();
        var scan = Measure(counter, () => engine.Query("SELECT Id, Payload FROM T"));

        TestContext.WriteLine($"one full scan of {MEASURED_ROWS * 4} rows: {scan}");

        Assert.That(scan.PagesRead, Is.GreaterThan(MEASURED_ROWS / 10),
            "a full scan of a table larger than the page cache read almost nothing, so this counter "
            + "cannot see a scan and the growth case above proves nothing");
    }

    /// <summary>
    /// Control: the counter can see work that DOES grow. Without it, "the two counts are equal"
    /// would be satisfied by an instrument that counts nothing.
    /// </summary>
    [Test]
    public void ControlTwiceTheRowsCostTwiceTheWorkTest()
    {
        using var db = OpenCounted("insert-control.witdb", out var counter);
        using var engine = new WitSqlEngine(db);

        engine.Execute(
            "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE, Payload VARCHAR(200))");

        counter.Reset();
        var once = Measure(counter, () => InsertGenerated(engine, MEASURED_ROWS));

        counter.Reset();
        var twice = Measure(counter, () => InsertGenerated(engine, MEASURED_ROWS * 2));

        TestContext.WriteLine($"{MEASURED_ROWS} inserts: {once}; {MEASURED_ROWS * 2} inserts: {twice}");

        Assert.That(twice.PagesWritten, Is.GreaterThan((int)(once.PagesWritten * 1.5)),
            "twice the rows did not cost appreciably more, so this counter cannot see the work it "
            + "is used to compare");
    }

    /// <summary>
    /// An explicit key must cost no more work than a generated one.
    /// </summary>
    /// <remarks>
    /// The old version allowed an explicit key twice the wall clock of a generated one, on the
    /// grounds that it also calls <c>EnsureAutoIncrementAtLeast</c>. Measured as work, with the two
    /// arms matched - the same table, the same 500 keys - they are <b>identical</b>, so the
    /// allowance was for a cost that does not exist.
    /// </remarks>
    [Test]
    public void AnExplicitKeyCostsTheSameWorkAsAGeneratedOneTest()
    {
        var generated = WorkOfFiveHundredInserts("keys-generated.witdb", seedId: 0, explicitKeys: false);
        var explicitly = WorkOfFiveHundredInserts("keys-explicit.witdb", seedId: 0, explicitKeys: true);

        TestContext.WriteLine($"generated: {generated}; explicit: {explicitly}");

        Assert.That(explicitly.PagesWritten, Is.EqualTo(generated.PagesWritten),
            "an explicit key costs more pages than a generated one");

        Assert.That(explicitly.Flushes, Is.EqualTo(generated.Flushes),
            "an explicit key reaches storage more often than a generated one");
    }

    /// <summary>
    /// An explicit key BELOW the row-id counter must cost no more than one above it.
    /// </summary>
    /// <remarks>
    /// <b>Named after what it measures, which is not what it was written for.</b> The old case was
    /// called <c>InsertExplicitIdWithReadLockOptimizationTest</c> and its comment claimed that a key
    /// below the counter needs "read-lock only, no disk write". Measured with the two arms matched -
    /// one table with its counter at 0, one with it at 100,000, the same 500 keys into each - the
    /// storage traffic is <b>identical</b>: 917 pages written and 507 flushes in both, twice over.
    /// So the saving the name promised is not observable as work at all; whatever the optimisation
    /// avoids, it is not a page. What can be asserted is the absence of a PENALTY, and that is what
    /// this case says now.
    /// </remarks>
    [Test]
    public void AnExplicitKeyBelowTheCounterCostsNoMoreThanOneAboveItTest()
    {
        var above = WorkOfFiveHundredInserts("counter-above.witdb", seedId: 0, explicitKeys: true);
        var below = WorkOfFiveHundredInserts("counter-below.witdb", seedId: 100_000, explicitKeys: true);

        TestContext.WriteLine($"above the counter: {above}; below the counter: {below}");

        Assert.That(below.PagesWritten, Is.EqualTo(above.PagesWritten),
            "a key below the counter costs more pages than one above it, which is the opposite of "
            + "the fast path this was written to protect");

        Assert.That(below.Flushes, Is.EqualTo(above.Flushes),
            "a key below the counter reaches storage more often than one above it");
    }

    #endregion

    #endregion

    #region Counting tools

    /// <summary>
    /// How many rows each measured stretch inserts. Large enough that a per-row difference of one
    /// page is far outside anything the tree's shape can account for, small enough to stay a test.
    /// </summary>
    private const int MEASURED_ROWS = 500;

    /// <summary>What a stretch of statements cost, in work rather than in milliseconds.</summary>
    private sealed record Work(int PagesRead, int PagesWritten, int Flushes)
    {
        public override string ToString() =>
            $"{PagesRead} pages read, {PagesWritten} written, {Flushes} flushes";
    }

    /// <summary>
    /// A file-backed B+Tree database with every page write counted on the way through.
    /// </summary>
    /// <remarks>
    /// <b>The cache is deliberately smaller than the table will be.</b> With the default cache the
    /// whole file fits, every read is answered from memory and the counter sees <c>0</c> reads - so
    /// a defect that reads the table once per inserted row, which is exactly the quadratic insert
    /// 12.3.0 fixed, would leave no trace in these numbers at all. At this size the table grows past
    /// the cache and a scan has to reach the disk to happen.
    /// </remarks>
    private WitDatabase OpenCounted(string fileName, out CountingStorage counter)
    {
        counter = new CountingStorage(new StorageFile(Path.Combine(m_testDir, fileName)));

        return new WitDatabaseBuilder()
            .WithStorage(counter)
            .WithBTree()
            .WithTransactions()
            .WithCacheSize(CACHE_PAGES)
            .Build();
    }

    /// <summary>
    /// Small enough that the table outgrows it - see <see cref="OpenCounted"/> - and large enough
    /// that a statement does not run out of unpinned slots, which a build over a table this size
    /// does at eight.
    /// </summary>
    private const int CACHE_PAGES = 64;

    private static Work Measure(CountingStorage counter, Action work)
    {
        var before = (counter.PagesRead, counter.PagesWritten, counter.Flushes);

        work();

        return new Work(
            counter.PagesRead - before.PagesRead,
            counter.PagesWritten - before.PagesWritten,
            counter.Flushes - before.Flushes);
    }

    private static readonly string Payload = new('x', 200);

    private static void InsertGenerated(WitSqlEngine engine, int rows)
    {
        for (var i = 0; i < rows; i++)
            engine.Execute($"INSERT INTO T (Value, Payload) VALUES ({i}.0, '{Payload}')");
    }

    /// <summary>
    /// One arm of a matched pair: a table holding a single row whose key puts the row-id counter
    /// where the caller wants it, and then the same 500 keys inserted into it.
    /// </summary>
    /// <remarks>
    /// The seed row is what makes the two arms comparable. An earlier shape of this measurement
    /// compared a table of 500 rows against a table of one and read the difference as the counter's
    /// cost; it was the tree's shape, and with the arms matched the difference is zero.
    /// </remarks>
    private Work WorkOfFiveHundredInserts(string fileName, long seedId, bool explicitKeys)
    {
        using var db = OpenCounted(fileName, out var counter);
        using var engine = new WitSqlEngine(db);

        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute($"INSERT INTO T (Id, Value) VALUES ({seedId}, 1.0)");

        counter.Reset();

        return Measure(counter, () =>
        {
            for (var i = 1; i <= MEASURED_ROWS; i++)
            {
                engine.Execute(explicitKeys
                    ? $"INSERT INTO T (Id, Value) VALUES ({i}, {i}.0)"
                    : $"INSERT INTO T (Value) VALUES ({i}.0)");
            }
        });
    }

    #endregion
}
