using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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
        
        // RelationalModel should reference the same model as context.Model (RuntimeModel)
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
        Assert.That(tables.Any(t => t.Name == "TestItem"), Is.True, "Should have TestItem table");
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
                CREATE TABLE IF NOT EXISTS ""TestItem"" (
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
                CREATE TABLE IF NOT EXISTS ""TestItem"" (
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

    #region Raw SQL Tests

    [Test]
    public void CanExecuteRawSqlTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        context.Database.OpenConnection();
        
        var connection = context.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        
        var result = command.ExecuteScalar();
        
        Assert.That(result, Is.Not.Null);
        
        context.Database.CloseConnection();
    }

    [Test]
    public void CanCreateAndQueryTableViaRawSqlTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<SaveChangesTestContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new SaveChangesTestContext(optionsBuilder.Options);
        
        context.Database.OpenConnection();
        var connection = context.Database.GetDbConnection();
        
        // Create table
        using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS RawSqlTestTable (
                    Id INT PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                )";
            createCmd.ExecuteNonQuery();
        }
        
        // Insert data
        using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO RawSqlTestTable (Name) VALUES ('Test')";
            var rowsAffected = insertCmd.ExecuteNonQuery();
            Assert.That(rowsAffected, Is.EqualTo(1));
        }
        
        // Query data
        using (var selectCmd = connection.CreateCommand())
        {
            selectCmd.CommandText = "SELECT Name FROM RawSqlTestTable WHERE Id = 1";
            var name = selectCmd.ExecuteScalar();
            Assert.That(name, Is.EqualTo("Test"));
        }
        
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
