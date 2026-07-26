using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Migrations;

/// <summary>
/// Tests for EF Core migrations functionality with WitDatabase.
/// </summary>
[TestFixture]
public class MigrationsTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbMigrations_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test database files
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

    #region Service Registration Tests

    [Test]
    public void MigrationsSqlGeneratorIsRegisteredTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
        
        var sqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        
        Assert.That(sqlGenerator, Is.Not.Null);
        Assert.That(sqlGenerator.GetType().Name, Is.EqualTo("WitMigrationsSqlGenerator"));
    }

    [Test]
    public void HistoryRepositoryIsRegisteredTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
        
        var historyRepository = context.GetService<IHistoryRepository>();
        
        Assert.That(historyRepository, Is.Not.Null);
        Assert.That(historyRepository.GetType().Name, Is.EqualTo("WitHistoryRepository"));
    }

    [Test]
    public void CanGetPendingMigrationsTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
        
        // This should not throw
        var pendingMigrations = context.Database.GetPendingMigrations().ToList();
        
        // No migrations defined in test context, so should be empty
        Assert.That(pendingMigrations, Is.Empty);
    }

    #endregion

    #region Manual Table Creation Tests (bypassing EnsureCreated)

    [Test]
    public void CanCreateTableManuallyAndUseSaveChangesTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
        
        // Create table manually (simulating what migrations would do)
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Products"" (
                    ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL,
                    ""Price"" DECIMAL(10, 2) NOT NULL
                )";
            cmd.ExecuteNonQuery();
        }
        
        // Verify we can add and query data via EF Core
        context.Products.Add(new MigrationTestProduct { Name = "Test", Price = 9.99m });
        var saved = context.SaveChanges();
        Assert.That(saved, Is.EqualTo(1));
        
        var count = context.Products.Count();
        Assert.That(count, Is.EqualTo(1));
        
        context.Database.CloseConnection();
    }

    [Test]
    public void CanQueryDataAfterManualTableCreationTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        // First scope - create table and insert data
        using (var scope = serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
            
            context.Database.OpenConnection();
            var connection = context.Database.GetDbConnection();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ""Products"" (
                        ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                        ""Name"" TEXT NOT NULL,
                        ""Price"" DECIMAL(10, 2) NOT NULL
                    )";
                cmd.ExecuteNonQuery();
            }
            
            context.Products.Add(new MigrationTestProduct { Name = "Widget", Price = 19.99m });
            context.Products.Add(new MigrationTestProduct { Name = "Gadget", Price = 29.99m });
            context.SaveChanges();
            
            context.Database.CloseConnection();
        }
        
        // Second scope - query data
        using (var scope = serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
            
            var products = context.Products.OrderBy(p => p.Name).ToList();
            
            Assert.That(products.Count, Is.EqualTo(2));
            Assert.That(products[0].Name, Is.EqualTo("Gadget"));
            Assert.That(products[1].Name, Is.EqualTo("Widget"));
        }
    }

    #endregion

    #region Migration SQL Generation Tests

    [Test]
    public void MigrationsSqlGeneratorCanGenerateCreateTableSqlTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
        
        var sqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        Assert.That(sqlGenerator, Is.Not.Null);
        
        // Create a simple CreateTable operation
        var operation = new Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation
        {
            Name = "TestTable",
            Columns =
            {
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "Id",
                    Table = "TestTable",
                    ClrType = typeof(int),
                    IsNullable = false
                },
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "Name",
                    Table = "TestTable",
                    ClrType = typeof(string),
                    IsNullable = false
                }
            },
            PrimaryKey = new Microsoft.EntityFrameworkCore.Migrations.Operations.AddPrimaryKeyOperation
            {
                Name = "PK_TestTable",
                Table = "TestTable",
                Columns = new[] { "Id" }
            }
        };

        var commands = sqlGenerator.Generate(new[] { operation });
        
        Assert.That(commands, Is.Not.Empty);
        var sql = commands.First().CommandText;
        Assert.That(sql, Does.Contain("CREATE TABLE"));
        Assert.That(sql, Does.Contain("TestTable"));
        Assert.That(sql, Does.Contain("Id"));
        Assert.That(sql, Does.Contain("Name"));
    }

    [Test]
    public void MigrationsSqlGeneratorUsesCorrectTypesTest()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MigrationTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        var serviceProvider = services.BuildServiceProvider();
        
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigrationTestContext>();
        
        var sqlGenerator = context.GetService<IMigrationsSqlGenerator>();
        
        // Create a CreateTable with various types
        var operation = new Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation
        {
            Name = "TypesTable",
            Columns =
            {
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "IntCol",
                    Table = "TypesTable",
                    ClrType = typeof(int),
                    IsNullable = false
                },
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "StringCol",
                    Table = "TypesTable",
                    ClrType = typeof(string),
                    IsNullable = true
                },
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "BoolCol",
                    Table = "TypesTable",
                    ClrType = typeof(bool),
                    IsNullable = false
                },
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "DecimalCol",
                    Table = "TypesTable",
                    ClrType = typeof(decimal),
                    IsNullable = false
                },
                new Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation
                {
                    Name = "DateTimeCol",
                    Table = "TypesTable",
                    ClrType = typeof(DateTime),
                    IsNullable = false
                }
            }
        };

        var commands = sqlGenerator.Generate(new[] { operation });
        var sql = commands.First().CommandText;
        
        // WitDatabase uses specific type names
        Assert.That(sql, Does.Contain("INT"));
        Assert.That(sql, Does.Contain("TEXT"));
        Assert.That(sql, Does.Contain("BOOLEAN"));
        Assert.That(sql, Does.Contain("DECIMAL"));
        Assert.That(sql, Does.Contain("DATETIME"));
    }

    #endregion

    #region Test Context and Models

    public class MigrationTestProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class MigrationTestContext : DbContext
    {
        public MigrationTestContext(DbContextOptions<MigrationTestContext> options) 
            : base(options) 
        { 
        }

        public DbSet<MigrationTestProduct> Products => Set<MigrationTestProduct>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<MigrationTestProduct>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Price).HasColumnType("DECIMAL(10, 2)");
            });
        }
    }

    #endregion
}
