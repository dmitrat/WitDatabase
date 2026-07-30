using System.Data;
using System.Data.Common;
using OutWit.Database.AdoNet.Engines;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Exceptions;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// Several connections to one database in one process - the shape the project targets.
/// </summary>
/// <remarks>
/// An ASP.NET Core service resolves a scoped <c>DbContext</c> per request, so a host serving requests
/// concurrently holds several connections to one database at once. Before 5.0.0 each connection built its
/// own engine and the second one simply failed, so this shape did not work at all.
///
/// The tests are written through <see cref="DbConnection"/> where the behaviour is part of the ADO.NET
/// contract, because a shadowed member passes every test written against the concrete type and fails for
/// the consumer - a rule this project has already paid for once.
/// </remarks>
[TestFixture]
public class SharedDatabaseConnectionTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_shared_conn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Many connections, one database

    [Test]
    public void SecondConnectionToTheSameFileOpensTest()
    {
        var cs = FileConnectionString("two_open.witdb");

        using DbConnection first = new WitDbConnection(cs);
        first.Open();

        using DbConnection second = new WitDbConnection(cs);

        Assert.Multiple(() =>
        {
            Assert.That(() => second.Open(), Throws.Nothing,
                "a second connection in the same process is the supported shape");
            Assert.That(second.State, Is.EqualTo(ConnectionState.Open));
            Assert.That(first.State, Is.EqualTo(ConnectionState.Open));
        });
    }

    [Test]
    public void SecondConnectionSeesTheFirstConnectionsCommittedWorkTest()
    {
        var cs = FileConnectionString("visibility.witdb");

        using DbConnection writer = new WitDbConnection(cs);
        writer.Open();
        Execute(writer, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(writer, "INSERT INTO T (Id, V) VALUES (1, 'a')");

        using DbConnection reader = new WitDbConnection(cs);
        reader.Open();

        // Both, because on this engine COUNT(*) is a cached counter and a scan is the rows - they are
        // separate state, and a per-session schema catalog used to make them disagree across sessions.
        Assert.Multiple(() =>
        {
            Assert.That(CountRows(reader, "SELECT Id FROM T"), Is.EqualTo(1), "the row must be visible");
            Assert.That(Convert.ToInt64(Scalar(reader, "SELECT COUNT(*) FROM T")), Is.EqualTo(1L),
                "and its count must agree");
        });
    }

    /// <summary>
    /// A table created through one connection after another is already open - the case that failed with
    /// <c>Table not found</c> while each session had its own schema catalog.
    /// </summary>
    [Test]
    public void SecondConnectionSeesATableCreatedAfterItOpenedTest()
    {
        var cs = FileConnectionString("later_table.witdb");

        using DbConnection first = new WitDbConnection(cs);
        first.Open();

        using DbConnection second = new WitDbConnection(cs);
        second.Open();

        Execute(first, "CREATE TABLE Later (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(first, "INSERT INTO Later (Id, V) VALUES (1, 'a')");

        Assert.That(CountRows(second, "SELECT Id FROM Later"), Is.EqualTo(1),
            "a connection must see a table another connection created and committed");
    }

    /// <summary>
    /// Writes must be visible in both directions, not only from the connection that opened first.
    /// </summary>
    [Test]
    public void BothConnectionsSeeEachOthersWritesTest()
    {
        var cs = FileConnectionString("both_ways.witdb");

        using DbConnection first = new WitDbConnection(cs);
        first.Open();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using DbConnection second = new WitDbConnection(cs);
        second.Open();

        Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'from-first')");
        Execute(second, "INSERT INTO T (Id, V) VALUES (2, 'from-second')");

        // Rows AND counts, from BOTH connections. Rows alone would pass without a shared catalog,
        // because a scan reads the shared store - it is the cached counter that used to diverge. A
        // revert test proved this suite was weaker than it looked before these count assertions existed.
        Assert.Multiple(() =>
        {
            Assert.That(CountRows(first, "SELECT Id FROM T"), Is.EqualTo(2));
            Assert.That(CountRows(second, "SELECT Id FROM T"), Is.EqualTo(2));
            Assert.That(Convert.ToInt64(Scalar(first, "SELECT COUNT(*) FROM T")), Is.EqualTo(2L));
            Assert.That(Convert.ToInt64(Scalar(second, "SELECT COUNT(*) FROM T")), Is.EqualTo(2L));
        });
    }

    /// <summary>
    /// The case the rest of this fixture nearly missed: a row inserted <b>after</b> the second connection
    /// is already open, checked by its <c>COUNT(*)</c>.
    /// </summary>
    /// <remarks>
    /// Every other visibility test here creates and populates the table before the second connection
    /// opens, so that connection's catalog picks the state up when it is constructed and the test passes
    /// with a per-session catalog too. Reverting the shared catalog and re-running is what exposed that:
    /// only one test went red. This one goes red as well, because a counter that was incremented in
    /// another session's catalog is the exact thing that used to be stale.
    /// </remarks>
    [Test]
    public void SecondConnectionsCountSeesRowsInsertedAfterItOpenedTest()
    {
        var cs = FileConnectionString("count_after_open.witdb");

        using DbConnection writer = new WitDbConnection(cs);
        writer.Open();
        Execute(writer, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using DbConnection reader = new WitDbConnection(cs);
        reader.Open();

        // Nothing yet, through both paths - the baseline the assertions below move away from.
        Assert.Multiple(() =>
        {
            Assert.That(CountRows(reader, "SELECT Id FROM T"), Is.Zero);
            Assert.That(Convert.ToInt64(Scalar(reader, "SELECT COUNT(*) FROM T")), Is.Zero);
        });

        Execute(writer, "INSERT INTO T (Id, V) VALUES (1, 'a')");
        Execute(writer, "INSERT INTO T (Id, V) VALUES (2, 'b')");

        Assert.Multiple(() =>
        {
            Assert.That(CountRows(reader, "SELECT Id FROM T"), Is.EqualTo(2),
                "the reader must see rows the writer committed after it opened");
            Assert.That(Convert.ToInt64(Scalar(reader, "SELECT COUNT(*) FROM T")), Is.EqualTo(2L),
                "and its COUNT(*) must agree - this is the counter that used to be per-session");
        });
    }

    /// <summary>
    /// The per-request shape at a small scale: several connections opened and closed in overlapping
    /// pairs, as a host would, with the data checked at the end by a fresh reader.
    /// </summary>
    [Test]
    public void ManyOverlappingConnectionsBehaveLikeScopedContextsTest()
    {
        var cs = FileConnectionString("scoped.witdb");

        using (DbConnection seed = new WitDbConnection(cs))
        {
            seed.Open();
            Execute(seed, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        }

        // Overlapping, so the database is never left with zero connections in the middle - that is what
        // a busy host looks like, and it is also what keeps one engine alive across the whole loop.
        DbConnection? previous = null;

        for (var i = 0; i < 10; i++)
        {
            DbConnection current = new WitDbConnection(cs);
            current.Open();
            Execute(current, $"INSERT INTO T (Id, V) VALUES ({i}, 'row{i}')");

            previous?.Dispose();
            previous = current;
        }

        previous?.Dispose();

        using DbConnection verify = new WitDbConnection(cs);
        verify.Open();

        Assert.That(CountRows(verify, "SELECT Id FROM T"), Is.EqualTo(10),
            "every insert through every connection must have landed");
    }

    #endregion

    #region Lifetime

    /// <summary>
    /// The database must outlive any one connection but not all of them: the engine is disposed, and its
    /// exclusive lock released, only when the last connection goes.
    /// </summary>
    [Test]
    public void DatabaseIsDisposedOnlyWhenTheLastConnectionClosesTest()
    {
        var path = Path.Combine(m_testDir, "lifetime.witdb");
        var cs = $"Data Source={path}";
        var key = OperatingSystem.IsLinux()
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path).ToLowerInvariant();

        var before = SharedDatabase.LiveDatabaseCount;

        using (DbConnection first = new WitDbConnection(cs))
        {
            first.Open();
            Assert.That(SharedDatabase.LeaseCount(key), Is.EqualTo(1), "one connection, one share");

            using (DbConnection second = new WitDbConnection(cs))
            {
                second.Open();
                Assert.That(SharedDatabase.LeaseCount(key), Is.EqualTo(2), "two connections, two shares");
            }

            Assert.Multiple(() =>
            {
                Assert.That(SharedDatabase.LeaseCount(key), Is.EqualTo(1),
                    "closing one connection must not take the database with it");
                Assert.That(first.State, Is.EqualTo(ConnectionState.Open),
                    "and must not disturb the connection still using it");
            });
        }

        Assert.Multiple(() =>
        {
            Assert.That(SharedDatabase.LeaseCount(key), Is.Zero, "the last share went with the last connection");
            Assert.That(SharedDatabase.LiveDatabaseCount, Is.EqualTo(before),
                "and the registry is back where it started, so nothing leaked");
        });
    }

    /// <summary>
    /// Close-then-Dispose is ordinary on a <see cref="DbConnection"/>, and must release one share rather
    /// than two - releasing twice would dispose the database underneath another connection.
    /// </summary>
    [Test]
    public void CloseThenDisposeReleasesOneShareTest()
    {
        var path = Path.Combine(m_testDir, "double_release.witdb");
        var cs = $"Data Source={path}";
        var key = OperatingSystem.IsLinux()
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path).ToLowerInvariant();

        using DbConnection keepAlive = new WitDbConnection(cs);
        keepAlive.Open();

        var victim = new WitDbConnection(cs);
        victim.Open();
        Assert.That(SharedDatabase.LeaseCount(key), Is.EqualTo(2));

        victim.Close();
        victim.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(SharedDatabase.LeaseCount(key), Is.EqualTo(1),
                "Close and Dispose together released two shares for one connection");
            Assert.That(() => Execute(keepAlive, "CREATE TABLE Alive (Id BIGINT PRIMARY KEY)"),
                Throws.Nothing, "the surviving connection must still work");
        });
    }

    /// <summary>
    /// Reopening after every connection has gone must build a fresh engine and see the persisted data -
    /// the registry must not hand out a disposed database.
    /// </summary>
    [Test]
    public void ReopeningAfterTheLastConnectionClosedWorksTest()
    {
        var cs = FileConnectionString("reopen.witdb");

        using (DbConnection first = new WitDbConnection(cs))
        {
            first.Open();
            Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
            Execute(first, "INSERT INTO T (Id, V) VALUES (1, 'a')");
        }

        using DbConnection second = new WitDbConnection(cs);
        second.Open();

        Assert.That(CountRows(second, "SELECT Id FROM T"), Is.EqualTo(1),
            "the data must survive, and the registry must not return the disposed database");
    }

    #endregion

    #region Refusals that remain

    /// <summary>
    /// Sharing is by database, so two connections asking for incompatible engines cannot both be served.
    /// Refused with an explanation rather than by silently handing over somebody else's configuration.
    /// </summary>
    [Test]
    public void SameDatabaseWithDifferentOptionsIsRefusedTest()
    {
        var path = Path.Combine(m_testDir, "mismatch.witdb");

        using DbConnection first = new WitDbConnection($"Data Source={path}");
        first.Open();

        using DbConnection second = new WitDbConnection($"Data Source={path};MVCC=false");

        Assert.That(() => second.Open(),
            Throws.InstanceOf<InvalidOperationException>()
                .With.Message.Contains("different options"),
            "sharing an engine built to other options would be worse than refusing");
    }

    /// <summary>
    /// In-memory databases stay private to their connection, as they were before 5.0.0 and as SQLite's
    /// are without <c>Cache=Shared</c>. Pinned so that making them shared is a deliberate decision
    /// rather than a side effect.
    /// </summary>
    [Test]
    public void MemoryConnectionsRemainPrivateTest()
    {
        const string cs = "Data Source=:memory:";

        using DbConnection first = new WitDbConnection(cs);
        first.Open();
        Execute(first, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");

        using DbConnection second = new WitDbConnection(cs);
        second.Open();

        Assert.That(() => CountRows(second, "SELECT Id FROM T"),
            Throws.InstanceOf<InvalidOperationException>(),
            "two :memory: connections are two databases - if that changes, it must be on purpose");
    }

    /// <summary>
    /// A second <i>engine</i> - as opposed to a second connection - is still refused. The exclusivity
    /// guard is about processes, and this test is what keeps the two ideas from being conflated as the
    /// sharing work goes on.
    /// </summary>
    [Test]
    public void SecondEngineOverTheSameFileIsStillRefusedTest()
    {
        var path = Path.Combine(m_testDir, "second_engine.witdb");

        using DbConnection connection = new WitDbConnection($"Data Source={path}");
        connection.Open();

        Assert.That(() => new WitDatabaseBuilder()
                .WithFilePath(path)
                .WithBTree()
                .WithTransactions()
                .Build(),
            Throws.InstanceOf<DatabaseAlreadyOpenException>(),
            "connections share an engine; a second engine is a second process's business and refused");
    }

    #endregion

    #region Tools

    private string FileConnectionString(string fileName) =>
        $"Data Source={Path.Combine(m_testDir, fileName)}";

    private static void Execute(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>
    /// Counts rows by reading them, never by <c>COUNT(*)</c> - that is a cached counter on this engine,
    /// and phase 4 published a false catastrophe by trusting it.
    /// </summary>
    private static int CountRows(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader();

        var count = 0;
        while (reader.Read())
            count++;

        return count;
    }

    #endregion
}
