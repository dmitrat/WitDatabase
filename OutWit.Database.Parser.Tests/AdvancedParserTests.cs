using OutWit.Database.Parser.Exceptions;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for advanced SQL features: CTE, set operations, transactions, error handling.
/// </summary>
[TestFixture]
public class AdvancedParserTests
{
    #region CTE (throws NotImplementedException)

    [Test]
    public void ParseWithCteTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("WITH ActiveOrders AS (SELECT * FROM Orders WHERE Status = 'active') SELECT * FROM ActiveOrders"));
    }

    [Test]
    public void ParseWithRecursiveCteTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement(@"
                WITH RECURSIVE CategoryTree AS (
                    SELECT Id, Name, ParentId FROM Categories WHERE ParentId IS NULL
                    UNION ALL
                    SELECT c.Id, c.Name, c.ParentId FROM Categories c 
                    INNER JOIN CategoryTree ct ON c.ParentId = ct.Id
                )
                SELECT * FROM CategoryTree"));
    }

    #endregion

    #region Set Operations (throws NotImplementedException)

    [Test]
    public void ParseUnionTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Email FROM Customers UNION SELECT Email FROM Subscribers"));
    }

    [Test]
    public void ParseUnionAllTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Id FROM A UNION ALL SELECT Id FROM B"));
    }

    [Test]
    public void ParseIntersectTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Id FROM A INTERSECT SELECT Id FROM B"));
    }

    [Test]
    public void ParseExceptTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Id FROM A EXCEPT SELECT Id FROM B"));
    }

    #endregion

    #region Transactions (throws NotImplementedException)

    [Test]
    public void ParseBeginTransactionTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("BEGIN TRANSACTION"));
    }

    [Test]
    public void ParseCommitTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("COMMIT"));
    }

    [Test]
    public void ParseRollbackTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("ROLLBACK"));
    }

    [Test]
    public void ParseSavepointTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SAVEPOINT sp1"));
    }

    [Test]
    public void ParseRollbackToSavepointTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("ROLLBACK TO SAVEPOINT sp1"));
    }

    [Test]
    public void ParseReleaseSavepointTest()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("RELEASE SAVEPOINT sp1"));
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
