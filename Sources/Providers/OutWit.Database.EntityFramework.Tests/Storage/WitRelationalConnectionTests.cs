using Microsoft.EntityFrameworkCore;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Storage;

namespace OutWit.Database.EntityFramework.Tests.Storage;

/// <summary>
/// Unit tests for WitRelationalConnection.
/// </summary>
[TestFixture]
public class WitRelationalConnectionTests
{
    #region Fields

    private string? m_testDbPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbConn_{Guid.NewGuid():N}.witdb");
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

    #region Connection String Tests

    [Test]
    public void ConnectionStringIsStoredCorrectlyTest()
    {
        var connectionString = $"Data Source={m_testDbPath}";
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDb(connectionString);

        using var context = new TestDbContext(optionsBuilder.Options);
        var connection = context.Database.GetDbConnection();

        Assert.That(connection.ConnectionString, Does.Contain(m_testDbPath!));
    }

    [Test]
    public void InMemoryConnectionWorksTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        Assert.DoesNotThrow(() => context.Database.OpenConnection());
        Assert.DoesNotThrow(() => context.Database.CloseConnection());
    }

    #endregion

    #region DbConnection Tests

    [Test]
    public void GetDbConnectionReturnsWitDbConnectionTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        var connection = context.Database.GetDbConnection();

        Assert.That(connection, Is.InstanceOf<WitDbConnection>());
    }

    [Test]
    public void OpenConnectionSucceedsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        using var context = new TestDbContext(optionsBuilder.Options);
        
        context.Database.OpenConnection();
        
        Assert.That(context.Database.GetDbConnection().State, Is.EqualTo(System.Data.ConnectionState.Open));
        
        context.Database.CloseConnection();
    }

    [Test]
    public async Task OpenConnectionAsyncSucceedsTest()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDbInMemory();

        await using var context = new TestDbContext(optionsBuilder.Options);
        
        await context.Database.OpenConnectionAsync();
        
        Assert.That(context.Database.GetDbConnection().State, Is.EqualTo(System.Data.ConnectionState.Open));
        
        await context.Database.CloseConnectionAsync();
    }

    #endregion

    #region Existing Connection Tests

    [Test]
    public void UseExistingConnectionWorksTest()
    {
        using var existingConnection = new WitDbConnection("Data Source=:memory:");
        existingConnection.Open();

        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        optionsBuilder.UseWitDb(existingConnection);

        using var context = new TestDbContext(optionsBuilder.Options);
        var connection = context.Database.GetDbConnection();

        Assert.That(connection, Is.SameAs(existingConnection));
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
