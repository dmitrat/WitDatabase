using OutWit.Database.Iterators;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorSingleRowTests
{
    #region Basic Tests

    [Test]
    public void SingleRowIteratorReturnsOneRowTest()
    {
        var values = new[] { WitSqlValue.FromInt(42) };
        var names = new[] { "Value" };
        var iterator = new IteratorSingleRow(values, names);
        iterator.Open();

        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.MoveNext(), Is.False);
    }

    [Test]
    public void SingleRowIteratorReturnsCorrectValueTest()
    {
        var values = new[] { WitSqlValue.FromInt(42), WitSqlValue.FromText("Hello") };
        var names = new[] { "Number", "Text" };
        var iterator = new IteratorSingleRow(values, names);
        iterator.Open();
        iterator.MoveNext();

        Assert.That(iterator.Current["Number"].AsInt64(), Is.EqualTo(42));
        Assert.That(iterator.Current["Text"].AsString(), Is.EqualTo("Hello"));
    }

    [Test]
    public void SingleRowIteratorHasCorrectSchemaTest()
    {
        var values = new[] { WitSqlValue.FromInt(42), WitSqlValue.FromText("Hello") };
        var names = new[] { "Number", "Text" };
        var iterator = new IteratorSingleRow(values, names);

        Assert.That(iterator.Schema, Has.Count.EqualTo(2));
        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Number"));
        Assert.That(iterator.Schema[0].Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(iterator.Schema[1].Name, Is.EqualTo("Text"));
        Assert.That(iterator.Schema[1].Type, Is.EqualTo(WitSqlType.Text));
    }

    [Test]
    public void SingleRowIteratorEstimatedRowCountIsOneTest()
    {
        var values = new[] { WitSqlValue.FromInt(42) };
        var names = new[] { "Value" };
        var iterator = new IteratorSingleRow(values, names);

        Assert.That(iterator.EstimatedRowCount, Is.EqualTo(1));
    }

    [Test]
    public void SingleRowIteratorCanBeResetTest()
    {
        var values = new[] { WitSqlValue.FromInt(42) };
        var names = new[] { "Value" };
        var iterator = new IteratorSingleRow(values, names);
        
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.MoveNext(), Is.False);

        iterator.Reset();
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["Value"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void SingleRowIteratorWithNullValueTest()
    {
        var values = new[] { WitSqlValue.Null };
        var names = new[] { "Value" };
        var iterator = new IteratorSingleRow(values, names);
        iterator.Open();
        iterator.MoveNext();

        Assert.That(iterator.Current["Value"].IsNull, Is.True);
    }

    [Test]
    public void SingleRowIteratorWithMultipleValuesTest()
    {
        var values = new[]
        {
            WitSqlValue.FromInt(1),
            WitSqlValue.FromReal(3.14),
            WitSqlValue.FromBool(true),
            WitSqlValue.FromText("test")
        };
        var names = new[] { "Int", "Real", "Bool", "Text" };
        var iterator = new IteratorSingleRow(values, names);
        iterator.Open();
        iterator.MoveNext();

        Assert.That(iterator.Current["Int"].AsInt64(), Is.EqualTo(1));
        Assert.That(iterator.Current["Real"].AsDouble(), Is.EqualTo(3.14));
        Assert.That(iterator.Current["Bool"].AsBool(), Is.True);
        Assert.That(iterator.Current["Text"].AsString(), Is.EqualTo("test"));
    }

    #endregion
}
