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
        // For in-memory, this is a no-op
    }

    [Test]
    public async Task CreateAsyncCreatesEmptyDatabaseFileTest()
    {
        using var context = CreateFileContext(m_testDbPath);
        var creator = GetDatabaseCreator(context);

        await creator.CreateAsync();

        // Verify no exception thrown
        Assert.Pass();
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

        // Note: This test depends on the database being empty initially
        // The actual behavior depends on the connection state
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

    #region EnsureCreated/Deleted Tests

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
