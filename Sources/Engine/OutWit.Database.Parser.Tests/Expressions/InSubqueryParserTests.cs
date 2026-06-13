using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Expressions;

/// <summary>
/// SQLite-compat: ORDER BY / LIMIT in subqueries used by IN / NOT IN.
/// </summary>
[TestFixture]
public sealed class InSubqueryParserTests
{
    [Test]
    public void ParseNotInSubqueryWithOrderByLimitTest()
    {
        var stmt = WitSql.ParseStatement(
            "DELETE FROM t WHERE id NOT IN (SELECT id FROM t ORDER BY id DESC LIMIT @p0)");

        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDelete>());
        var delete = (WitSqlStatementDelete)stmt;
        Assert.That(delete.WhereClause, Is.Not.Null);

        var where = delete.WhereClause!;
        Assert.That(where, Is.InstanceOf<WitSqlExpressionIn>());
        var inExpr = (WitSqlExpressionIn)where;
        Assert.That(inExpr.IsNot, Is.True);
        Assert.That(inExpr.Subquery, Is.Not.Null);
        Assert.That(inExpr.Subquery!.OrderByClause, Is.Not.Null);
        Assert.That(inExpr.Subquery.LimitCount, Is.Not.Null);
    }
}
