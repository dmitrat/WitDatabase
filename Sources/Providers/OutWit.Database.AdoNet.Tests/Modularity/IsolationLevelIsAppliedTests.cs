using System.Data;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Is the isolation level applied, or only reported? Phase 6 recorded "reported and applied by
/// nothing" and it has been carried forward ever since without a measurement.
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
    /// PINS A DEFECT, NOT CORRECT BEHAVIOUR. Every isolation level answers the same, on both read
    /// shapes - a scan and a single-key seek.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 6 recorded this and it is confirmed by measurement here rather than carried forward: a
    /// transaction opened at <c>Serializable</c>, <c>RepeatableRead</c> or <c>Snapshot</c> sees a row
    /// another connection commits after it began, which each of those three levels exists to prevent.
    /// </para>
    /// <para>
    /// <b>The control that makes it a finding rather than a broken probe:</b> <c>ReadCommitted</c> is
    /// asserted to see the row, and it does. So the probe can distinguish the two behaviours; there is
    /// only one behaviour to distinguish.
    /// </para>
    /// <para>
    /// <b>The seek is measured next to the scan on purpose.</b> <c>MvccTransaction.Get</c> does switch
    /// on the level - the three snapshot levels take a different branch from <c>ReadCommitted</c> - so
    /// "the level reaches nothing" would be the wrong description. It reaches the transaction and the
    /// transaction is not what the statement path reads through. Measuring both shapes is what
    /// separates those two explanations, and both come back identical.
    /// </para>
    /// <para>
    /// <b>To invert when it is fixed:</b> the three snapshot levels become <c>False</c> and
    /// <c>ReadCommitted</c> stays <c>True</c>, on both shapes. A fix that only moves the seek is half a
    /// fix, which is why the scan is here.
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

            foreach (var level in levels.Where(l => l != IsolationLevel.ReadCommitted))
            {
                Assert.That(byScan[level], Is.True,
                    $"PINS A DEFECT: {level} saw a commit that happened after the transaction began, " +
                    "on the scan path. See the remarks for what this should become.");

                Assert.That(bySeek[level], Is.True,
                    $"PINS A DEFECT: {level} saw a commit that happened after the transaction began, " +
                    "on the single-key path.");
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
