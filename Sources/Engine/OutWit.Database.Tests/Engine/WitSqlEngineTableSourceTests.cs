namespace OutWit.Database.Tests;

/// <summary>
/// The three capabilities phase 9b adds: <c>TOP n</c>, a <c>VALUES</c> table source, and a derived
/// column list.
/// </summary>
/// <remarks>
/// <para>
/// All three are supported by <b>both</b> drop-in targets, which is what decided them. The dialect
/// oracle measured it: a derived column list is accepted by PostgreSQL 17 and SQL Server 2022 and
/// <b>rejected by SQLite</b> - so the SQLite oracle every other instrument here uses would have read
/// this as parity and answered the question wrongly.
/// </para>
/// <para>
/// <c>TOP n</c> is SQL Server's spelling of a row limit rather than a capability of its own, and it
/// maps onto the limit the engine already had.
/// </para>
/// </remarks>
[TestFixture]
[Category("Engine")]
public sealed class WitSqlEngineTableSourceTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(50))");
        m_engine.Execute("INSERT INTO T (Id, Name) VALUES (1, 'a')");
        m_engine.Execute("INSERT INTO T (Id, Name) VALUES (2, 'b')");
        m_engine.Execute("INSERT INTO T (Id, Name) VALUES (3, 'c')");
    }

    #endregion

    #region TOP

    [Test]
    public void TopLimitsTheRowsReturnedTest()
    {
        Assert.That(Ids("SELECT TOP 2 Id FROM T ORDER BY Id"), Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public void TopAgreesWithLimitTest()
    {
        Assert.That(Ids("SELECT TOP 1 Id FROM T ORDER BY Id"),
            Is.EqualTo(Ids("SELECT Id FROM T ORDER BY Id LIMIT 1")),
            "TOP is a spelling of LIMIT, so the two must answer identically");
    }

    [Test]
    public void TopWorksWithDistinctTest()
    {
        Assert.That(Ids("SELECT DISTINCT TOP 2 Id FROM T ORDER BY Id"), Is.EqualTo(new long[] { 1, 2 }));
    }

    #endregion

    #region VALUES as a table source

    [Test]
    public void ValuesIsATableSourceTest()
    {
        var rows = m_engine.Query("SELECT * FROM (VALUES (10), (20)) AS V");

        Assert.That(rows.Select(r => r[0].AsInt64()).ToArray(), Is.EqualTo(new long[] { 10, 20 }));
    }

    [Test]
    public void ValuesNamesItsColumnsLikePostgresTest()
    {
        var row = m_engine.Query("SELECT * FROM (VALUES (10, 'x')) AS V")[0];

        Assert.That(row.ColumnNames, Is.EqualTo(new[] { "column1", "column2" }),
            "PostgreSQL names them column1, column2; SQL Server names nothing and requires a "
            + "derived column list, so following PostgreSQL gives a caller something to select by");
    }

    [Test]
    public void ValuesRowsMayBeExpressionsTest()
    {
        var rows = m_engine.Query("SELECT * FROM (VALUES (1 + 1), (2 * 3)) AS V");

        Assert.That(rows.Select(r => r[0].AsInt64()).ToArray(), Is.EqualTo(new long[] { 2, 6 }));
    }

    [Test]
    public void ValuesIsAQueryInItsOwnRightTest()
    {
        var rows = m_engine.Query("VALUES (1), (2)");

        Assert.That(rows, Has.Count.EqualTo(2),
            "both targets accept VALUES wherever a query goes, not only in a FROM clause");
    }

    [Test]
    public void RaggedValuesAreRefusedTest()
    {
        Assert.That(() => m_engine.Query("SELECT * FROM (VALUES (1), (2, 3)) AS V"),
            Throws.Exception,
            "rows of different widths have no column set; refused rather than padded");
    }

    #endregion

    #region Derived column list

    [Test]
    public void DerivedColumnListRenamesTheColumnsTest()
    {
        var row = m_engine.Query("SELECT * FROM (SELECT Id, Name FROM T) AS V (Key, Label)")[0];

        Assert.That(row.ColumnNames, Is.EqualTo(new[] { "Key", "Label" }));
    }

    [Test]
    public void DerivedColumnListNamesAreSelectableTest()
    {
        var rows = m_engine.Query("SELECT V.Label FROM (SELECT Id, Name FROM T) AS V (Key, Label) ORDER BY V.Key");

        Assert.That(rows.Select(r => r[0].AsString()).ToArray(), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void DerivedColumnListNamesAValuesSourceTest()
    {
        var row = m_engine.Query("SELECT * FROM (VALUES (10, 'x')) AS V (N, S)")[0];

        Assert.That(row.ColumnNames, Is.EqualTo(new[] { "N", "S" }),
            "the two features compose - which is the pair the dialect corpus first measured as one "
            + "shape and had to be split apart");
    }

    [Test]
    public void DerivedColumnListOfTheWrongWidthIsRefusedTest()
    {
        Assert.That(() => m_engine.Query("SELECT * FROM (SELECT Id, Name FROM T) AS V (Only)"),
            Throws.Exception,
            "both targets refuse a mismatched list; a silently padded rename gives columns names "
            + "that mean something other than what they hold");
    }

    #endregion

    #region Helpers

    private long[] Ids(string sql) =>
        m_engine.Query(sql).Select(row => row[0].AsInt64()).ToArray();

    #endregion
}
