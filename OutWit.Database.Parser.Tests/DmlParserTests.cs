using OutWit.Database.Parser.Expressions;
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
    #region SELECT Basic

    [Test]
    public void ParseSelectStarTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseSelectAllTest()
    {
        var stmt = WitSql.ParseStatement("SELECT ALL Id, Name FROM Users");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.IsDistinct, Is.False);
        Assert.That(select.SelectList, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseSelectColumnsTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id, Name, Email FROM Users");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseSelectWithAliasTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id AS UserId, Name UserName FROM Users u");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].Alias, Is.EqualTo("UserId"));
        Assert.That(select.SelectList[1].Alias, Is.EqualTo("UserName"));
    }

    [Test]
    public void ParseSelectDistinctTest()
    {
        var stmt = WitSql.ParseStatement("SELECT DISTINCT Status FROM Orders");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.IsDistinct, Is.True);
    }

    [Test]
    public void ParseSelectQualifiedColumnTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Users.Id, Users.Name FROM Users");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList, Has.Count.EqualTo(2));
        
        var col1 = (WitSqlExpressionColumnRef)select.SelectList[0].Expression!;
        Assert.That(col1.TableName, Is.EqualTo("Users"));
        Assert.That(col1.ColumnName, Is.EqualTo("Id"));
    }

    [Test]
    public void ParseSelectTableAliasWithStarTest()
    {
        var stmt = WitSql.ParseStatement("SELECT t.* FROM Users t");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].IsStar, Is.True);
        Assert.That(select.SelectList[0].TableName, Is.EqualTo("t"));
    }

    #endregion

    #region SELECT FROM

    [Test]
    public void ParseSelectMultipleTablesTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users, Orders");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.FromClause, Has.Count.EqualTo(2));
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceSimple>());
        Assert.That(select.FromClause[1], Is.InstanceOf<TableSourceSimple>());
    }

    [Test]
    public void ParseSelectSubqueryInFromTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT sub.Id FROM (SELECT Id FROM Users WHERE IsActive = TRUE) AS sub");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceSubquery>());
        
        var subquery = (TableSourceSubquery)select.FromClause[0];
        Assert.That(subquery.Alias, Is.EqualTo("sub"));
        Assert.That(subquery.Subquery, Is.Not.Null);
    }

    #endregion

    #region SELECT WHERE

    [Test]
    public void ParseSelectWithWhereTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Age >= 18 AND IsActive = TRUE");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.WhereClause, Is.Not.Null);
    }

    [Test]
    public void ParseSelectSubqueryInWhereTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users WHERE Id IN (SELECT UserId FROM Orders)");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.WhereClause, Is.InstanceOf<WitSqlExpressionIn>());
        
        var inExpr = (WitSqlExpressionIn)select.WhereClause!;
        Assert.That(inExpr.Subquery, Is.Not.Null);
        Assert.That(inExpr.Values, Is.Null);
    }

    [Test]
    public void ParseSelectSubqueryInSelectListTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT Name, (SELECT COUNT(*) FROM Orders WHERE Orders.UserId = Users.Id) AS OrderCount FROM Users");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList, Has.Count.EqualTo(2));
        Assert.That(select.SelectList[1].Expression, Is.InstanceOf<WitSqlExpressionSubquery>());
    }

    #endregion

    #region SELECT ORDER BY

    [Test]
    public void ParseSelectWithOrderByTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name ASC, CreatedAt DESC");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause, Has.Count.EqualTo(2));
        Assert.That(select.OrderByClause![0].Descending, Is.False);
        Assert.That(select.OrderByClause[1].Descending, Is.True);
    }

    [Test]
    public void ParseSelectOrderByNullsFirstTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name ASC NULLS FIRST");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause![0].NullsOrder, Is.EqualTo(NullsOrderType.First));
    }

    [Test]
    public void ParseSelectOrderByNullsLastTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name DESC NULLS LAST");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause![0].NullsOrder, Is.EqualTo(NullsOrderType.Last));
    }

    #endregion

    #region SELECT LIMIT

    [Test]
    public void ParseSelectWithLimitOffsetTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LIMIT 10 OFFSET 20");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.LimitCount, Is.Not.Null);
        Assert.That(select.LimitOffset, Is.Not.Null);
    }

    [Test]
    public void ParseSelectMySqlStyleLimitTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LIMIT 20, 10");
        var select = (WitSqlStatementSelect)stmt;
        // MySQL style: LIMIT offset, count - ?????? ????? ??? offset, ?????? - count
        Assert.That(select.LimitCount, Is.Not.Null);
        Assert.That(select.LimitOffset, Is.Not.Null);
    }

    #endregion

    #region SELECT GROUP BY/HAVING

    [Test]
    public void ParseSelectWithGroupByHavingTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT Status, COUNT(*) FROM Orders GROUP BY Status HAVING COUNT(*) > 5");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.GroupByClause, Has.Count.EqualTo(1));
        Assert.That(select.HavingClause, Is.Not.Null);
    }

    [Test]
    public void ParseSelectGroupByMultipleColumnsTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT SaleYear, SaleMonth, SUM(Amount) FROM Sales GROUP BY SaleYear, SaleMonth");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.GroupByClause, Has.Count.EqualTo(2));
    }

    #endregion

    #region SELECT JOIN

    [Test]
    public void ParseSelectWithJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT o.Id, u.Name FROM Orders o INNER JOIN Users u ON o.UserId = u.Id");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceJoin>());
    }

    [Test]
    public void ParseSelectWithLeftJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users u LEFT JOIN Orders o ON u.Id = o.UserId");
        var select = (WitSqlStatementSelect)stmt;
        var join = (TableSourceJoin)select.FromClause![0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Left));
    }

    [Test]
    public void ParseSelectWithRightJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Orders o RIGHT JOIN Users u ON o.UserId = u.Id");
        var select = (WitSqlStatementSelect)stmt;
        var join = (TableSourceJoin)select.FromClause![0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Right));
    }

    [Test]
    public void ParseSelectWithFullJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users u FULL JOIN Orders o ON u.Id = o.UserId");
        var select = (WitSqlStatementSelect)stmt;
        var join = (TableSourceJoin)select.FromClause![0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Full));
    }

    [Test]
    public void ParseSelectWithCrossJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Colors CROSS JOIN Sizes");
        var select = (WitSqlStatementSelect)stmt;
        var join = (TableSourceJoin)select.FromClause![0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Cross));
    }

    [Test]
    public void ParseSelectWithMultipleJoinsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT o.Id, u.Name, p.Title
            FROM Orders o
            INNER JOIN Users u ON o.UserId = u.Id
            INNER JOIN Products p ON o.ProductId = p.Id");
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceJoin>());
    }

    [Test]
    public void ParseSelectSelfJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT e.Name, m.Name AS Manager FROM Employees e LEFT JOIN Employees m ON e.ManagerId = m.Id");
        var select = (WitSqlStatementSelect)stmt;
        var join = (TableSourceJoin)select.FromClause![0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Left));
    }

    #endregion

    #region INSERT

    [Test]
    public void ParseInsertWithColumnsTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name, Email) VALUES (1, 'John', 'john@test.com')");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.TableName, Is.EqualTo("Users"));
        Assert.That(insert.ColumnNames, Has.Count.EqualTo(3));
        Assert.That(insert.Values![0], Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseInsertWithoutColumnsTest()
    {
        var stmt = WitSql.ParseStatement("INSERT INTO Users VALUES (1, 'John')");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ColumnNames, Is.Empty);
    }

    [Test]
    public void ParseInsertMultipleRowsTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Logs (Message, Level) VALUES ('Msg1', 1), ('Msg2', 2), ('Msg3', 3)");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.Values, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseInsertSelectTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Archive SELECT * FROM Orders WHERE Status = 'completed'");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.SelectSource, Is.Not.Null);
    }

    #endregion

    #region UPDATE

    [Test]
    public void ParseUpdateTest()
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
    public void ParseDeleteTest()
    {
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE IsActive = FALSE");
        var delete = (WitSqlStatementDelete)stmt;
        Assert.That(delete.TableName, Is.EqualTo("Users"));
        Assert.That(delete.WhereClause, Is.Not.Null);
    }

    [Test]
    public void ParseDeleteAllTest()
    {
        var stmt = WitSql.ParseStatement("DELETE FROM TempData");
        var delete = (WitSqlStatementDelete)stmt;
        Assert.That(delete.WhereClause, Is.Null);
    }

    #endregion

    #region INSERT OR REPLACE/IGNORE

    [Test]
    public void ParseInsertOrReplaceTest()
    {
        var stmt = WitSql.ParseStatement("INSERT OR REPLACE INTO Users (Id, Name) VALUES (1, 'John')");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ConflictResolution, Is.EqualTo(ConflictResolutionType.Replace));
        Assert.That(insert.TableName, Is.EqualTo("Users"));
    }

    [Test]
    public void ParseInsertOrIgnoreTest()
    {
        var stmt = WitSql.ParseStatement("INSERT OR IGNORE INTO Users (Id, Name) VALUES (1, 'John')");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ConflictResolution, Is.EqualTo(ConflictResolutionType.Ignore));
    }

    #endregion

    #region INSERT ON CONFLICT

    [Test]
    public void ParseInsertOnConflictDoNothingTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name) VALUES (1, 'John') ON CONFLICT DO NOTHING");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.OnConflict, Is.Not.Null);
        Assert.That(insert.OnConflict!.ActionType, Is.EqualTo(ConflictActionType.Nothing));
        Assert.That(insert.OnConflict.ConflictColumns, Is.Null.Or.Empty);
    }

    [Test]
    public void ParseInsertOnConflictWithColumnsDoNothingTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name) VALUES (1, 'John') ON CONFLICT (Id) DO NOTHING");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.OnConflict, Is.Not.Null);
        Assert.That(insert.OnConflict!.ConflictColumns, Has.Count.EqualTo(1));
        Assert.That(insert.OnConflict.ConflictColumns![0], Is.EqualTo("Id"));
        Assert.That(insert.OnConflict.ActionType, Is.EqualTo(ConflictActionType.Nothing));
    }

    [Test]
    public void ParseInsertOnConflictDoUpdateTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name) VALUES (1, 'John') ON CONFLICT (Id) DO UPDATE SET Name = 'Jane'");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.OnConflict, Is.Not.Null);
        Assert.That(insert.OnConflict!.ActionType, Is.EqualTo(ConflictActionType.Update));
        Assert.That(insert.OnConflict.UpdateClauses, Has.Count.EqualTo(1));
        Assert.That(insert.OnConflict.UpdateClauses![0].ColumnName, Is.EqualTo("Name"));
    }

    [Test]
    public void ParseInsertOnConflictDoUpdateMultipleColumnsTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name, Email) VALUES (1, 'John', 'john@test.com') " +
            "ON CONFLICT (Id, Email) DO UPDATE SET Name = 'Jane', Email = 'jane@test.com'");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.OnConflict!.ConflictColumns, Has.Count.EqualTo(2));
        Assert.That(insert.OnConflict.UpdateClauses, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseInsertOnConflictDoUpdateWithWhereTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name) VALUES (1, 'John') " +
            "ON CONFLICT (Id) DO UPDATE SET Name = 'Jane' WHERE Status = 'active'");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.OnConflict, Is.Not.Null);
        Assert.That(insert.OnConflict!.WhereClause, Is.Not.Null);
    }

    [Test]
    public void ParseInsertOnConflictWithReturningTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name) VALUES (1, 'John') " +
            "ON CONFLICT (Id) DO UPDATE SET Name = 'Jane' RETURNING *");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.OnConflict, Is.Not.Null);
        Assert.That(insert.ReturningClause, Is.Not.Null);
    }

    #endregion
}
