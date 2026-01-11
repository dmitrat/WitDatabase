using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for WitSqlExpressionSerializer.
/// </summary>
[TestFixture]
public class SerializerTests
{
    #region Column Reference Tests

    [Test]
    public void SerializeSimpleColumnRefTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            ColumnName = "Id"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("Id"));
    }

    [Test]
    public void SerializeQualifiedColumnRefTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            TableName = "Users",
            ColumnName = "Name"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("Users.Name"));
    }

    [Test]
    public void SerializeExcludedColumnRefTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            ColumnName = "Name",
            IsExcluded = true
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("EXCLUDED.Name"));
    }

    [Test]
    public void SerializeColumnWithSpecialCharsIsQuotedTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            ColumnName = "User Name"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("\"User Name\""));
    }

    [Test]
    public void SerializeColumnStartingWithDigitIsQuotedTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            ColumnName = "123Column"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("\"123Column\""));
    }

    [Test]
    public void SerializeReservedWordColumnIsQuotedTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            ColumnName = "SELECT"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("\"SELECT\""));
    }

    [Test]
    public void SerializeReservedWordTableIsQuotedTest()
    {
        var expr = new WitSqlExpressionColumnRef
        {
            TableName = "Order",
            ColumnName = "Id"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);
        
        // "ORDER" is a reserved word
        Assert.That(result, Does.Contain("\"Order\""));
    }

    #endregion

    #region Literal Tests

    [Test]
    public void SerializeNullLiteralTest()
    {
        var expr = new WitSqlExpressionLiteral
        {
            Type = LiteralType.Null
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("NULL"));
    }

    [Test]
    public void SerializeIntegerLiteralTest()
    {
        var expr = new WitSqlExpressionLiteral
        {
            Type = LiteralType.Integer,
            Value = 42L
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("42"));
    }

    [Test]
    public void SerializeStringLiteralTest()
    {
        var expr = new WitSqlExpressionLiteral
        {
            Type = LiteralType.String,
            Value = "hello"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("'hello'"));
    }

    [Test]
    public void SerializeStringWithQuoteIsEscapedTest()
    {
        var expr = new WitSqlExpressionLiteral
        {
            Type = LiteralType.String,
            Value = "it's"
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("'it''s'"));
    }

    [Test]
    public void SerializeBooleanTrueTest()
    {
        var expr = new WitSqlExpressionLiteral
        {
            Type = LiteralType.Boolean,
            Value = true
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("TRUE"));
    }

    [Test]
    public void SerializeBooleanFalseTest()
    {
        var expr = new WitSqlExpressionLiteral
        {
            Type = LiteralType.Boolean,
            Value = false
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("FALSE"));
    }

    #endregion

    #region Binary Expression Tests

    [Test]
    public void SerializeBinaryAddTest()
    {
        var expr = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Price" },
            Operator = BinaryOperatorType.Add,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 10L }
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("(Price + 10)"));
    }

    [Test]
    public void SerializeBinaryEqualTest()
    {
        var expr = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Status" },
            Operator = BinaryOperatorType.Equal,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.String, Value = "active" }
        };

        var result = WitSqlExpressionSerializer.Serialize(expr);

        Assert.That(result, Is.EqualTo("(Status = 'active')"));
    }

    #endregion

    #region Round-trip Tests

    [Test]
    public void RoundTripSimpleExpressionTest()
    {
        const string sql = "Price * Quantity";
        var expr = WitSql.ParseExpression(sql);
        var serialized = WitSqlExpressionSerializer.Serialize(expr);

        // Re-parse and compare structure (serialized may have extra parens)
        var reparsed = WitSql.ParseExpression(serialized);
        
        // Both should be binary multiply expressions
        var original = expr as WitSqlExpressionBinary;
        var parsed = reparsed as WitSqlExpressionBinary;
        
        Assert.That(original, Is.Not.Null);
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Operator, Is.EqualTo(original!.Operator));
    }

    [Test]
    public void RoundTripComplexExpressionTest()
    {
        const string sql = "CASE WHEN Status = 'active' THEN Price * 1.1 ELSE Price END";
        var expr = WitSql.ParseExpression(sql);
        var serialized = WitSqlExpressionSerializer.Serialize(expr);

        // Re-parse - should not throw
        var reparsed = WitSql.ParseExpression(serialized);
        Assert.That(reparsed, Is.Not.Null);
        Assert.That(reparsed, Is.InstanceOf<WitSqlExpressionCase>());
    }

    #endregion
}
