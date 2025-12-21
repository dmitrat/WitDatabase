using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Expressions;
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

    #region RETURNING Clause

    [Test]
    public void ParseInsertWithReturningTest()
    {
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name) VALUES ('John') RETURNING Id");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ReturningClause, Is.Not.Null);
        Assert.That(insert.ReturningClause, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseInsertWithReturningStarTest()
    {
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name) VALUES ('John') RETURNING *");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ReturningClause, Is.Not.Null);
        Assert.That(insert.ReturningClause![0].IsStar, Is.True);
    }

    [Test]
    public void ParseInsertWithReturningMultipleColumnsTest()
    {
        var stmt = WitSql.ParseStatement(
            "INSERT INTO Users (Name, Email) VALUES ('John', 'john@example.com') RETURNING Id, CreatedAt");
        var insert = (WitSqlStatementInsert)stmt;
        Assert.That(insert.ReturningClause, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseUpdateWithReturningTest()
    {
        var stmt = WitSql.ParseStatement(
            "UPDATE Users SET Name = 'Jane' WHERE Id = 1 RETURNING Id, Name, UpdatedAt");
        var update = (WitSqlStatementUpdate)stmt;
        Assert.That(update.ReturningClause, Is.Not.Null);
        Assert.That(update.ReturningClause, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseDeleteWithReturningTest()
    {
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Id = 1 RETURNING Id, Name");
        var delete = (WitSqlStatementDelete)stmt;
        Assert.That(delete.ReturningClause, Is.Not.Null);
        Assert.That(delete.ReturningClause, Has.Count.EqualTo(2));
    }

    #endregion

    #region Date Extraction Functions

    [Test]
    public void ParseYearFunctionTest()
    {
        var expr = WitSql.ParseExpression("YEAR(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("YEAR"));
    }

    [Test]
    public void ParseMonthFunctionTest()
    {
        var expr = WitSql.ParseExpression("MONTH(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MONTH"));
    }

    [Test]
    public void ParseDayFunctionTest()
    {
        var expr = WitSql.ParseExpression("DAY(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DAY"));
    }

    [Test]
    public void ParseHourFunctionTest()
    {
        var expr = WitSql.ParseExpression("HOUR(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("HOUR"));
    }

    [Test]
    public void ParseMinuteFunctionTest()
    {
        var expr = WitSql.ParseExpression("MINUTE(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MINUTE"));
    }

    [Test]
    public void ParseSecondFunctionTest()
    {
        var expr = WitSql.ParseExpression("SECOND(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SECOND"));
    }

    #endregion

    #region System Functions

    [Test]
    public void ParseLastInsertRowIdFunctionTest()
    {
        var expr = WitSql.ParseExpression("LAST_INSERT_ROWID()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LAST_INSERT_ROWID"));
    }

    [Test]
    public void ParseTypeOfFunctionTest()
    {
        var expr = WitSql.ParseExpression("TYPEOF(Value)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("TYPEOF"));
    }

    [Test]
    public void ParseIfNullFunctionTest()
    {
        var expr = WitSql.ParseExpression("IFNULL(Name, 'Unknown')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("IFNULL"));
    }

    [Test]
    public void ParseNvlFunctionTest()
    {
        var expr = WitSql.ParseExpression("NVL(Status, 'pending')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NVL"));
    }

    [Test]
    public void ParseChangesFunctionTest()
    {
        var expr = WitSql.ParseExpression("CHANGES()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CHANGES"));
    }

    [Test]
    public void ParseDatabaseFunctionTest()
    {
        var expr = WitSql.ParseExpression("DATABASE()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DATABASE"));
    }

    [Test]
    public void ParseVersionFunctionTest()
    {
        var expr = WitSql.ParseExpression("VERSION()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("VERSION"));
    }

    #endregion

    #region Extended String Functions

    [Test]
    public void ParseSubstringFunctionTest()
    {
        var expr = WitSql.ParseExpression("SUBSTRING(Name, 1, 5)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SUBSTRING"));
    }

    [Test]
    public void ParseLtrimFunctionTest()
    {
        var expr = WitSql.ParseExpression("LTRIM(Name)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LTRIM"));
    }

    [Test]
    public void ParseRtrimFunctionTest()
    {
        var expr = WitSql.ParseExpression("RTRIM(Name)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RTRIM"));
    }

    [Test]
    public void ParseInstrFunctionTest()
    {
        var expr = WitSql.ParseExpression("INSTR(Email, '@')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("INSTR"));
    }

    [Test]
    public void ParseReverseFunctionTest()
    {
        var expr = WitSql.ParseExpression("REVERSE(Name)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("REVERSE"));
    }

    [Test]
    public void ParseConcatFunctionTest()
    {
        var expr = WitSql.ParseExpression("CONCAT(FirstName, ' ', LastName)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CONCAT"));
    }

    [Test]
    public void ParseConcatWsFunctionTest()
    {
        var expr = WitSql.ParseExpression("CONCAT_WS(', ', City, Country)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CONCAT_WS"));
    }

    [Test]
    public void ParseCharLengthFunctionTest()
    {
        var expr = WitSql.ParseExpression("CHAR_LENGTH(Name)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CHAR_LENGTH"));
    }

    [Test]
    public void ParseOctetLengthFunctionTest()
    {
        var expr = WitSql.ParseExpression("OCTET_LENGTH(Data)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("OCTET_LENGTH"));
    }

    [Test]
    public void ParseLpadFunctionTest()
    {
        var expr = WitSql.ParseExpression("LPAD(Id, 10, '0')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LPAD"));
    }

    [Test]
    public void ParseRpadFunctionTest()
    {
        var expr = WitSql.ParseExpression("RPAD(Name, 20, ' ')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RPAD"));
    }

    [Test]
    public void ParseRepeatFunctionTest()
    {
        var expr = WitSql.ParseExpression("REPEAT('*', 10)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("REPEAT"));
    }

    [Test]
    public void ParseSpaceFunctionTest()
    {
        var expr = WitSql.ParseExpression("SPACE(5)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SPACE"));
    }

    #endregion

    #region Extended Numeric Functions

    [Test]
    public void ParseCeilingFunctionTest()
    {
        var expr = WitSql.ParseExpression("CEILING(3.14)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("CEILING"));
    }

    [Test]
    public void ParseSignFunctionTest()
    {
        var expr = WitSql.ParseExpression("SIGN(-5)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SIGN"));
    }

    [Test]
    public void ParseTruncFunctionTest()
    {
        var expr = WitSql.ParseExpression("TRUNC(3.14159, 2)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("TRUNC"));
    }

    [Test]
    public void ParseModFunctionTest()
    {
        var expr = WitSql.ParseExpression("MOD(10, 3)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MOD"));
    }

    [Test]
    public void ParsePowerFunctionTest()
    {
        var expr = WitSql.ParseExpression("POWER(2, 10)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("POWER"));
    }

    [Test]
    public void ParseSqrtFunctionTest()
    {
        var expr = WitSql.ParseExpression("SQRT(16)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SQRT"));
    }

    [Test]
    public void ParseExpFunctionTest()
    {
        var expr = WitSql.ParseExpression("EXP(1)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("EXP"));
    }

    [Test]
    public void ParseLogFunctionTest()
    {
        var expr = WitSql.ParseExpression("LOG(10)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LOG"));
    }

    [Test]
    public void ParseLog10FunctionTest()
    {
        var expr = WitSql.ParseExpression("LOG10(100)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LOG10"));
    }

    [Test]
    public void ParseLog2FunctionTest()
    {
        var expr = WitSql.ParseExpression("LOG2(8)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LOG2"));
    }

    [Test]
    public void ParsePiFunctionTest()
    {
        var expr = WitSql.ParseExpression("PI()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("PI"));
    }

    [Test]
    public void ParseRandomFunctionTest()
    {
        var expr = WitSql.ParseExpression("RANDOM()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RANDOM"));
    }

    [Test]
    public void ParseSinFunctionTest()
    {
        var expr = WitSql.ParseExpression("SIN(0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("SIN"));
    }

    [Test]
    public void ParseCosFunctionTest()
    {
        var expr = WitSql.ParseExpression("COS(0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("COS"));
    }

    [Test]
    public void ParseTanFunctionTest()
    {
        var expr = WitSql.ParseExpression("TAN(0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("TAN"));
    }

    [Test]
    public void ParseAsinFunctionTest()
    {
        var expr = WitSql.ParseExpression("ASIN(0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ASIN"));
    }

    [Test]
    public void ParseAcosFunctionTest()
    {
        var expr = WitSql.ParseExpression("ACOS(1)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ACOS"));
    }

    [Test]
    public void ParseAtanFunctionTest()
    {
        var expr = WitSql.ParseExpression("ATAN(0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ATAN"));
    }

    [Test]
    public void ParseAtan2FunctionTest()
    {
        var expr = WitSql.ParseExpression("ATAN2(1, 1)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("ATAN2"));
    }

    [Test]
    public void ParseDegreesFunctionTest()
    {
        var expr = WitSql.ParseExpression("DEGREES(PI())");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DEGREES"));
    }

    [Test]
    public void ParseRadiansFunctionTest()
    {
        var expr = WitSql.ParseExpression("RADIANS(180)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("RADIANS"));
    }

    #endregion

    #region Extended Date Functions

    [Test]
    public void ParseDayOfWeekFunctionTest()
    {
        var expr = WitSql.ParseExpression("DAYOFWEEK(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DAYOFWEEK"));
    }

    [Test]
    public void ParseDayOfYearFunctionTest()
    {
        var expr = WitSql.ParseExpression("DAYOFYEAR(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DAYOFYEAR"));
    }

    [Test]
    public void ParseWeekOfYearFunctionTest()
    {
        var expr = WitSql.ParseExpression("WEEKOFYEAR(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("WEEKOFYEAR"));
    }

    [Test]
    public void ParseQuarterFunctionTest()
    {
        var expr = WitSql.ParseExpression("QUARTER(CreatedAt)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("QUARTER"));
    }

    [Test]
    public void ParseDateAddFunctionTest()
    {
        var expr = WitSql.ParseExpression("DATEADD('day', 7, NOW())");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DATEADD"));
    }

    [Test]
    public void ParseDateDiffFunctionTest()
    {
        var expr = WitSql.ParseExpression("DATEDIFF('day', StartDate, EndDate)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("DATEDIFF"));
    }

    [Test]
    public void ParseStrftimeFunctionTest()
    {
        var expr = WitSql.ParseExpression("STRFTIME('%Y-%m-%d', NOW())");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("STRFTIME"));
    }

    [Test]
    public void ParseMakeDateFunctionTest()
    {
        var expr = WitSql.ParseExpression("MAKEDATE(2024, 100)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MAKEDATE"));
    }

    [Test]
    public void ParseMakeTimeFunctionTest()
    {
        var expr = WitSql.ParseExpression("MAKETIME(14, 30, 0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("MAKETIME"));
    }

    #endregion

    #region Conversion Functions

    [Test]
    public void ParseConvertFunctionTest()
    {
        var expr = WitSql.ParseExpression("CONVERT(INT, '123')");
        var cast = (WitSqlExpressionCast)expr;
        Assert.That(cast.TargetType.TypeName, Is.EqualTo("INT"));
        Assert.That(cast.Expression, Is.InstanceOf<WitSqlExpressionLiteral>());
    }

    [Test]
    public void ParseHexFunctionTest()
    {
        var expr = WitSql.ParseExpression("HEX(Data)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("HEX"));
    }

    [Test]
    public void ParseUnhexFunctionTest()
    {
        var expr = WitSql.ParseExpression("UNHEX('48656C6C6F')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("UNHEX"));
    }

    #endregion

    #region Aggregate Functions

    [Test]
    public void ParseGroupConcatFunctionTest()
    {
        var expr = WitSql.ParseExpression("GROUP_CONCAT(Name)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("GROUP_CONCAT"));
    }

    #endregion

    #region Extended Window Functions

    [Test]
    public void ParseNtileFunctionTest()
    {
        var expr = WitSql.ParseExpression("NTILE(4) OVER (ORDER BY Price)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NTILE"));
        Assert.That(func.Over, Is.Not.Null);
    }

    [Test]
    [Ignore("FIRST_VALUE token conflict with FIRST keyword - to be fixed")]
    public void ParseFirstValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("FIRST_VALUE(Price) OVER (ORDER BY Date)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("FIRST_VALUE"));
    }

    [Test]
    [Ignore("LAST_VALUE token conflict with LAST keyword - to be fixed")]
    public void ParseLastValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("LAST_VALUE(Price) OVER (ORDER BY Date)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LAST_VALUE"));
    }

    [Test]
    [Ignore("NTH_VALUE token parsing issue - to be fixed")]
    public void ParseNthValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("NTH_VALUE(Price, 3) OVER (ORDER BY Date)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NTH_VALUE"));
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

    #region ID Generation Functions

    [Test]
    public void ParseNewUuidFunctionTest()
    {
        var expr = WitSql.ParseExpression("NEWUUID()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("NEWUUID"));
    }

    [Test]
    public void ParseLastIncrementFunctionTest()
    {
        var expr = WitSql.ParseExpression("LASTINCREMENT('orders')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("LASTINCREMENT"));
    }

    #endregion
}
