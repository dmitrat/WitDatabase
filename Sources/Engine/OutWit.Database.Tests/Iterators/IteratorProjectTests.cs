using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorProjectTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void ProjectIteratorProjectsColumnsTest()
    {
        var source = CreateMockIterator(
            CreateRow(("A", WitSqlValue.FromInt(1)), ("B", WitSqlValue.FromInt(2)), ("C", WitSqlValue.FromInt(3)))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "A" } },
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "C" } }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ColumnCount, Is.EqualTo(2));
        Assert.That(rows[0]["A"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["C"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void ProjectIteratorUsesAliasTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 42))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new() 
            { 
                Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" },
                Alias = "MyId"
            }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("MyId"));
        Assert.That(rows[0]["MyId"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void ProjectIteratorEvaluatesExpressionsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("A", 10), ("B", 5))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionBinary
                {
                    Left = new WitSqlExpressionColumnRef { ColumnName = "A" },
                    Operator = BinaryOperatorType.Add,
                    Right = new WitSqlExpressionColumnRef { ColumnName = "B" }
                },
                Alias = "Sum"
            }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Sum"].AsInt64(), Is.EqualTo(15));
    }

    [Test]
    public void ProjectIteratorWithLiteralExpressionTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 42L },
                Alias = "Constant"
            }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Constant"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void ProjectIteratorPreservesRowCountTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" } }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void ProjectIteratorSchemaHasCorrectTypesTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 42L },
                Alias = "IntCol"
            },
            new()
            {
                Expression = new WitSqlExpressionLiteral { Type = LiteralType.String, Value = "hello" },
                Alias = "TextCol"
            },
            new()
            {
                Expression = new WitSqlExpressionLiteral { Type = LiteralType.Boolean, Value = true },
                Alias = "BoolCol"
            }
        };

        var iterator = new IteratorProject(source, selectList, m_context);

        Assert.That(iterator.Schema, Has.Count.EqualTo(3));
        Assert.That(iterator.Schema[0].Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(iterator.Schema[1].Type, Is.EqualTo(WitSqlType.Text));
        Assert.That(iterator.Schema[2].Type, Is.EqualTo(WitSqlType.Boolean));
    }

    [Test]
    public void ProjectIteratorWithEmptySourceReturnsEmptyTest()
    {
        var source = CreateMockIterator();

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L } }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void ProjectIteratorWithNullExpressionReturnsNullTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = null, Alias = "NullCol" }
        };

        var iterator = new IteratorProject(source, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["NullCol"].IsNull, Is.True);
    }

    [Test]
    public void ProjectIteratorGeneratesColumnNamesTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionBinary
                {
                    Left = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L },
                    Operator = BinaryOperatorType.Add,
                    Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L }
                }
            }
        };

        var iterator = new IteratorProject(source, selectList, m_context);

        Assert.That(iterator.Schema[0].Name, Is.EqualTo("column0"));
    }

    [Test]
    public void ProjectIteratorResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" } }
        };

        var iterator = new IteratorProject(source, selectList, m_context);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
    }

    #endregion
}
