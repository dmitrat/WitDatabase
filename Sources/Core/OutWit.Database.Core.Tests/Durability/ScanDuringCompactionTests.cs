using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// A scan that is still running when compaction replaces the tables underneath it.
/// </summary>
/// <remarks>
/// <c>StoreLsm.Scan</c> takes the SSTable read lock only long enough to collect an enumerable from
/// each reader, then lets it go - the files are read afterwards, as the caller pulls. Compaction takes
/// the write lock, <b>disposes every reader</b> and clears the list. A scan in flight then read from a
/// closed file: <c>ObjectDisposedException: Cannot access a closed file</c>.
///
/// It is a pre-existing race and it was found the way this project has learned such things are found -
/// on a second machine. Locally the whole suite was green; CI failed
/// <c>LsmStoreWithEncryptionTest</c>, because the implicit per-statement transaction made commits (and
/// each commit scans the whole store) and memtable flushes far more frequent, so the window that had
/// always been there started being hit.
///
/// <b>Deterministic rather than stressed.</b> A loop that hammers scans and compactions proves only
/// that the race did not happen that time. Here the scan is parked mid-enumeration - one item pulled,
/// the enumerator held - compaction is run to completion, and only then is the scan resumed. The
/// interleaving is exact.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class ScanDuringCompactionTests
{
    #region Fields

    private const int ROWS = 200;
    private const int SMALL_BLOCK = 128;

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-scanrace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    [Test]
    public void ScanSurvivesACompactionThatReplacesItsTablesTest()
    {
        WriteSeveralTables();

        using var store = new StoreLsm(m_directory, CompactsOnDemand());

        Assert.That(Directory.GetFiles(m_directory, "sst_*.sst"), Has.Length.GreaterThan(1),
            "the fixture needs more than one table, or compaction has nothing to replace");

        using var scan = store.Scan(null, null).GetEnumerator();

        Assert.That(scan.MoveNext(), Is.True, "the scan has started and is holding its readers");

        var firstKey = scan.Current.Key;

        // The tables the scan is reading are merged and the old ones deleted, right underneath it.
        store.Compact();
        store.WaitForCompaction();

        var seen = 1;

        Assert.DoesNotThrow(() =>
        {
            while (scan.MoveNext())
                seen++;
        }, "a scan already in flight must keep working when compaction replaces the tables beneath "
           + "it - the readers it is using are its own until it is finished with them");

        TestContext.Out.WriteLine($"the scan returned {seen} of {ROWS} rows across the compaction");

        Assert.Multiple(() =>
        {
            Assert.That(firstKey, Is.EqualTo(Key(0)), "the scan started where it should have");

            Assert.That(seen, Is.EqualTo(ROWS),
                "and it returned every row - a scan that survives by silently stopping early would "
                + "be worse than one that throws");
        });
    }

    /// <summary>
    /// The control: the file the scan was reading really is gone by the time the scan finishes. If
    /// compaction left the inputs in place the test above would pass without proving anything.
    /// </summary>
    [Test]
    public void ControlCompactionReallyReplacesTheTablesTest()
    {
        WriteSeveralTables();

        using var store = new StoreLsm(m_directory, CompactsOnDemand());

        var before = Directory.GetFiles(m_directory, "sst_*.sst");

        store.Compact();
        store.WaitForCompaction();

        var after = Directory.GetFiles(m_directory, "sst_*.sst");

        Assert.Multiple(() =>
        {
            Assert.That(after, Has.Length.LessThan(before.Length),
                "compaction must actually reduce the table count");

            Assert.That(before.Any(f => !after.Contains(f)), Is.True,
                "and at least one of the tables the scan would have been reading must be gone - "
                + "otherwise the race the other test provokes is not being provoked");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Leaves several uncompacted tables on disk, then lets go of them.
    /// </summary>
    /// <remarks>
    /// Written with the trigger out of the way, because compaction runs at the end of the very flush
    /// that reaches it - so a store can never be caught holding trigger-many uncompacted tables. The
    /// scan fixture then reopens the directory with a low trigger, and recovery loads the tables
    /// without merging them, which is the state the test needs.
    /// </remarks>
    private void WriteSeveralTables()
    {
        using var store = new StoreLsm(m_directory, new LsmOptions
        {
            BackgroundCompaction = false,
            Level0CompactionTrigger = 100,
            MemTableSizeLimit = 64 * 1024,
            BlockSize = SMALL_BLOCK,
            EnableBlockCache = false
        });

        for (int batch = 0; batch < 4; batch++)
        {
            for (int i = 0; i < ROWS / 4; i++)
                store.Put(Key(batch * (ROWS / 4) + i), Value(batch * (ROWS / 4) + i));

            store.Flush();
        }
    }

    // A small block and no block cache on purpose. With the defaults each table fits in a single
    // block, so the merge reads every source's first (and only) block while it is priming its heap -
    // and never touches a file again. The scan would then survive a compaction for the wrong reason,
    // and this fixture would be green whether or not the readers were held.
    private static LsmOptions CompactsOnDemand() => new()
    {
        BackgroundCompaction = false,
        Level0CompactionTrigger = 2,
        MemTableSizeLimit = 64 * 1024,
        BlockSize = SMALL_BLOCK,
        EnableBlockCache = false
    };

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"k{i:D5}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i:D5}");

    #endregion
}
