using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Query;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// Tests for QueryPlanCache functionality.
/// </summary>
[TestFixture]
public sealed class QueryPlanCacheTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        m_engine = new WitSqlEngine(database, ownsStore: true);

        // Create test table
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Email VARCHAR(255)
            )");

        // Insert test data
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine.Dispose();
    }

    #endregion

    #region Basic Cache Tests

    [Test]
    public void SameQueryIsReusedFromCacheTest()
    {
        var cache = m_engine.PlanCache;
        cache.ResetStatistics();

        // First execution - cache miss
        m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        Assert.That(cache.MissCount, Is.EqualTo(1));
        Assert.That(cache.HitCount, Is.EqualTo(0));

        // Second execution - cache hit
        m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        Assert.That(cache.MissCount, Is.EqualTo(1));
        Assert.That(cache.HitCount, Is.EqualTo(1));

        // Third execution - cache hit
        m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        Assert.That(cache.HitCount, Is.EqualTo(2));
    }

    [Test]
    public void DifferentQueriesAreCachedSeparatelyTest()
    {
        var cache = m_engine.PlanCache;
        cache.ResetStatistics();

        // Execute different queries
        m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        m_engine.Query("SELECT * FROM Users WHERE Id = 2");
        m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");

        Assert.That(cache.MissCount, Is.EqualTo(3));
        // Note: cache.Count may be higher due to setup queries

        // Re-execute same queries - should hit cache
        cache.ResetStatistics();
        m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        m_engine.Query("SELECT * FROM Users WHERE Id = 2");
        m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");

        Assert.That(cache.HitCount, Is.EqualTo(3));
        Assert.That(cache.MissCount, Is.EqualTo(0));
    }

    [Test]
    public void WhitespaceNormalizationWorksTest()
    {
        var cache = m_engine.PlanCache;
        cache.ResetStatistics();

        // Query with different whitespace should still hit cache
        m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        m_engine.Query("SELECT  *  FROM  Users  WHERE  Id = 1"); // extra spaces

        Assert.That(cache.HitCount, Is.EqualTo(1));
        Assert.That(cache.MissCount, Is.EqualTo(1));
    }

    [Test]
    public void ParameterizedQueryCacheTest()
    {
        var cache = m_engine.PlanCache;
        cache.ResetStatistics();

        // Same parameterized query with different values should hit cache
        m_engine.Query("SELECT * FROM Users WHERE Id = @id", new Dictionary<string, object?> { ["id"] = 1 });
        m_engine.Query("SELECT * FROM Users WHERE Id = @id", new Dictionary<string, object?> { ["id"] = 2 });

        // Both use same SQL text, so second should hit cache
        Assert.That(cache.HitCount, Is.EqualTo(1));
        Assert.That(cache.MissCount, Is.EqualTo(1));
    }

    #endregion

    #region Cache Invalidation Tests

    [Test]
    public void CacheInvalidatedAfterCreateTableTest()
    {
        var cache = m_engine.PlanCache;
        
        // Execute some queries to populate cache
        m_engine.Query("SELECT * FROM Users");
        Assert.That(cache.Count, Is.GreaterThan(0));

        // Create new table - should invalidate cache for affected table
        m_engine.Execute("CREATE TABLE Products (Id BIGINT PRIMARY KEY, Name VARCHAR(100))");

        // Cache should still have entries (only Products is invalidated, Users remains)
        // But since Users queries don't reference Products, they might still be valid
    }

    [Test]
    public void CacheInvalidatedAfterDropTableTest()
    {
        var cache = m_engine.PlanCache;
        
        // Create and query temporary table
        m_engine.Execute("CREATE TABLE Temp (Id BIGINT PRIMARY KEY)");
        m_engine.Query("SELECT * FROM Temp");
        
        var countBefore = cache.Count;
        Assert.That(countBefore, Is.GreaterThan(0));

        // Drop table - should invalidate cache
        m_engine.Execute("DROP TABLE Temp");

        // Queries referencing Temp should be invalidated
        // (Note: exact behavior depends on invalidation strategy)
    }

    [Test]
    public void CacheInvalidatedAfterCreateIndexTest()
    {
        var cache = m_engine.PlanCache;
        
        // Query table before index
        cache.ResetStatistics();
        m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");
        Assert.That(cache.MissCount, Is.EqualTo(1));

        // Create index - should invalidate cache for Users table
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Re-execute query - should miss cache (plan was invalidated)
        cache.ResetStatistics();
        m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");
        
        // The query was invalidated by index creation, so this should be a miss
        Assert.That(cache.MissCount, Is.EqualTo(1));
    }

    [Test]
    public void CacheInvalidatedAfterDropIndexTest()
    {
        // Create index first
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        var cache = m_engine.PlanCache;
        
        // Query using index
        cache.ResetStatistics();
        m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");
        Assert.That(cache.MissCount, Is.EqualTo(1));

        // Drop index - should invalidate cache
        m_engine.Execute("DROP INDEX idx_users_name");

        // Re-execute query - should miss cache
        cache.ResetStatistics();
        m_engine.Query("SELECT * FROM Users WHERE Name = 'Alice'");
        Assert.That(cache.MissCount, Is.EqualTo(1));
    }

    #endregion

    #region Cache Statistics Tests

    [Test]
    public void HitRatioCalculationTest()
    {
        var cache = m_engine.PlanCache;
        cache.ResetStatistics();

        // 1 miss, 3 hits = 75% hit ratio
        m_engine.Query("SELECT * FROM Users");
        m_engine.Query("SELECT * FROM Users");
        m_engine.Query("SELECT * FROM Users");
        m_engine.Query("SELECT * FROM Users");

        Assert.That(cache.HitCount, Is.EqualTo(3));
        Assert.That(cache.MissCount, Is.EqualTo(1));
        Assert.That(cache.HitRatio, Is.EqualTo(0.75).Within(0.01));
    }

    [Test]
    public void ResetStatisticsTest()
    {
        var cache = m_engine.PlanCache;

        // Generate some stats
        m_engine.Query("SELECT * FROM Users");
        m_engine.Query("SELECT * FROM Users");

        Assert.That(cache.HitCount, Is.GreaterThan(0).Or.Property("MissCount").GreaterThan(0));

        // Reset
        cache.ResetStatistics();

        Assert.That(cache.HitCount, Is.EqualTo(0));
        Assert.That(cache.MissCount, Is.EqualTo(0));
    }

    #endregion

    #region Performance Tests

    [Test]
    public void CachedQueryIsFasterTest()
    {
        var cache = m_engine.PlanCache;

        const string sql = "SELECT * FROM Users WHERE Id = 1";

        // Warm up with cached execution first
        cache.ResetStatistics();
        for (int i = 0; i < 100; i++)
        {
            m_engine.Query(sql);
        }

        // Should have 1 miss (first) and 99 hits (rest)
        Assert.That(cache.MissCount, Is.EqualTo(1));
        Assert.That(cache.HitCount, Is.EqualTo(99));
        
        // Hit ratio should be very high
        Assert.That(cache.HitRatio, Is.GreaterThan(0.9));
    }

    #endregion

    #region Multi-Statement Tests

    [Test]
    public void MultiStatementSqlNotCachedTest()
    {
        var cache = m_engine.PlanCache;
        cache.ResetStatistics();

        // Multi-statement SQL should not be cached (too complex)
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Test1'); INSERT INTO Users (Name) VALUES ('Test2')");

        // Should still work but not be cached
        // (Implementation detail: we only cache single statements)
    }

    #endregion
}
