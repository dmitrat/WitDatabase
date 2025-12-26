using OutWit.Database.Iterators;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorDistinctTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void DistinctIteratorRemovesDuplicatesTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 3)),
            CreateRowWithInts(("Id", 2))
        );

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void DistinctIteratorWithNoDuplicatesReturnsAllRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void DistinctIteratorWithAllDuplicatesReturnsOneRowTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 1))
        );

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void DistinctIteratorWithEmptySourceReturnsEmptyTest()
    {
        var source = CreateMockIterator();

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void DistinctIteratorComparesAllColumnsTest()
    {
        var source = CreateMockIterator(
            CreateRow(("A", WitSqlValue.FromInt(1)), ("B", WitSqlValue.FromInt(1))),
            CreateRow(("A", WitSqlValue.FromInt(1)), ("B", WitSqlValue.FromInt(2))),
            CreateRow(("A", WitSqlValue.FromInt(1)), ("B", WitSqlValue.FromInt(1)))
        );

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void DistinctIteratorHandlesNullValuesTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.Null)),
            CreateRow(("Id", WitSqlValue.FromInt(1))),
            CreateRow(("Id", WitSqlValue.Null)),
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void DistinctIteratorResetClearsSeenRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 1))
        );

        var iterator = new IteratorDistinct(source);
        
        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(1));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(1));
    }

    [Test]
    public void DistinctIteratorPreservesSchemaTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 1)));
        var iterator = new IteratorDistinct(source);

        Assert.That(iterator.Schema, Has.Count.EqualTo(1));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Id"));
    }

    [Test]
    public void DistinctIteratorWithDifferentTypesTest()
    {
        var source = CreateMockIterator(
            CreateRow(("Val", WitSqlValue.FromText("hello"))),
            CreateRow(("Val", WitSqlValue.FromText("HELLO"))),
            CreateRow(("Val", WitSqlValue.FromText("hello")))
        );

        var iterator = new IteratorDistinct(source);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    #endregion
}
