namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A defect found by phase 3's round-trip harness, not by the audit: the expression serializer
/// replaces every subquery with the literal text <c>SELECT ...</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>WitSqlExpressionSerializer</c> emits <c>(SELECT ...)</c> for a scalar subquery,
/// <c>(EXISTS (SELECT ...))</c>, <c>(x IN (SELECT ...))</c> and
/// <c>(x = ANY (SELECT ...))</c> — the ellipsis is literal, not an abbreviation in a log message.
/// </para>
/// <para>
/// That would be harmless if the serializer were only a debugging aid. It is not: the DDL path
/// persists schema through it. <c>StatementExecutor.Ddl.Tables.cs</c> stores <c>CHECK</c> conditions
/// and computed-column expressions this way, <c>Ddl.Indexes.cs</c> stores a partial index's
/// <c>WHERE</c>, <c>Ddl.Triggers.cs</c> stores a trigger's <c>WHEN</c>, and <c>Ddl.Views.cs</c>
/// stores the <b>entire body of a view</b>.
/// </para>
/// <para>
/// Found by <c>GrammarRoundTripTests</c> while building phase 3's regression net, before the grammar
/// was touched. It is <b>not</b> a phase-3 defect and is not fixed there; it is recorded here so the
/// fix has a test waiting, following the convention used throughout <c>AuditVerification/</c>.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class SerializerSubqueryFindingsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT, Total INT)");
        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY, Name VARCHAR(50))");

        m_engine.Execute("INSERT INTO Customers (Id, Name) VALUES (1, 'alice')");
        m_engine.Execute("INSERT INTO Customers (Id, Name) VALUES (2, 'bob')");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, Total) VALUES (10, 1, 100)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, Total) VALUES (11, 2, 200)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, Total) VALUES (12, 1, 300)");
    }

    #endregion

    #region A view whose body contains a subquery

    [Test]
    [Ignore("CONFIRMED 2026-07-28 by execution, and worse than 'the subquery is lost': CREATE VIEW " +
            "SUCCEEDS, and every SELECT against the view then throws WitSqlParsingException " +
            "'Line 1:45 - mismatched input .'. QueryPlanner.CreateViewIterator re-parses the stored " +
            "body at query time, and the body was persisted with the subquery replaced by the " +
            "literal text 'SELECT ...'. So the view is accepted, written to the schema, and is " +
            "permanently unusable. Found by phase 3's round-trip harness; not a phase-3 defect. " +
            "parser, Parser/Serializers/WitSqlExpressionSerializer.cs:316")]
    public void ViewBodyKeepsItsSubqueryTest()
    {
        m_engine.Execute(@"
            CREATE VIEW BigOrders AS
            SELECT Id FROM Orders WHERE Total > (SELECT 150)");

        var ids = m_engine.Query("SELECT Id FROM BigOrders ORDER BY Id")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(ids, Is.EqualTo(new long[] { 11, 12 }),
            "the view's WHERE clause compares against a subquery, so the view must not lose it");
    }

    #endregion

    #region A partial index filtered by a subquery

    [Test]
    [Ignore("CONFIRMED 2026-07-28 by execution: INFORMATION_SCHEMA.INDEXES.FILTER_CONDITION reads " +
            "'(CustomerId IN (SELECT ...))' - the subquery replaced by a literal ellipsis. Same root " +
            "cause as ViewBodyKeepsItsSubqueryTest; Ddl.Indexes.cs persists the partial index's " +
            "WHERE clause through the same serializer. " +
            "parser, Parser/Serializers/WitSqlExpressionSerializer.cs:285")]
    public void PartialIndexKeepsItsSubqueryFilterTest()
    {
        m_engine.Execute(@"
            CREATE INDEX IX_Active ON Orders (Total)
            WHERE CustomerId IN (SELECT Id FROM Customers)");

        var stored = m_engine.Query(
                "SELECT FILTER_CONDITION FROM INFORMATION_SCHEMA.INDEXES WHERE INDEX_NAME = 'IX_Active'")
            .Select(row => row[0].ToString())
            .FirstOrDefault();

        Assert.That(stored, Does.Not.Contain("..."),
            $"the stored filter must be the real predicate, not a placeholder; it is <{stored}>");
    }

    #endregion

    #region Characterisation - what the serializer actually emits

    [Test]
    public void SubquerySerializationCharacterisationTest()
    {
        // Prints what each subquery-bearing expression serializes to, so the defect is on record as
        // an observation rather than only as an assertion about a consequence.
        foreach (var sql in new[]
                 {
                     "SELECT (SELECT MAX(Total) FROM Orders)",
                     "SELECT * FROM Orders WHERE EXISTS (SELECT 1 FROM Customers)",
                     "SELECT * FROM Orders WHERE CustomerId IN (SELECT Id FROM Customers)",
                     "SELECT * FROM Orders WHERE Total > ANY (SELECT Total FROM Orders)",
                 })
        {
            var statement = Parser.WitSql.Parse(sql)[0];

            TestContext.Out.WriteLine(
                $"{sql}{Environment.NewLine}  -> {Parser.Serializers.WitSqlStatementSerializer.Serialize(statement)}");
        }

        Assert.Pass("characterisation only - see the printed result");
    }

    #endregion
}
