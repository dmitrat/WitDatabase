using System.Data;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Is the isolation level applied, or only reported? Phase 6 recorded "reported and applied by
/// nothing"; it was measured here in 2026-08-09, confirmed, and fixed on 2026-08-10.
/// </summary>
/// <remarks>
/// <para>
/// The record deserves re-measuring rather than re-copying: the census (instrument A) shows
/// <c>Isolation Level</c> reaching <c>m_defaultIsolationLevel</c> on the transactional store, and
/// <c>MvccTransaction.Get</c> switches on it - <c>ReadUncommitted</c>, <c>ReadCommitted</c> and the
/// three snapshot levels take three different paths. Reaching a field and switching on it is not the
/// same as a consumer seeing different answers, which is what this asks.
/// </para>
/// <para>
/// <b>The question, in one sentence:</b> a transaction opens, another connection commits a row, and the
/// first transaction reads again - does it see the row? Under <c>ReadCommitted</c> it must; under
/// <c>Serializable</c>, <c>RepeatableRead</c> and <c>Snapshot</c> it must not, because those levels
/// promise a stable view. If both levels answer the same, the setting is decoration.
/// </para>
/// <para>
/// <b>The control is the pair itself.</b> Neither level's answer is interesting alone - "sees the row"
/// is correct for one and wrong for the other, so a harness that ran one level could report either
/// outcome as success. What is asserted is that the two levels DIFFER, plus the direction of each.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class IsolationLevelIsAppliedTests
{
    #region Fields

    private string m_root = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_isolation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
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

    #region Probes

    /// <summary>
    /// The isolation level is applied: the three snapshot levels hold their view where
    /// <c>ReadCommitted</c> does not, on both read shapes - a scan and a single-key seek.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This case PINNED a defect from phase 6 until 2026-08-10: a transaction opened at
    /// <c>Serializable</c>, <c>RepeatableRead</c> or <c>Snapshot</c> saw a row another connection
    /// committed after it began, which each of those three levels exists to prevent. It said, in its
    /// own text, that the fix would turn the three snapshot levels to <c>False</c> while
    /// <c>ReadCommitted</c> stayed <c>True</c>, on both shapes. That is what it asserts now.
    /// </para>
    /// <para>
    /// <b>The control that makes it a measurement rather than a broken probe:</b> <c>ReadCommitted</c>
    /// is asserted to SEE the row, and it does. Neither level's answer is interesting alone - "sees
    /// the row" is correct for one and wrong for the other - so what is asserted is that the two
    /// DIFFER, plus the direction of each.
    /// </para>
    /// <para>
    /// <b>The seek is measured next to the scan on purpose</b>, and the reason survived the fix: a
    /// repair that reached only the point read would leave the scan wrong, and the two paths take
    /// different code in <c>MvccTransaction</c>.
    /// </para>
    /// <para>
    /// <b>The cause was neither the store nor the transaction, both of which were correct.</b>
    /// <c>SET TRANSACTION ISOLATION LEVEL</c> recorded the level on the per-<c>Execute</c> execution
    /// context, so it could only survive to <c>BEGIN TRANSACTION</c> when both statements arrived in
    /// one batch - and the ADO layer sent them as two, in the wrong order besides. Both were fixed;
    /// <c>Docs/KnownIssues.md</c> 21.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryIsolationLevelAnswersTheSameTest()
    {
        var levels = new[]
        {
            IsolationLevel.ReadCommitted, IsolationLevel.RepeatableRead,
            IsolationLevel.Serializable, IsolationLevel.Snapshot
        };

        var byScan = new Dictionary<IsolationLevel, bool>();
        var bySeek = new Dictionary<IsolationLevel, bool>();

        foreach (var level in levels)
        {
            byScan[level] = ReadsACommitOfAnotherConnection(level, seek: false);
            bySeek[level] = ReadsACommitOfAnotherConnection(level, seek: true);

            TestContext.Out.WriteLine(
                $"{level,-16} sees the other connection's commit - scan: {byScan[level],-6} seek: {bySeek[level]}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(byScan[IsolationLevel.ReadCommitted], Is.True,
                "The control: ReadCommitted must see a row another connection has committed. If this " +
                "fails, the probe is measuring something other than the isolation level and no verdict " +
                "below may be believed.");

            Assert.That(bySeek[IsolationLevel.ReadCommitted], Is.True,
                "The same control on the single-key path.");

            foreach (var level in levels.Where(l => l != IsolationLevel.ReadCommitted))
            {
                Assert.That(byScan[level], Is.False,
                    $"{level} must not see a commit that happened after the transaction began, " +
                    "on the scan path - that is what the level is for.");

                Assert.That(bySeek[level], Is.False,
                    $"{level} must not see it on the single-key path either.");
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Opens a transaction at the given level, reads once so the view is established, lets a second
    /// connection commit a new row, and reads again.
    /// </summary>
    private bool ReadsACommitOfAnotherConnection(IsolationLevel level, bool seek)
    {
        var path = Path.Combine(m_root, $"isolation_{level}_{(seek ? "seek" : "scan")}.witdb");
        var connectionString = $"Data Source={path}";

        using var writer = new WitDbConnection(connectionString);
        writer.Open();

        Execute(writer, "CREATE TABLE Isolated (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");
        Execute(writer, "INSERT INTO Isolated (Id, Name) VALUES (1, 'first')");

        using var reader = new WitDbConnection(connectionString);
        reader.Open();

        using var transaction = reader.BeginTransaction(level);

        // Read once inside the transaction: a snapshot taken lazily is taken here.
        var before = Count(reader, transaction, seek);

        Execute(writer, "INSERT INTO Isolated (Id, Name) VALUES (2, 'second')");

        var after = Count(reader, transaction, seek);
        transaction.Rollback();

        return after > before;
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Scans the rows and counts what comes back - never <c>COUNT(*)</c>, which this engine answers
    /// from a cached per-table counter that is separate state with separate visibility.
    /// </summary>
    private static int Count(WitDbConnection connection, IDbTransaction transaction, bool seek)
    {
        using var command = connection.CreateCommand();
        command.CommandText = seek
            ? "SELECT Id FROM Isolated WHERE Id = 2"
            : "SELECT Id FROM Isolated ORDER BY Id";
        command.Transaction = (WitDbTransaction)transaction;

        using var reader = command.ExecuteReader();
        var rows = 0;

        while (reader.Read())
            rows++;

        return rows;
    }

    #endregion
}
