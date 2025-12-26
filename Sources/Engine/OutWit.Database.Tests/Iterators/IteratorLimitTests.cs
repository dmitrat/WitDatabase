using OutWit.Database.Iterators;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorLimitTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void LimitIteratorLimitsRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3)),
            CreateRowWithInts(("Id", 4)),
            CreateRowWithInts(("Id", 5))
        );

        var iterator = new IteratorLimit(source, limit: 3);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void LimitIteratorWithOffsetSkipsRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3)),
            CreateRowWithInts(("Id", 4)),
            CreateRowWithInts(("Id", 5))
        );

        var iterator = new IteratorLimit(source, limit: 2, offset: 2);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(3));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(4));
    }

    [Test]
    public void LimitIteratorWithOffsetExceedingSourceReturnsEmptyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var iterator = new IteratorLimit(source, limit: 10, offset: 10);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void LimitIteratorWithZeroLimitReturnsEmptyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var iterator = new IteratorLimit(source, limit: 0);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void LimitIteratorPreservesSchemaTest()
    {
        var source = CreateMockIterator(CreateRowWithInts(("Id", 1)));
        var iterator = new IteratorLimit(source, limit: 10);

        Assert.That(iterator.Schema, Has.Count.EqualTo(1));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Id"));
    }

    [Test]
    public void LimitIteratorWithSourceSmallerThanLimitReturnsAllRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var iterator = new IteratorLimit(source, limit: 100);
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void LimitIteratorResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var iterator = new IteratorLimit(source, limit: 2);

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
    }

    #endregion
}
