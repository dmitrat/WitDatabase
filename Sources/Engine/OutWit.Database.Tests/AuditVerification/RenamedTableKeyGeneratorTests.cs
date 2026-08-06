using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// After <c>ALTER TABLE … RENAME TO</c> the table's key generator restarted at zero, and the next
/// generated INSERT landed on key 1 and OVERWROTE the row that was there - silently, reporting one row
/// affected.
///
/// The cause is one line of bookkeeping: the generator is persisted under a key built from the table
/// NAME, and <c>RenameTable</c> carried the definition, the indexes and the row count across but not
/// the counter. The renamed table therefore had none, which reads as zero.
///
/// Found on 2026-08-06 while building Studio's schema designer, whose table rebuild ended - as the
/// design asked - with a rename. It does not any more; this is why.
///
/// Controls that came with it, and they are what attributes the defect: renaming a COLUMN does not do
/// it, ADD COLUMN does not do it, and an INSERT that names the duplicate key explicitly IS refused with
/// a UNIQUE violation. So it was the rename that lost the counter.
/// </summary>
[TestFixture]
public sealed class RenamedTableKeyGeneratorTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_rename_rowid_{Guid.NewGuid():N}");

        m_engine = new WitSqlEngine(WitDatabase.Create(m_databasePath), ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;

        if (!Directory.Exists(m_databasePath))
            return;

        try
        {
            Directory.Delete(m_databasePath, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only - a locked file must not fail the run.
        }
    }

    #endregion

    #region Tests

    [Test]
    public void AnInsertAfterARenameDoesNotOverwriteAnExistingRowTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('one')");
        m_engine.Execute("INSERT INTO R (V) VALUES ('two')");

        m_engine.Execute("ALTER TABLE R RENAME TO R2");
        m_engine.Execute("INSERT INTO R2 (V) VALUES ('three')");

        var rows = Rows("SELECT Id, V FROM R2");

        Assert.That(rows, Is.EqualTo(new[] { "1|one", "2|two", "3|three" }),
            "the insert must add a row - it landed on key 1 and destroyed 'one'");
    }

    [Test]
    public void TheGeneratorKeepsCountingAfterARenameTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");

        for (var i = 1; i <= 3; i++)
            m_engine.Execute($"INSERT INTO R (V) VALUES ('v{i}')");

        m_engine.Execute("ALTER TABLE R RENAME TO R2");

        m_engine.Execute("INSERT INTO R2 (V) VALUES ('v4')");
        m_engine.Execute("INSERT INTO R2 (V) VALUES ('v5')");

        Assert.That(Ids("SELECT Id FROM R2"), Is.EqualTo(new[] { 1L, 2L, 3L, 4L, 5L }));
    }

    /// <summary>
    /// The counter is persisted, so it has to travel on disk as well as in memory - otherwise the
    /// defect comes back the next time the database is opened.
    /// </summary>
    [Test]
    public void TheGeneratorSurvivesARenameAndAReopenTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('one')");
        m_engine.Execute("INSERT INTO R (V) VALUES ('two')");
        m_engine.Execute("ALTER TABLE R RENAME TO R2");

        Reopen();

        m_engine.Execute("INSERT INTO R2 (V) VALUES ('three')");

        Assert.That(Rows("SELECT Id, V FROM R2"),
            Is.EqualTo(new[] { "1|one", "2|two", "3|three" }));
    }

    /// <summary>
    /// And the other direction: a NEW table created under the old name must start from one. The
    /// counter is moved rather than copied, so nothing is left behind for it to inherit.
    /// </summary>
    [Test]
    public void ATableCreatedUnderTheOldNameStartsFromOneTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('one')");
        m_engine.Execute("INSERT INTO R (V) VALUES ('two')");

        m_engine.Execute("ALTER TABLE R RENAME TO R2");

        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('fresh')");

        Assert.That(Rows("SELECT Id, V FROM R"), Is.EqualTo(new[] { "1|fresh" }));
        Assert.That(Rows("SELECT Id, V FROM R2"), Is.EqualTo(new[] { "1|one", "2|two" }),
            "and the renamed table is untouched by it");
    }

    /// <summary>
    /// The rename is not the only way a counter could end up behind the data - it is only the one that
    /// was found. A generated key that lands on an occupied row must FAIL rather than overwrite it,
    /// whatever put the counter there.
    ///
    /// The counter is set by hand here, which is what TRUNCATE does through the same method, so this
    /// is a state the engine can reach without any defect at all.
    /// </summary>
    [Test]
    public void AGeneratedKeyThatCollidesIsRefusedRatherThanOverwritingTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('one')");
        m_engine.Execute("INSERT INTO R (V) VALUES ('two')");

        m_engine.Catalog.ResetRowId("R", 0);

        Assert.Throws<InvalidOperationException>(
            () => m_engine.Execute("INSERT INTO R (V) VALUES ('three')"),
            "a generated key on top of an existing row is a broken counter, and overwriting the row "
            + "is the worst possible answer to it");

        Assert.That(Rows("SELECT Id, V FROM R"), Is.EqualTo(new[] { "1|one", "2|two" }),
            "and nothing was destroyed on the way to finding out");
    }

    /// <summary>
    /// CONTROL: the same insert, with the counter where it belongs, is not refused. Without this the
    /// case above would pass for an engine that refuses every insert.
    /// </summary>
    [Test]
    public void AGeneratedKeyThatDoesNotCollideIsAcceptedTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('one')");
        m_engine.Execute("INSERT INTO R (V) VALUES ('two')");

        Assert.DoesNotThrow(() => m_engine.Execute("INSERT INTO R (V) VALUES ('three')"));

        Assert.That(Rows("SELECT Id, V FROM R"),
            Is.EqualTo(new[] { "1|one", "2|two", "3|three" }));
    }

    /// <summary>
    /// CONTROL: after TRUNCATE the counter is reset on purpose and the table is empty, so key 1 is
    /// free and the insert must go through. A guard that refused here would break TRUNCATE.
    /// </summary>
    [Test]
    public void TruncateResetsTheCounterAndTheNextInsertStartsFromOneTest()
    {
        m_engine.Execute("CREATE TABLE R (Id INT NOT NULL PRIMARY KEY AUTOINCREMENT, V TEXT NOT NULL)");
        m_engine.Execute("INSERT INTO R (V) VALUES ('one')");
        m_engine.Execute("INSERT INTO R (V) VALUES ('two')");

        m_engine.Execute("TRUNCATE TABLE R");
        m_engine.Execute("INSERT INTO R (V) VALUES ('fresh')");

        Assert.That(Rows("SELECT Id, V FROM R"), Is.EqualTo(new[] { "1|fresh" }));
    }

    #endregion

    #region Helpers

    private void Reopen()
    {
        m_engine.Dispose();
        m_engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);
    }

    private string[] Rows(string sql)
    {
        return m_engine.Query(sql)
            .Select(row => $"{row["Id"].AsInt64()}|{row["V"].AsString()}")
            .OrderBy(text => text)
            .ToArray();
    }

    private long[] Ids(string sql)
    {
        return m_engine.Query(sql).Select(row => row["Id"].AsInt64()).OrderBy(id => id).ToArray();
    }

    #endregion
}
