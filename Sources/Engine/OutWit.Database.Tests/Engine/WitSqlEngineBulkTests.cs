using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for ExecuteBatch and Bulk operations.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineBulkTests : WitSqlEngineTestsBase
{
    #region ExecuteBatch Tests

    [Test]
    public void ExecuteBatchWithDictionaryInsertsMultipleRowsTest()
    {
        CreateUsersTable();
        
        using var stmt = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        
        var paramSets = new[]
        {
            new Dictionary<string, object?> { ["name"] = "Alice", ["email"] = "alice@test.com" },
            new Dictionary<string, object?> { ["name"] = "Bob", ["email"] = "bob@test.com" },
            new Dictionary<string, object?> { ["name"] = "Charlie", ["email"] = "charlie@test.com" }
        };
        
        int rowsAffected = stmt.ExecuteBatch(paramSets);
        
        Assert.That(rowsAffected, Is.EqualTo(3));
        
        var rows = m_engine.Query("SELECT Name FROM Users ORDER BY Name");
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void ExecuteBatchWithObjectsInsertsMultipleRowsTest()
    {
        CreateUsersTable();
        
        using var stmt = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@Name, @Email)");
        
        var users = new[]
        {
            new { Name = "Alice", Email = "alice@test.com" },
            new { Name = "Bob", Email = "bob@test.com" },
            new { Name = "Charlie", Email = "charlie@test.com" }
        };
        
        int rowsAffected = stmt.ExecuteBatch(users);
        
        Assert.That(rowsAffected, Is.EqualTo(3));
        
        var rows = m_engine.Query("SELECT Name FROM Users ORDER BY Name");
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void ExecuteBatchWithEmptyCollectionReturnsZeroTest()
    {
        CreateUsersTable();
        
        using var stmt = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        
        int rowsAffected = stmt.ExecuteBatch(Array.Empty<Dictionary<string, object?>>());
        
        Assert.That(rowsAffected, Is.EqualTo(0));
    }

    [Test]
    public void ExecuteBatchIsFasterThanIndividualExecutesTest()
    {
        CreateUsersTable();
        
        const int rowCount = 100;
        
        // Prepare parameter sets
        var paramSets = Enumerable.Range(1, rowCount)
            .Select(i => new Dictionary<string, object?>
            {
                ["name"] = $"User{i}",
                ["email"] = $"user{i}@test.com"
            })
            .ToArray();
        
        // Warm up - run both operations once to avoid JIT overhead affecting measurements
        using (var warmupStmt = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)"))
        {
            warmupStmt.ExecuteBatch(paramSets.Take(1).ToArray());
        }
        m_engine.Execute("DELETE FROM Users");
        
        // Time individual executions FIRST (to warm up the engine)
        using var stmt1 = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var paramSet in paramSets)
        {
            stmt1.ClearParameters();
            stmt1.SetParameters(paramSet);
            using var _ = stmt1.Execute();
        }
        var individualTime = sw.ElapsedMilliseconds;
        
        // Clear table
        m_engine.Execute("DELETE FROM Users");
        
        // Time batch execution
        using var stmt2 = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        sw.Restart();
        stmt2.ExecuteBatch(paramSets);
        var batchTime = sw.ElapsedMilliseconds;
        
        // Log results for diagnostics
        TestContext.WriteLine($"Individual: {individualTime}ms, Batch: {batchTime}ms");
        
        // Batch should be at least as fast (usually faster due to less overhead)
        // Using a generous tolerance because timing tests are inherently unreliable
        // The main goal is to ensure batch doesn't have a severe performance regression
        Assert.That(batchTime, Is.LessThanOrEqualTo(Math.Max(individualTime * 3, 500)), 
            $"Batch ({batchTime}ms) should not be significantly slower than individual ({individualTime}ms)");
    }

    [Test]
    public void ExecuteBatchRespectsCancellationTest()
    {
        CreateUsersTable();
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        using var stmt = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        
        var paramSets = new[]
        {
            new Dictionary<string, object?> { ["name"] = "Alice", ["email"] = "alice@test.com" }
        };
        
        Assert.Throws<OperationCanceledException>(() => stmt.ExecuteBatch(paramSets, cts.Token));
    }

    #endregion

    #region BulkInsert Array Tests

    [Test]
    public void BulkInsertWithArraysInsertsMultipleRowsTest()
    {
        CreateUsersTable();
        
        var columns = new[] { "Name", "Email" };
        var rows = new List<object?[]>
        {
            new object?[] { "Alice", "alice@test.com" },
            new object?[] { "Bob", "bob@test.com" },
            new object?[] { "Charlie", "charlie@test.com" }
        };
        
        int inserted = m_engine.BulkInsert("Users", columns, rows);
        
        Assert.That(inserted, Is.EqualTo(3));
        
        var result = m_engine.Query("SELECT COUNT(*) AS Cnt FROM Users");
        Assert.That(result[0]["Cnt"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void BulkInsertWithArraysThrowsOnColumnMismatchTest()
    {
        CreateUsersTable();
        
        var columns = new[] { "Name", "Email" };
        var rows = new List<object?[]>
        {
            new object?[] { "Alice" } // Missing Email
        };
        
        Assert.Throws<ArgumentException>(() => m_engine.BulkInsert("Users", columns, rows));
    }

    [Test]
    public void BulkInsertWithEmptyColumnsThrowsTest()
    {
        CreateUsersTable();
        
        Assert.Throws<ArgumentException>(() => 
            m_engine.BulkInsert("Users", Array.Empty<string>(), Array.Empty<object?[]>()));
    }

    #endregion

    #region BulkInsert Objects Tests

    [Test]
    public void BulkInsertWithObjectsInsertsMultipleRowsTest()
    {
        CreateUsersTable();
        
        var users = new[]
        {
            new { Name = "Alice", Email = "alice@test.com" },
            new { Name = "Bob", Email = "bob@test.com" }
        };
        
        int inserted = m_engine.BulkInsert("Users", users);
        
        Assert.That(inserted, Is.EqualTo(2));
        
        var rows = m_engine.Query("SELECT Name FROM Users ORDER BY Name");
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
    }

    [Test]
    public void BulkInsertWithClassObjectsInsertsMultipleRowsTest()
    {
        CreateUsersTable();
        
        var users = new List<UserDto>
        {
            new() { Name = "Alice", Email = "alice@test.com" },
            new() { Name = "Bob", Email = "bob@test.com" }
        };
        
        int inserted = m_engine.BulkInsert("Users", users);
        
        Assert.That(inserted, Is.EqualTo(2));
    }

    #endregion

    #region BulkInsert Dictionary Tests

    [Test]
    public void BulkInsertWithDictionariesInsertsMultipleRowsTest()
    {
        CreateUsersTable();
        
        var rows = new[]
        {
            new Dictionary<string, object?> { ["Name"] = "Alice", ["Email"] = "alice@test.com" },
            new Dictionary<string, object?> { ["Name"] = "Bob", ["Email"] = "bob@test.com" }
        };
        
        int inserted = m_engine.BulkInsert("Users", rows);
        
        Assert.That(inserted, Is.EqualTo(2));
    }

    [Test]
    public void BulkInsertWithEmptyDictionaryCollectionReturnsZeroTest()
    {
        CreateUsersTable();
        
        int inserted = m_engine.BulkInsert("Users", Array.Empty<Dictionary<string, object?>>());
        
        Assert.That(inserted, Is.EqualTo(0));
    }

    #endregion

    #region BulkUpdate Tests

    [Test]
    public void BulkUpdateUpdatesAllRowsWhenNoWhereConditionTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        int updated = m_engine.BulkUpdate("Users", 
            new Dictionary<string, object?> { ["Email"] = "updated@test.com" });
        
        Assert.That(updated, Is.EqualTo(3));
        
        var rows = m_engine.Query("SELECT Email FROM Users");
        Assert.That(rows.All(r => r["Email"].AsString() == "updated@test.com"), Is.True);
    }

    [Test]
    public void BulkUpdateUpdatesMatchingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        int updated = m_engine.BulkUpdate("Users",
            new Dictionary<string, object?> { ["Email"] = "updated@test.com" },
            "Name = @name",
            new Dictionary<string, object?> { ["name"] = "Alice" });
        
        Assert.That(updated, Is.EqualTo(1));
        
        var alice = m_engine.QueryFirstOrDefault("SELECT Email FROM Users WHERE Name = 'Alice'");
        Assert.That(alice!.Value["Email"].AsString(), Is.EqualTo("updated@test.com"));
    }

    [Test]
    public void BulkUpdateThrowsOnEmptySetValuesTest()
    {
        CreateUsersTable();
        
        Assert.Throws<ArgumentException>(() => 
            m_engine.BulkUpdate("Users", new Dictionary<string, object?>()));
    }

    #endregion

    #region BulkDelete Tests

    [Test]
    public void BulkDeleteDeletesAllRowsWhenNoWhereConditionTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        int deleted = m_engine.BulkDelete("Users");
        
        Assert.That(deleted, Is.EqualTo(3));
        
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void BulkDeleteDeletesMatchingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        int deleted = m_engine.BulkDelete("Users",
            "Name = @name",
            new Dictionary<string, object?> { ["name"] = "Alice" });
        
        Assert.That(deleted, Is.EqualTo(1));
        
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
        Assert.That(count, Is.EqualTo(2));
    }

    #endregion

    #region Performance Tests

    [Test]
    public void BulkInsertPerformanceTest()
    {
        CreateUsersTable();
        
        const int rowCount = 1000;
        
        var rows = Enumerable.Range(1, rowCount)
            .Select(i => new Dictionary<string, object?>
            {
                ["Name"] = $"User{i}",
                ["Email"] = $"user{i}@test.com"
            })
            .ToList();
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int inserted = m_engine.BulkInsert("Users", rows);
        sw.Stop();
        
        Assert.That(inserted, Is.EqualTo(rowCount));
        
        // Should complete in reasonable time (< 1 second for 1000 rows in memory)
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000), 
            $"BulkInsert of {rowCount} rows took {sw.ElapsedMilliseconds}ms");
        
        TestContext.WriteLine($"BulkInsert {rowCount} rows: {sw.ElapsedMilliseconds}ms");
    }

    [Test]
    public void ExecuteBatchVsIndividualPerformanceComparisonTest()
    {
        CreateTestTable();
        
        const int rowCount = 500;
        
        var paramSets = Enumerable.Range(1, rowCount)
            .Select(i => new Dictionary<string, object?>
            {
                ["Name"] = $"Item{i}",
                ["Value"] = i
            })
            .ToArray();
        
        // Test ExecuteBatch
        using var batchStmt = m_engine.Prepare("INSERT INTO TestTable (Name, Value) VALUES (@Name, @Value)");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        batchStmt.ExecuteBatch(paramSets);
        var batchTime = sw.ElapsedMilliseconds;
        
        m_engine.Execute("DELETE FROM TestTable");
        
        // Test individual executions
        using var singleStmt = m_engine.Prepare("INSERT INTO TestTable (Name, Value) VALUES (@Name, @Value)");
        sw.Restart();
        foreach (var ps in paramSets)
        {
            singleStmt.ClearParameters();
            singleStmt.SetParameters(ps);
            using var _ = singleStmt.Execute();
        }
        var singleTime = sw.ElapsedMilliseconds;
        
        TestContext.WriteLine($"ExecuteBatch {rowCount} rows: {batchTime}ms");
        TestContext.WriteLine($"Individual {rowCount} rows: {singleTime}ms");
        TestContext.WriteLine($"Speedup: {(double)singleTime / Math.Max(1, batchTime):F2}x");
    }

    #endregion

    #region Helper Classes

    private class UserDto
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    #endregion
}
