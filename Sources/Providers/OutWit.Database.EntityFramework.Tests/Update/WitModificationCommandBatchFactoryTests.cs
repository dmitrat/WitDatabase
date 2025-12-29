using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Update;

/// <summary>
/// Unit tests for WitModificationCommandBatchFactory.
/// </summary>
[TestFixture]
public class WitModificationCommandBatchFactoryTests
{
    #region Batch Creation Tests

    [Test]
    public void AddEntityCreatesAddedEntryTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entity = new TestEntity { Name = "Test" };
        
        context.Entities.Add(entity);
        
        var entry = context.Entry(entity);
        Assert.That(entry.State, Is.EqualTo(EntityState.Added));
    }

    [Test]
    public void UpdateEntityCreatesModifiedEntryTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entity = new TestEntity { Id = 1, Name = "Test" };
        
        context.Entities.Attach(entity);
        entity.Name = "Updated";
        
        var entry = context.Entry(entity);
        Assert.That(entry.State, Is.EqualTo(EntityState.Modified));
    }

    [Test]
    public void RemoveEntityCreatesDeletedEntryTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var entity = new TestEntity { Id = 1, Name = "Test" };
        
        context.Entities.Attach(entity);
        context.Entities.Remove(entity);
        
        var entry = context.Entry(entity);
        Assert.That(entry.State, Is.EqualTo(EntityState.Deleted));
    }

    #endregion

    #region Multiple Operations Tests

    [Test]
    public void MultipleAddsAreTrackedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        context.Entities.AddRange(
            new TestEntity { Name = "Test1" },
            new TestEntity { Name = "Test2" },
            new TestEntity { Name = "Test3" }
        );

        var entries = context.ChangeTracker.Entries<TestEntity>().ToList();
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries.All(e => e.State == EntityState.Added), Is.True);
    }

    [Test]
    public void MixedOperationsAreTrackedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        // Add
        var newEntity = new TestEntity { Name = "New" };
        context.Entities.Add(newEntity);
        
        // Update
        var existingEntity = new TestEntity { Id = 1, Name = "Existing" };
        context.Entities.Attach(existingEntity);
        existingEntity.Name = "Updated";
        
        // Delete
        var toDelete = new TestEntity { Id = 2, Name = "ToDelete" };
        context.Entities.Attach(toDelete);
        context.Entities.Remove(toDelete);

        var entries = context.ChangeTracker.Entries<TestEntity>().ToList();
        Assert.That(entries.Count(e => e.State == EntityState.Added), Is.EqualTo(1));
        Assert.That(entries.Count(e => e.State == EntityState.Modified), Is.EqualTo(1));
        Assert.That(entries.Count(e => e.State == EntityState.Deleted), Is.EqualTo(1));
    }

    #endregion

    #region Test Models

    public class TestEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<TestEntity> Entities => Set<TestEntity>();
    }

    #endregion
}
