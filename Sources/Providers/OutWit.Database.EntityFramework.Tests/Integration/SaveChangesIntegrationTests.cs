using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for SaveChanges functionality with WitDatabase.
/// </summary>
[TestFixture]
public class SaveChangesIntegrationTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbSaveChanges_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(m_testDbPath))
        {
            try { File.Delete(m_testDbPath); } catch { }
        }
    }

    #endregion

    #region Model Configuration Tests

    [Test]
    public void RelationalModelUsesRuntimeModelTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        var model = context.Model;
        var relationalModel = model.GetRelationalModel();
        
        Assert.That(ReferenceEquals(relationalModel.Model, model), Is.True, 
            "RelationalModel.Model should be the same as context.Model (RuntimeModel)");
    }

    [Test]
    public void EntityTypeHasTableMappingsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        var entityType = context.Model.FindEntityType(typeof(TestItem));
        Assert.That(entityType, Is.Not.Null);
        
        var tableMappings = entityType!.GetTableMappings().ToList();
        Assert.That(tableMappings.Count, Is.GreaterThan(0), "Entity type should have table mappings");
    }

    [Test]
    public void RelationalModelHasTablesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        var relationalModel = context.Model.GetRelationalModel();
        var tables = relationalModel.Tables.ToList();
        
        Assert.That(tables.Count, Is.GreaterThan(0), "RelationalModel should have tables");
        // Table name comes from the DbSet property (Items), as in every other EF provider.
        Assert.That(tables.Any(t => t.Name == "Items"), Is.True, "Should have Items table");
    }

    #endregion

    #region SaveChanges Tests

    [Test]
    public void SaveChangesInsertsEntityTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        // Create table manually
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Items"" (
                    ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }
        
        // Add and save entity
        var item = new TestItem { Name = "Test Item" };
        context.Items.Add(item);
        
        var result = context.SaveChanges();
        
        Assert.That(result, Is.EqualTo(1), "SaveChanges should return 1 for one inserted entity");
        
        context.Database.CloseConnection();
    }

    [Test]
    public void SaveChangesMultipleEntitiesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        // Create table manually
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Items"" (
                    ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }
        
        // Add multiple entities
        context.Items.AddRange(
            new TestItem { Name = "Item 1" },
            new TestItem { Name = "Item 2" },
            new TestItem { Name = "Item 3" }
        );
        
        var result = context.SaveChanges();
        
        Assert.That(result, Is.EqualTo(3), "SaveChanges should return 3 for three inserted entities");
        
        context.Database.CloseConnection();
    }

    #endregion

    #region DI Simulation Tests

    [Test]
    public void SaveChangesWithServiceProviderTest()
    {
        // Simulate how web applications create DbContext via DI
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        
        services.AddDbContext<SaveChangesTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SaveChangesTestContext>();
        
        // Create table manually
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Items"" (
                    ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }
        
        // Add and save entity
        var item = new TestItem { Name = "DI Test Item" };
        context.Items.Add(item);
        
        var result = context.SaveChanges();
        
        Assert.That(result, Is.EqualTo(1), "SaveChanges should return 1 for one inserted entity");
        
        context.Database.CloseConnection();
    }

    #endregion

    #region WebApiEF Simulation Tests

    [Test]
    public void SaveChangesWithWebApiEFStyleContextTest()
    {
        var services = new ServiceCollection();
        
        services.AddDbContext<FourEntityEFContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FourEntityEFContext>();
        
        // Create tables manually
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Users"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS ""Products"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT NOT NULL, ""Price"" DECIMAL NOT NULL);
                CREATE TABLE IF NOT EXISTS ""Orders"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""UserId"" INT NOT NULL, ""TotalAmount"" DECIMAL NOT NULL);
                CREATE TABLE IF NOT EXISTS ""OrderItems"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""OrderId"" INT NOT NULL, ""ProductId"" INT NOT NULL, ""Quantity"" INT NOT NULL, ""UnitPrice"" DECIMAL NOT NULL)";
            cmd.ExecuteNonQuery();
        }
        
        // Add user
        var user = new FourUser { Name = "Alice" };
        context.Users.Add(user);
        var result1 = context.SaveChanges();
        Assert.That(result1, Is.EqualTo(1));
        
        // Add product
        var product = new FourProduct { Name = "Laptop", Price = 1299.99m };
        context.Products.Add(product);
        var result2 = context.SaveChanges();
        Assert.That(result2, Is.EqualTo(1));
        
        // Add order
        var order = new FourOrder { UserId = user.Id, TotalAmount = product.Price };
        context.Orders.Add(order);
        var result3 = context.SaveChanges();
        Assert.That(result3, Is.EqualTo(1));
        
        // Add order item
        var orderItem = new FourOrderItem { OrderId = order.Id, ProductId = product.Id, Quantity = 1, UnitPrice = product.Price };
        context.OrderItems.Add(orderItem);
        var result4 = context.SaveChanges();
        Assert.That(result4, Is.EqualTo(1));
        
        context.Database.CloseConnection();
    }

    #endregion

    #region Test Models

    public class TestItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SaveChangesTestContext : DbContext
    {
        public SaveChangesTestContext(DbContextOptions<SaveChangesTestContext> options) : base(options) { }

        public DbSet<TestItem> Items => Set<TestItem>();
    }

    #endregion
}

#region Four Entity Models (outside test class)

public class FourUser
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public class FourProduct
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
}

public class FourOrder
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal TotalAmount { get; set; }
}

public class FourOrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class FourEntityEFContext : DbContext
{
    public FourEntityEFContext(DbContextOptions<FourEntityEFContext> options) : base(options) { }

    public DbSet<FourUser> Users => Set<FourUser>();
    public DbSet<FourProduct> Products => Set<FourProduct>();
    public DbSet<FourOrder> Orders => Set<FourOrder>();
    public DbSet<FourOrderItem> OrderItems => Set<FourOrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FourUser>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<FourProduct>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("DECIMAL(10, 2)");
        });

        modelBuilder.Entity<FourOrder>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.TotalAmount).HasColumnType("DECIMAL(15, 2)");
        });

        modelBuilder.Entity<FourOrderItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UnitPrice).HasColumnType("DECIMAL(10, 2)");
        });
    }
}

#endregion
