using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// <c>INFORMATION_SCHEMA.COLUMNS.ORDINAL_POSITION</c> was 1 for every column of every table, so the
/// catalogue could not say what order a table's columns were in. A client that orders by it - which is
/// what the column is for, and what Studio's designer does - got them in whatever order the catalogue
/// happened to return.
///
/// The cause: only ADD COLUMN and DROP COLUMN numbered the columns. CREATE TABLE left every one of
/// them at the default zero, and the view publishes <c>Ordinal + 1</c>.
///
/// Found on 2026-08-06 while building Studio's schema designer.
/// </summary>
[TestFixture]
public sealed class ColumnOrdinalPositionTests
{
    #region Fields

    private string m_databasePath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_databasePath = Path.Combine(Path.GetTempPath(), $"witdb_ordinal_{Guid.NewGuid():N}");

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
    public void ColumnsAreNumberedInDeclarationOrderTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, Name TEXT, Age INT)");

        Assert.That(Positions("T"), Is.EqualTo(new[] { "Id=1", "Name=2", "Age=3" }));
    }

    [Test]
    public void AnAddedColumnGetsTheNextPositionTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, Name TEXT)");
        m_engine.Execute("ALTER TABLE T ADD COLUMN Age INT");

        Assert.That(Positions("T"), Is.EqualTo(new[] { "Id=1", "Name=2", "Age=3" }));
    }

    [Test]
    public void TheRemainingColumnsAreRenumberedAfterADropTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, Name TEXT, Age INT)");
        m_engine.Execute("ALTER TABLE T DROP COLUMN Name");

        Assert.That(Positions("T"), Is.EqualTo(new[] { "Id=1", "Age=2" }),
            "a gap in the numbering would be as unusable as all-ones");
    }

    [Test]
    public void ThePositionsSurviveAReopenTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT NOT NULL PRIMARY KEY, Name TEXT, Age INT)");

        m_engine.Dispose();
        m_engine = new WitSqlEngine(WitDatabase.Open(m_databasePath), ownsStore: true);

        Assert.That(Positions("T"), Is.EqualTo(new[] { "Id=1", "Name=2", "Age=3" }),
            "the ordinals are part of the stored schema, not something worked out on the way out");
    }

    [Test]
    public void OrderingByThePositionGivesTheDeclarationOrderTest()
    {
        // The way a client actually asks the question.
        m_engine.Execute("CREATE TABLE T (Zulu INT, Alpha INT, Mike INT)");

        var names = m_engine.Query(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'T' ORDER BY ORDINAL_POSITION")
            .Select(row => row["COLUMN_NAME"].AsString())
            .ToArray();

        Assert.That(names, Is.EqualTo(new[] { "Zulu", "Alpha", "Mike" }),
            "and not in alphabetical order, which is what ordering by a constant leaves behind");
    }

    #endregion

    #region Helpers

    private string[] Positions(string tableName)
    {
        return m_engine.Query(
            $"SELECT COLUMN_NAME, ORDINAL_POSITION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}'")
            .Select(row => $"{row["COLUMN_NAME"].AsString()}={row["ORDINAL_POSITION"].AsInt64()}")
            .OrderBy(text => text.Split('=')[1])
            .ToArray();
    }

    #endregion
}
