using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Collation;

/// <summary>
/// Tests for collation parsing (SS24).
/// Covers: COLLATE in expressions, COLLATE in ORDER BY,
/// collation names (BINARY, NOCASE, UNICODE, UNICODE_CI).
/// </summary>
[TestFixture]
public class CollationParserTests
{
    #region COLLATE in Expression (SS24)

    [Test]
    public void ParseCollateInExpressionTest()
    {
        var expr = WitSql.ParseExpression("Name COLLATE NOCASE");
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCollate>());
        var collate = (WitSqlExpressionCollate)expr;
        Assert.That(collate.CollationName, Is.EqualTo("NOCASE"));
    }

    [Test]
    public void ParseCollateInWhereClauseTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users WHERE Name COLLATE NOCASE = 'john'");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCollateInComparisonTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users WHERE FirstName COLLATE NOCASE = LastName COLLATE NOCASE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCollateWithLikeTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Products WHERE Name COLLATE NOCASE LIKE '%search%'");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCollateInCaseExpressionTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                CASE WHEN Name COLLATE NOCASE = 'admin' THEN 'Administrator' ELSE Name END
            FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion

    #region COLLATE in ORDER BY (SS24)

    [Test]
    public void ParseCollateInOrderByTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users ORDER BY Name COLLATE NOCASE");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause, Is.Not.Null);
    }

    [Test]
    public void ParseCollateInOrderByDescTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users ORDER BY Name COLLATE UNICODE_CI DESC");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause, Is.Not.Null);
        Assert.That(select.OrderByClause![0].Descending, Is.True);
    }

    [Test]
    public void ParseMultipleCollateInOrderByTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT * FROM Users 
            ORDER BY LastName COLLATE NOCASE ASC, FirstName COLLATE NOCASE ASC");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause, Has.Count.EqualTo(2));
    }

    #endregion

    #region Collation Names (SS24)

    [Test]
    [TestCase("BINARY")]
    [TestCase("NOCASE")]
    [TestCase("UNICODE")]
    [TestCase("UNICODE_CI")]
    public void ParseAllCollationNamesInExpressionTest(string collation)
    {
        var expr = WitSql.ParseExpression($"Name COLLATE {collation}");
        var collateExpr = (WitSqlExpressionCollate)expr;
        Assert.That(collateExpr.CollationName, Is.EqualTo(collation));
    }

    [Test]
    public void ParseCollationCaseInsensitiveTest()
    {
        // Collation names are case-insensitive, but parser normalizes to uppercase
        var expr = WitSql.ParseExpression("Name COLLATE nocase");
        var collateExpr = (WitSqlExpressionCollate)expr;
        Assert.That(collateExpr.CollationName.ToUpperInvariant(), Is.EqualTo("NOCASE"));
    }

    #endregion

    #region Complex Scenarios

    [Test]
    public void ParseCollateInIndexTest()
    {
        // Collation can affect how index is used
        var stmt = WitSql.ParseStatement(@"
            SELECT * FROM Users 
            WHERE Email COLLATE NOCASE = 'TEST@EXAMPLE.COM' 
            ORDER BY Email COLLATE NOCASE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCollateWithGroupByTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT Name COLLATE NOCASE, COUNT(*) 
            FROM Users 
            GROUP BY Name COLLATE NOCASE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCollateInJoinConditionTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT * FROM Users u
            INNER JOIN Accounts a ON u.Name COLLATE NOCASE = a.Username COLLATE NOCASE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion
}
