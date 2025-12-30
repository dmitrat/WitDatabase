namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests to verify there are no memory leaks in repeated operations.
/// </summary>
[TestFixture]
public class MemoryLeakTests : PerformanceTestsBase
{
    #region Constants

    private const int TABLE_SIZE = 100;
    private const int ITERATIONS = 100;

    #endregion

    #region Repeated Query Tests

    [Test]
    public void RepeatedQueriesDoNotLeakMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(TABLE_SIZE);
        
        // Warm up
        for (int i = 0; i < 10; i++)
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read()) { }
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Measure baseline
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Act - Run many queries
        for (int i = 0; i < ITERATIONS; i++)
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read()) { }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        var memoryGrowth = finalMemory - baselineMemory;

        // Assert - Memory growth should be bounded (not proportional to iterations)
        // Allow some growth for caching, but not linear growth
        var maxGrowth = 10_000_000L; // 10MB max growth allowed
        Assert.That(memoryGrowth, Is.LessThan(maxGrowth),
            $"Memory grew by {memoryGrowth:N0} bytes after {ITERATIONS} queries. Possible leak.");
    }

    [Test]
    public void RepeatedParameterizedQueriesDoNotLeakMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(TABLE_SIZE);
        var random = new Random(42);
        
        // Warm up
        for (int i = 0; i < 10; i++)
        {
            using var result = m_engine.Execute(
                "SELECT * FROM T WHERE Id = @id",
                new Dictionary<string, object?> { { "@id", random.Next(TABLE_SIZE) } });
            while (result.Read()) { }
        }
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Act - Run many parameterized queries
        for (int i = 0; i < ITERATIONS; i++)
        {
            using var result = m_engine.Execute(
                "SELECT * FROM T WHERE Id = @id",
                new Dictionary<string, object?> { { "@id", random.Next(TABLE_SIZE) } });
            while (result.Read()) { }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        var memoryGrowth = finalMemory - baselineMemory;

        // Assert
        var maxGrowth = 10_000_000L;
        Assert.That(memoryGrowth, Is.LessThan(maxGrowth),
            $"Memory grew by {memoryGrowth:N0} bytes after {ITERATIONS} parameterized queries. Possible leak.");
    }

    [Test]
    public void RepeatedInsertsDoNotLeakMemoryTest()
    {
        // Arrange
        CreateTestTable();
        
        // Warm up
        for (int i = 0; i < 10; i++)
        {
            m_engine.Execute(
                "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                new Dictionary<string, object?>
                {
                    { "@id", i },
                    { "@name", $"Name{i}" },
                    { "@value", i * 1.5 }
                });
        }
        
        m_engine.Execute("DELETE FROM T");
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Act - Run many inserts and deletes
        for (int batch = 0; batch < 10; batch++)
        {
            for (int i = 0; i < 50; i++)
            {
                m_engine.Execute(
                    "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
            m_engine.Execute("DELETE FROM T");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        var memoryGrowth = finalMemory - baselineMemory;

        // Assert
        var maxGrowth = 20_000_000L; // 20MB - INSERT is more memory intensive
        Assert.That(memoryGrowth, Is.LessThan(maxGrowth),
            $"Memory grew by {memoryGrowth:N0} bytes after repeated INSERTs. Possible leak.");
    }

    #endregion

    #region Result Disposal Tests

    [Test]
    public void UndisposedResultsDoNotCauseLeaksTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(TABLE_SIZE);
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);

        // Act - Create results but let GC clean them up
        for (int i = 0; i < ITERATIONS; i++)
        {
            // Not using 'using' - relying on GC
            var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read()) { }
            // result goes out of scope here
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        var memoryGrowth = finalMemory - baselineMemory;

        // Assert - Even without explicit disposal, GC should clean up
        var maxGrowth = 15_000_000L;
        Assert.That(memoryGrowth, Is.LessThan(maxGrowth),
            $"Memory grew by {memoryGrowth:N0} bytes with undisposed results. GC not cleaning up properly.");
    }

    #endregion
}
