namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests for INSERT performance and memory characteristics.
/// </summary>
[TestFixture]
public class InsertPerformanceTests : PerformanceTestsBase
{
    #region Constants

    private const int SMALL_BATCH = 100;
    private const int MEDIUM_BATCH = 500;

    // Memory thresholds
    private const long MAX_BYTES_PER_INSERT = 50_000; // 50KB per insert (including overhead)

    #endregion

    #region Single Insert Tests

    [Test]
    public void SingleInsertMemoryTest()
    {
        // Arrange
        CreateTestTable();

        // Act
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            m_engine.Execute(
                "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                new Dictionary<string, object?>
                {
                    { "@id", 1 },
                    { "@name", "Test" },
                    { "@value", 1.5 }
                });
        });

        // Assert
        Assert.That(allocatedBytes, Is.LessThan(MAX_BYTES_PER_INSERT * 2),
            $"Single INSERT allocated {allocatedBytes:N0} bytes");
    }

    #endregion

    #region Batch Insert Tests

    [Test]
    public void BatchInsertMemoryTest()
    {
        // Arrange
        CreateTestTable();

        // Act
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < SMALL_BATCH; i++)
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
        });

        // Assert
        var maxExpected = SMALL_BATCH * MAX_BYTES_PER_INSERT * 5; // Allow 5x overhead
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"Batch INSERT of {SMALL_BATCH} rows allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    [Test]
    public void BatchInsertMemoryGrowsLinearlyTest()
    {
        // Arrange & Act - Measure memory for small batch
        CreateTestTable();
        var bytesSmall = MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < SMALL_BATCH; i++)
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
        });

        // Clear table
        m_engine.Execute("DELETE FROM T");

        // Measure memory for 2x batch
        var bytesDouble = MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < SMALL_BATCH * 2; i++)
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
        });

        // Assert - Memory should grow roughly linearly, not quadratically
        // For 2x data, expect at most 4x memory (allowing for overhead)
        var ratio = (double)bytesDouble / bytesSmall;
        Assert.That(ratio, Is.LessThan(5),
            $"Memory ratio for 2x data is {ratio:F1}x (small={bytesSmall:N0}, double={bytesDouble:N0}). Expected < 5x.");
    }

    #endregion

    #region Insert Performance Tests

    [Test]
    public void BatchInsertPerformanceTest()
    {
        // Arrange
        CreateTestTable();

        // Act
        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < MEDIUM_BATCH; i++)
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
        });

        // Assert - Should complete in reasonable time
        // Target: < 10 seconds for 500 rows (lenient for now)
        Assert.That(elapsed.TotalSeconds, Is.LessThan(10),
            $"Batch INSERT of {MEDIUM_BATCH} rows took {elapsed.TotalSeconds:F2}s");
        
        // Verify all rows inserted
        using var result = m_engine.Execute("SELECT COUNT(*) FROM T");
        Assert.That(result.Read(), Is.True);
        Assert.That(result.CurrentRow[0].AsInt64(), Is.EqualTo(MEDIUM_BATCH));
    }

    #endregion

    #region Conflict Check Tests

    [Test]
    public void InsertWithExistingDataMemoryTest()
    {
        // This test documents the current behavior of conflict checking
        // After optimization, this test should pass with linear memory growth

        // Arrange
        CreateTestTable();
        
        // Insert some initial rows
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

        // Act - Insert more rows (this triggers conflict check)
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            for (int i = 50; i < 100; i++)
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
        });

        // Assert - Memory should be bounded
        var maxExpected = 50 * MAX_BYTES_PER_INSERT * 30; // Very lenient for now
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"50 INSERTs with conflict check allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    #endregion
}
