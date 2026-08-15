using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// The in-memory fixture: configuration, change tracking, and - since 2026-08-15 - whether it holds
/// a row.
/// </summary>
/// <remarks>
/// <para>
/// <b>This file used to carry the note "these tests verify configuration and tracking, not full
/// database execution", and that sentence was the whole defect.</b> Eighteen cases drove the
/// fixture the documentation recommends for testing and not one of them wrote a row, so nobody
/// found out that <c>SaveChanges</c> fails on it: EF opens and closes a connection per operation,
/// an in-memory database is private to its connection, and the table <c>EnsureCreated</c> made was
/// gone before the insert ran.
/// </para>
/// <para>
/// The cases in <c>The database itself</c> are the ones that could see it, and they are written the
/// way the defect was found - a round trip, then a second context, then the control that two
/// fixtures are still separate databases.
/// </para>
/// </remarks>
[TestFixture]
public class InMemoryTests
{
    #region The database itself

    /// <summary>
    /// The round trip the documentation promises: create the schema, save a row, read it back.
    /// </summary>
    /// <remarks>
    /// Red before the fix with <c>WitDbException: Table 'Items' not found</c> on the SAVE - which is
    /// worth noticing, because <c>EnsureCreated</c> returned <c>true</c> a line earlier. The store
    /// was built and thrown away between two operations of the same context.
    /// </remarks>
    [Test]
    public void AnInMemoryFixtureKeepsItsRowsAcrossOperationsTest()
    {
        var options = new DbContextOptionsBuilder<InMemoryDbContext>()
            .UseWitDbInMemory()
            .Options;

        using var context = new InMemoryDbContext(options);

        Assert.That(context.Database.EnsureCreated(), Is.True, "the schema is created");

        context.Entities.Add(new SimpleEntity { Name = "kept", Value = 42 });

        Assert.That(context.SaveChanges(), Is.EqualTo(1),
            "the row is saved - this is where the fixture used to fail, one operation after the "
            + "table was created");

        var sameContext = context.Entities.AsNoTracking().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sameContext, Has.Count.EqualTo(1), "and read back in the same context");
            Assert.That(sameContext[0].Name, Is.EqualTo("kept"));
            Assert.That(sameContext[0].Id, Is.GreaterThan(0), "with the key the database gave it");
        });

        // A SECOND context over the same options, which is what a test fixture actually does between
        // arrange and act. Reading in the same context proves the store survived one close; reading
        // in another proves the options carry it rather than the context holding it alive.
        using var second = new InMemoryDbContext(options);

        Assert.That(second.Entities.AsNoTracking().Single().Name, Is.EqualTo("kept"),
            "a second context over the same options sees the same database");
    }

    /// <summary>
    /// CONTROL, and the one that decides whether the fix is worth having: two fixtures are two
    /// databases.
    /// </summary>
    /// <remarks>
    /// Sharing an in-memory store by making it global would pass the case above perfectly and turn
    /// every test in a suite into a neighbour of every other - which is worse than the defect, and
    /// silent.
    /// </remarks>
    [Test]
    public void ControlTwoInMemoryFixturesAreTwoDatabasesTest()
    {
        var first = new DbContextOptionsBuilder<InMemoryDbContext>().UseWitDbInMemory().Options;
        var second = new DbContextOptionsBuilder<InMemoryDbContext>().UseWitDbInMemory().Options;

        using var one = new InMemoryDbContext(first);
        using var two = new InMemoryDbContext(second);

        one.Database.EnsureCreated();
        two.Database.EnsureCreated();

        one.Entities.Add(new SimpleEntity { Name = "only in the first", Value = 1 });
        one.SaveChanges();

        Assert.Multiple(() =>
        {
            Assert.That(one.Entities.AsNoTracking().ToList(), Has.Count.EqualTo(1));
            Assert.That(two.Entities.AsNoTracking().ToList(), Is.Empty,
                "the second fixture must not see the first's rows");
        });
    }

    /// <summary>
    /// CONTROL: nothing is written to disk. An in-memory fixture that quietly became a file would
    /// pass both cases above and leave a database behind after every test.
    /// </summary>
    [Test]
    public void ControlAnInMemoryFixtureWritesNoFileTest()
    {
        var before = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.witdb").Length;

        var options = new DbContextOptionsBuilder<InMemoryDbContext>()
            .UseWitDbInMemory()
            .Options;

        using var context = new InMemoryDbContext(options);

        context.Database.EnsureCreated();
        context.Entities.Add(new SimpleEntity { Name = "in memory", Value = 1 });
        context.SaveChanges();

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.witdb"),
                Has.Length.EqualTo(before), "an in-memory fixture leaves no file behind");

            // The creator decides in-memory from the CONNECTION STRING, so the connection handed to
            // EF has to keep saying so. Without this, the fix could hand over a connection the
            // creator then treats as a file-based one.
            Assert.That(context.Database.CanConnect(), Is.True);
        });
    }

    #endregion

    #region Configuration Tests

    [Test]
    public void InMemoryContextCanBeCreatedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        Assert.That(context, Is.Not.Null);
        Assert.That(context.Database, Is.Not.Null);
    }

    [Test]
    public void InMemoryContextHasCorrectProviderTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        Assert.That(context.Database.ProviderName, Is.EqualTo(WitDatabaseProvider.PROVIDER_NAME));
    }

    [Test]
    public void InMemoryContextCanConnectTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        Assert.That(context.Database.CanConnect(), Is.True);
    }

    #endregion

    #region Change Tracking Tests

    [Test]
    public void InMemoryAddEntityTracksCorrectlyTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);
        
        var entity = new SimpleEntity { Name = "Test", Value = 42 };
        context.Entities.Add(entity);

        var entry = context.Entry(entity);
        Assert.That(entry.State, Is.EqualTo(EntityState.Added));
    }

    [Test]
    public void InMemoryUpdateEntityTracksCorrectlyTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);
        
        var entity = new SimpleEntity { Id = 1, Name = "Original", Value = 100 };
        context.Entities.Attach(entity);
        entity.Name = "Updated";

        var entry = context.Entry(entity);
        Assert.That(entry.State, Is.EqualTo(EntityState.Modified));
    }

    [Test]
    public void InMemoryDeleteEntityTracksCorrectlyTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);
        
        var entity = new SimpleEntity { Id = 1, Name = "ToDelete", Value = 0 };
        context.Entities.Attach(entity);
        context.Entities.Remove(entity);

        var entry = context.Entry(entity);
        Assert.That(entry.State, Is.EqualTo(EntityState.Deleted));
    }

    [Test]
    public void InMemoryAddRangeTracksAllEntitiesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);
        
        context.Entities.AddRange(
            new SimpleEntity { Name = "A", Value = 10 },
            new SimpleEntity { Name = "B", Value = 20 },
            new SimpleEntity { Name = "C", Value = 30 }
        );

        var entries = context.ChangeTracker.Entries<SimpleEntity>().ToList();
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries.All(e => e.State == EntityState.Added), Is.True);
    }

    #endregion

    #region Query Expression Tests

    [Test]
    public void InMemoryWhereQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        var query = context.Entities.Where(e => e.Value > 15);

        Assert.That(query.Expression.ToString(), Does.Contain("Value"));
    }

    [Test]
    public void InMemoryOrderByQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        var query = context.Entities.OrderBy(e => e.Name);

        Assert.That(query.Expression.ToString(), Does.Contain("OrderBy"));
    }

    [Test]
    public void InMemorySelectQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        var query = context.Entities.Select(e => new { e.Name, e.Value });

        Assert.That(query.Expression.ToString(), Does.Contain("Select"));
    }

    #endregion

    #region Model Tests

    [Test]
    public void InMemoryModelHasCorrectEntityTypeTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);
        var model = context.Model;

        var entityType = model.FindEntityType(typeof(SimpleEntity));
        Assert.That(entityType, Is.Not.Null);
    }

    [Test]
    public void InMemoryModelHasCorrectPrimaryKeyTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);
        var entityType = context.Model.FindEntityType(typeof(SimpleEntity));
        var primaryKey = entityType?.FindPrimaryKey();

        Assert.That(primaryKey, Is.Not.Null);
        Assert.That(primaryKey!.Properties[0].Name, Is.EqualTo("Id"));
    }

    #endregion

    #region Connection Tests

    [Test]
    public void InMemoryOpenConnectionSucceedsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new InMemoryDbContext(optionsBuilder.Options);

        Assert.DoesNotThrow(() => context.Database.OpenConnection());
        Assert.DoesNotThrow(() => context.Database.CloseConnection());
    }

    [Test]
    public async Task InMemoryOpenConnectionAsyncSucceedsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<InMemoryDbContext>();
        optionsBuilder.UseWitDbInMemory();

        await using var context = new InMemoryDbContext(optionsBuilder.Options);

        Assert.DoesNotThrowAsync(async () => await context.Database.OpenConnectionAsync());
        Assert.DoesNotThrowAsync(async () => await context.Database.CloseConnectionAsync());
    }

    #endregion

    #region Test Models

    public class SimpleEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class InMemoryDbContext : DbContext
    {
        public InMemoryDbContext(DbContextOptions<InMemoryDbContext> options) : base(options) { }

        public DbSet<SimpleEntity> Entities => Set<SimpleEntity>();
    }

    #endregion
}
