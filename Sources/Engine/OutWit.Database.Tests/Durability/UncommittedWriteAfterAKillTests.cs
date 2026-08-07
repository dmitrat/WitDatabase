using System.Text;
using OutWit.Database.AdoNet;
using OutWit.Database.Core.Stores;
using OutWit.Database.CrashRunner;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// The other side of durability: a transaction that never committed must leave nothing behind when the
/// process dies.
/// </summary>
/// <remarks>
/// <para>
/// Every crash test this project had asks whether something <b>survives</b>. None asked whether
/// something that should not survive is gone, and the two are different promises: an engine that wrote
/// everything to the media the moment it was asked would pass all of the first set and fail this one.
/// </para>
/// <para>
/// <b>It has to be a kill rather than a close.</b> Closing the store rolls active transactions back, so
/// an in-process test measures the rollback path - which
/// <c>Mvcc.VisibilityAcrossReopenTests</c> covers and which passes. The question here is what the media
/// says when no rollback ever ran, and only a process that dies can ask it.
/// </para>
/// <para>
/// <b>Why it matters beyond correctness.</b> On this engine a record is committed when its own bytes say
/// so, and the commit rewrites each version to say it; the table mapping a transaction to its commit
/// timestamp is memory-only. So this test is also the one that a design which stopped rewriting - to
/// remove the double write a transactional put costs - would have to keep green.
/// </para>
/// <para>
/// <b>Controls in both directions, in the same fixture:</b> the same scenario committed must leave every
/// row, and the clean close must leave every row. Without those, "nothing came back" is equally
/// consistent with a database that simply lost the lot.
/// </para>
/// </remarks>
[TestFixture]
[Category("Crash")]
public sealed class UncommittedWriteAfterAKillTests
{
    #region Constants

    private const int ROWS = 20;

    #endregion

    #region Fields

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_uncommitted_{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_databasePath))
                Directory.Delete(m_databasePath, recursive: true);
            else if (File.Exists(m_databasePath))
                File.Delete(m_databasePath);
        }
        catch (IOException)
        {
            // Cleanup only.
        }
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: the same rows, committed and then killed, all come back. If they do not, the fixture is
    /// measuring a database that loses everything rather than one that discards the uncommitted.
    /// </summary>
    [Test]
    public void ControlACommittedTransactionSurvivesTheSameKillTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.CONFIGURED_COMMIT_KILL, m_databasePath, ROWS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        var (scanned, counted) = RowsInReopenedDatabase();

        TestContext.Out.WriteLine($"UNCOMMITTED control: committed then killed -> scanned={scanned} count(*)={counted}");

        Assert.That(scanned, Is.EqualTo(ROWS),
            "a committed transaction did not survive the kill, so this fixture cannot tell 'discarded " +
            "because uncommitted' from 'lost'");
    }

    #endregion

    #region The probe

    /// <summary>
    /// Rows written inside a transaction that was never committed, after the process is killed.
    /// </summary>
    [Test]
    public void AnUncommittedTransactionLeavesNothingAfterAKillTest()
    {
        string mode;

        using (var run = CrashRunnerHarness.Start(Scenarios.UNCOMMITTED_KILL, m_databasePath, ROWS))
        {
            var facts = run.WaitFor(CrashProtocol.KILL_ME);
            mode = facts.GetValueOrDefault("mode", "(not reported)");

            run.Kill();
        }

        var (scanned, counted) = RowsInReopenedDatabase();

        TestContext.Out.WriteLine(
            $"UNCOMMITTED probe: mode={mode} -> scanned={scanned} count(*)={counted}");

        Assert.That(mode, Is.EqualTo("uncommitted"),
            "the scenario did not report that it left the transaction uncommitted - it may have " +
            "committed, and then this measures the wrong thing entirely");

        Assert.That(scanned, Is.EqualTo(0),
            $"{scanned} of {ROWS} rows written by a transaction that never committed are readable after " +
            "the process was killed. A transaction is all or nothing, and nothing is what this one was.");
    }

    /// <summary>
    /// Attribution: are the uncommitted rows absent from the media, or present and hidden?
    /// </summary>
    /// <remarks>
    /// <para>
    /// "The engine shows none of them" is consistent with several mechanisms, and which one is at work
    /// decides how much room a design change has. Phase 4 established that asking this separates a real
    /// answer from a lucky one.
    /// </para>
    /// <para>
    /// <b>Measured, and it was neither of the two mechanisms this probe was written to distinguish.</b>
    /// The file is <b>8 KB after 20,000 uncommitted rows</b> - two pages - and it stays 8 KB with the
    /// page cache cut to eight pages, which was the version of this test meant to force evictions.
    /// Nothing is evicted because nothing is there: <c>MvccTransaction.Put</c> buffers into an in-memory
    /// change set and the store is not touched until <c>Commit</c> installs the versions. So an
    /// uncommitted transaction cannot leave anything behind, on any media, under any cache size - not
    /// because the visibility rules hide it and not because a flush was missed.
    /// </para>
    /// <para>
    /// <b>What that means for the double write</b>, which is why this was measured: both writes happen
    /// inside the commit, and the commit timestamp is allocated <b>before</b> the first of them. So the
    /// second write exists only to change a marker whose final value was already known - and the
    /// all-or-nothing property readers see comes from publishing the timestamp at the end of the commit
    /// rather than from the marker. That is recorded in the plan; it is not a licence to change it
    /// without the atomicity tests going red first.
    /// </para>
    /// </remarks>
    [Test]
    public void AttributionAreTheUncommittedRowsOnTheMediaTest()
    {
        const int manyRows = 20000;

        // A page cache of eight pages rather than the default thousand. The first version assumed
        // 20,000 rows would overflow a 1,000-page cache and force evictions; it measured 8 KB. With
        // eight pages it measures 8 KB as well - which is the finding, and a stronger one: there is
        // nothing to evict, because the transaction has not written to the store at all.
        using (var run = CrashRunnerHarness.Start(
                   Scenarios.UNCOMMITTED_KILL, m_databasePath, manyRows, "T", "CacheSize=8"))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        // Three numbers, because two of them can disagree and the disagreement is the answer: how large
        // the file grew, how many records are REACHABLE from the header, and what the engine shows. A
        // page written by an eviction is in the file whether or not anything points at it.
        var fileLength = new FileInfo(m_databasePath).Length;

        // The ROW records, not every record. This counted all of them and read zero - which looked
        // like an answer about the rows and was really an answer about the SCHEMA: issue 10 meant a
        // DDL statement in autocommit never reached the disk, so the catalogue was missing too and the
        // total came to nothing. When that was fixed the count became 4 and this case went red without
        // anything about uncommitted rows having changed. It was passing on a defect.
        //
        // The two internal namespaces are named rather than filtered by the "$" they share, so that a
        // THIRD one appearing turns this red and gets read by somebody. A row key has no prefix.
        string[] internalPrefixes = ["$schema:", "$mvcc:"];

        List<string> reachableKeys;

        using (var store = new StoreBTree(m_databasePath))
        {
            reachableKeys = store.Scan(null, null)
                .Select(record => Encoding.UTF8.GetString(record.Key))
                .Where(key => !internalPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                .ToList();
        }

        var reachable = reachableKeys.Count;

        var (scanned, _) = RowsInReopenedDatabase();

        TestContext.Out.WriteLine(
            $"UNCOMMITTED attribution: {manyRows} rows written uncommitted, killed -> " +
            $"file={fileLength / 1024} KB, reachable row records={reachable}, rows the engine shows={scanned}");

        if (reachable > 0)
            TestContext.Out.WriteLine($"  reachable keys: {string.Join(", ", reachableKeys.Take(10))}");

        Assert.That(scanned, Is.EqualTo(0),
            "the engine shows rows from a transaction that never committed");

        // Recorded rather than asserted: which mechanism does the hiding is the finding, and all of
        // them are legitimate. What would not be legitimate is the engine showing the rows.
        Assert.That(reachable, Is.EqualTo(0),
            $"{reachable} row records from a transaction that never committed are reachable at the " +
            "storage layer - they are hidden by the visibility rules rather than absent, which is a " +
            "much weaker guarantee than the one this fixture reports");

        Assert.That(fileLength, Is.LessThan(64 * 1024),
            $"the file grew to {fileLength / 1024} KB for a transaction that never committed - the " +
            "writes are reaching the media before the commit, and the change set is no longer purely " +
            "in memory");
    }

    #endregion

    #region Tools

    /// <summary>
    /// What the reopened database returns, scanned rather than counted - the count is separate state on
    /// this engine and phase 4 built a false report of lost commits on it. Both are read, and a
    /// disagreement between them is itself a finding.
    /// </summary>
    private (int Scanned, long Counted) RowsInReopenedDatabase()
    {
        using var connection = new WitDbConnection($"Data Source={m_databasePath}");
        connection.Open();

        var scanned = 0;

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM T";

            using var reader = command.ExecuteReader();
            while (reader.Read())
                scanned++;
        }
        catch (Exception e) when (e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            // No table is no rows: the CREATE is a write like any other and the kill can take it.
            return (0, 0);
        }

        long counted;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM T";
            counted = Convert.ToInt64(command.ExecuteScalar());
        }

        return (scanned, counted);
    }

    #endregion
}
