using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Functions;

/// <summary>
/// Tests for window function parsing (SS7).
/// Covers: OVER, PARTITION BY, ORDER BY, frame clause, ROW_NUMBER, RANK, DENSE_RANK,
/// NTILE, PERCENT_RANK, CUME_DIST, FIRST_VALUE, LAST_VALUE, NTH_VALUE, LAG, LEAD.
/// </summary>
[TestFixture]
public class WindowFunctionParserTests
{
    #region Basic OVER Clause (SS7)

    [Test]
    public void ParseSimpleOverTest()
    {
        var expr = WitSql.ParseExpression("SUM(Amount) OVER ()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SUM"));
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseOverWithPartitionByTest()
    {
        var expr = WitSql.ParseExpression("SUM(Amount) OVER (PARTITION BY UserId)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
        Assert.That(func.Over!.PartitionBy, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseOverWithMultiplePartitionsTest()
    {
        var expr = WitSql.ParseExpression(
            "AVG(Price) OVER (PARTITION BY Category, SubCategory)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over!.PartitionBy, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseOverWithOrderByTest()
    {
        var expr = WitSql.ParseExpression("ROW_NUMBER() OVER (ORDER BY Id)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
        Assert.That(func.Over!.OrderBy, Is.Not.Null);
        Assert.That(func.Over.OrderBy, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseOverWithOrderByDescTest()
    {
        var expr = WitSql.ParseExpression("RANK() OVER (ORDER BY Score DESC)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over!.OrderBy![0].Descending, Is.True);
    }

    [Test]
    public void ParseOverWithPartitionAndOrderTest()
    {
        var expr = WitSql.ParseExpression(
            "DENSE_RANK() OVER (PARTITION BY Department ORDER BY Salary DESC)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DENSE_RANK"));
        Assert.That(func.Over!.PartitionBy, Is.Not.Null);
        Assert.That(func.Over.OrderBy, Is.Not.Null);
    }

    #endregion

    #region Ranking Functions (SS7.1)

    [Test]
    public void ParseRowNumberFunctionTest()
    {
        var expr = WitSql.ParseExpression("ROW_NUMBER() OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ROW_NUMBER"));
        Assert.That(func.Arguments, Is.Null.Or.Empty);
    }

    [Test]
    public void ParseRankFunctionTest()
    {
        var expr = WitSql.ParseExpression("RANK() OVER (ORDER BY Score)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RANK"));
    }

    [Test]
    public void ParseDenseRankFunctionTest()
    {
        var expr = WitSql.ParseExpression("DENSE_RANK() OVER (ORDER BY Points DESC)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DENSE_RANK"));
    }

    [Test]
    public void ParseNtileFunctionTest()
    {
        var expr = WitSql.ParseExpression("NTILE(4) OVER (ORDER BY Price)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NTILE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParsePercentRankFunctionTest()
    {
        var expr = WitSql.ParseExpression("PERCENT_RANK() OVER (ORDER BY Score)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("PERCENT_RANK"));
    }

    [Test]
    public void ParseCumeDistFunctionTest()
    {
        var expr = WitSql.ParseExpression("CUME_DIST() OVER (ORDER BY Score)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CUME_DIST"));
    }

    #endregion

    #region Value Functions (SS7.2)

    [Test]
    public void ParseFirstValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("FIRST_VALUE(Price) OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("FIRST_VALUE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseLastValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("LAST_VALUE(Price) OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LAST_VALUE"));
    }

    [Test]
    public void ParseNthValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("NTH_VALUE(Price, 3) OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NTH_VALUE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseLagFunctionTest()
    {
        var expr = WitSql.ParseExpression("LAG(Price, 1) OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LAG"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseLagWithDefaultTest()
    {
        var expr = WitSql.ParseExpression("LAG(Price, 1, 0) OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LAG"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseLeadFunctionTest()
    {
        var expr = WitSql.ParseExpression("LEAD(Price) OVER (ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LEAD"));
    }

    [Test]
    public void ParseLeadWithOffsetTest()
    {
        var expr = WitSql.ParseExpression("LEAD(Price, 2) OVER (PARTITION BY Category ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LEAD"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
        Assert.That(func.Over!.PartitionBy, Is.Not.Null);
    }

    #endregion

    #region Frame Clause (SS7)

    [Test]
    public void ParseRowsUnboundedPrecedingTest()
    {
        var expr = WitSql.ParseExpression(
            "SUM(Amount) OVER (ORDER BY CreatedAt ROWS UNBOUNDED PRECEDING)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseRowsCurrentRowTest()
    {
        var expr = WitSql.ParseExpression(
            "AVG(Price) OVER (ORDER BY CreatedAt ROWS CURRENT ROW)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseRowsBetweenUnboundedAndCurrentTest()
    {
        var expr = WitSql.ParseExpression(
            "SUM(Amount) OVER (ORDER BY CreatedAt ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseRowsBetweenNPrecedingAndNFollowingTest()
    {
        var expr = WitSql.ParseExpression(
            "AVG(Price) OVER (ORDER BY CreatedAt ROWS BETWEEN 3 PRECEDING AND 1 FOLLOWING)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseRowsBetweenCurrentAndUnboundedTest()
    {
        var expr = WitSql.ParseExpression(
            "MAX(Price) OVER (ORDER BY CreatedAt ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseRangeBetweenTest()
    {
        var expr = WitSql.ParseExpression(
            "SUM(Amount) OVER (ORDER BY CreatedAt RANGE BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    public void ParseRangeUnboundedPrecedingTest()
    {
        var expr = WitSql.ParseExpression(
            "COUNT(*) OVER (ORDER BY OrderDate RANGE UNBOUNDED PRECEDING)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.Over, Is.Not.Null);
    }

    #endregion

    #region Aggregate Functions with OVER (SS7)

    [Test]
    public void ParseSumOverPartitionTest()
    {
        var expr = WitSql.ParseExpression("SUM(Amount) OVER (PARTITION BY UserId)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SUM"));
        Assert.That(func.Over!.PartitionBy, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseAvgOverTest()
    {
        var expr = WitSql.ParseExpression("AVG(Price) OVER (PARTITION BY Category ORDER BY CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("AVG"));
    }

    [Test]
    public void ParseCountOverTest()
    {
        var expr = WitSql.ParseExpression("COUNT(*) OVER (ORDER BY CreatedAt ROWS UNBOUNDED PRECEDING)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("COUNT"));
        Assert.That(func.IsStar, Is.True);
    }

    [Test]
    public void ParseMinMaxOverTest()
    {
        var exprMin = WitSql.ParseExpression("MIN(Price) OVER (PARTITION BY Category)");
        var exprMax = WitSql.ParseExpression("MAX(Price) OVER (PARTITION BY Category)");
        
        Assert.That(((WitSqlExpressionFunctionCall)exprMin).FunctionName, Is.EqualTo("MIN"));
        Assert.That(((WitSqlExpressionFunctionCall)exprMax).FunctionName, Is.EqualTo("MAX"));
    }

    #endregion

    #region Window Functions in SELECT

    [Test]
    public void ParseWindowFunctionInSelectTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                Name,
                Salary,
                ROW_NUMBER() OVER (ORDER BY Salary DESC) AS RowRank
            FROM Employees");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseMultipleWindowFunctionsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                Name,
                Salary,
                Department,
                ROW_NUMBER() OVER (PARTITION BY Department ORDER BY Salary DESC) AS DeptRank,
                ROW_NUMBER() OVER (ORDER BY Salary DESC) AS GlobalRank,
                SUM(Salary) OVER (PARTITION BY Department) AS DeptTotal
            FROM Employees");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList, Has.Count.EqualTo(6));
    }

    [Test]
    public void ParseRunningTotalTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                OrderDate,
                Amount,
                SUM(Amount) OVER (ORDER BY OrderDate ROWS UNBOUNDED PRECEDING) AS RunningTotal
            FROM Orders");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseCompareWithPreviousRowTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                MonthNum,
                Revenue,
                LAG(Revenue, 1) OVER (ORDER BY MonthNum) AS PrevMonthRevenue,
                Revenue - LAG(Revenue, 1) OVER (ORDER BY MonthNum) AS Change
            FROM MonthlyRevenue");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseTopNPerGroupTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT * FROM (
                SELECT 
                    Category,
                    Name,
                    Price,
                    ROW_NUMBER() OVER (PARTITION BY Category ORDER BY Price DESC) AS RowRank
                FROM Products
            ) AS ranked
            WHERE RowRank <= 3");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion
}
