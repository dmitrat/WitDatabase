using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for advanced SQL features: CTE, set operations, transactions, error handling.
/// </summary>
[TestFixture]
public class AdvancedParserTests
{
    #region CTE

    [Test]
    public void ParseSimpleCteTest()
    {
        var stmt = WitSql.ParseStatement(
            "WITH ActiveOrders AS (SELECT * FROM Orders WHERE Status = 'active') SELECT * FROM ActiveOrders");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.CteDefinitions, Is.Not.Null);
        Assert.That(select.CteDefinitions, Has.Count.EqualTo(1));
        Assert.That(select.CteDefinitions![0].Name, Is.EqualTo("ActiveOrders"));
        Assert.That(select.IsRecursive, Is.False);
    }

    [Test]
    public void ParseCteWithColumnsTest()
    {
        var stmt = WitSql.ParseStatement(
            "WITH UserCounts (UserId, OrderCount) AS (SELECT UserId, COUNT(*) FROM Orders GROUP BY UserId) SELECT * FROM UserCounts");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.CteDefinitions![0].ColumnNames, Has.Count.EqualTo(2));
        Assert.That(select.CteDefinitions[0].ColumnNames![0], Is.EqualTo("UserId"));
    }

    [Test]
    public void ParseMultipleCteTest()
    {
        var stmt = WitSql.ParseStatement(@"
            WITH 
                ActiveUsers AS (SELECT * FROM Users WHERE IsActive = TRUE),
                RecentOrders AS (SELECT * FROM Orders WHERE OrderDate > '2024-01-01')
            SELECT u.Name, o.Total 
            FROM ActiveUsers u 
            INNER JOIN RecentOrders o ON u.Id = o.UserId");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.CteDefinitions, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseRecursiveCteTest()
    {
        var stmt = WitSql.ParseStatement(@"
            WITH RECURSIVE CategoryTree AS (
                SELECT Id, Name, ParentId, 0 AS Level FROM Categories WHERE ParentId IS NULL
                UNION ALL
                SELECT c.Id, c.Name, c.ParentId, ct.Level + 1 
                FROM Categories c 
                INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
            )
            SELECT * FROM CategoryTree");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.IsRecursive, Is.True);
        Assert.That(select.CteDefinitions, Has.Count.EqualTo(1));
        // The CTE query itself contains a UNION ALL
        Assert.That(select.CteDefinitions![0].Query.SetOperations, Has.Count.EqualTo(1));
    }

    #endregion

    #region Set Operations

    [Test]
    public void ParseUnionTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Email FROM Customers UNION SELECT Email FROM Subscribers");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SetOperations, Is.Not.Null);
        Assert.That(select.SetOperations, Has.Count.EqualTo(1));
        Assert.That(select.SetOperations![0].OperationType, Is.EqualTo(SetOperationType.Union));
        Assert.That(select.SetOperations[0].IsAll, Is.False);
    }

    [Test]
    public void ParseUnionAllTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id FROM A UNION ALL SELECT Id FROM B");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SetOperations![0].OperationType, Is.EqualTo(SetOperationType.Union));
        Assert.That(select.SetOperations[0].IsAll, Is.True);
    }

    [Test]
    public void ParseIntersectTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id FROM A INTERSECT SELECT Id FROM B");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SetOperations![0].OperationType, Is.EqualTo(SetOperationType.Intersect));
    }

    [Test]
    public void ParseExceptTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id FROM A EXCEPT SELECT Id FROM B");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SetOperations![0].OperationType, Is.EqualTo(SetOperationType.Except));
    }

    [Test]
    public void ParseMultipleSetOperationsTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT Id FROM A UNION SELECT Id FROM B UNION ALL SELECT Id FROM C");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SetOperations, Has.Count.EqualTo(2));
        Assert.That(select.SetOperations![0].IsAll, Is.False);
        Assert.That(select.SetOperations[1].IsAll, Is.True);
    }

    [Test]
    public void ParseSetOperationWithOrderByTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT Name FROM Users UNION SELECT Name FROM Customers ORDER BY Name");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SetOperations, Has.Count.EqualTo(1));
        Assert.That(select.OrderByClause, Is.Not.Null);
    }

    #endregion

    #region Transactions

    [Test]
    public void ParseBeginTransactionTest()
    {
        var stmt = WitSql.ParseStatement("BEGIN TRANSACTION");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementBeginTransaction>());
    }

    [Test]
    public void ParseBeginWithoutTransactionKeywordTest()
    {
        var stmt = WitSql.ParseStatement("BEGIN");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementBeginTransaction>());
    }

    [Test]
    public void ParseCommitTest()
    {
        var stmt = WitSql.ParseStatement("COMMIT");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCommit>());
    }

    [Test]
    public void ParseRollbackTest()
    {
        var stmt = WitSql.ParseStatement("ROLLBACK");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.Null);
    }

    [Test]
    public void ParseRollbackToSavepointTest()
    {
        var stmt = WitSql.ParseStatement("ROLLBACK TO SAVEPOINT sp1");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseRollbackToWithoutSavepointKeywordTest()
    {
        var stmt = WitSql.ParseStatement("ROLLBACK TO sp1");
        var rollback = (WitSqlStatementRollback)stmt;
        Assert.That(rollback.SavepointName, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseSavepointTest()
    {
        var stmt = WitSql.ParseStatement("SAVEPOINT sp1");
        var savepoint = (WitSqlStatementSavepoint)stmt;
        Assert.That(savepoint.Name, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseReleaseSavepointTest()
    {
        var stmt = WitSql.ParseStatement("RELEASE SAVEPOINT sp1");
        var release = (WitSqlStatementReleaseSavepoint)stmt;
        Assert.That(release.Name, Is.EqualTo("sp1"));
    }

    [Test]
    public void ParseReleaseWithoutSavepointKeywordTest()
    {
        var stmt = WitSql.ParseStatement("RELEASE sp1");
        var release = (WitSqlStatementReleaseSavepoint)stmt;
        Assert.That(release.Name, Is.EqualTo("sp1"));
    }

    #endregion

    #region Error Handling - Basic

    [Test]
    public void InvalidSyntaxThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.Parse("SELECT FROM"));
    }

    [Test]
    public void EmptyInputThrowsTest()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.ParseStatement(""));
    }

    [Test]
    public void TryParseReturnsFalseForInvalidSqlTest()
    {
        var result = WitSql.TryParse("SELECT * FORM Users"); // typo: FORM instead of FROM
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Has.Count.GreaterThan(0));
    }

    #endregion

    #region Error Handling - Specific Errors

    [Test]
    public void UnbalancedParenthesesThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE (Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void MissingFromKeywordThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * Users WHERE Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void UnexpectedTokenThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("SELECT * FROM Users WHERE WHERE Id = 1"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void MultipleStatementsWhenSingleExpectedThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.ParseStatement("SELECT 1; SELECT 2"));
        Assert.That(ex!.Message, Does.Contain("single statement"));
    }

    [Test]
    public void InvalidCreateTableSyntaxThrowsTest()
    {
        var ex = Assert.Throws<WitSqlParsingException>(() =>
            WitSql.Parse("CREATE TABLE ()"));
        Assert.That(ex!.Errors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void TryParseReturnsErrorDetailsTest()
    {
        var result = WitSql.TryParse("SELECT * FROM WHERE");
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Is.Not.Empty);

        var firstError = result.Errors.First();
        Assert.That(firstError.Line, Is.GreaterThan(0));
        Assert.That(firstError.Message, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TryParseSucceedsForValidSqlTest()
    {
        var result = WitSql.TryParse("SELECT * FROM Users");
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Statements, Has.Count.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);
    }

    #endregion

    #region Comments

    [Test]
    public void ParseWithLineCommentTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users -- get all users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithBlockCommentTest()
    {
        var stmt = WitSql.ParseStatement("SELECT /* columns */ * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithMultiLineBlockCommentTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                /* 
                 * This is a multi-line comment
                 * explaining the query
                 */
                * 
            FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithCommentOnlyLineTest()
    {
        var stmt = WitSql.ParseStatement(@"
            -- This is a comment
            SELECT * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    #endregion

    #region Multiple Statements

    [Test]
    public void ParseMultipleStatementsTest()
    {
        var statements = WitSql.Parse("SELECT 1; SELECT 2; SELECT 3");
        Assert.That(statements, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseMultipleStatementsWithTrailingSemicolonTest()
    {
        var statements = WitSql.Parse("SELECT 1; SELECT 2;");
        Assert.That(statements, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseMixedStatementTypesTest()
    {
        var statements = WitSql.Parse(@"
            CREATE TABLE T (Id INT);
            INSERT INTO T (Id) VALUES (1);
            SELECT * FROM T;
            DROP TABLE T");
        Assert.That(statements, Has.Count.EqualTo(4));
    }

    #endregion
}
