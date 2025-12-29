using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Metadata;

/// <summary>
/// Unit tests for WitAnnotationProvider.
/// </summary>
[TestFixture]
public class WitAnnotationProviderTests
{
    #region AutoIncrement Tests

    [Test]
    public void AutoIncrementPropertyIsRecognizedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithAutoIncrement));
        var idProperty = entityType?.FindProperty(nameof(EntityWithAutoIncrement.Id));

        Assert.That(idProperty, Is.Not.Null);
        Assert.That(idProperty!.ValueGenerated, Is.EqualTo(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd));
    }

    [Test]
    public void NonAutoIncrementPropertyIsNotMarkedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithAutoIncrement));
        var nameProperty = entityType?.FindProperty(nameof(EntityWithAutoIncrement.Name));

        Assert.That(nameProperty, Is.Not.Null);
        Assert.That(nameProperty!.ValueGenerated, Is.EqualTo(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never));
    }

    #endregion

    #region Column Mapping Tests

    [Test]
    public void IntColumnHasCorrectTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithTypes));
        var intProperty = entityType?.FindProperty(nameof(EntityWithTypes.IntValue));

        Assert.That(intProperty, Is.Not.Null);
        Assert.That(intProperty!.GetColumnType(), Is.EqualTo("INT"));
    }

    [Test]
    public void StringColumnHasCorrectTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithTypes));
        var stringProperty = entityType?.FindProperty(nameof(EntityWithTypes.StringValue));

        Assert.That(stringProperty, Is.Not.Null);
        Assert.That(stringProperty!.GetColumnType(), Is.EqualTo("TEXT"));
    }

    [Test]
    public void BoolColumnHasCorrectTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithTypes));
        var boolProperty = entityType?.FindProperty(nameof(EntityWithTypes.BoolValue));

        Assert.That(boolProperty, Is.Not.Null);
        Assert.That(boolProperty!.GetColumnType(), Is.EqualTo("BOOLEAN"));
    }

    [Test]
    public void DecimalColumnHasCorrectTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithTypes));
        var decimalProperty = entityType?.FindProperty(nameof(EntityWithTypes.DecimalValue));

        Assert.That(decimalProperty, Is.Not.Null);
        Assert.That(decimalProperty!.GetColumnType(), Is.EqualTo("DECIMAL"));
    }

    [Test]
    public void GuidColumnHasCorrectTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithTypes));
        var guidProperty = entityType?.FindProperty(nameof(EntityWithTypes.GuidValue));

        Assert.That(guidProperty, Is.Not.Null);
        Assert.That(guidProperty!.GetColumnType(), Is.EqualTo("GUID"));
    }

    [Test]
    public void DateTimeColumnHasCorrectTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithTypes));
        var dateTimeProperty = entityType?.FindProperty(nameof(EntityWithTypes.DateTimeValue));

        Assert.That(dateTimeProperty, Is.Not.Null);
        Assert.That(dateTimeProperty!.GetColumnType(), Is.EqualTo("DATETIME"));
    }

    #endregion

    #region Test Models

    public class EntityWithAutoIncrement
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class EntityWithTypes
    {
        public int Id { get; set; }
        public int IntValue { get; set; }
        public string? StringValue { get; set; }
        public bool BoolValue { get; set; }
        public decimal DecimalValue { get; set; }
        public Guid GuidValue { get; set; }
        public DateTime DateTimeValue { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<EntityWithAutoIncrement> AutoIncrementEntities => Set<EntityWithAutoIncrement>();
        public DbSet<EntityWithTypes> TypedEntities => Set<EntityWithTypes>();
    }

    #endregion
}
