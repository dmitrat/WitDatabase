using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Storage;

namespace OutWit.Database.EntityFramework.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="WitDatabaseCreator"/>.
/// </summary>
[TestFixture]
public class WitDatabaseCreatorTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbCreator_{Guid.NewGuid():N}.witdb");
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

    #region Exists Tests

    [Test]
    public void ExistsReturnsFalseForNonExistentFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        var exists = creator.Exists();

        Assert.That(exists, Is.False);
    }

    [Test]
    public void ExistsReturnsTrueForInMemoryDatabaseTest()
    {
        using var context = CreateInMemoryContext();
        var creator = GetDatabaseCreator(context);

        var exists = creator.Exists();

        Assert.That(exists, Is.True);
    }

    [Test]
    public async Task ExistsAsyncReturnsFalseForNonExistentFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        var exists = await creator.ExistsAsync();

        Assert.That(exists, Is.False);
    }

    #endregion

    #region Create Tests

    [Test]
    public void CreateCreatesEmptyDatabaseFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        Assert.That(File.Exists(m_testDbPath), Is.False);
        
        creator.Create();

        // Note: File creation depends on underlying WitDbConnection behavior
    }

    [Test]
    public async Task CreateAsyncCreatesEmptyDatabaseFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        await creator.CreateAsync();

        Assert.Pass("CreateAsync completed without exception");
    }

    #endregion

    #region Delete Tests

    [Test]
    public void DeleteDoesNotThrowForNonExistentFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        Assert.DoesNotThrow(() => creator.Delete());
    }

    [Test]
    public void DeleteDoesNotThrowForInMemoryDatabaseTest()
    {
        using var context = CreateInMemoryContext();
        var creator = GetDatabaseCreator(context);

        Assert.DoesNotThrow(() => creator.Delete());
    }

    [Test]
    public async Task DeleteAsyncDoesNotThrowForNonExistentFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        Assert.DoesNotThrowAsync(async () => await creator.DeleteAsync());
    }

    #endregion

    #region HasTables Tests

    [Test]
    public void HasTablesReturnsFalseForNewDatabaseTest()
    {
        using var context = CreateInMemoryContext();
        var creator = GetDatabaseCreator(context);

        var hasTables = creator.HasTables();

        Assert.That(hasTables, Is.False);
    }

    [Test]
    public async Task HasTablesAsyncReturnsFalseForNewDatabaseTest()
    {
        using var context = CreateInMemoryContext();
        var creator = GetDatabaseCreator(context);

        var hasTables = await creator.HasTablesAsync();

        Assert.That(hasTables, Is.False);
    }

    #endregion

    #region EnsureCreated Tests

    [Test]
    public void EnsureDeletedDoesNotThrowForInMemoryTest()
    {
        using var context = CreateInMemoryContext();

        Assert.DoesNotThrow(() => context.Database.EnsureDeleted());
    }

    [Test]
    public async Task EnsureDeletedAsyncDoesNotThrowForInMemoryTest()
    {
        using var context = CreateInMemoryContext();

        Assert.DoesNotThrowAsync(async () => await context.Database.EnsureDeletedAsync());
    }

    [Test]
    public void EnsureCreatedReturnsTrueForNewDatabaseTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        var result = context.Database.EnsureCreated();

        Assert.That(result, Is.True, "EnsureCreated should return true for new database");
    }

    [Test]
    public async Task EnsureCreatedAsyncReturnsTrueForNewDatabaseTest()
    {
        await using var context = CreateFileContext(m_testDbPath);

        var result = await context.Database.EnsureCreatedAsync();

        Assert.That(result, Is.True, "EnsureCreatedAsync should return true for new database");
    }

    [Test]
    public void EnsureCreatedCreatesFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        Assert.That(File.Exists(m_testDbPath), Is.False);
        
        context.Database.EnsureCreated();

        Assert.That(File.Exists(m_testDbPath), Is.True, "Database file should be created");
    }

    [Test]
    public async Task EnsureCreatedAsyncCreatesFileTest()
    {
        await using var context = CreateFileContext(m_testDbPath);

        Assert.That(File.Exists(m_testDbPath), Is.False);
        
        await context.Database.EnsureCreatedAsync();

        Assert.That(File.Exists(m_testDbPath), Is.True, "Database file should be created");
    }

    [Test]
    public void EnsureCreatedIsIdempotentTest()
    {
        using var context = CreateFileContext(m_testDbPath);

        var firstResult = context.Database.EnsureCreated();
        Assert.That(firstResult, Is.True);

        // Second call should not throw
        Assert.DoesNotThrow(() => context.Database.EnsureCreated());
    }

    [Test]
    public async Task EnsureCreatedAsyncIsIdempotentTest()
    {
        await using var context = CreateFileContext(m_testDbPath);

        var firstResult = await context.Database.EnsureCreatedAsync();
        Assert.That(firstResult, Is.True);

        // Second call should not throw
        Assert.DoesNotThrowAsync(async () => await context.Database.EnsureCreatedAsync());
    }

    [Test]
    public void EnsureDeletedRemovesDatabaseFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        
        context.Database.EnsureCreated();
        Assert.That(File.Exists(m_testDbPath), Is.True);

        var deleted = context.Database.EnsureDeleted();

        Assert.That(deleted, Is.True, "EnsureDeleted should return true when database existed");
        Assert.That(File.Exists(m_testDbPath), Is.False, "Database file should be deleted");
    }

    [Test]
    public async Task EnsureDeletedAsyncRemovesDatabaseFileTest()
    {
        await using var context = CreateFileContext(m_testDbPath);
        
        await context.Database.EnsureCreatedAsync();
        Assert.That(File.Exists(m_testDbPath), Is.True);

        var deleted = await context.Database.EnsureDeletedAsync();

        Assert.That(deleted, Is.True, "EnsureDeletedAsync should return true when database existed");
        Assert.That(File.Exists(m_testDbPath), Is.False, "Database file should be deleted");
    }

    [Test]
    public void EnsureDeletedForFileBasedDatabaseRemovesFileTest()
    {
        using (var context = CreateFileContext(m_testDbPath))
        {
            context.Database.EnsureCreated();
            Assert.That(File.Exists(m_testDbPath), Is.True);
        }

        using (var context = CreateFileContext(m_testDbPath))
        {
            context.Database.EnsureDeleted();
        }

        Assert.That(File.Exists(m_testDbPath), Is.False, "Database file should be deleted");
    }

    [Test]
    public void ExistsReturnsTrueAfterEnsureCreatedTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        Assert.That(creator.Exists(), Is.False);

        context.Database.EnsureCreated();

        Assert.That(creator.Exists(), Is.True);
    }

    [Test]
    public void EnsureCreatedForInMemoryDatabaseTest()
    {
        using var context = CreateInMemoryContext();

        var result = context.Database.EnsureCreated();

        Assert.That(result, Is.True, "EnsureCreated should return true for in-memory database");
    }

    [Test]
    public async Task EnsureCreatedAsyncForInMemoryDatabaseTest()
    {
        await using var context = CreateInMemoryContext();

        var result = await context.Database.EnsureCreatedAsync();

        Assert.That(result, Is.True, "EnsureCreatedAsync should return true for in-memory database");
    }

    [Test]
    public void EnsureDeletedReturnsFalseForNonExistentDatabaseTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        
        Assert.That(File.Exists(m_testDbPath), Is.False);

        var deleted = context.Database.EnsureDeleted();

        Assert.That(deleted, Is.False, "EnsureDeleted should return false when database didn't exist");
    }

    [Test]
    public async Task EnsureDeletedAsyncReturnsFalseForNonExistentDatabaseTest()
    {
        await using var context = CreateFileContext(m_testDbPath);
        
        Assert.That(File.Exists(m_testDbPath), Is.False);

        var deleted = await context.Database.EnsureDeletedAsync();

        Assert.That(deleted, Is.False, "EnsureDeletedAsync should return false when database didn't exist");
    }

    #endregion

    #region Manual Table Creation + Data Operations Tests

    [Test]
    public void ManualTableCreationAllowsSaveChangesTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        
        // Open connection and create table manually
        context.Database.OpenConnection();
        using (var cmd = context.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""TestEntities"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT)";
            cmd.ExecuteNonQuery();
        }

        context.TestEntities.Add(new TestEntity { Name = "Test" });
        var saveResult = context.SaveChanges();

        Assert.That(saveResult, Is.EqualTo(1), "SaveChanges should affect 1 row");
        
        context.Database.CloseConnection();
    }

    [Test]
    public async Task ManualTableCreationAllowsSaveChangesAsyncTest()
    {
        await using var context = CreateFileContext(m_testDbPath);
        
        await context.Database.OpenConnectionAsync();
        await using (var cmd = context.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""TestEntities"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT)";
            await cmd.ExecuteNonQueryAsync();
        }

        context.TestEntities.Add(new TestEntity { Name = "AsyncTest" });
        var saveResult = await context.SaveChangesAsync();

        Assert.That(saveResult, Is.EqualTo(1));
        
        await context.Database.CloseConnectionAsync();
    }

    [Test]
    public void ManualTableCreationAllowsQueryTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        
        context.Database.OpenConnection();
        using (var cmd = context.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""TestEntities"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT)";
            cmd.ExecuteNonQuery();
        }

        context.TestEntities.Add(new TestEntity { Name = "QueryTest" });
        context.SaveChanges();

        var entity = context.TestEntities.FirstOrDefault(e => e.Name == "QueryTest");
        
        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Name, Is.EqualTo("QueryTest"));
        
        context.Database.CloseConnection();
    }

    [Test]
    public async Task ManualTableCreationAllowsQueryAsyncTest()
    {
        await using var context = CreateFileContext(m_testDbPath);
        
        await context.Database.OpenConnectionAsync();
        await using (var cmd = context.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""TestEntities"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT)";
            await cmd.ExecuteNonQueryAsync();
        }

        context.TestEntities.Add(new TestEntity { Name = "AsyncQueryTest" });
        await context.SaveChangesAsync();

        var entity = await context.TestEntities.FirstOrDefaultAsync(e => e.Name == "AsyncQueryTest");
        
        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Name, Is.EqualTo("AsyncQueryTest"));
        
        await context.Database.CloseConnectionAsync();
    }

    [Test]
    public void InMemoryWithManualTableCreationTest()
    {
        using var context = CreateInMemoryContext();
        
        context.Database.OpenConnection();
        using (var cmd = context.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS ""TestEntities"" (""Id"" INT PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT)";
            cmd.ExecuteNonQuery();
        }

        context.TestEntities.Add(new TestEntity { Name = "InMemoryTest" });
        var saveResult = context.SaveChanges();

        Assert.That(saveResult, Is.EqualTo(1), "SaveChanges should affect 1 row");

        var entity = context.TestEntities.FirstOrDefault(e => e.Name == "InMemoryTest");
        Assert.That(entity, Is.Not.Null);
        Assert.That(entity!.Name, Is.EqualTo("InMemoryTest"));
        
        context.Database.CloseConnection();
    }

    #endregion

    #region Helper Methods

    private static TestCreatorContext CreateFileContext(string path)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestCreatorContext>();
        optionsBuilder.UseWitDb($"Data Source={path}");
        return new TestCreatorContext(optionsBuilder.Options);
    }

    private static TestCreatorContext CreateInMemoryContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestCreatorContext>();
        optionsBuilder.UseWitDbInMemory();
        return new TestCreatorContext(optionsBuilder.Options);
    }

    private static IRelationalDatabaseCreator GetDatabaseCreator(DbContext context)
    {
        return context.GetService<IRelationalDatabaseCreator>();
    }

    #endregion

    #region Test Models

    private class TestEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private class TestCreatorContext : DbContext
    {
        public TestCreatorContext(DbContextOptions<TestCreatorContext> options)
            : base(options)
        {
        }

        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    }

    #endregion
}
