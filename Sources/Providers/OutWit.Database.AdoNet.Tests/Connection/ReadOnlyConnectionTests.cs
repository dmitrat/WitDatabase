using System.Data.Common;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// <c>Read Only=true</c> and <c>Mode=ReadOnly</c>, which were parsed and dropped until 5.0.0.
/// </summary>
/// <remarks>
/// Read-only is enforced per <b>session</b>, not by opening the storage differently, because
/// connections share one database per file: a read-only connection must not stop the others writing,
/// and a reader alongside writers is exactly the shape a consumer reaches for.
///
/// The enforcement is <b>fail-closed</b> - a read-only session allows a named list of statement kinds
/// and refuses everything else - so a statement added to the grammar later is refused until somebody
/// decides it is safe. A read-only guarantee built the other way round would weaken silently every time
/// the language grew.
/// </remarks>
[TestFixture]
public class ReadOnlyConnectionTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"witdb_readonly_{Guid.NewGuid():N}");
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

    #region Writes are refused

    [Test]
    [TestCase("INSERT INTO T (Id, V) VALUES (2, 'b')")]
    [TestCase("UPDATE T SET V = 'z' WHERE Id = 1")]
    [TestCase("DELETE FROM T WHERE Id = 1")]
    [TestCase("CREATE TABLE Other (Id BIGINT PRIMARY KEY)")]
    [TestCase("DROP TABLE T")]
    [TestCase("ALTER TABLE T ADD COLUMN W TEXT")]
    [TestCase("CREATE INDEX ix_t ON T (V)")]
    [TestCase("TRUNCATE TABLE T")]
    [TestCase("CREATE VIEW V1 AS SELECT Id FROM T")]
    public void ReadOnlyConnectionRefusesWritingStatementsTest(string sql)
    {
        var path = Seed("refuses.witdb");

        using DbConnection reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        Assert.That(() => Execute(reader, sql),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("read-only"),
            $"<{sql}> must be refused on a read-only connection");
    }

    /// <summary>
    /// The bulk API writes without parsing anything, so guarding statement execution alone would have
    /// left it as a way straight through a read-only connection.
    /// </summary>
    [Test]
    public void ReadOnlyConnectionRefusesTheBulkApiTest()
    {
        var path = Seed("bulk.witdb");

        using var reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        var engine = reader.Engine;

        Assert.Multiple(() =>
        {
            Assert.That(() => engine!.BulkInsert("T",
                    ["Id", "V"],
                    [[2L, "b"]]),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("read-only"));

            Assert.That(() => engine!.BulkUpdate("T", new Dictionary<string, object?> { ["V"] = "z" }),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("read-only"));

            Assert.That(() => engine!.BulkDelete("T"),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("read-only"));
        });
    }

    [Test]
    public void RefusedWriteLeavesNoTraceTest()
    {
        var path = Seed("no_trace.witdb");

        using DbConnection reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        Assert.That(() => Execute(reader, "INSERT INTO T (Id, V) VALUES (2, 'b')"),
            Throws.InstanceOf<InvalidOperationException>());

        // Counted by reading, not by COUNT(*): on this engine the count is a cached counter and a scan
        // is the rows, and a refused write must not have moved either.
        using DbConnection verify = new WitDbConnection($"Data Source={path}");
        verify.Open();

        Assert.Multiple(() =>
        {
            Assert.That(CountRows(verify, "SELECT Id FROM T"), Is.EqualTo(1));
            Assert.That(Convert.ToInt64(Scalar(verify, "SELECT COUNT(*) FROM T")), Is.EqualTo(1L));
        });
    }

    #endregion

    #region Reads are allowed

    [Test]
    [TestCase("SELECT Id, V FROM T")]
    [TestCase("SELECT COUNT(*) FROM T")]
    [TestCase("SELECT V FROM T WHERE Id = 1")]
    public void ReadOnlyConnectionAllowsReadsTest(string sql)
    {
        var path = Seed("reads.witdb");

        using DbConnection reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        Assert.That(() => Scalar(reader, sql), Throws.Nothing, $"<{sql}> must be allowed");
    }

    /// <summary>
    /// Transaction control is allowed on a read-only session: it changes nothing by itself, and EF Core
    /// and ADO.NET callers wrap reads in transactions routinely. A transaction that then tries to write
    /// is refused on the writing statement, which is where the error belongs.
    /// </summary>
    [Test]
    public void ReadOnlyConnectionAllowsATransactionAroundReadsTest()
    {
        var path = Seed("txn.witdb");

        using DbConnection reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        using var transaction = reader.BeginTransaction();

        Assert.Multiple(() =>
        {
            Assert.That(CountRows(reader, "SELECT Id FROM T"), Is.EqualTo(1),
                "reads inside the transaction must work");
            Assert.That(() => Execute(reader, "INSERT INTO T (Id, V) VALUES (2, 'b')"),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("read-only"),
                "and the write must still be refused, inside a transaction as outside one");
        });

        transaction.Rollback();
    }

    #endregion

    #region Alongside writers

    /// <summary>
    /// The shape read-only exists for: a read-only connection and a writing one on the same database at
    /// the same time. This is why read-only is a session property - as a storage property, these two
    /// would be an options mismatch and one of them would be refused.
    /// </summary>
    [Test]
    public void ReadOnlyAndWritingConnectionsCoexistTest()
    {
        var path = Seed("coexist.witdb");

        using DbConnection writer = new WitDbConnection($"Data Source={path}");
        writer.Open();

        using DbConnection reader = new WitDbConnection($"Data Source={path};Read Only=true");
        reader.Open();

        Execute(writer, "INSERT INTO T (Id, V) VALUES (2, 'from-writer')");

        Assert.Multiple(() =>
        {
            Assert.That(CountRows(reader, "SELECT Id FROM T"), Is.EqualTo(2),
                "the reader must see what the writer committed");
            Assert.That(() => Execute(reader, "INSERT INTO T (Id, V) VALUES (3, 'c')"),
                Throws.InstanceOf<InvalidOperationException>(),
                "and must still refuse to write itself");
            Assert.That(() => Execute(writer, "INSERT INTO T (Id, V) VALUES (3, 'c')"),
                Throws.Nothing,
                "while the writer is unaffected by the reader's restriction");
        });
    }

    /// <summary>
    /// Both spellings must work, and must work the same way. Fixing only one would leave a silent hole
    /// behind whichever a given consumer happened to reach for.
    /// </summary>
    [Test]
    [TestCase("Read Only=true")]
    [TestCase("Mode=ReadOnly")]
    public void BothSpellingsOfReadOnlyAreHonouredTest(string setting)
    {
        var path = Seed($"spelling_{setting.GetHashCode():x8}.witdb");

        using DbConnection reader = new WitDbConnection($"Data Source={path};{setting}");
        reader.Open();

        Assert.Multiple(() =>
        {
            Assert.That(CountRows(reader, "SELECT Id FROM T"), Is.EqualTo(1), "reads must work");
            Assert.That(() => Execute(reader, "INSERT INTO T (Id, V) VALUES (2, 'b')"),
                Throws.InstanceOf<InvalidOperationException>(), "writes must not");
        });
    }

    /// <summary>
    /// An in-memory read-only connection takes the other branch of <c>Open</c> - it builds its own
    /// database rather than leasing a shared one - so the flag has to be threaded through both.
    /// </summary>
    [Test]
    public void ReadOnlyIsHonouredOnAnInMemoryConnectionTest()
    {
        using DbConnection reader = new WitDbConnection("Data Source=:memory:;Read Only=true");
        reader.Open();

        Assert.That(() => Execute(reader, "CREATE TABLE T (Id BIGINT PRIMARY KEY)"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("read-only"),
            "the in-memory branch of Open must honour the flag too");
    }

    /// <summary>
    /// The sibling setting, checked while read-only was being fixed: <c>Mode=ReadWrite</c> means "open an
    /// existing database and fail if it is not there", as against <c>ReadWriteCreate</c>.
    /// </summary>
    /// <remarks>
    /// Found while fixing read-only, and left as a marker rather than fixed with it: this is a
    /// <i>database-level</i> setting (<c>FileMode.Open</c> against <c>OpenOrCreate</c>) whereas read-only
    /// turned out to be session-level, so it is a different change with a different blast radius - it
    /// affects what happens to every consumer who currently gets a database created for them.
    /// </remarks>
    [Test]
    [Ignore("CONFIRMED 2026-07-30: Mode=ReadWrite silently CREATES a database that does not exist, and "
            + "leaves the file behind. ConfigureStorage only asks whether the mode is Memory, so the "
            + "other three values are dropped - the same defect family as read-only, which this PR "
            + "fixed, but a database-level one: honouring it means passing FileMode.Open instead of "
            + "OpenOrCreate, which changes behaviour for anyone relying on a database being created. "
            + "SQLite refuses this shape with 'unable to open database file'. "
            + "adonet, AdoNet/WitDbConnection.cs:ConfigureStorage")]
    public void ModeReadWriteRefusesToCreateAMissingDatabaseTest()
    {
        var path = Path.Combine(m_testDir, "must_exist.witdb");

        using DbConnection conn = new WitDbConnection($"Data Source={path};Mode=ReadWrite");

        Assert.Multiple(() =>
        {
            Assert.That(() => conn.Open(), Throws.Exception,
                "Mode=ReadWrite must not create a database that is not there");
            Assert.That(File.Exists(path), Is.False,
                "and must not leave a file behind either");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Creates a database with one table and one row, and closes it, so that each test starts from a
    /// database that exists and has content to read.
    /// </summary>
    private string Seed(string fileName)
    {
        var path = Path.Combine(m_testDir, fileName);

        using DbConnection seed = new WitDbConnection($"Data Source={path}");
        seed.Open();
        Execute(seed, "CREATE TABLE T (Id BIGINT PRIMARY KEY, V TEXT)");
        Execute(seed, "INSERT INTO T (Id, V) VALUES (1, 'a')");

        return path;
    }

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
