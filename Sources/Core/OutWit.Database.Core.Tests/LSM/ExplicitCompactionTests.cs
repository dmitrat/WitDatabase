using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.LSM;

/// <summary>
/// An explicit <c>Compact()</c> compacts. The trigger decides the AUTOMATIC path and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>It used to do nothing, silently.</b> <c>Compact()</c> applied
/// <see cref="LsmOptions.Level0CompactionTrigger"/> - the threshold that decides whether a flush is
/// worth an automatic rewrite - to an explicit call as well, and returned without a word when the
/// store was below it. Measured 2026-08-07 before the change: six checkpoints with the trigger raised
/// to 100 left <b>six</b> SSTables, and <c>Compact()</c> left six, with the compaction counter still
/// at zero. Worse at the default trigger of four, where the automatic path keeps the store BELOW its
/// own threshold - so an explicit call was refused nearly every time it could be made.
/// </para>
/// <para>
/// This is WS-57's first finding and it decided the shape of the maintenance surface: a button that
/// calls a method which quietly declines is the thing phase 4 met once already and named
/// "a stable, powerless instrument". The method had to become true before anything could offer it.
/// </para>
/// <para>
/// <b>The control is the second case</b>, and it is what stops this from being "compact everything
/// always": with the trigger raised, six checkpoints must still leave six files, because nothing asked.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ExplicitCompactionTests
{
    #region Setup

    private string m_directory = null!;

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"lsm_compact_{Guid.NewGuid():N}");
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
    public void AnExplicitCompactMergesBelowTheTriggerTest()
    {
        // Raised far above what this test produces, so the automatic path cannot be what merges.
        var options = Options(trigger: 100);

        using var store = new StoreLsm(m_directory, options);

        WriteCheckpoints(store, tables: 6, perTable: 50);

        var before = SstableCount();
        var compactionsBefore = store.Statistics.Compactions;

        store.Compact();

        var after = SstableCount();

        TestContext.Out.WriteLine(
            $"explicit compact: {before} -> {after} sst files, compactions " +
            $"{compactionsBefore} -> {store.Statistics.Compactions}");

        Assert.Multiple(() =>
        {
            // CONTROL: without several files there is nothing to merge and the case would pass on an
            // engine that never compacts at all.
            Assert.That(before, Is.EqualTo(6),
                "CONTROL: the six checkpoints did not leave six SSTables, so this case is not measuring "
                + "an explicit compaction below the trigger");

            Assert.That(after, Is.EqualTo(1),
                "an explicit Compact() left the SSTables alone. It is applying the automatic trigger to "
                + "a call that asked for the work, which is a refusal with no message.");

            Assert.That(store.Statistics.Compactions, Is.EqualTo(compactionsBefore + 1),
                "no compaction was recorded, so whatever changed the file count was not one");

            // Scanned, not counted: on this engine a count is separate state, and phase 4 built a
            // false report of lost commits on exactly that difference.
            Assert.That(Scan(store), Is.EqualTo(300),
                "the compaction lost rows");
        });
    }

    /// <summary>
    /// The control: the trigger still decides the automatic path.
    /// </summary>
    [Test]
    public void TheAutomaticPathStillObeysTheTriggerTest()
    {
        using var store = new StoreLsm(m_directory, Options(trigger: 100));

        WriteCheckpoints(store, tables: 6, perTable: 50);

        Assert.That(SstableCount(), Is.EqualTo(6),
            "six checkpoints below a trigger of 100 compacted anyway - the explicit threshold has "
            + "leaked into the automatic path, and every flush now rewrites the whole store");
    }

    /// <summary>
    /// And a single SSTable is not a compaction, however loudly it is asked for.
    /// </summary>
    [Test]
    public void OneSstableIsNothingToMergeTest()
    {
        using var store = new StoreLsm(m_directory, Options(trigger: 100));

        WriteCheckpoints(store, tables: 1, perTable: 50);

        var compactionsBefore = store.Statistics.Compactions;

        store.Compact();

        Assert.Multiple(() =>
        {
            Assert.That(SstableCount(), Is.EqualTo(1), "one file was rewritten for no reason");

            Assert.That(store.Statistics.Compactions, Is.EqualTo(compactionsBefore),
                "a compaction was recorded for a store with a single SSTable");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Foreground compaction, so the case measures the work rather than a race with a pool thread.
    /// </summary>
    private static LsmOptions Options(int trigger) => new()
    {
        BackgroundCompaction = false,
        Level0CompactionTrigger = trigger
    };

    private static void WriteCheckpoints(StoreLsm store, int tables, int perTable)
    {
        for (var table = 0; table < tables; table++)
        {
            for (var i = 0; i < perTable; i++)
                store.Put(Bytes($"k{table:D2}_{i:D3}"), Bytes($"value {table} {i}"));

            store.Checkpoint();
        }
    }

    private int SstableCount() => Directory.GetFiles(m_directory, "*.sst").Length;

    private static int Scan(StoreLsm store) => store.Scan(null, null).Count();

    private static byte[] Bytes(string text) => TextEncoding.UTF8.GetBytes(text);

    #endregion
}
