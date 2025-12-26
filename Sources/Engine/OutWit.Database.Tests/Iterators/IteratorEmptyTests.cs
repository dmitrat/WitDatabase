using OutWit.Database.Iterators;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorEmptyTests
{
    #region Basic Tests

    [Test]
    public void EmptyIteratorReturnsNoRowsTest()
    {
        var iterator = new IteratorEmpty();
        iterator.Open();

        Assert.That(iterator.MoveNext(), Is.False);
    }

    [Test]
    public void EmptyIteratorHasEmptySchemaByDefaultTest()
    {
        var iterator = new IteratorEmpty();

        Assert.That(iterator.Schema, Is.Empty);
    }

    [Test]
    public void EmptyIteratorWithSchemaPreservesSchemaTest()
    {
        var schema = new List<WitSqlColumnInfo>
        {
            new() { Name = "Id", Type = WitSqlType.Integer },
            new() { Name = "Name", Type = WitSqlType.Text }
        };

        var iterator = new IteratorEmpty(schema);

        Assert.That(iterator.Schema, Has.Count.EqualTo(2));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Id"));
        Assert.That(iterator.Schema[1].Name, Is.EqualTo("Name"));
    }

    [Test]
    public void EmptyIteratorEstimatedRowCountIsZeroTest()
    {
        var iterator = new IteratorEmpty();

        Assert.That(iterator.EstimatedRowCount, Is.EqualTo(0));
    }

    [Test]
    public void EmptyIteratorCurrentThrowsInvalidOperationExceptionTest()
    {
        var iterator = new IteratorEmpty();
        iterator.Open();

        Assert.Throws<InvalidOperationException>(() => _ = iterator.Current);
    }

    [Test]
    public void EmptyIteratorMoveNextAfterOpenReturnsFalseTest()
    {
        var iterator = new IteratorEmpty();
        iterator.Open();

        Assert.That(iterator.MoveNext(), Is.False);
        Assert.That(iterator.MoveNext(), Is.False);
    }

    #endregion
}
