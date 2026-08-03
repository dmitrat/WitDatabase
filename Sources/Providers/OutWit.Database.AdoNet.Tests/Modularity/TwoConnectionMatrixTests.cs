using System.Text;

namespace OutWit.Database.AdoNet.Tests.Modularity;

/// <summary>
/// Phase 11 instrument D - the combinations crossed with the shape a real host actually runs: more
/// than one connection over one database.
/// </summary>
/// <remarks>
/// <para>
/// The matrix (instrument B) opens one connection, runs a workload and reopens it. That is not the
/// shape this engine is built for: the concurrency model is <i>one process, one engine per database,
/// many connections</i>, because the target is ASP.NET Core, where the host is one process and the
/// <c>DbContext</c>s are many. 5.0.0 made that shape work; nothing has ever crossed it with the
/// configurations, and 5.0.0's own defects were configuration-shaped - a table created by one
/// connection was <c>Table not found</c> to another, and a row was visible to one connection's scan
/// while its own <c>COUNT(*)</c> said zero.
/// </para>
/// <para>
/// <b>The workload asks the two questions that failed then</b>, and asks them in the order that
/// matters: the second connection reads what the first wrote <i>before</i> it opened, and the first
/// connection reads what the second wrote <i>after</i> it opened. The second is the state that went
/// stale - a catalog picked up at construction looks correct for every test that populates its table
/// first. It also crosses schema with connections: a table created by the second connection is written
/// to by the first.
/// </para>
/// <para>
/// <b>The control is the same workload through one connection.</b> Every configuration must run it -
/// and if one cannot, the failure is the configuration or the workload, not the two-connection shape,
/// which is the distinction this fixture exists to make. The expected answers are hard-coded literals
/// rather than another run of the engine, for the reason the matrix records: a comparison harness that
/// only sees disagreement passes an engine-wide regression.
/// </para>
/// </remarks>
[TestFixture]
[Category("Modularity")]
public class TwoConnectionMatrixTests
{
    #region Types

    /// <param name="Persistent">
    /// Whether the database is expected to outlive its connections. The in-memory store is not, which
    /// is what the store is rather than a defect - but it changes the last question this probe asks.
    /// </param>
    public sealed record Combination(string Label, string Settings, bool Persistent)
    {
        public override string ToString() => Label;
    }

    #endregion

    #region Constants

    /// <summary>What the first connection has written when the second one opens.</summary>
    private const string AFTER_FIRST = "1:a1|2:a2|3:a3|4:a4";

    /// <summary>And what is there once the second connection has written its own rows.</summary>
    private const string AFTER_SECOND = "1:a1|2:a2|3:a3|4:a4|5:b5|6:b6|7:b7|8:b8";

    /// <summary>The first connection then updates a row the second one wrote.</summary>
    private const string AFTER_UPDATE = "1:a1|2:a2|3:a3|4:a4|5:updated|6:b6|7:b7|8:b8";

    /// <summary>And the second one writes a last row after the first has closed.</summary>
    private const string FINAL = "1:a1|2:a2|3:a3|4:a4|5:updated|6:b6|7:b7|8:b8|9:b9";

    #endregion

    #region Fields

    private string m_root = null!;
    private int m_sequence;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_twoconn_{Guid.NewGuid():N}");
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

    #region The matrix

    private static readonly string[] STORES = ["btree", "lsm", "inmemory"];

    private static readonly (string Label, string Settings)[] TRANSACTIONS =
    [
        ("tx=mvcc", "Transactions=true;MVCC=true"),
        ("tx=locks", "Transactions=true;MVCC=false"),
        ("tx=off", "Transactions=false")
    ];

    private static readonly (string Label, string Settings)[] ENCRYPTION =
    [
        ("plain", ""),
        ("aes", "Encryption=aes-gcm;Password=twoconn-secret;FastEncryption=true")
    ];

    /// <summary>
    /// Swept against the default base rather than crossed with everything, exactly as the matrix does:
    /// they are separate subsystems and crossing them asks no new question of two connections.
    /// </summary>
    private static readonly (string Label, string Settings)[] ADD_ONS =
    [
        ("journal=wal+locks", "Journal=wal;MVCC=false"),
        ("journal=rollback+locks", "Journal=rollback;MVCC=false"),
        ("cache=lru", "Cache=lru"),
        ("pagesize=16384", "PageSize=16384"),
        ("sync=off", "Synchronous Commit=false"),
        ("locking=off", "FileLocking=false")
    ];

    private static IEnumerable<Combination> Matrix()
    {
        foreach (var store in STORES)
        foreach (var (transactionLabel, transactionSettings) in TRANSACTIONS)
        foreach (var (encryptionLabel, encryptionSettings) in ENCRYPTION)
        {
            yield return new Combination(
                $"{store} {transactionLabel} {encryptionLabel}",
                Join($"Store={store}", transactionSettings, encryptionSettings),
                Persistent: store != "inmemory");
        }

        foreach (var (label, settings) in ADD_ONS)
            yield return new Combination($"btree {label}", settings, Persistent: true);
    }

    #endregion

    #region The control

    /// <summary>
    /// Control: the same workload down one connection. A configuration that fails here cannot be asked
    /// anything about two connections - and this is what separates "this combination cannot do the
    /// work" from "this combination cannot do the work twice at once".
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Matrix))]
    public void ControlOneConnectionRunsTheWholeWorkloadTest(Combination combination)
    {
        var dataSource = NewDataSource();

        using var connection = new WitDbConnection(Compose(dataSource, combination.Settings));
        connection.Open();

        Execute(connection, "CREATE TABLE Two (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");
        Insert(connection, 1, 4, "a");

        Assert.That(Scan(connection), Is.EqualTo(AFTER_FIRST), $"{combination.Label}: the first four rows");

        Insert(connection, 5, 8, "b");
        Assert.That(Scan(connection), Is.EqualTo(AFTER_SECOND), $"{combination.Label}: all eight rows");

        Execute(connection, "CREATE TABLE TwoMore (Id BIGINT PRIMARY KEY)");
        Execute(connection, "INSERT INTO TwoMore (Id) VALUES (1)");

        Execute(connection, "UPDATE Two SET Name = 'updated' WHERE Id = 5");
        Assert.That(Scan(connection), Is.EqualTo(AFTER_UPDATE), $"{combination.Label}: after the update");

        Execute(connection, "INSERT INTO Two (Id, Name) VALUES (9, 'b9')");
        Assert.That(Scan(connection), Is.EqualTo(FINAL), $"{combination.Label}: the final state");
    }

    /// <summary>
    /// Control in the other direction: two connections to two <b>different</b> databases must share
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Without this, "the second connection sees the first connection's rows" is an assertion no run
    /// could fail - a probe that answered the expected string for any pair of connections would pass
    /// the whole fixture. This is the shape phase 3 named: a harness that can only agree.
    /// </remarks>
    [Test]
    public void ControlTwoDatabasesShareNothingTest()
    {
        using var first = new WitDbConnection(Compose(NewDataSource(), ""));
        first.Open();

        Execute(first, "CREATE TABLE Two (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");
        Insert(first, 1, 4, "a");

        using var second = new WitDbConnection(Compose(NewDataSource(), ""));
        second.Open();

        Assert.That(() => Scan(second), Throws.Exception,
            "a connection to a different database answered the first database's query - this fixture " +
            "cannot tell a shared database from an unshared one, so none of its verdicts mean anything");
    }

    #endregion

    #region The probe

    /// <summary>
    /// Two connections over one database, opened the way a host opens them: the second while the first
    /// is still open, both from the same connection string.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Matrix))]
    public void TwoConnectionsShareOneDatabaseTest(Combination combination)
    {
        var dataSource = NewDataSource();

        var first = new WitDbConnection(Compose(dataSource, combination.Settings));

        try
        {
            first.Open();

            Execute(first, "CREATE TABLE Two (Id BIGINT PRIMARY KEY, Name VARCHAR(50))");
            Insert(first, 1, 4, "a");

            var second = new WitDbConnection(Compose(dataSource, combination.Settings));

            try
            {
                try
                {
                    second.Open();
                }
                catch (Exception e)
                {
                    Assert.Fail(
                        $"{combination.Label}: the second connection was refused while the first was open - " +
                        $"{e.GetType().Name}: {Short(e)}. Many connections over one database in one process " +
                        "is the shape this engine is designed for; refusing it is a limitation that has to " +
                        "be either fixed or written into WitSQL.md 14.10.");
                    return;
                }

                // The question 5.0.0 answered: does a second connection see a database it did not build?
                Assert.That(Scan(second), Is.EqualTo(AFTER_FIRST),
                    $"{combination.Label}: the second connection cannot see what the first wrote before it opened");

                Insert(second, 5, 8, "b");

                // And the question the eleven tests of that phase did NOT ask, because they all populated
                // their table before the second connection existed: does the FIRST connection see writes
                // that arrived after it had already read the database?
                Assert.That(Scan(first), Is.EqualTo(AFTER_SECOND),
                    $"{combination.Label}: the first connection cannot see what the second wrote after it opened");

                // Schema crossed with connections, which is the other half of the same defect: a table
                // created by one connection used to be Table not found to the other.
                Execute(second, "CREATE TABLE TwoMore (Id BIGINT PRIMARY KEY)");
                Execute(first, "INSERT INTO TwoMore (Id) VALUES (1)");

                Execute(first, "UPDATE Two SET Name = 'updated' WHERE Id = 5");

                Assert.That(Scan(second), Is.EqualTo(AFTER_UPDATE),
                    $"{combination.Label}: the second connection cannot see the first connection's update");

                first.Close();

                // The first connection is gone; the second must still hold a working database. Closing one
                // connection used to release the shared database's file lock while another still held it.
                Execute(second, "INSERT INTO Two (Id, Name) VALUES (9, 'b9')");

                Assert.That(Scan(second), Is.EqualTo(FINAL),
                    $"{combination.Label}: the surviving connection lost the database when the other closed");
            }
            finally
            {
                second.Dispose();
            }
        }
        finally
        {
            first.Dispose();
        }

        if (!combination.Persistent)
            return;

        using var reopened = new WitDbConnection(Compose(dataSource, combination.Settings));
        reopened.Open();

        Assert.That(Scan(reopened), Is.EqualTo(FINAL),
            $"{combination.Label}: what two connections wrote did not survive them both closing");
    }

    #endregion

    #region The workload

    private static void Insert(WitDbConnection connection, int from, int to, string prefix)
    {
        for (var i = from; i <= to; i++)
            Execute(connection, $"INSERT INTO Two (Id, Name) VALUES ({i}, '{prefix}{i}')");
    }

    /// <summary>
    /// Every row, read back by scanning. Never <c>COUNT(*)</c>: on this engine that is a cached counter,
    /// kept per table and persisted separately, and it has disagreed with the rows before.
    /// </summary>
    private static string Scan(WitDbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Two ORDER BY Id";

        using var reader = command.ExecuteReader();
        var builder = new StringBuilder();

        while (reader.Read())
        {
            if (builder.Length > 0)
                builder.Append('|');

            builder.Append($"{reader.GetInt64(0)}:{reader.GetString(1)}");
        }

        return builder.ToString();
    }

    private static void Execute(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    #endregion

    #region Helpers

    private string NewDataSource()
    {
        var directory = Path.Combine(m_root, $"case{Interlocked.Increment(ref m_sequence):D3}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "twoconn.witdb");
    }

    private static string Compose(string dataSource, string settings)
    {
        return string.IsNullOrEmpty(settings)
            ? $"Data Source={dataSource}"
            : $"Data Source={dataSource};{settings}";
    }

    private static string Join(params string[] parts)
    {
        return string.Join(';', parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    private static string Short(Exception exception)
    {
        var line = string.Join(" / ",
            exception.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return line.Length > 200 ? line[..200] : line;
    }

    #endregion
}
