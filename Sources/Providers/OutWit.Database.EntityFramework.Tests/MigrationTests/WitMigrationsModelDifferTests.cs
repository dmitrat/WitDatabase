using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Migrations;

/// <summary>
/// Tests for <see cref="Migrations.WitMigrationsModelDiffer"/> covering the EnsureCreated
/// table-creation bug: EnsureCreated() used to create the .witdb file but generated zero
/// CreateTableOperations, so no tables were actually created inside the database.
/// </summary>
[TestFixture]
public class WitMigrationsModelDifferTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbDiffer_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(m_testDbPath)}*"))
        {
            try { File.Delete(file); } catch { }
        }
        foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(m_testDbPath)}*"))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    #endregion

    #region GenerateCreateScript Tests

    [Test]
    public void GenerateCreateScriptReturnsNonEmptyForFileBasedTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        var script = context.Database.GenerateCreateScript();

        Assert.That(script, Is.Not.Null.And.Not.Empty,
            "GenerateCreateScript must produce SQL when a model has entities");
        Assert.That(script, Does.Contain("CREATE TABLE"),
            "Script should contain CREATE TABLE statements");
    }

    [Test]
    public void GenerateCreateScriptReturnsNonEmptyForInMemoryTest()
    {
        using var context = CreateInMemoryContext();

        var script = context.Database.GenerateCreateScript();

        Assert.That(script, Is.Not.Null.And.Not.Empty);
        Assert.That(script, Does.Contain("CREATE TABLE"));
    }

    [Test]
    public void GenerateCreateScriptContainsAllTablesTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        var script = context.Database.GenerateCreateScript();

        Assert.That(script, Does.Contain("DifferProduct"));
        Assert.That(script, Does.Contain("DifferCategory"));
    }

    #endregion

    #region HasTables After EnsureCreated Tests

    [Test]
    public void HasTablesReturnsTrueAfterEnsureCreatedTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        context.Database.EnsureCreated();

        var creator = context.GetService<IRelationalDatabaseCreator>();
        var hasTables = creator.HasTables();

        Assert.That(hasTables, Is.True,
            "HasTables should return true after EnsureCreated creates tables");
    }

    [Test]
    public async Task HasTablesAsyncReturnsTrueAfterEnsureCreatedAsyncTest()
    {
        await using var context = CreateFileContext(m_testDbPath);

        await context.Database.EnsureCreatedAsync();

        var creator = context.GetService<IRelationalDatabaseCreator>();
        var hasTables = await creator.HasTablesAsync();

        Assert.That(hasTables, Is.True,
            "HasTablesAsync should return true after EnsureCreatedAsync creates tables");
    }

    #endregion

    #region EnsureCreated Then CRUD Tests

    [Test]
    public void EnsureCreatedThenInsertSucceedsTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        context.Database.EnsureCreated();

        context.Products.Add(new DifferProduct { Name = "Widget", Price = 9.99m });
        var saved = context.SaveChanges();

        Assert.That(saved, Is.EqualTo(1));
    }

    [Test]
    public async Task EnsureCreatedAsyncThenInsertSucceedsTest()
    {
        await using var context = CreateFileContext(m_testDbPath);

        await context.Database.EnsureCreatedAsync();

        context.Products.Add(new DifferProduct { Name = "AsyncWidget", Price = 19.99m });
        var saved = await context.SaveChangesAsync();

        Assert.That(saved, Is.EqualTo(1));
    }

    [Test]
    public void EnsureCreatedThenQueryReturnsInsertedDataTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        context.Database.EnsureCreated();

        context.Products.Add(new DifferProduct { Name = "Gadget", Price = 49.99m });
        context.SaveChanges();

        var product = context.Products.FirstOrDefault(p => p.Name == "Gadget");

        Assert.That(product, Is.Not.Null);
        Assert.That(product!.Price, Is.EqualTo(49.99m));
    }

    [Test]
    public void EnsureCreatedThenInsertMultipleTablesSucceedsTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        context.Database.EnsureCreated();

        context.Categories.Add(new DifferCategory { Name = "Electronics" });
        context.Products.Add(new DifferProduct { Name = "Phone", Price = 999m });
        var saved = context.SaveChanges();

        Assert.That(saved, Is.EqualTo(2));
        Assert.That(context.Categories.Count(), Is.EqualTo(1));
        Assert.That(context.Products.Count(), Is.EqualTo(1));
    }

    #endregion

    #region Idempotency Tests

    [Test]
    public void EnsureCreatedSecondCallReturnsFalseTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        var first = context.Database.EnsureCreated();
        Assert.That(first, Is.True);

        // Second call on the same context — database & tables already exist
        var second = context.Database.EnsureCreated();
        Assert.That(second, Is.False,
            "Second EnsureCreated should return false because tables already exist");
    }

    [Test]
    public void EnsureCreatedSecondContextReturnsFalseTest()
    {
        // First context creates everything
        using (var ctx1 = CreateFileContext(m_testDbPath))
        {
            var result = ctx1.Database.EnsureCreated();
            Assert.That(result, Is.True);
        }

        // Second context finds existing tables
        using (var ctx2 = CreateFileContext(m_testDbPath))
        {
            var result = ctx2.Database.EnsureCreated();
            Assert.That(result, Is.False,
                "EnsureCreated on an already-created database should return false");
        }
    }

    #endregion

    #region Cross-Context Data Persistence Test

    [Test]
    public void DataPersistsAcrossContextsAfterEnsureCreatedTest()
    {
        // Context 1: create tables and insert data
        using (var ctx = CreateFileContext(m_testDbPath))
        {
            ctx.Database.EnsureCreated();
            ctx.Products.Add(new DifferProduct { Name = "Persisted", Price = 42.00m });
            ctx.SaveChanges();
        }

        // Context 2: read back
        using (var ctx = CreateFileContext(m_testDbPath))
        {
            var product = ctx.Products.FirstOrDefault(p => p.Name == "Persisted");

            Assert.That(product, Is.Not.Null);
            Assert.That(product!.Price, Is.EqualTo(42.00m));
        }
    }

    #endregion

    #region Helper Methods

    private static DifferTestContext CreateFileContext(string path)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DifferTestContext>();
        optionsBuilder.UseWitDb($"Data Source={path}");
        return new DifferTestContext(optionsBuilder.Options);
    }

    private static DifferTestContext CreateInMemoryContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DifferTestContext>();
        optionsBuilder.UseWitDbInMemory();
        return new DifferTestContext(optionsBuilder.Options);
    }

    #endregion

    #region Test Models

    public class DifferProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class DifferCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class DifferTestContext : DbContext
    {
        public DifferTestContext(DbContextOptions<DifferTestContext> options)
            : base(options)
        {
        }

        public DbSet<DifferProduct> Products => Set<DifferProduct>();
        public DbSet<DifferCategory> Categories => Set<DifferCategory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DifferProduct>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Price).HasColumnType("DECIMAL(10, 2)");
            });

            modelBuilder.Entity<DifferCategory>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            });
        }
    }

    #endregion
}
