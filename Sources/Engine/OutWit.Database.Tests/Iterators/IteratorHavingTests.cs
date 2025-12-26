using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorHavingTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void HavingIteratorFiltersGroupsTest()
    {
        // Simulating aggregated results with COUNT column
        var source = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Count", WitSqlValue.FromInt(5))),
            CreateRow(("Category", WitSqlValue.FromText("B")), ("Count", WitSqlValue.FromInt(10))),
            CreateRow(("Category", WitSqlValue.FromText("C")), ("Count", WitSqlValue.FromInt(3)))
        );

        // HAVING Count > 4
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Count" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 4L }
        };

        var iterator = new IteratorHaving(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Category"].AsString(), Is.EqualTo("A"));
        Assert.That(rows[1]["Category"].AsString(), Is.EqualTo("B"));
    }

    [Test]
    public void HavingIteratorWithNoMatchesReturnsEmptyTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Count", WitSqlValue.FromInt(1))),
            CreateRow(("Count", WitSqlValue.FromInt(2)))
        );

        // HAVING Count > 100
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Count" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 100L }
        };

        var iterator = new IteratorHaving(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void HavingIteratorWithAllMatchesReturnsAllRowsTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Count", WitSqlValue.FromInt(10))),
            CreateRow(("Count", WitSqlValue.FromInt(20)))
        );

        // HAVING Count > 0
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Count" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 0L }
        };

        var iterator = new IteratorHaving(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void HavingIteratorPreservesSchemaTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Count", WitSqlValue.FromInt(5)))
        );
        var predicate = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true };

        var iterator = new IteratorHaving(source, predicate, m_context);

        Assert.That(iterator.Schema, Has.Count.EqualTo(2));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Category"));
        Assert.That(iterator.Schema[1].Name, Is.EqualTo("Count"));
    }

    [Test]
    public void HavingIteratorResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Count", WitSqlValue.FromInt(10))),
            CreateRow(("Count", WitSqlValue.FromInt(5))),
            CreateRow(("Count", WitSqlValue.FromInt(20)))
        );

        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Count" },
            Operator = BinaryOperatorType.GreaterThan,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 8L }
        };

        var iterator = new IteratorHaving(source, predicate, m_context);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
    }

    [Test]
    public void HavingIteratorWithComplexConditionTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Sum", WitSqlValue.FromInt(100)), ("Count", WitSqlValue.FromInt(10))),
            CreateRow(("Category", WitSqlValue.FromText("B")), ("Sum", WitSqlValue.FromInt(50)), ("Count", WitSqlValue.FromInt(5))),
            CreateRow(("Category", WitSqlValue.FromText("C")), ("Sum", WitSqlValue.FromInt(200)), ("Count", WitSqlValue.FromInt(8)))
        );

        // HAVING Sum > 80 AND Count > 7
        var predicate = new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionBinary
            {
                Left = new WitSqlExpressionColumnRef { ColumnName = "Sum" },
                Operator = BinaryOperatorType.GreaterThan,
                Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 80L }
            },
            Operator = BinaryOperatorType.And,
            Right = new WitSqlExpressionBinary
            {
                Left = new WitSqlExpressionColumnRef { ColumnName = "Count" },
                Operator = BinaryOperatorType.GreaterThan,
                Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 7L }
            }
        };

        var iterator = new IteratorHaving(source, predicate, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Category"].AsString(), Is.EqualTo("A"));
        Assert.That(rows[1]["Category"].AsString(), Is.EqualTo("C"));
    }

    #endregion
}
