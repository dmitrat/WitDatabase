using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Functions;

/// <summary>
/// Tests for JSON function parsing (SS21.2).
/// Covers: JSON_VALUE, JSON_QUERY, JSON_EXTRACT, JSON_SET, JSON_INSERT, 
/// JSON_REPLACE, JSON_REMOVE, JSON_TYPE, JSON_VALID, JSON_ARRAY, JSON_OBJECT.
/// </summary>
[TestFixture]
public class JsonFunctionParserTests
{
    #region JSON_VALUE (SS21.2)

    [Test]
    public void ParseJsonValueFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_VALUE(Data, '$.name')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_VALUE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonValueNestedPathTest()
    {
        var expr = WitSql.ParseExpression("JSON_VALUE(Data, '$.user.address.city')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_VALUE"));
    }

    [Test]
    public void ParseJsonValueInSelectTest()
    {
        var stmt = WitSql.ParseStatement("SELECT JSON_VALUE(Profile, '$.email') AS Email FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
        var select = (WitSqlStatementSelect)stmt;
        Assert.That(select.SelectList[0].Expression, Is.InstanceOf<WitSqlExpressionFunctionCall>());
    }

    [Test]
    public void ParseJsonValueInWhereTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Users WHERE JSON_VALUE(Settings, '$.theme') = 'dark'");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion

    #region JSON_QUERY (SS21.2)

    [Test]
    public void ParseJsonQueryFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_QUERY(Data, '$.items')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_QUERY"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonQueryArrayPathTest()
    {
        var expr = WitSql.ParseExpression("JSON_QUERY(Data, '$.products[*]')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_QUERY"));
    }

    #endregion

    #region JSON_EXTRACT (SS21.2)

    [Test]
    public void ParseJsonExtractFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_EXTRACT(Payload, '$.user.id')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_EXTRACT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonExtractRootPathTest()
    {
        var expr = WitSql.ParseExpression("JSON_EXTRACT(Data, '$')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_EXTRACT"));
    }

    #endregion

    #region JSON_SET (SS21.2)

    [Test]
    public void ParseJsonSetFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_SET(Data, '$.status', 'active')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_SET"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseJsonSetNumericValueTest()
    {
        var expr = WitSql.ParseExpression("JSON_SET(Data, '$.count', 42)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_SET"));
    }

    [Test]
    public void ParseJsonSetInUpdateTest()
    {
        var stmt = WitSql.ParseStatement(
            "UPDATE Products SET Metadata = JSON_SET(Metadata, '$.lastUpdated', NOW()) WHERE Id = 1");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementUpdate>());
    }

    #endregion

    #region JSON_INSERT (SS21.2)

    [Test]
    public void ParseJsonInsertFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_INSERT(Data, '$.newField', 123)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_INSERT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseJsonInsertNullValueTest()
    {
        var expr = WitSql.ParseExpression("JSON_INSERT(Data, '$.optional', NULL)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_INSERT"));
    }

    #endregion

    #region JSON_REPLACE (SS21.2)

    [Test]
    public void ParseJsonReplaceFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_REPLACE(Data, '$.name', 'NewName')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_REPLACE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseJsonReplaceBooleanValueTest()
    {
        var expr = WitSql.ParseExpression("JSON_REPLACE(Data, '$.active', TRUE)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_REPLACE"));
    }

    #endregion

    #region JSON_REMOVE (SS21.2)

    [Test]
    public void ParseJsonRemoveFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_REMOVE(Data, '$.obsolete')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_REMOVE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseJsonRemoveNestedPathTest()
    {
        var expr = WitSql.ParseExpression("JSON_REMOVE(Settings, '$.user.preferences.theme')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_REMOVE"));
    }

    #endregion

    #region JSON_TYPE (SS21.2)

    [Test]
    public void ParseJsonTypeFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_TYPE(Data)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_TYPE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseJsonTypeWithPathTest()
    {
        var expr = WitSql.ParseExpression("JSON_TYPE(JSON_EXTRACT(Data, '$.value'))");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_TYPE"));
    }

    #endregion

    #region JSON_VALID (SS21.2)

    [Test]
    public void ParseJsonValidFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_VALID(RawText)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_VALID"));
        Assert.That(func.Arguments, Has.Count.EqualTo(1));
    }

    [Test]
    public void ParseJsonValidWithLiteralTest()
    {
        var expr = WitSql.ParseExpression("JSON_VALID('{\"key\": \"value\"}')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_VALID"));
    }

    [Test]
    public void ParseJsonValidInWhereTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT * FROM Logs WHERE JSON_VALID(Payload) = TRUE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion

    #region JSON_ARRAY (SS21.2)

    [Test]
    public void ParseJsonArrayFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_ARRAY(1, 2, 'three', 4.0)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_ARRAY"));
        Assert.That(func.Arguments, Has.Count.EqualTo(4));
    }

    [Test]
    public void ParseJsonArrayEmptyTest()
    {
        var expr = WitSql.ParseExpression("JSON_ARRAY()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_ARRAY"));
        Assert.That(func.Arguments, Is.Null.Or.Empty);
    }

    [Test]
    public void ParseJsonArrayWithExpressionsTest()
    {
        var expr = WitSql.ParseExpression("JSON_ARRAY(Id, Name, Price * 1.1)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_ARRAY"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    #endregion

    #region JSON_OBJECT (SS21.2)

    [Test]
    public void ParseJsonObjectFunctionTest()
    {
        var expr = WitSql.ParseExpression("JSON_OBJECT('name', Name, 'id', Id)");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_OBJECT"));
        Assert.That(func.Arguments, Has.Count.EqualTo(4));
    }

    [Test]
    public void ParseJsonObjectEmptyTest()
    {
        var expr = WitSql.ParseExpression("JSON_OBJECT()");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_OBJECT"));
    }

    [Test]
    public void ParseJsonObjectInSelectTest()
    {
        var stmt = WitSql.ParseStatement(
            "SELECT JSON_OBJECT('user', Name, 'email', Email) AS UserJson FROM Users");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion

    #region Complex JSON Expressions

    [Test]
    public void ParseNestedJsonFunctionsTest()
    {
        var expr = WitSql.ParseExpression(
            "JSON_SET(JSON_REMOVE(Data, '$.old'), '$.new', 'value')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("JSON_SET"));
    }

    [Test]
    public void ParseJsonInCaseExpressionTest()
    {
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                CASE 
                    WHEN JSON_VALID(Data) = TRUE THEN JSON_VALUE(Data, '$.name')
                    ELSE 'Invalid JSON'
                END AS Name
            FROM Documents");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementSelect>());
    }

    [Test]
    public void ParseJsonWithCoalesceTest()
    {
        var expr = WitSql.ParseExpression(
            "COALESCE(JSON_VALUE(Data, '$.nickname'), JSON_VALUE(Data, '$.name'), 'Anonymous')");
        var func = (WitSqlExpressionFunctionCall)expr;
        Assert.That(func.FunctionName, Is.EqualTo("COALESCE"));
        Assert.That(func.Arguments, Has.Count.EqualTo(3));
    }

    #endregion
}
