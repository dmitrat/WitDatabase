using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.AlterActions;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableConstraints;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for SELECT statement parsing.
/// </summary>
[TestFixture]
public class SelectStatementParserTest
{
    [Test]
    public void ParseSimpleSelectTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        
        Assert.That(select.SelectList, Has.Count.EqualTo(1));
        Assert.That(select.SelectList[0].IsStar, Is.True);
        Assert.That(select.FromClause, Is.Not.Null);
        Assert.That(select.FromClause, Has.Count.EqualTo(1));
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceSimple>());
        Assert.That(((TableSourceSimple)select.FromClause[0]).TableName, Is.EqualTo("Users"));
    }

    [Test]
    public void ParseSelectWithColumnsTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id, Name, Email FROM Users");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        
        Assert.That(select.SelectList, Has.Count.EqualTo(3));
        Assert.That(select.SelectList[0].Expression, Is.InstanceOf<WitSqlExpressionColumnRef>());
        Assert.That(((WitSqlExpressionColumnRef)select.SelectList[0].Expression!).ColumnName, Is.EqualTo("Id"));
        Assert.That(((WitSqlExpressionColumnRef)select.SelectList[1].Expression!).ColumnName, Is.EqualTo("Name"));
        Assert.That(((WitSqlExpressionColumnRef)select.SelectList[2].Expression!).ColumnName, Is.EqualTo("Email"));
    }

    [Test]
    public void ParseSelectWithAliasTest()
    {
        var stmt = WitSql.ParseStatement("SELECT Id AS UserId, Name UserName FROM Users u");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        
        Assert.That(select.SelectList[0].Alias, Is.EqualTo("UserId"));
        Assert.That(select.SelectList[1].Alias, Is.EqualTo("UserName"));
        
        var table = (TableSourceSimple)select.FromClause![0];
        Assert.That(table.Alias, Is.EqualTo("u"));
    }

    [Test]
    public void ParseSelectWithWhereTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Age >= 18 AND IsActive = TRUE");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.WhereClause, Is.Not.Null);
        Assert.That(select.WhereClause, Is.InstanceOf<WitSqlExpressionBinary>());
        
        var where = (WitSqlExpressionBinary)select.WhereClause!;
        Assert.That(where.Operator, Is.EqualTo(BinaryOperatorType.And));
    }

    [Test]
    public void ParseSelectDistinctTest()
    {
        var stmt = WitSql.ParseStatement("SELECT DISTINCT Status FROM Orders");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.IsDistinct, Is.True);
    }

    [Test]
    public void ParseSelectWithOrderByTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name ASC, CreatedAt DESC");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.OrderByClause, Is.Not.Null);
        Assert.That(select.OrderByClause, Has.Count.EqualTo(2));
        Assert.That(select.OrderByClause![0].Descending, Is.False);
        Assert.That(select.OrderByClause[1].Descending, Is.True);
    }

    [Test]
    public void ParseSelectWithLimitOffsetTest()
    {
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LIMIT 10 OFFSET 20");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.LimitCount, Is.Not.Null);
        Assert.That(select.LimitOffset, Is.Not.Null);
        
        Assert.That(((WitSqlExpressionLiteral)select.LimitCount!).Value, Is.EqualTo(10L));
        Assert.That(((WitSqlExpressionLiteral)select.LimitOffset!).Value, Is.EqualTo(20L));
    }

    [Test]
    public void ParseSelectWithGroupByHavingTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT Status, COUNT(*) AS Cnt FROM Orders GROUP BY Status HAVING COUNT(*) > 5");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.GroupByClause, Is.Not.Null);
        Assert.That(select.GroupByClause, Has.Count.EqualTo(1));
        Assert.That(select.HavingClause, Is.Not.Null);
    }

    [Test]
    public void ParseSelectWithJoinTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT o.Id, u.Name FROM Orders o INNER JOIN Users u ON o.UserId = u.Id");
        
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.FromClause, Has.Count.EqualTo(1));
        Assert.That(select.FromClause![0], Is.InstanceOf<TableSourceJoin>());
        
        var join = (TableSourceJoin)select.FromClause[0];
        Assert.That(join.JoinType, Is.EqualTo(JoinType.Inner));
    }
}

/// <summary>
/// Tests for INSERT statement parsing.
/// </summary>
[TestFixture]
public class InsertStatementParserTest
{
    [Test]
    public void ParseSimpleInsertTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Id, Name, Email) VALUES (1, 'John', 'john@test.com')");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementInsert>());
        var insert = (WitSqlStatementInsert)stmt;
        
        Assert.That(insert.TableName, Is.EqualTo("Users"));
        Assert.That(insert.ColumnNames, Has.Count.EqualTo(3));
        Assert.That(insert.Values, Is.Not.Null);
        Assert.That(insert.Values![0], Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseInsertWithoutColumnsTest()
    {
        var stmt = WitSql.ParseStatement("INSERT INTO Users VALUES (1, 'John')");
        
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ColumnNames, Is.Empty); // Empty list when no columns specified
    }
}

/// <summary>
/// Tests for UPDATE statement parsing.
/// </summary>
[TestFixture]
public class UpdateStatementParserTest
{
    [Test]
    public void ParseSimpleUpdateTest()
    {
        var stmt = WitSql.ParseStatement(
            "UPDATE Users SET Name = 'Jane', Age = 25 WHERE Id = 1");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementUpdate>());
        var update = (WitSqlStatementUpdate)stmt;
        
        Assert.That(update.TableName, Is.EqualTo("Users"));
        Assert.That(update.SetClauses, Has.Count.EqualTo(2));
        Assert.That(update.SetClauses[0].ColumnName, Is.EqualTo("Name"));
        Assert.That(update.WhereClause, Is.Not.Null);
    }
}

/// <summary>
/// Tests for DELETE statement parsing.
/// </summary>
[TestFixture]
public class DeleteStatementParserTest
{
    [Test]
    public void ParseSimpleDeleteTest()
    {
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Id = 1");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDelete>());
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
}

/// <summary>
/// Tests for CREATE TABLE statement parsing.
/// </summary>
[TestFixture]
public class CreateTableStatementParserTest
{
    [Test]
    public void ParseSimpleCreateTableTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Email TEXT UNIQUE
            )");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        var create = (WitSqlStatementCreateTable)stmt;
        
        Assert.That(create.TableName, Is.EqualTo("Users"));
        Assert.That(create.Columns, Has.Count.EqualTo(3));
        Assert.That(create.Columns[0].Name, Is.EqualTo("Id"));
        Assert.That(create.Columns[0].DataType.TypeName, Is.EqualTo("BIGINT"));
    }

    [Test]
    public void ParseCreateTableIfNotExistsTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE IF NOT EXISTS Logs (Id INT)");
        
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.IfNotExists, Is.True);
    }

    [Test]
    public void ParseCreateTableWithConstraintsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Orders (
                Id GUID PRIMARY KEY,
                UserId GUID NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
                Amount DECIMAL DEFAULT 0,
                Status VARCHAR(20) CHECK (Status IN ('pending', 'completed'))
            )");
        
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(4));
        
        // Check REFERENCES constraint
        var userIdConstraints = create.Columns[1].Constraints;
        Assert.That(userIdConstraints, Is.Not.Null);
        Assert.That(userIdConstraints!.Any(c => c is ColumnConstraintReferences), Is.True);
    }

    [Test]
    public void ParseCreateTableWithTableConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE OrderItems (
                OrderId BIGINT,
                ProductId BIGINT,
                Quantity INT,
                PRIMARY KEY (OrderId, ProductId)
            )");
        
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Constraints, Is.Not.Null);
        Assert.That(create.Constraints![0], Is.InstanceOf<TableConstraintPrimaryKey>());
        
        var pk = (TableConstraintPrimaryKey)create.Constraints[0];
        Assert.That(pk.Columns, Has.Count.EqualTo(2));
    }
}

/// <summary>
/// Tests for expression parsing.
/// </summary>
[TestFixture]
public class ExpressionParserTest
{
    [Test]
    public void ParseArithmeticExpressionTest()
    {
        var expr = WitSql.ParseExpression("1 + 2 * 3");
        
        // Should be parsed as 1 + (2 * 3) due to precedence
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBinary>());
        var add = (WitSqlExpressionBinary)expr;
        Assert.That(add.Operator, Is.EqualTo(BinaryOperatorType.Add));
        Assert.That(add.Right, Is.InstanceOf<WitSqlExpressionBinary>());
        
        var mul = (WitSqlExpressionBinary)add.Right;
        Assert.That(mul.Operator, Is.EqualTo(BinaryOperatorType.Multiply));
    }

    [Test]
    public void ParseFunctionCallTest()
    {
        var expr = WitSql.ParseExpression("UPPER(Name)");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionFunctionCall>());
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("UPPER"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseCountStarTest()
    {
        var expr = WitSql.ParseExpression("COUNT(*)");
        
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("COUNT"));
        Assert.That(func.IsStar, Is.True);
    }

    [Test]
    public void ParseCaseExpressionTest()
    {
        var expr = WitSql.ParseExpression(
            "CASE WHEN Status = 1 THEN 'Active' ELSE 'Inactive' END");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionCase>());
        var caseExpr = (WitSqlExpressionCase)expr;
        Assert.That(caseExpr.WhenClauses, Has.Count.EqualTo(1));
        Assert.That(caseExpr.ElseResult, Is.Not.Null);
    }

    [Test]
    public void ParseBetweenExpressionTest()
    {
        var expr = WitSql.ParseExpression("Age BETWEEN 18 AND 65");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionBetween>());
        var between = (WitSqlExpressionBetween)expr;
        Assert.That(between.IsNot, Is.False);
    }

    [Test]
    public void ParseInExpressionTest()
    {
        var expr = WitSql.ParseExpression("Status IN ('A', 'B', 'C')");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIn>());
        var inExpr = (WitSqlExpressionIn)expr;
        Assert.That(inExpr.Values, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseLikeExpressionTest()
    {
        var expr = WitSql.ParseExpression("Name LIKE 'John%'");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionLike>());
    }

    [Test]
    public void ParseIsNullExpressionTest()
    {
        var expr = WitSql.ParseExpression("DeletedAt IS NULL");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIsNull>());
        Assert.That(((WitSqlExpressionIsNull)expr).IsNot, Is.False);
    }

    [Test]
    public void ParseIsNotNullExpressionTest()
    {
        var expr = WitSql.ParseExpression("Email IS NOT NULL");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionIsNull>());
        Assert.That(((WitSqlExpressionIsNull)expr).IsNot, Is.True);
    }

    [Test]
    public void ParseNewGuidFunctionTest()
    {
        var expr = WitSql.ParseExpression("NEWGUID()");
        
        Assert.That(expr, Is.InstanceOf<WitSqlExpressionFunctionCall>());
        Assert.That(((WitSqlExpressionFunctionCall)expr).FunctionName, Is.EqualTo("NEWGUID"));
    }
}

/// <summary>
/// Tests for error handling.
/// </summary>
[TestFixture]
public class ParserErrorHandlingTest
{
    [Test]
    public void InvalidSyntaxThrowsExceptionTest()
    {
        Assert.Throws<WitSqlParsingException>(() => 
            WitSql.Parse("SELECT FROM"));
    }

    [Test]
    public void EmptyInputThrowsExceptionTest()
    {
        Assert.Throws<WitSqlParsingException>(() => 
            WitSql.ParseStatement(""));
    }

    [Test]
    public void TryParseReturnsErrorsTest()
    {
        var result = WitSql.TryParse("SELECT * FORM Users"); // typo: FORM instead of FROM
        
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Has.Count.GreaterThan(0));
    }
}

/// <summary>
/// Tests for DROP and ALTER statements.
/// </summary>
[TestFixture]
public class DdlStatementsParserTest
{
    [Test]
    public void ParseDropTableTest()
    {
        var stmt = WitSql.ParseStatement("DROP TABLE IF EXISTS TempData");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDropTable>());
        var drop = (WitSqlStatementDropTable)stmt;
        Assert.That(drop.TableName, Is.EqualTo("TempData"));
        Assert.That(drop.IfExists, Is.True);
    }

    [Test]
    public void ParseAlterTableAddColumnTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ADD COLUMN Age INT");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementAlterTable>());
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionAddColumn>());
        
        var add = (AlterActionAddColumn)alter.Action;
        Assert.That(add.WitSqlColumn.Name, Is.EqualTo("Age"));
    }

    [Test]
    public void ParseCreateIndexTest()
    {
        var stmt = WitSql.ParseStatement(
            "CREATE UNIQUE INDEX IX_Users_Email ON Users (Email)");
        
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateIndex>());
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.IndexName, Is.EqualTo("IX_Users_Email"));
        Assert.That(create.IsUnique, Is.True);
        Assert.That(create.Columns, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseDropIndexTest()
    {
        var stmt = WitSql.ParseStatement("DROP INDEX IF EXISTS IX_Users_Email");
        
        var drop = (WitSqlStatementDropIndex)stmt;
        Assert.That(drop.IfExists, Is.True);
    }
}
