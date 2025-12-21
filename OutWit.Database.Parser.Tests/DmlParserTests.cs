using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for DML statement parsing: SELECT, INSERT, UPDATE, DELETE.
/// </summary>
[TestFixture]
public class DmlParserTests
{
    #region SELECT

    [Test]
    public void ParseSelectStar()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseSelectColumns()
    {
        var stmt = WitSql.ParseStatement("SELECT Id, Name, Email FROM Users");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseSelectWithAlias()
    {
        var stmt = WitSql.ParseStatement("SELECT Id AS UserId, Name UserName FROM Users u");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].Alias, Is.EqualTo("UserId"));
        Assert.That(select.SelectList[1].Alias, Is.EqualTo("UserName"));
    }

    [Test]
    public void ParseSelectDistinct()
    {
        var stmt = WitSql.ParseStatement("SELECT DISTINCT Status FROM Orders");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.IsDistinct, Is.True);
    }

    [Test]
    public void ParseSelectWithWhere()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Age >= 18 AND IsActive = TRUE");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.WhereClause, Is.Not.Null);
    }

    [Test]
    public void ParseSelectWithOrderBy()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name ASC, CreatedAt DESC");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause, Has.Count.EqualTo(2));
        Assert.That(select.OrderByClause![0].Descending, Is.False);
        Assert.That(select.OrderByClause[1].Descending, Is.True);
    }

    [Test]
    public void ParseSelectWithLimitOffset()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LIMIT 10 OFFSET 20");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.LimitCount, Is.Not.Null);
        Assert.That(select.LimitOffset, Is.Not.Null);
    }

    [Test]
    public void ParseSelectWithGroupByHaving()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT Status, COUNT(*) FROM Orders GROUP BY Status HAVING COUNT(*) > 5");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.GroupByClause, Has.Count.EqualTo(1));
        Assert.That(select.HavingClause, Is.Not.Null);
    }

    [Test]
    public void ParseSelectWithJoin()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT o.Id, u.Name FROM Orders o INNER JOIN Users u ON o.UserId = u.Id");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceJoin>());
    }

    [Test]
    public void ParseSelectWithLeftJoin()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users u LEFT JOIN Orders o ON u.Id = o.UserId");
        var select = (WitSqlStatementSelect)stmt;
        var join = (TableSourceJoin)select.FromClause![0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Left));
    }

    #endregion

    #region INSERT

    [Test]
    public void ParseInsertWithColumns()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name, Email) VALUES (1, 'John', 'john@test.com')");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.TableName, Is.EqualTo("Users"));
        Assert.That(insert.ColumnNames, Has.Count.EqualTo(3));
        Assert.That(insert.Values![0], Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseInsertWithoutColumns()
    {
        var stmt = WitSql.ParseStatement("INSERT INTO Users VALUES (1, 'John')");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ColumnNames, Is.Empty);
    }

    [Test]
    public void ParseInsertMultipleRows()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Logs (Message, Level) VALUES ('Msg1', 1), ('Msg2', 2), ('Msg3', 3)");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.Values, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseInsertSelect()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Archive SELECT * FROM Orders WHERE Status = 'completed'");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.SelectSource, Is.Not.Null);
    }

    #endregion

    #region UPDATE

    [Test]
    public void ParseUpdate()
    {
        var stmt = WitSql.ParseStatement(
            "UPDATE Users SET Name = 'Jane', Age = 25 WHERE Id = 1");
        var update = (WitSqlStatementUpdate)stmt;
        Assert.That(update.TableName, Is.EqualTo("Users"));
        Assert.That(update.SetClauses, Has.Count.EqualTo(2));
        Assert.That(update.WhereClause, Is.Not.Null);
    }

    #endregion

    #region DELETE

    [Test]
    public void ParseDelete()
    {
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE IsActive = FALSE");
        var delete = (WitSqlStatementDelete)stmt;
        Assert.That(delete.TableName, Is.EqualTo("Users"));
        Assert.That(delete.WhereClause, Is.Not.Null);
    }

    [Test]
    public void ParseDeleteAll()
    {
        var stmt = WitSql.ParseStatement("DELETE FROM TempData");
        var delete = (WitSqlStatementDelete)stmt;
        Assert.That(delete.WhereClause, Is.Null);
    }

    #endregion
}
