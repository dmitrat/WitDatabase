using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorGroupByTests : IteratorTestsBase
{
    #region COUNT Tests

    [Test]
    public void GroupByCountAllRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall { FunctionName = "COUNT", IsStar = true },
                Alias = "Total"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Total"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void GroupByCountByGroupTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Value", WitSqlValue.FromInt(1))),
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Value", WitSqlValue.FromInt(2))),
            CreateRow(("Category", WitSqlValue.FromText("B")), ("Value", WitSqlValue.FromInt(3)))
        );

        var groupBy = new List<WitSqlExpression>
        {
            new WitSqlExpressionColumnRef { ColumnName = "Category" }
        };

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Category" } },
            new()
            {
                Expression = new WitSqlExpressionFunctionCall { FunctionName = "COUNT", IsStar = true },
                Alias = "Count"
            }
        };

        var iterator = new IteratorGroupBy(source, groupBy, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        
        var categoryA = rows.FirstOrDefault(r => r["Category"].AsString() == "A");
        var categoryB = rows.FirstOrDefault(r => r["Category"].AsString() == "B");
        
        Assert.That(categoryA["Count"].AsInt64(), Is.EqualTo(2));
        Assert.That(categoryB["Count"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void GroupByCountDistinctTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Value", WitSqlValue.FromInt(1))),
            CreateRow(("Value", WitSqlValue.FromInt(1))),
            CreateRow(("Value", WitSqlValue.FromInt(2))),
            CreateRow(("Value", WitSqlValue.FromInt(2))),
            CreateRow(("Value", WitSqlValue.FromInt(3)))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "COUNT",
                    IsDistinct = true,
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "DistinctCount"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["DistinctCount"].AsInt64(), Is.EqualTo(3));
    }

    #endregion

    #region SUM Tests

    [Test]
    public void GroupBySumTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Value", 10)),
            CreateRowWithInts(("Value", 20)),
            CreateRowWithInts(("Value", 30))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "SUM",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Total"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Total"].AsInt64(), Is.EqualTo(60));
    }

    [Test]
    public void GroupBySumByGroupTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Value", WitSqlValue.FromInt(10))),
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Value", WitSqlValue.FromInt(20))),
            CreateRow(("Category", WitSqlValue.FromText("B")), ("Value", WitSqlValue.FromInt(100)))
        );

        var groupBy = new List<WitSqlExpression>
        {
            new WitSqlExpressionColumnRef { ColumnName = "Category" }
        };

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Category" } },
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "SUM",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Sum"
            }
        };

        var iterator = new IteratorGroupBy(source, groupBy, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        
        var categoryA = rows.FirstOrDefault(r => r["Category"].AsString() == "A");
        var categoryB = rows.FirstOrDefault(r => r["Category"].AsString() == "B");
        
        Assert.That(categoryA["Sum"].AsInt64(), Is.EqualTo(30));
        Assert.That(categoryB["Sum"].AsInt64(), Is.EqualTo(100));
    }

    #endregion

    #region AVG Tests

    [Test]
    public void GroupByAvgTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Value", 10)),
            CreateRowWithInts(("Value", 20)),
            CreateRowWithInts(("Value", 30))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "AVG",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Average"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Average"].AsDouble(), Is.EqualTo(20.0));
    }

    #endregion

    #region MIN/MAX Tests

    [Test]
    public void GroupByMinMaxTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Value", 30)),
            CreateRowWithInts(("Value", 10)),
            CreateRowWithInts(("Value", 20))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "MIN",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "MinValue"
            },
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "MAX",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "MaxValue"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["MinValue"].AsInt64(), Is.EqualTo(10));
        Assert.That(rows[0]["MaxValue"].AsInt64(), Is.EqualTo(30));
    }

    #endregion

    #region GROUP_CONCAT Tests

    [Test]
    public void GroupByGroupConcatTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Name", WitSqlValue.FromText("Alice"))),
            CreateRow(("Name", WitSqlValue.FromText("Bob"))),
            CreateRow(("Name", WitSqlValue.FromText("Charlie")))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "GROUP_CONCAT",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Name" }]
                },
                Alias = "Names"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        var names = rows[0]["Names"].AsString();
        Assert.That(names, Does.Contain("Alice"));
        Assert.That(names, Does.Contain("Bob"));
        Assert.That(names, Does.Contain("Charlie"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void GroupByEmptySourceReturnsOneRowForAggregatesTest()
    {
        var source = CreateMockIterator();

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall { FunctionName = "COUNT", IsStar = true },
                Alias = "Count"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Count"].AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void GroupByWithNullValuesTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Value", WitSqlValue.FromInt(10))),
            CreateRow(("Value", WitSqlValue.Null)),
            CreateRow(("Value", WitSqlValue.FromInt(20)))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "COUNT",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Count"
            },
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "SUM",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Sum"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Count"].AsInt64(), Is.EqualTo(2)); // NULL not counted
        Assert.That(rows[0]["Sum"].AsInt64(), Is.EqualTo(30)); // NULL not summed
    }

    [Test]
    public void GroupBySchemaHasCorrectTypesTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Value", 1)));

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall { FunctionName = "COUNT", IsStar = true },
                Alias = "Count"
            },
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "SUM",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Sum"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);

        Assert.That(iterator.Schema[0].Type, Is.EqualTo(WitSqlType.Integer)); // COUNT returns integer
        Assert.That(iterator.Schema[1].Type, Is.EqualTo(WitSqlType.Real)); // SUM returns real
    }

    [Test]
    public void GroupByResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Value", 10)),
            CreateRowWithInts(("Value", 20))
        );

        var selectList = new List<ClauseSelectItem>
        {
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "SUM",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                },
                Alias = "Sum"
            }
        };

        var iterator = new IteratorGroupBy(source, null, selectList, m_context);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1[0]["Sum"].AsInt64(), Is.EqualTo(30));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2[0]["Sum"].AsInt64(), Is.EqualTo(30));
    }

    [Test]
    public void GroupByMultipleGroupColumnsTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Year", WitSqlValue.FromInt(2024)), ("Month", WitSqlValue.FromInt(1)), ("Sales", WitSqlValue.FromInt(100))),
            CreateRow(("Year", WitSqlValue.FromInt(2024)), ("Month", WitSqlValue.FromInt(1)), ("Sales", WitSqlValue.FromInt(200))),
            CreateRow(("Year", WitSqlValue.FromInt(2024)), ("Month", WitSqlValue.FromInt(2)), ("Sales", WitSqlValue.FromInt(150)))
        );

        var groupBy = new List<WitSqlExpression>
        {
            new WitSqlExpressionColumnRef { ColumnName = "Year" },
            new WitSqlExpressionColumnRef { ColumnName = "Month" }
        };

        var selectList = new List<ClauseSelectItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Year" } },
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Month" } },
            new()
            {
                Expression = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "SUM",
                    Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Sales" }]
                },
                Alias = "TotalSales"
            }
        };

        var iterator = new IteratorGroupBy(source, groupBy, selectList, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        
        var jan = rows.FirstOrDefault(r => r["Month"].AsInt64() == 1);
        var feb = rows.FirstOrDefault(r => r["Month"].AsInt64() == 2);
        
        Assert.That(jan["TotalSales"].AsInt64(), Is.EqualTo(300));
        Assert.That(feb["TotalSales"].AsInt64(), Is.EqualTo(150));
    }

    #endregion
}
