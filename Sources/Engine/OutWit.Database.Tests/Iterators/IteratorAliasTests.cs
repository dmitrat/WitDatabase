using OutWit.Database.Iterators;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

[TestFixture]
public class IteratorAliasTests : IteratorTestsBase
{
    #region Basic Tests

    [Test]
    public void AliasIteratorChangesTableNameInSchemaTest()
    {
        var schema = new List<WitSqlColumnInfo>
        {
            new() { Name = "Id", Type = WitSqlType.Integer, TableName = "Users" },
            new() { Name = "Name", Type = WitSqlType.Text, TableName = "Users" }
        };
        var source = CreateMockIterator(schema, CreateRowWithInts(("Id", 1)));

        var iterator = new IteratorAlias(source, "u");

        Assert.That(iterator.Schema[0].TableName, Is.EqualTo("u"));
        Assert.That(iterator.Schema[1].TableName, Is.EqualTo("u"));
    }

    [Test]
    public void AliasIteratorPreservesColumnNamesTest()
    {
        var schema = new List<WitSqlColumnInfo>
        {
            new() { Name = "Id", Type = WitSqlType.Integer, TableName = "Users" },
            new() { Name = "Name", Type = WitSqlType.Text, TableName = "Users" }
        };
        var source = CreateMockIterator(schema, CreateRowWithInts(("Id", 1)));

        var iterator = new IteratorAlias(source, "u");

        Assert.That(iterator.Schema[0].Name, Is.EqualTo("Id"));
        Assert.That(iterator.Schema[1].Name, Is.EqualTo("Name"));
    }

    [Test]
    public void AliasIteratorPreservesColumnTypesTest()
    {
        var schema = new List<WitSqlColumnInfo>
        {
            new() { Name = "Id", Type = WitSqlType.Integer, TableName = "Users" },
            new() { Name = "Name", Type = WitSqlType.Text, TableName = "Users" }
        };
        var source = CreateMockIterator(schema, CreateRowWithInts(("Id", 1)));

        var iterator = new IteratorAlias(source, "u");

        Assert.That(iterator.Schema[0].Type, Is.EqualTo(WitSqlType.Integer));
        Assert.That(iterator.Schema[1].Type, Is.EqualTo(WitSqlType.Text));
    }

    [Test]
    public void AliasIteratorPassesThroughRowsTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2)),
            CreateRowWithInts(("Id", 3))
        );

        var iterator = new IteratorAlias(source, "u");
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["Id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void AliasIteratorWithEmptySourceReturnsEmptyTest()
    {
        var source = CreateMockIterator();

        var iterator = new IteratorAlias(source, "u");
        var rows = CollectAllRows(iterator);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void AliasIteratorResetWorksCorrectlyTest()
    {
        var source = CreateMockIterator(
            CreateRowWithInts(("Id", 1)),
            CreateRowWithInts(("Id", 2))
        );

        var iterator = new IteratorAlias(source, "u");

        var rows1 = CollectAllRows(iterator);
        Assert.That(rows1, Has.Count.EqualTo(2));

        iterator.Reset();
        var rows2 = CollectAllRows(iterator);
        Assert.That(rows2, Has.Count.EqualTo(2));
    }

    [Test]
    public void AliasIteratorPreservesNullabilityTest()
    {
        var schema = new List<WitSqlColumnInfo>
        {
            new() { Name = "Id", Type = WitSqlType.Integer, IsNullable = false, TableName = "Users" },
            new() { Name = "Name", Type = WitSqlType.Text, IsNullable = true, TableName = "Users" }
        };
        var source = CreateMockIterator(schema, CreateRowWithInts(("Id", 1)));

        var iterator = new IteratorAlias(source, "u");

        Assert.That(iterator.Schema[0].IsNullable, Is.False);
        Assert.That(iterator.Schema[1].IsNullable, Is.True);
    }

    #endregion
}
