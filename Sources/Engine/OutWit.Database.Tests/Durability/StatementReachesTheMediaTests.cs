using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Storage;
using OutWit.Database.Engine;
using OutWit.Database.Tests.Performance;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// When a statement's writes reach the media - which is what decides whether it is atomic against a
/// process that dies in the middle of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism behind `KnownIssues` 10's mid-statement window, and it is one fact.</b> Killed
/// in flight, an `UPDATE` leaves three different kinds of damage depending on the configuration:
/// with MVCC the database will not open, on the transactional store with no journal the statement is
/// half applied, and with `wal` or `rollback` it comes back clean. The same `UPDATE` inside an
/// explicit transaction leaves nothing behind in every configuration.
/// </para>
/// <para>
/// All of it follows from what these cases count: <b>a statement in autocommit writes through to the
/// media as it runs, and the same statement inside an explicit transaction writes nothing until the
/// commit.</b> Under MVCC ten thousand pages land while the statement runs and the header is not
/// among them, so the two are at different vintages; without MVCC two thousand land with nothing to
/// take them back; and an explicit transaction is atomic against a kill because nothing has reached
/// the media to be atomic about.
/// </para>
/// <para>
/// <b>No kill is needed and no clock is read.</b> A crash test can only see the aftermath; this
/// counts the pages as they go past, which is exact and does not move with the machine. What it
/// pins is therefore a property of the engine, not a symptom: the day the implicit transaction
/// starts buffering, the first case here goes red and should be inverted rather than deleted.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StatementReachesTheMediaTests
{
    #region Constants

    /// <summary>
    /// Rows in the table the statement rewrites. Large enough that the update is far more than the
    /// cache can hold, so "nothing reached the media" cannot be an accident of everything fitting.
    /// </summary>
    private const int ROWS = 5_000;

    /// <summary>
    /// Smaller than the update it is asked to hold. This is the same lesson issue 10 paid for: with
    /// the default cache nothing is ever evicted and every arm answers "nothing reached the media"
    /// for a reason that has nothing to do with the subject.
    /// </summary>
    private const int CACHE_PAGES = 8;

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb_reach_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_directory))
                Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only.
        }
    }

    #endregion

    #region Cases

    /// <summary>
    /// A statement in autocommit puts its writes on the media while it is still running - so a
    /// process that dies in the middle of one leaves part of it behind.
    /// </summary>
    [TestCase(false, TestName = "AStatementInAutocommitReachesTheMediaWhileItRunsTest(btree)")]
    [TestCase(true, TestName = "AStatementInAutocommitReachesTheMediaWhileItRunsTest(mvcc)")]
    public void AStatementInAutocommitReachesTheMediaWhileItRunsTest(bool mvcc)
    {
        using var db = Open(mvcc, out var counter);
        using var engine = new WitSqlEngine(db);

        Seed(engine);
        counter.Reset();

        engine.Execute("UPDATE T SET V = 1");

        TestContext.Out.WriteLine($"autocommit, mvcc={mvcc}: {counter}");

        Assert.That(counter.PagesWritten, Is.GreaterThan(100),
            "the statement wrote almost nothing to the media, so either it did not run or the "
            + "implicit transaction has started buffering - if it is the second, this is the good "
            + "news the mid-statement window was waiting for, and the case below should be read "
            + "again before this one is changed");
    }

    /// <summary>
    /// The same statement inside an explicit transaction puts nothing on the media until the commit,
    /// which is what makes it all-or-nothing against a kill.
    /// </summary>
    [TestCase(false, TestName = "AStatementInATransactionReachesTheMediaOnlyAtTheCommitTest(btree)")]
    [TestCase(true, TestName = "AStatementInATransactionReachesTheMediaOnlyAtTheCommitTest(mvcc)")]
    public void AStatementInATransactionReachesTheMediaOnlyAtTheCommitTest(bool mvcc)
    {
        using var db = Open(mvcc, out var counter);
        using var engine = new WitSqlEngine(db);

        Seed(engine);
        counter.Reset();

        using (engine.BeginTransaction())
        {
            engine.Execute("UPDATE T SET V = 1");

            var duringTheStatement = counter.PagesWritten;

            TestContext.Out.WriteLine(
                $"transaction, mvcc={mvcc}: {counter} before the commit");

            Assert.That(duringTheStatement, Is.Zero,
                "the statement inside a transaction reached the media before its commit, so it is "
                + "no more atomic against a kill than autocommit is");

            engine.Commit();
        }

        TestContext.Out.WriteLine($"transaction, mvcc={mvcc}: {counter} after the commit");

        // The control, and it is not decoration: "nothing reached the media" is exactly what a
        // statement that never ran would also report.
        Assert.That(counter.PagesWritten, Is.GreaterThan(100),
            "nothing reached the media at the commit either, so the case above is measuring a "
            + "statement that did no work rather than one that buffered it");
    }

    #endregion

    #region Tools

    private WitDatabase Open(bool mvcc, out CountingStorage counter)
    {
        counter = new CountingStorage(new StorageFile(Path.Combine(m_directory, "r.witdb")));

        var builder = new WitDatabaseBuilder()
            .WithStorage(counter)
            .WithBTree()
            .WithTransactions()
            .WithCacheSize(CACHE_PAGES);

        if (mvcc)
            builder = builder.WithMvcc();

        return builder.Build();
    }

    /// <summary>
    /// The rows the subject statement rewrites, written inside a transaction so that they are
    /// durable and finished before anything is counted.
    /// </summary>
    private static void Seed(WitSqlEngine engine)
    {
        engine.Execute(
            "CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, V INT, Payload VARCHAR(200))");

        var payload = new string('x', 200);

        using (engine.BeginTransaction())
        {
            for (var i = 0; i < ROWS; i++)
                engine.Execute($"INSERT INTO T (V, Payload) VALUES (0, '{payload}')");

            engine.Commit();
        }
    }

    #endregion
}
