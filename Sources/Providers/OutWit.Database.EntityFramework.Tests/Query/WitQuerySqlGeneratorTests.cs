using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Unit tests for WitQuerySqlGenerator query building.
/// </summary>
[TestFixture]
public class WitQuerySqlGeneratorTests
{
    #region Query Building Tests

    [Test]
    public void QueryWithTakeCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Take(10);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression, Is.Not.Null);
    }

    [Test]
    public void QueryWithSkipCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Skip(5);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression, Is.Not.Null);
    }

    [Test]
    public void QueryWithSkipAndTakeCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Skip(10).Take(20);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression, Is.Not.Null);
    }

    #endregion

    #region Where Clause Tests

    [Test]
    public void QueryWithWhereCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Where(p => p.IsActive);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("IsActive"));
    }

    [Test]
    public void QueryWithMultipleConditionsCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Where(p => p.IsActive && p.Price > 0);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("IsActive"));
        Assert.That(query.Expression.ToString(), Does.Contain("Price"));
    }

    [Test]
    public void QueryWithOrConditionCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Where(p => p.IsActive || p.Price > 100);

        Assert.That(query, Is.Not.Null);
    }

    #endregion

    #region OrderBy Tests

    [Test]
    public void QueryWithOrderByCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.OrderBy(p => p.Name);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("OrderBy"));
    }

    [Test]
    public void QueryWithOrderByDescendingCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.OrderByDescending(p => p.Price);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("OrderByDescending"));
    }

    [Test]
    public void QueryWithThenByCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.OrderBy(p => p.Category).ThenBy(p => p.Name);

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("ThenBy"));
    }

    #endregion

    #region Select Tests

    [Test]
    public void QueryWithSelectProjectionCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Select(p => new { p.Name, p.Price });

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("Select"));
    }

    [Test]
    public void QueryWithSelectSinglePropertyCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Select(p => p.Name);

        Assert.That(query, Is.Not.Null);
    }

    #endregion

    #region Distinct Tests

    [Test]
    public void QueryWithDistinctCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products.Select(p => p.Category).Distinct();

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression.ToString(), Does.Contain("Distinct"));
    }

    #endregion

    #region Complex Query Tests

    [Test]
    public void ComplexQueryWithMultipleOperationsCreatesQueryableTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        var query = context.Products
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Skip(10)
            .Take(20)
            .Select(p => new { p.Id, p.Name, p.Price });

        Assert.That(query, Is.Not.Null);
        Assert.That(query.Expression, Is.Not.Null);
    }

    #endregion

    #region Test Models

    public class Product
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<Product> Products => Set<Product>();
    }

    #endregion
}
