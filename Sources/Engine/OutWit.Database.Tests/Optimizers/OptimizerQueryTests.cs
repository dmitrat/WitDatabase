using OutWit.Database.Definitions;
using OutWit.Database.Model;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// Unit tests for QueryOptimizer predicate extraction and index selection.
/// </summary>
[TestFixture]
public sealed class OptimizerQueryTests
{
    #region Fields

    private OptimizerQuery m_optimizer = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_optimizer = new OptimizerQuery();
    }

    #endregion

    #region Index Selection - Equality Tests

    [Test]
    public void FindBestIndexWithEqualityPredicateTest()
    {
        // Arrange
        var whereClause = CreateEqualityExpression("Name", "Alice");
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_name", "Users", ["Name"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.IndexName, Is.EqualTo("idx_name"));
        Assert.That(strategy.AccessType, Is.EqualTo(IndexAccessType.Seek));
    }

    [Test]
    public void FindBestIndexWithUniqueIndexPreferredTest()
    {
        // Arrange
        var whereClause = CreateEqualityExpression("Email", "alice@test.com");
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_email", "Users", ["Email"], isUnique: true),
            CreateIndex("idx_email_nonunique", "Users", ["Email"], isUnique: false)
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        // Unique index should be preferred (lower cost)
        Assert.That(strategy!.IndexName, Is.EqualTo("idx_email"));
        Assert.That(strategy.EstimatedRowsReturned, Is.EqualTo(1));
    }

    [Test]
    public void FindBestIndexReturnsNullWithNoMatchingIndexTest()
    {
        // Arrange
        var whereClause = CreateEqualityExpression("Age", 25);
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_name", "Users", ["Name"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Null);
    }

    [Test]
    public void FindBestIndexReturnsNullWithNullWhereClauseTest()
    {
        // Arrange
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_name", "Users", ["Name"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", null, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Null);
    }

    #endregion

    #region Index Selection - Range Tests

    [Test]
    public void FindBestIndexWithLessThanPredicateTest()
    {
        // Arrange
        var whereClause = CreateComparisonExpression("Price", BinaryOperatorType.LessThan, 100);
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_price", "Products", ["Price"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Products", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.RangeScan));
        Assert.That(strategy.RangeEnd, Is.Not.Null);
        Assert.That(strategy.RangeEndInclusive, Is.False);
    }

    [Test]
    public void FindBestIndexWithGreaterOrEqualPredicateTest()
    {
        // Arrange
        var whereClause = CreateComparisonExpression("Price", BinaryOperatorType.GreaterOrEqual, 50);
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_price", "Products", ["Price"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Products", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.RangeScan));
        Assert.That(strategy.RangeStart, Is.Not.Null);
        Assert.That(strategy.RangeStartInclusive, Is.True);
    }

    [Test]
    public void FindBestIndexWithBetweenPredicateTest()
    {
        // Arrange - BETWEEN creates two range predicates with AND
        var between = new WitSqlExpressionBetween
        {
            Expression = new WitSqlExpressionColumnRef { ColumnName = "Price" },
            Low = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 10L },
            High = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 50L },
            IsNot = false
        };
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_price", "Products", ["Price"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Products", between, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.RangeScan));
        Assert.That(strategy.RangeStart, Is.Not.Null);
        Assert.That(strategy.RangeEnd, Is.Not.Null);
    }

    #endregion

    #region Index Selection - AND Predicates Tests

    [Test]
    public void FindBestIndexWithAndPredicatesUsesFirstIndexableTest()
    {
        // Arrange - col1 = 'A' AND col2 = 'B'
        var whereClause = new WitSqlExpressionBinary
        {
            Left = CreateEqualityExpression("Name", "Alice"),
            Operator = BinaryOperatorType.And,
            Right = CreateEqualityExpression("Email", "alice@test.com")
        };
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_name", "Users", ["Name"]),
            CreateIndex("idx_email", "Users", ["Email"], isUnique: true)
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        // Unique index should be chosen since it has lower cost (returns 1 row vs ~10 rows)
        // Both indexes match predicates, but unique index has cost ~10 while non-unique has ~10 as well
        // for seeks. The optimizer should pick unique index since EstimatedRowsReturned = 1
        // Actually, idx_name is evaluated first and its cost is lower than table scan,
        // so it gets picked. The optimizer picks first index that beats table scan.
        // The correct behavior is to use the unique index as it has lower estimated rows.
        Assert.That(strategy!.IndexName, Is.EqualTo("idx_email"));
    }

    [Test]
    public void FindBestIndexWithAndExtractsBothBoundsTest()
    {
        // Arrange - Price > 10 AND Price < 100
        var whereClause = new WitSqlExpressionBinary
        {
            Left = CreateComparisonExpression("Price", BinaryOperatorType.GreaterThan, 10),
            Operator = BinaryOperatorType.And,
            Right = CreateComparisonExpression("Price", BinaryOperatorType.LessThan, 100)
        };
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_price", "Products", ["Price"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Products", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.RangeScan));
        // Should have both bounds set
        Assert.That(strategy.RangeStart, Is.Not.Null);
        Assert.That(strategy.RangeEnd, Is.Not.Null);
    }

    #endregion

    #region Index Selection - Primary Key Skip Tests

    [Test]
    public void FindBestIndexSkipsPrimaryKeyIndexTest()
    {
        // Arrange
        var whereClause = CreateEqualityExpression("Id", 1);
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("pk_users", "Users", ["Id"], isPrimaryKey: true),
            CreateIndex("idx_id", "Users", ["Id"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.IndexName, Is.EqualTo("idx_id"));
    }

    #endregion

    #region Index Selection - Cost Based Tests

    [Test]
    public void FindBestIndexChoosesLowerCostIndexTest()
    {
        // Arrange
        var whereClause = CreateEqualityExpression("Status", "active");
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_status_unique", "Users", ["Status"], isUnique: true),
            CreateIndex("idx_status", "Users", ["Status"], isUnique: false)
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        // Unique index returns 1 row, non-unique returns more
        Assert.That(strategy!.IndexName, Is.EqualTo("idx_status_unique"));
    }

    #endregion

    #region Index Selection - IN Clause Tests

    [Test]
    public void FindBestIndexWithSingleValueInClauseTest()
    {
        // Arrange - IN with single value is like equality
        var inExpr = new WitSqlExpressionIn
        {
            Expression = new WitSqlExpressionColumnRef { ColumnName = "Status" },
            Values = [new WitSqlExpressionLiteral { Type = LiteralType.String, Value = "active" }],
            IsNot = false
        };
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_status", "Users", ["Status"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Users", inExpr, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.Seek));
    }

    #endregion

    #region Index Selection - Column on Right Side Tests

    [Test]
    public void FindBestIndexWithColumnOnRightSideTest()
    {
        // Arrange - '100' = Price (column on right side)
        var whereClause = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 100L },
            Operator = BinaryOperatorType.Equal,
            Right = new WitSqlExpressionColumnRef { ColumnName = "Price" }
        };
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_price", "Products", ["Price"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Products", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.Seek));
    }

    [Test]
    public void FindBestIndexWithColumnOnRightSideRangeTest()
    {
        // Arrange - 100 > Price means Price < 100
        var whereClause = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 100L },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionColumnRef { ColumnName = "Price" }
        };
        var indexes = new List<DefinitionIndex>
        {
            CreateIndex("idx_price", "Products", ["Price"])
        };

        // Act
        var strategy = m_optimizer.FindBestIndex("Products", whereClause, indexes, 1000);

        // Assert
        Assert.That(strategy, Is.Not.Null);
        Assert.That(strategy!.AccessType, Is.EqualTo(IndexAccessType.RangeScan));
        // Operator should be flipped: 100 > Price becomes Price < 100
        Assert.That(strategy.RangeEnd, Is.Not.Null);
    }

    #endregion

    #region Helper Methods

    private static WitSqlExpression CreateEqualityExpression(string columnName, object value)
    {
        return new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = columnName },
            Operator = BinaryOperatorType.Equal,
            Right = CreateLiteral(value)
        };
    }

    private static WitSqlExpression CreateComparisonExpression(string columnName, BinaryOperatorType op, object value)
    {
        return new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = columnName },
            Operator = op,
            Right = CreateLiteral(value)
        };
    }

    private static WitSqlExpressionLiteral CreateLiteral(object value)
    {
        return value switch
        {
            string s => new WitSqlExpressionLiteral { Type = LiteralType.String, Value = s },
            int i => new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = (long)i },
            long l => new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = l },
            double d => new WitSqlExpressionLiteral { Type = LiteralType.Real, Value = d },
            bool b => new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = b },
            _ => throw new ArgumentException($"Unsupported literal type: {value.GetType()}")
        };
    }

    private static DefinitionIndex CreateIndex(
        string name, 
        string tableName, 
        IReadOnlyList<string> columns,
        bool isUnique = false,
        bool isPrimaryKey = false)
    {
        return new DefinitionIndex
        {
            Name = name,
            TableName = tableName,
            Columns = columns,
            IsUnique = isUnique,
            IsPrimaryKey = isPrimaryKey
        };
    }

    #endregion
}
