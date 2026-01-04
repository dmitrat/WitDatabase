using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Migrations;

namespace OutWit.Database.EntityFramework.Tests.Migrations;

/// <summary>
/// Unit tests for <see cref="WitHistoryRepository"/>.
/// </summary>
[TestFixture]
public class WitHistoryRepositoryTests
{
    #region Fields

    private WitHistoryRepository m_repository = null!;
    private TestMigrationContext m_context = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestMigrationContext>();
        optionsBuilder.UseWitDbInMemory();
        
        m_context = new TestMigrationContext(optionsBuilder.Options);
        var dependencies = m_context.GetService<HistoryRepositoryDependencies>();
        
        m_repository = new WitHistoryRepository(dependencies);
    }

    [TearDown]
    public void TearDown()
    {
        m_context?.Dispose();
    }

    #endregion

    #region GetCreateScript Tests

    [Test]
    public void GetCreateScriptReturnsValidSqlTest()
    {
        var script = m_repository.GetCreateScript();

        Assert.That(script, Is.Not.Null.And.Not.Empty);
        Assert.That(script, Does.Contain("CREATE TABLE"));
        Assert.That(script, Does.Contain("IF NOT EXISTS"));
        Assert.That(script, Does.Contain("__EFMigrationsHistory"));
        Assert.That(script, Does.Contain("MigrationId"));
        Assert.That(script, Does.Contain("ProductVersion"));
        Assert.That(script, Does.Contain("PRIMARY KEY"));
    }

    [Test]
    public void GetCreateScriptContainsCorrectColumnTypesTest()
    {
        var script = m_repository.GetCreateScript();

        Assert.That(script, Does.Contain("VARCHAR(150)"));
        Assert.That(script, Does.Contain("VARCHAR(32)"));
    }

    [Test]
    public void GetCreateIfNotExistsScriptReturnsSameAsCreateScriptTest()
    {
        var createScript = m_repository.GetCreateScript();
        var createIfNotExistsScript = m_repository.GetCreateIfNotExistsScript();

        Assert.That(createIfNotExistsScript, Is.EqualTo(createScript));
    }

    #endregion

    #region GetInsertScript Tests

    [Test]
    public void GetInsertScriptReturnsValidInsertStatementTest()
    {
        var row = new HistoryRow("20250101000000_InitialMigration", "9.0.0");
        
        var script = m_repository.GetInsertScript(row);

        Assert.That(script, Is.Not.Null.And.Not.Empty);
        // Uses INSERT OR IGNORE for idempotent migrations
        Assert.That(script, Does.Contain("INSERT").Or.Contain("INSERT OR IGNORE"));
        Assert.That(script, Does.Contain("__EFMigrationsHistory"));
        Assert.That(script, Does.Contain("20250101000000_InitialMigration"));
        Assert.That(script, Does.Contain("9.0.0"));
    }

    [Test]
    public void GetInsertScriptEscapesSingleQuotesTest()
    {
        var row = new HistoryRow("20250101000000_It's_A_Migration", "9.0.0");
        
        var script = m_repository.GetInsertScript(row);

        Assert.That(script, Does.Contain("It''s_A_Migration"));
    }

    #endregion

    #region GetDeleteScript Tests

    [Test]
    public void GetDeleteScriptReturnsValidDeleteStatementTest()
    {
        var script = m_repository.GetDeleteScript("20250101000000_InitialMigration");

        Assert.That(script, Is.Not.Null.And.Not.Empty);
        Assert.That(script, Does.Contain("DELETE FROM"));
        Assert.That(script, Does.Contain("__EFMigrationsHistory"));
        Assert.That(script, Does.Contain("20250101000000_InitialMigration"));
    }

    [Test]
    public void GetDeleteScriptEscapesSingleQuotesTest()
    {
        var script = m_repository.GetDeleteScript("20250101000000_It's_A_Migration");

        Assert.That(script, Does.Contain("It''s_A_Migration"));
    }

    #endregion

    #region Conditional Script Tests

    [Test]
    public void GetBeginIfNotExistsScriptReturnsEmptyStringTest()
    {
        var script = m_repository.GetBeginIfNotExistsScript("20250101000000_Migration");

        Assert.That(script, Is.Empty);
    }

    [Test]
    public void GetBeginIfExistsScriptReturnsEmptyStringTest()
    {
        var script = m_repository.GetBeginIfExistsScript("20250101000000_Migration");

        Assert.That(script, Is.Empty);
    }

    [Test]
    public void GetEndIfScriptReturnsEmptyStringTest()
    {
        var script = m_repository.GetEndIfScript();

        Assert.That(script, Is.Empty);
    }

    #endregion

    #region Lock Tests

    [Test]
    public void LockReleaseBehaviorIsExplicitTest()
    {
        Assert.That(m_repository.LockReleaseBehavior, Is.EqualTo(LockReleaseBehavior.Explicit));
    }

    [Test]
    public void AcquireDatabaseLockReturnsValidLockTest()
    {
        using var dbLock = m_repository.AcquireDatabaseLock();

        Assert.That(dbLock, Is.Not.Null);
        Assert.That(dbLock, Is.InstanceOf<IMigrationsDatabaseLock>());
    }

    [Test]
    public async Task AcquireDatabaseLockAsyncReturnsValidLockTest()
    {
        await using var dbLock = await m_repository.AcquireDatabaseLockAsync();

        Assert.That(dbLock, Is.Not.Null);
        Assert.That(dbLock, Is.InstanceOf<IMigrationsDatabaseLock>());
    }

    [Test]
    public void AcquireDatabaseLockDisposeDoesNotThrowTest()
    {
        var dbLock = m_repository.AcquireDatabaseLock();
        
        Assert.DoesNotThrow(() => dbLock.Dispose());
    }

    [Test]
    public async Task AcquireDatabaseLockAsyncDisposeDoesNotThrowTest()
    {
        var dbLock = await m_repository.AcquireDatabaseLockAsync();
        
        Assert.DoesNotThrowAsync(async () => await dbLock.DisposeAsync());
    }

    [Test]
    public void AcquireDatabaseLockMultipleTimesDoesNotThrowTest()
    {
        using var lock1 = m_repository.AcquireDatabaseLock();
        using var lock2 = m_repository.AcquireDatabaseLock();

        Assert.That(lock1, Is.Not.Null);
        Assert.That(lock2, Is.Not.Null);
    }

    #endregion

    #region Test Models

    private class TestMigrationContext : DbContext
    {
        public TestMigrationContext(DbContextOptions<TestMigrationContext> options) 
            : base(options)
        {
        }
    }

    #endregion
}
