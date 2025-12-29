using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Extensions;

/// <summary>
/// Unit tests for <see cref="WitPropertyBuilderExtensions"/>.
/// </summary>
[TestFixture]
public class WitPropertyBuilderExtensionsTests
{
    #region Row Version Tests

    [Test]
    public void IsWitRowVersionConfiguresConcurrencyTokenTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithRowVersion));
        var versionProperty = entityType?.FindProperty(nameof(EntityWithRowVersion.Version));

        Assert.That(versionProperty, Is.Not.Null);
        Assert.That(versionProperty!.IsConcurrencyToken, Is.True);
    }

    [Test]
    public void IsWitRowVersionConfiguresValueGeneratedOnAddOrUpdateTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithRowVersion));
        var versionProperty = entityType?.FindProperty(nameof(EntityWithRowVersion.Version));

        Assert.That(versionProperty, Is.Not.Null);
        Assert.That(versionProperty!.ValueGenerated, Is.EqualTo(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate));
    }

    [Test]
    public void IsWitRowVersionSetsDefaultValueTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithRowVersion));
        var versionProperty = entityType?.FindProperty(nameof(EntityWithRowVersion.Version));

        Assert.That(versionProperty, Is.Not.Null);
        Assert.That(versionProperty!.GetDefaultValue(), Is.EqualTo(1));
    }

    #endregion

    #region Computed Column Tests

    [Test]
    public void HasWitComputedColumnSqlConfiguresComputedColumnTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithComputedColumn));
        var computedProperty = entityType?.FindProperty(nameof(EntityWithComputedColumn.FullName));

        Assert.That(computedProperty, Is.Not.Null);
        Assert.That(computedProperty!.GetComputedColumnSql(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void HasWitComputedColumnSqlWithStoredConfiguresStoredColumnTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithComputedColumn));
        var computedProperty = entityType?.FindProperty(nameof(EntityWithComputedColumn.FullName));

        Assert.That(computedProperty, Is.Not.Null);
        Assert.That(computedProperty!.GetIsStored(), Is.True);
    }

    #endregion

    #region Concurrency Token Tests

    [Test]
    public void ConcurrencyTokenPropertyIsConfiguredTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithConcurrencyToken));
        var tokenProperty = entityType?.FindProperty(nameof(EntityWithConcurrencyToken.ConcurrencyStamp));

        Assert.That(tokenProperty, Is.Not.Null);
        Assert.That(tokenProperty!.IsConcurrencyToken, Is.True);
    }

    #endregion

    #region JSON Column Tests

    [Test]
    public void HasJsonColumnTypeConfiguresJsonTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<JsonTestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new JsonTestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithJsonColumn));
        var jsonProperty = entityType?.FindProperty(nameof(EntityWithJsonColumn.JsonData));

        Assert.That(jsonProperty, Is.Not.Null);
        Assert.That(jsonProperty!.GetColumnType(), Is.EqualTo("JSON"));
    }

    #endregion

    #region Enum Conversion Tests

    [Test]
    public void EnumDefaultsToIntTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EnumTestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new EnumTestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithEnum));
        var enumProperty = entityType?.FindProperty(nameof(EntityWithEnum.Status));

        Assert.That(enumProperty, Is.Not.Null);
        Assert.That(enumProperty!.GetColumnType(), Is.EqualTo("INT"));
    }

    [Test]
    public void HasEnumToStringConversionConfiguresTextTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EnumTestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new EnumTestDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(EntityWithEnumAsString));
        var enumProperty = entityType?.FindProperty(nameof(EntityWithEnumAsString.Status));

        Assert.That(enumProperty, Is.Not.Null);
        Assert.That(enumProperty!.GetColumnType(), Is.EqualTo("TEXT"));
    }

    #endregion

    #region Test Models

    public class EntityWithRowVersion
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public int Version { get; set; }
    }

    public class EntityWithComputedColumn
    {
        public int Id { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FullName { get; set; }
    }

    public class EntityWithConcurrencyToken
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public Guid ConcurrencyStamp { get; set; }
    }

    public class EntityWithJsonColumn
    {
        public int Id { get; set; }
        public string? JsonData { get; set; }
    }

    public enum TestStatus { Active, Inactive, Pending }

    public class EntityWithEnum
    {
        public int Id { get; set; }
        public TestStatus Status { get; set; }
    }

    public class EntityWithEnumAsString
    {
        public int Id { get; set; }
        public TestStatus Status { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<EntityWithRowVersion> EntitiesWithRowVersion => Set<EntityWithRowVersion>();
        public DbSet<EntityWithComputedColumn> EntitiesWithComputedColumn => Set<EntityWithComputedColumn>();
        public DbSet<EntityWithConcurrencyToken> EntitiesWithConcurrencyToken => Set<EntityWithConcurrencyToken>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EntityWithRowVersion>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Version).IsWitRowVersion();
            });

            modelBuilder.Entity<EntityWithComputedColumn>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FullName)
                    .HasWitComputedColumnSql("FirstName || ' ' || LastName", stored: true);
            });

            modelBuilder.Entity<EntityWithConcurrencyToken>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
            });
        }
    }

    public class JsonTestDbContext : DbContext
    {
        public JsonTestDbContext(DbContextOptions<JsonTestDbContext> options) : base(options) { }

        public DbSet<EntityWithJsonColumn> JsonEntities => Set<EntityWithJsonColumn>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EntityWithJsonColumn>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.JsonData).HasJsonColumnType();
            });
        }
    }

    public class EnumTestDbContext : DbContext
    {
        public EnumTestDbContext(DbContextOptions<EnumTestDbContext> options) : base(options) { }

        public DbSet<EntityWithEnum> EnumEntities => Set<EntityWithEnum>();
        public DbSet<EntityWithEnumAsString> EnumAsStringEntities => Set<EntityWithEnumAsString>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EntityWithEnum>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<EntityWithEnumAsString>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Status).HasEnumToStringConversion();
            });
        }
    }

    #endregion
}
