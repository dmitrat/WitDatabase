using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorSortTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void SortIteratorSortsAscendingTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 3)),
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void SortIteratorSortsDescendingTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 3)),
            CreateRowWithInts(("Id", 2))
        );

        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = true }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(3));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void SortIteratorWithMultipleColumnsTest()
    {
        var source = CreateMockIterator(
            CreateRow(("A", WitSqlValue.FromInt(1)), ("B", WitSqlValue.FromInt(2))),
            CreateRow(("A", WitSqlValue.FromInt(1)), ("B", WitSqlValue.FromInt(1))),
            CreateRow(("A", WitSqlValue.FromInt(2)), ("B", WitSqlValue.FromInt(1)))
        );

        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "A" }, Descending = false },
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "B" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["A"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0]["B"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["A"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["B"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["A"].AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void SortIteratorWithEmptySourceReturnsEmptyTest()
    {
        var source = CreateMockIterator();
        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void SortIteratorWithSingleRowReturnsSingleRowTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 42)));
        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void SortIteratorPreservesSchemaTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 1)));
        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);

        Assert.That(iterator.Schema, Has.Count.EqualTo(1));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Id"));
    }

    [Test]
    public void SortIteratorWithStringsTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Name", WitSqlValue.FromText("Charlie"))),
            CreateRow(("Name", WitSqlValue.FromText("Alice"))),
            CreateRow(("Name", WitSqlValue.FromText("Bob")))
        );

        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Name" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void SortIteratorCurrentThrowsBeforeMoveNextTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 1)));
        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);
        iterator.Open();

        Assert.Throws<InvalidOperationException>(() => _ = iterator.Current);
    }

    [Test]
    public void SortIteratorMoveNextThrowsBeforeOpenTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 1)));
        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);

        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
    }

    [Test]
    public void SortIteratorResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 1))
        );

        var orderBy = new List<ClauseOrderByItem>
        {
            new() { Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" }, Descending = false }
        };

        var iterator = new IteratorSort(source, orderBy, m_context);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));
        Assert.That(rows1[0]["Id"].AsInt64(), Is.EqualTo(1));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
        Assert.That(rows2[0]["Id"].AsInt64(), Is.EqualTo(1));
    }

    #endregion
}
