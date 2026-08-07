using OutWit.Database.AdoNet;
using OutWit.Database.CrashRunner;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// A DDL statement that reported success must still be there after the process dies.
/// </summary>
/// <remarks>
/// <para>
/// <b>KnownIssues issue 10, the engine half.</b> A page reaches the disk in a <c>Flush</c>, which
/// writes the header, or on its own when the cache evicts it, which does not. So the header - which
/// carries <c>TotalPageCount</c> and the free list - is only ever brought up to date by a flush, and
/// the only thing that flushed was a commit. Every DML statement runs in an implicit transaction and
/// therefore commits; <b>a DDL statement in autocommit committed nothing at all</b>, so the header
/// stayed at the vintage of the last INSERT while the file grew underneath it.
/// </para>
/// <para>
/// Two faces, and this fixture is about the quiet one. With nothing evicted the file is a consistent
/// but OLDER snapshot: it opens, and the schema is simply gone. With something evicted the header and
/// the pages disagree and the file will not open at all - which is how the defect was first met, as
/// two unreadable databases blamed on Studio's rebuild.
/// </para>
/// <para>
/// <b>Measured before the fix</b>, with the console probe and the same shape as this scenario: 240 DDL
/// statements in autocommit, killed, left the header counting <b>1 page of 591</b> and the database
/// read back EMPTY. The identical statements inside an explicit transaction left 200 of 200 and read
/// back whole - which is the fix pointing at itself, and what
/// <c>SchemaCatalog.MakeDurable</c> now does without opening a transaction of its own.
/// </para>
/// <para>
/// <b>It has to be a kill.</b> Closing the connection flushes, so an in-process test measures the
/// close path and passes on the defect. The whole suite closes cleanly, which is why ~10,000 tests
/// never saw this.
/// </para>
/// <para>
/// <b>What this does NOT claim.</b> A process that dies in the MIDDLE of a DDL statement can still
/// leave a header of one vintage beside pages of another, because eviction happens while the statement
/// runs. That window is identical for DML and closing it needs a journal that can be replayed at open,
/// which the MVCC store currently refuses to have. This fixture asks only that a statement which has
/// RETURNED is durable.
/// </para>
/// </remarks>
[TestFixture]
[Category("Crash")]
public sealed class DdlAfterAKillTests
{
    #region Constants

    /// <summary>
    /// How many CREATE/DROP pairs follow the table the test looks for. The catalogue is one value in
    /// an overflow chain, so each pair rewrites it, frees the old chain and grows the file - which is
    /// what moves the file out from under a stale header rather than merely leaving it behind.
    /// </summary>
    private const int CHURN_PAIRS = 30;

    /// <summary>
    /// The churn the unreadable-file case needs, and it is much larger than the one above for a
    /// measured reason: at 30 pairs with an eight-page cache the file still OPENED after a kill
    /// against the unfixed engine. The mixture needs the file to grow well past what the stale header
    /// counts, and the reproduction that produced it used 240 statements. A case that passes on the
    /// defect is worse than no case, so this number was raised until it went red without the fix.
    /// </summary>
    private const int UNREADABLE_CHURN_PAIRS = 200;

    #endregion

    #region Fields

    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_ddl_{Guid.NewGuid():N}");
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
    /// Control: the same statements, closed properly, leave the table behind. Without it, "the table
    /// is missing after a kill" is equally consistent with a scenario that never created one.
    /// </summary>
    [Test]
    public void ControlTheSameDdlSurvivesACleanCloseTest()
    {
        var result = CrashRunnerHarness.RunToCompletion(
            Scenarios.DDL_CONTROL_CLEAN, m_databasePath, CHURN_PAIRS);

        Assert.That(result.ExitCode, Is.Zero, "the control scenario did not finish cleanly");

        Assert.That(TableExistsInReopenedDatabase(), Is.True,
            "the table is not there after a CLEAN close, so this fixture cannot tell 'lost to the " +
            "kill' from 'never created'");
    }

    #endregion

    #region The probe

    /// <summary>
    /// A table created in autocommit, after the process is killed.
    /// </summary>
    [Test]
    public void ADdlStatementThatReturnedSurvivesAKillTest()
    {
        using (var run = CrashRunnerHarness.Start(Scenarios.DDL_KILL, m_databasePath, CHURN_PAIRS))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        var exists = TableExistsInReopenedDatabase();

        TestContext.Out.WriteLine(
            $"DDL probe: {2 + CHURN_PAIRS * 2} DDL statements in autocommit, killed -> table present={exists}");

        Assert.That(exists, Is.True,
            "the table created by a CREATE TABLE that returned successfully is gone after the process " +
            "was killed. Nothing flushed it: a commit is the only thing that writes the header, and a " +
            "DDL statement in autocommit does not commit.");
    }

    /// <summary>
    /// The loud face of the same defect: after the kill the database must OPEN.
    /// </summary>
    /// <remarks>
    /// Separate from the case above because the two failures are different sizes. A missing table is
    /// silent loss; a header that counts pages the file does not hold is a database nobody can read,
    /// and it is the one that put two files in <c>@Evidence/issue-10</c>. Asserted with a smaller page
    /// cache, because the mixture needs SOME pages evicted and others not - with the default cache the
    /// whole file fits and an abandoned process leaves a consistent older snapshot instead.
    /// </remarks>
    [Test]
    public void TheDatabaseOpensAfterAKillTest()
    {
        using (var run = CrashRunnerHarness.Start(
                   Scenarios.DDL_KILL, m_databasePath, UNREADABLE_CHURN_PAIRS, "T", "CacheSize=8"))
        {
            run.WaitFor(CrashProtocol.KILL_ME);
            run.Kill();
        }

        Assert.DoesNotThrow(() =>
            {
                using var connection = new WitDbConnection($"Data Source={m_databasePath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES";
                command.ExecuteScalar();
            },
            "the database cannot be opened after the process was killed - the header and the pages are " +
            "at different vintages, which is the signature of issue 10's two casualties");
    }

    #endregion

    #region Tools

    /// <summary>
    /// Whether the reopened database has the table. Asked of the catalogue rather than by selecting
    /// from it, because a table that exists and cannot be read is a different finding.
    /// </summary>
    private bool TableExistsInReopenedDatabase()
    {
        using var connection = new WitDbConnection($"Data Source={m_databasePath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'T'";

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    #endregion
}
