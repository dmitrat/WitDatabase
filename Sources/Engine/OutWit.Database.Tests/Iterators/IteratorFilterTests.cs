using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorFilterTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void FilterIteratorFiltersRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3)),
            CreateRowWithInts(("Id", 4)),
            CreateRowWithInts(("Id", 5))
        );

        // Filter: Id > 2
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Id" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 2L }
        };

        var iterator = new IteratorFilter(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(3));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(4));
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(5));
    }

    [Test]
    public void FilterIteratorWithNoMatchesReturnsEmptyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        // Filter: Id > 100
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Id" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 100L }
        };

        var iterator = new IteratorFilter(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void FilterIteratorWithAllMatchesReturnsAllRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        // Filter: Id > 0
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Id" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 0L }
        };

        var iterator = new IteratorFilter(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void FilterIteratorWithTrueConstantReturnsAllRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        // Filter: TRUE
        var predicate = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true };

        var iterator = new IteratorFilter(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void FilterIteratorWithFalseConstantReturnsEmptyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        // Filter: FALSE
        var predicate = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = false };

        var iterator = new IteratorFilter(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void FilterIteratorPreservesSchemaTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 1)));
        var predicate = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true };

        var iterator = new IteratorFilter(source, predicate, m_context);

        Assert.That(iterator.Schema, Has.Count.EqualTo(1));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Id"));
    }

    [Test]
    public void FilterIteratorWithAndConditionTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)), ("Active", WitSqlValue.FromBool(true))),
            CreateRow(("Id", WitSqlValue.FromInt(2)), ("Active", WitSqlValue.FromBool(false))),
            CreateRow(("Id", WitSqlValue.FromInt(3)), ("Active", WitSqlValue.FromBool(true)))
        );

        // Filter: Id > 1 AND Active = TRUE
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionBinary
            {
                Left = new WitSqlExpressionColumnRef { ColumnName = "Id" },
                Operator = BinaryOperatorType.GreaterThan,
                Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L }
            },
            Operator = BinaryOperatorType.And,
            Right = new WitSqlExpressionBinary
            {
                Left = new WitSqlExpressionColumnRef { ColumnName = "Active" },
                Operator = BinaryOperatorType.Equal,
                Right = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true }
            }
        };

        var iterator = new IteratorFilter(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void FilterIteratorResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Id" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L }
        };

        var iterator = new IteratorFilter(source, predicate, m_context);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
    }

    #endregion
}
