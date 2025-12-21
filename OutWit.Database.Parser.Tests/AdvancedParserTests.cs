using OutWit.Database.Parser.Exceptions;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for advanced SQL features: CTE, set operations, transactions.
/// </summary>
[TestFixture]
public class AdvancedParserTests
{
    #region CTE (throws NotImplementedException)

    [Test]
    public void ParseWithCte()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("WITH ActiveOrders AS (SELECT * FROM Orders WHERE Status = 'active') SELECT * FROM ActiveOrders"));
    }

    [Test]
    public void ParseWithRecursiveCte()
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
    public void ParseUnion()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Email FROM Customers UNION SELECT Email FROM Subscribers"));
    }

    [Test]
    public void ParseUnionAll()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Id FROM A UNION ALL SELECT Id FROM B"));
    }

    [Test]
    public void ParseIntersect()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Id FROM A INTERSECT SELECT Id FROM B"));
    }

    [Test]
    public void ParseExcept()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SELECT Id FROM A EXCEPT SELECT Id FROM B"));
    }

    #endregion

    #region Transactions (throws NotImplementedException)

    [Test]
    public void ParseBeginTransaction()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("BEGIN TRANSACTION"));
    }

    [Test]
    public void ParseCommit()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("COMMIT"));
    }

    [Test]
    public void ParseRollback()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("ROLLBACK"));
    }

    [Test]
    public void ParseSavepoint()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("SAVEPOINT sp1"));
    }

    [Test]
    public void ParseRollbackToSavepoint()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("ROLLBACK TO SAVEPOINT sp1"));
    }

    [Test]
    public void ParseReleaseSavepoint()
    {
        Assert.Throws<NotImplementedException>(() =>
            WitSql.ParseStatement("RELEASE SAVEPOINT sp1"));
    }

    #endregion

    #region Error Handling

    [Test]
    public void InvalidSyntaxThrows()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.Parse("SELECT FROM"));
    }

    [Test]
    public void EmptyInputThrows()
    {
        Assert.Throws<WitSqlParsingException>(() => WitSql.ParseStatement(""));
    }

    [Test]
    public void TryParseReturnsFalseForInvalidSql()
    {
        var result = WitSql.TryParse("SELECT * FORM Users"); // typo: FORM instead of FROM
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Has.Count.GreaterThan(0));
    }

    #endregion

    #region Comments

    [Test]
    public void ParseWithLineComment()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users -- get all users");
        Assert.That(stmt, Is.Not.Null);
    }

    [Test]
    public void ParseWithBlockComment()
    {
        var stmt = WitSql.ParseStatement("SELECT /* columns */ * FROM Users");
        Assert.That(stmt, Is.Not.Null);
    }

    #endregion
}
