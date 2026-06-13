namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Tests for SELECT query memory and performance characteristics.
/// These tests verify that streaming works correctly and memory usage is bounded.
/// </summary>
[TestFixture]
[Category("Performance")]
public class SelectPerformanceTests : PerformanceTestsBase
{
    #region Constants

    private const int SMALL_TABLE_SIZE = 100;
    private const int MEDIUM_TABLE_SIZE = 1000;
    private const int LARGE_TABLE_SIZE = 5000;

    // Memory thresholds per row (approximate)
    private const long MAX_BYTES_PER_ROW_STREAMING = 1500; // ~1.5KB per row when streaming
    private const long MAX_BYTES_OVERHEAD_STREAMING = 100_000; // Fixed overhead for query setup

    #endregion

    #region Full Scan Tests

    [Test]
    public void FullScanSmallTableMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(SMALL_TABLE_SIZE);

        // Act
        int rowCount = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read())
            {
                rowCount++;
                // Access values to ensure they're materialized
                _ = result.CurrentRow[0];
            }
        });

        // Assert
        Assert.That(rowCount, Is.EqualTo(SMALL_TABLE_SIZE));
        
        // Memory should be reasonable - streaming means we don't hold all rows
        var maxExpected = MAX_BYTES_OVERHEAD_STREAMING + (SMALL_TABLE_SIZE * MAX_BYTES_PER_ROW_STREAMING);
        Assert.That(allocatedBytes, Is.LessThan(maxExpected), 
            $"Full scan of {SMALL_TABLE_SIZE} rows allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    [Test]
    public void FullScanMediumTableMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(MEDIUM_TABLE_SIZE);

        // Act
        int rowCount = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read())
            {
                rowCount++;
                _ = result.CurrentRow[0];
            }
        });

        // Assert
        Assert.That(rowCount, Is.EqualTo(MEDIUM_TABLE_SIZE));
        
        var maxExpected = MAX_BYTES_OVERHEAD_STREAMING + (MEDIUM_TABLE_SIZE * MAX_BYTES_PER_ROW_STREAMING);
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"Full scan of {MEDIUM_TABLE_SIZE} rows allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    [Test]
    public void FullScanLargeTableMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(LARGE_TABLE_SIZE);

        // Act
        int rowCount = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read())
            {
                rowCount++;
                _ = result.CurrentRow[0];
            }
        });

        // Assert
        Assert.That(rowCount, Is.EqualTo(LARGE_TABLE_SIZE));
        
        var maxExpected = MAX_BYTES_OVERHEAD_STREAMING + (LARGE_TABLE_SIZE * MAX_BYTES_PER_ROW_STREAMING);
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"Full scan of {LARGE_TABLE_SIZE} rows allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    [Test]
    public void FullScanMemoryDoesNotGrowQuadraticallyTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(MEDIUM_TABLE_SIZE);

        // Measure memory for medium table
        int rowCount1 = 0;
        var bytes1 = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read()) rowCount1++;
        });

        // Clear and add more rows
        m_engine.Execute("DELETE FROM T");
        InsertRows(MEDIUM_TABLE_SIZE * 2);

        // Measure memory for 2x table
        int rowCount2 = 0;
        var bytes2 = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read()) rowCount2++;
        });

        // Assert
        Assert.That(rowCount1, Is.EqualTo(MEDIUM_TABLE_SIZE));
        Assert.That(rowCount2, Is.EqualTo(MEDIUM_TABLE_SIZE * 2));
        
        // Memory should grow roughly linearly, not quadratically
        // Allow 3x growth for 2x data (some overhead is expected)
        Assert.That(bytes2, Is.LessThan(bytes1 * 3),
            $"Memory grew from {bytes1:N0} to {bytes2:N0} bytes for 2x data. Should be < 3x.");
    }

    #endregion

    #region Point Query Tests

    [Test]
    public void PointQueryMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(MEDIUM_TABLE_SIZE);

        // Act - Single point query
        int rowCount = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute(
                "SELECT * FROM T WHERE Id = @id",
                new Dictionary<string, object?> { { "@id", 500 } });
            while (result.Read()) rowCount++;
        });

        // Assert
        Assert.That(rowCount, Is.EqualTo(1));
        
        // Point query should use minimal memory regardless of table size
        // Currently doing full scan, so allowing higher threshold for now
        var maxExpected = 2_000_000L; // 2MB - TODO: reduce after index optimization
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"Point query allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    [Test]
    public void MultiplePointQueriesMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(MEDIUM_TABLE_SIZE);
        var random = new Random(42);
        var ids = Enumerable.Range(0, 50).Select(_ => random.Next(MEDIUM_TABLE_SIZE)).ToArray();

        // Act - 50 point queries
        int totalRows = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            foreach (var id in ids)
            {
                using var result = m_engine.Execute(
                    "SELECT * FROM T WHERE Id = @id",
                    new Dictionary<string, object?> { { "@id", id } });
                while (result.Read()) totalRows++;
            }
        });

        // Assert
        Assert.That(totalRows, Is.EqualTo(50));
        
        // 50 point queries - allow more memory until index optimization
        var maxExpected = 100_000_000L; // 100MB for now
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"50 point queries allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    #endregion

    #region Aggregation Tests

    [Test]
    public void CountAggregationMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(LARGE_TABLE_SIZE);

        // Act
        long count = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT COUNT(*) FROM T");
            if (result.Read())
            {
                count = result.CurrentRow[0].AsInt64();
            }
        });

        // Assert
        Assert.That(count, Is.EqualTo(LARGE_TABLE_SIZE));
        
        // COUNT should stream and accumulate, not materialize all rows
        var maxExpected = 10_000_000L; // 10MB should be enough
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"COUNT(*) on {LARGE_TABLE_SIZE} rows allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    [Test]
    public void SumAggregationMemoryTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(LARGE_TABLE_SIZE);

        // Act
        double sum = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT SUM(Value) FROM T");
            if (result.Read())
            {
                sum = result.CurrentRow[0].AsDouble();
            }
        });

        // Assert
        var expectedSum = Enumerable.Range(0, LARGE_TABLE_SIZE).Sum(i => i * 1.5);
        Assert.That(sum, Is.EqualTo(expectedSum).Within(0.001));
        
        var maxExpected = 15_000_000L; // 15MB
        Assert.That(allocatedBytes, Is.LessThan(maxExpected),
            $"SUM on {LARGE_TABLE_SIZE} rows allocated {allocatedBytes:N0} bytes, expected < {maxExpected:N0}");
    }

    #endregion

    #region ReadAll Tests

    [Test]
    public void ReadAllMaterializesAllRowsTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(SMALL_TABLE_SIZE);

        // Act
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            var allRows = result.ReadAll();
            Assert.That(allRows, Has.Count.EqualTo(SMALL_TABLE_SIZE));
        });

        // Assert - ReadAll is expected to allocate more than streaming
        // This test documents the expected behavior
        Assert.That(allocatedBytes, Is.GreaterThan(0));
    }

    #endregion

    #region Early Exit Tests

    [Test]
    public void EarlyExitDoesNotReadAllRowsTest()
    {
        // Arrange
        CreateTestTable();
        InsertRows(LARGE_TABLE_SIZE);

        // Act - Read only first 10 rows
        int rowCount = 0;
        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            using var result = m_engine.Execute("SELECT * FROM T");
            while (result.Read() && rowCount < 10)
            {
                rowCount++;
            }
        });

        // Assert
        Assert.That(rowCount, Is.EqualTo(10));
        
        // Should allocate much less than full scan
        var fullScanEstimate = LARGE_TABLE_SIZE * MAX_BYTES_PER_ROW_STREAMING;
        Assert.That(allocatedBytes, Is.LessThan(fullScanEstimate / 2),
            $"Early exit allocated {allocatedBytes:N0} bytes, expected much less than full scan estimate {fullScanEstimate:N0}");
    }

    #endregion
}
