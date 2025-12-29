using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for CRUD operations setup with WitDatabase.
/// Note: These tests verify the configuration and tracking, not actual database execution,
/// as full integration requires the complete ADO.NET provider implementation.
/// </summary>
[TestFixture]
public class CrudOperationsTests
{
    #region Create Tests

    [Test]
    public void AddEntitySetsStateToAddedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var product = new Product
        {
            Name = "Test Product",
            Price = 99.99m,
            Category = "Electronics",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.Products.Add(product);

        var entry = context.Entry(product);
        Assert.That(entry.State, Is.EqualTo(EntityState.Added));
    }

    [Test]
    public void AddRangeAddsMultipleEntitiesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        context.Products.AddRange(
            new Product { Name = "Product 1", Price = 10m, Category = "A", IsActive = true },
            new Product { Name = "Product 2", Price = 20m, Category = "B", IsActive = true },
            new Product { Name = "Product 3", Price = 30m, Category = "A", IsActive = false }
        );

        var entries = context.ChangeTracker.Entries<Product>().ToList();
        Assert.That(entries.Count, Is.EqualTo(3));
        Assert.That(entries.All(e => e.State == EntityState.Added), Is.True);
    }

    #endregion

    #region Update Tests

    [Test]
    public void ModifyAttachedEntitySetsStateToModifiedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var product = new Product { Id = 1, Name = "Original", Price = 100m, Category = "Test", IsActive = true };
        context.Products.Attach(product);
        
        Assert.That(context.Entry(product).State, Is.EqualTo(EntityState.Unchanged));
        
        product.Name = "Updated";
        
        Assert.That(context.Entry(product).State, Is.EqualTo(EntityState.Modified));
    }

    [Test]
    public void UpdateMethodSetsStateToModifiedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var product = new Product { Id = 1, Name = "Test", Price = 100m, Category = "Test", IsActive = true };
        context.Products.Update(product);

        Assert.That(context.Entry(product).State, Is.EqualTo(EntityState.Modified));
    }

    #endregion

    #region Delete Tests

    [Test]
    public void RemoveEntitySetsStateToDeletedTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var product = new Product { Id = 1, Name = "ToDelete", Price = 50m, Category = "Test", IsActive = true };
        context.Products.Attach(product);
        context.Products.Remove(product);

        Assert.That(context.Entry(product).State, Is.EqualTo(EntityState.Deleted));
    }

    [Test]
    public void RemoveRangeRemovesMultipleEntitiesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var products = new []
        {
            new Product { Id = 1, Name = "P1", Price = 10m },
            new Product { Id = 2, Name = "P2", Price = 20m },
            new Product { Id = 3, Name = "P3", Price = 30m }
        };

        context.Products.AttachRange(products);
        context.Products.RemoveRange(products);

        var entries = context.ChangeTracker.Entries<Product>().ToList();
        Assert.That(entries.All(e => e.State == EntityState.Deleted), Is.True);
    }

    #endregion

    #region Query Building Tests

    [Test]
    public void WhereQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var query = context.Products.Where(p => p.IsActive);

        Assert.That(query.Expression.ToString(), Does.Contain("IsActive"));
    }

    [Test]
    public void OrderByQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var query = context.Products.OrderBy(p => p.Name);

        Assert.That(query.Expression.ToString(), Does.Contain("OrderBy"));
    }

    [Test]
    public void OrderByDescendingQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var query = context.Products.OrderByDescending(p => p.Price);

        Assert.That(query.Expression.ToString(), Does.Contain("OrderByDescending"));
    }

    [Test]
    public void SkipTakeQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var query = context.Products.Skip(10).Take(20);

        Assert.That(query.Expression.ToString(), Does.Contain("Skip"));
        Assert.That(query.Expression.ToString(), Does.Contain("Take"));
    }

    [Test]
    public void SelectQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var query = context.Products.Select(p => new { p.Name, p.Price });

        Assert.That(query.Expression.ToString(), Does.Contain("Select"));
    }

    [Test]
    public void DistinctQueryCreatesCorrectExpressionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        var query = context.Products.Select(p => p.Category).Distinct();

        Assert.That(query.Expression.ToString(), Does.Contain("Distinct"));
    }

    #endregion

    #region ChangeTracker Tests

    [Test]
    public void ChangeTrackerHasNoChangesInitiallyTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        Assert.That(context.ChangeTracker.HasChanges(), Is.False);
    }

    [Test]
    public void ChangeTrackerHasChangesAfterAddTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        context.Products.Add(new Product { Name = "Test" });

        Assert.That(context.ChangeTracker.HasChanges(), Is.True);
    }

    [Test]
    public void ChangeTrackerClearRemovesAllEntriesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        context.Products.Add(new Product { Name = "Test" });
        Assert.That(context.ChangeTracker.HasChanges(), Is.True);

        context.ChangeTracker.Clear();
        Assert.That(context.ChangeTracker.HasChanges(), Is.False);
    }

    [Test]
    public void MixedOperationsAreTrackedCorrectlyTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new ProductDbContext(optionsBuilder.Options);

        // Add
        context.Products.Add(new Product { Name = "New" });

        // Update (via Attach + modify)
        var existing = new Product { Id = 1, Name = "Existing" };
        context.Products.Attach(existing);
        existing.Name = "Modified";

        // Delete
        var toDelete = new Product { Id = 2, Name = "ToDelete" };
        context.Products.Attach(toDelete);
        context.Products.Remove(toDelete);

        var entries = context.ChangeTracker.Entries<Product>().ToList();
        Assert.That(entries.Count(e => e.State == EntityState.Added), Is.EqualTo(1));
        Assert.That(entries.Count(e => e.State == EntityState.Modified), Is.EqualTo(1));
        Assert.That(entries.Count(e => e.State == EntityState.Deleted), Is.EqualTo(1));
    }

    #endregion

    #region Test Models

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? Category { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ProductDbContext : DbContext
    {
        public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options) { }

        public DbSet<Product> Products => Set<Product>();
    }

    #endregion
}
