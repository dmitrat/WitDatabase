using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for basic DbContext operations with WitDatabase.
/// </summary>
[TestFixture]
public class BasicDbContextTests
{
    #region Fields

    private string? m_testDbPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEf_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDbPath != null && File.Exists(m_testDbPath))
        {
            try { File.Delete(m_testDbPath); } catch { }
        }
    }

    #endregion

    #region DbContext Creation Tests

    [Test]
    public void CreateDbContextWithConnectionStringSucceedsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");

        using var context = new TestDbContext(optionsBuilder.Options);

        Assert.That(context, Is.Not.Null);
        Assert.That(context.Database, Is.Not.Null);
    }

    [Test]
    public void CreateDbContextInMemorySucceedsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);

        Assert.That(context, Is.Not.Null);
        Assert.That(context.Database, Is.Not.Null);
    }

    [Test]
    [Ignore("Requires full provider implementation with database creation")]
    public void DatabaseProviderNameIsCorrectTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);

        Assert.That(context.Database.ProviderName, Is.EqualTo(WitDatabaseProvider.PROVIDER_NAME));
    }

    #endregion

    #region Model Tests

    [Test]
    [Ignore("Requires full provider implementation with database creation")]
    public void DbContextModelContainsEntityTypesTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var model = context.Model;

        Assert.That(model.GetEntityTypes().Any(e => e.ClrType == typeof(TestEntity)), Is.True);
    }

    #endregion

    #region Test Models

    public class TestEntity
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<TestEntity> TestEntities => Set<TestEntity>();
    }

    #endregion
}
