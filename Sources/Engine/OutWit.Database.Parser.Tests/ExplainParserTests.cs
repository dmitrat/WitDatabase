using OutWit.Database.Parser;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for parsing EXPLAIN statement.
/// </summary>
[TestFixture]
public class ExplainParserTests
{
    #region Basic EXPLAIN Tests

    [Test]
    public void ParseSimpleExplainTest()
    {
        var stmt = WitSql.ParseStatement("EXPLAIN SELECT * FROM Users");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.QueryPlan, Is.False);
        Assert.That(explain.Statement, Is.Not.Null);
        Assert.That(explain.Statement.SelectList, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseExplainQueryPlanTest()
    {
        var stmt = WitSql.ParseStatement("EXPLAIN QUERY PLAN SELECT * FROM Orders WHERE Id > 100");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.QueryPlan, Is.True);
        Assert.That(explain.Statement, Is.Not.Null);
        Assert.That(explain.Statement.WhereClause, Is.Not.Null);
    }

    #endregion

    #region Complex SELECT Tests

    [Test]
    public void ParseExplainWithJoinTest()
    {
        var stmt = WitSql.ParseStatement(@"
            EXPLAIN SELECT u.Name, o.Total 
            FROM Users u 
            INNER JOIN Orders o ON u.Id = o.UserId 
            WHERE o.Total > 100");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.QueryPlan, Is.False);
        Assert.That(explain.Statement.FromClause, Has.Count.GreaterThan(0));
    }

    [Test]
    public void ParseExplainWithGroupByTest()
    {
        var stmt = WitSql.ParseStatement(@"
            EXPLAIN QUERY PLAN 
            SELECT Category, COUNT(*) AS Count 
            FROM Products 
            GROUP BY Category 
            HAVING COUNT(*) > 5");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.QueryPlan, Is.True);
        Assert.That(explain.Statement.GroupByClause, Is.Not.Null);
        Assert.That(explain.Statement.HavingClause, Is.Not.Null);
    }

    [Test]
    public void ParseExplainWithSubqueryTest()
    {
        var stmt = WitSql.ParseStatement(@"
            EXPLAIN SELECT * FROM Users 
            WHERE Id IN (SELECT UserId FROM Orders WHERE Total > 1000)");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.Statement.WhereClause, Is.Not.Null);
    }

    [Test]
    public void ParseExplainWithOrderByLimitTest()
    {
        var stmt = WitSql.ParseStatement(@"
            EXPLAIN QUERY PLAN 
            SELECT * FROM Products 
            ORDER BY Price DESC 
            LIMIT 10 OFFSET 20");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.Statement.OrderByClause, Is.Not.Null);
        Assert.That(explain.Statement.LimitCount, Is.Not.Null);
        Assert.That(explain.Statement.LimitOffset, Is.Not.Null);
    }

    #endregion

    #region Window Function Tests

    [Test]
    public void ParseExplainWithWindowFunctionTest()
    {
        var stmt = WitSql.ParseStatement(@"
            EXPLAIN SELECT 
                Name,
                ROW_NUMBER() OVER (PARTITION BY Category ORDER BY Price) AS RowNum
            FROM Products");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.Statement.SelectList, Has.Count.EqualTo(2));
    }

    #endregion

    #region CTE Tests

    [Test]
    public void ParseExplainWithCteTest()
    {
        var stmt = WitSql.ParseStatement(@"
            EXPLAIN QUERY PLAN
            WITH TopCustomers AS (
                SELECT UserId, SUM(Total) AS TotalSpent
                FROM Orders
                GROUP BY UserId
                HAVING SUM(Total) > 10000
            )
            SELECT u.Name, tc.TotalSpent
            FROM Users u
            INNER JOIN TopCustomers tc ON u.Id = tc.UserId");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementExplain>());
        var explain = (WitSqlStatementExplain)stmt;
        
        Assert.That(explain.QueryPlan, Is.True);
        Assert.That(explain.Statement.CteDefinitions, Is.Not.Null);
        Assert.That(explain.Statement.CteDefinitions, Has.Count.EqualTo(1));
    }

    #endregion
}
