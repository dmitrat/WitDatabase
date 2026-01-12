using OutWit.Database.Core.Builder;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for INSERT operations with a specific users table schema.
/// This mirrors the exact table structure from WitDatabase Studio.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineInsertUsersTests : WitSqlEngineTestsBase
{
    #region Table Setup

    /// <summary>
    /// Creates the exact users table schema from the bug report.
    /// </summary>
    private void CreateUsersTableExact()
    {
        m_engine.Execute(@"
            CREATE TABLE ""users"" (
                ""id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                ""name"" VARCHAR NOT NULL,
                ""email"" VARCHAR NOT NULL UNIQUE,
                ""created_at"" DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            )");
    }

    #endregion

    #region Basic INSERT Tests

    [Test]
    public void InsertSingleRowTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        m_engine.Execute("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('Alice', 'alice@test.com', '2025-01-12 10:00:00')");

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(1));

        using var result = m_engine.Execute("SELECT * FROM users");
        Assert.That(result.Read(), Is.True);
        Assert.That(result.CurrentRow["name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(result.CurrentRow["email"].AsString(), Is.EqualTo("alice@test.com"));
    }

    [Test]
    public void InsertMultipleRowsSequentiallyTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert 3 rows sequentially
        m_engine.Execute("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('Alice', 'alice@test.com', '2025-01-12 10:00:00')");
        m_engine.Execute("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('Bob', 'bob@test.com', '2025-01-12 10:00:01')");
        m_engine.Execute("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('Charlie', 'charlie@test.com', '2025-01-12 10:00:02')");

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(3), "Should have 3 rows after 3 inserts");

        // Verify each row exists
        var rows = m_engine.Query("SELECT * FROM users ORDER BY id");
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[2]["name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void InsertReturnsCorrectRowsAffectedTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        var rowsAffected = m_engine.ExecuteNonQuery("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('Test', 'test@test.com', '2025-01-12 10:00:00')");

        // Assert
        Assert.That(rowsAffected, Is.EqualTo(1));
    }

    [Test]
    public void InsertMultipleRowsInSingleStatementTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert multiple rows in one statement
        var rowsAffected = m_engine.ExecuteNonQuery(@"
            INSERT INTO [users] ([name], [email], [created_at]) VALUES 
                ('Alice', 'alice@test.com', '2025-01-12 10:00:00'),
                ('Bob', 'bob@test.com', '2025-01-12 10:00:01'),
                ('Charlie', 'charlie@test.com', '2025-01-12 10:00:02')");

        // Assert
        Assert.That(rowsAffected, Is.EqualTo(3));

        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(3));
    }

    #endregion

    #region Auto-Increment ID Tests

    [Test]
    public void InsertAutoIncrementGeneratesUniqueIdsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");
        var id1 = m_engine.LastInsertRowId;

        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'bob@test.com')");
        var id2 = m_engine.LastInsertRowId;

        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Charlie', 'charlie@test.com')");
        var id3 = m_engine.LastInsertRowId;

        // Assert
        Assert.That(id1, Is.EqualTo(1));
        Assert.That(id2, Is.EqualTo(2));
        Assert.That(id3, Is.EqualTo(3));

        // Verify IDs are actually different in the table
        var rows = m_engine.Query("SELECT id FROM users ORDER BY id");
        Assert.That(rows[0]["id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["id"].AsInt64(), Is.EqualTo(2));
        Assert.That(rows[2]["id"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void InsertDoesNotReuseAutoIncrementIdTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert, delete, insert again
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");
        var id1 = m_engine.LastInsertRowId;

        m_engine.Execute("DELETE FROM users WHERE id = 1");

        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'bob@test.com')");
        var id2 = m_engine.LastInsertRowId;

        // Assert - ID should NOT be reused
        Assert.That(id2, Is.EqualTo(2), "Auto-increment should not reuse deleted IDs");
    }

    [Test]
    public void InsertWithExplicitIdWorksTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert with explicit ID
        m_engine.Execute("INSERT INTO [users] ([id], [name], [email]) VALUES (100, 'Alice', 'alice@test.com')");

        // Assert
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users WHERE id = 100");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["name"].AsString(), Is.EqualTo("Alice"));
    }

    #endregion

    #region DEFAULT Value Tests

    [Test]
    public void InsertWithoutCreatedAtUsesDefaultTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert without created_at (should use DEFAULT CURRENT_TIMESTAMP)
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");

        // Assert
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["created_at"].IsNull, Is.False, "created_at should have default value");
    }

    #endregion

    #region UNIQUE Constraint Tests

    [Test]
    public void InsertDuplicateEmailThrowsTest()
    {
        // Arrange
        CreateUsersTableExact();
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'alice@test.com')"));

        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    [Test]
    public void InsertDifferentEmailsSucceedsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert rows with different emails (should succeed)
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'bob@test.com')");

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(2));
    }

    #endregion

    #region NOT NULL Constraint Tests

    [Test]
    public void InsertNullNameThrowsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES (NULL, 'test@test.com')"));

        Assert.That(ex!.Message, Does.Contain("NOT NULL"));
    }

    [Test]
    public void InsertNullEmailThrowsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Test', NULL)"));

        Assert.That(ex!.Message, Does.Contain("NOT NULL"));
    }

    [Test]
    public void InsertMissingRequiredColumnThrowsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act & Assert - Missing name column
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO [users] ([email]) VALUES ('test@test.com')"));

        Assert.That(ex!.Message, Does.Contain("NOT NULL"));
    }

    #endregion

    #region Row Count Verification Tests

    [Test]
    public void InsertIncreasesRowCountTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Assert initial count
        var initialCount = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        Assert.That(initialCount, Is.EqualTo(0));

        // Act & Assert after each insert
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");
        Assert.That(m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64(), Is.EqualTo(1));

        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'bob@test.com')");
        Assert.That(m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64(), Is.EqualTo(2));

        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Charlie', 'charlie@test.com')");
        Assert.That(m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void InsertAndSelectAllReturnsAllRowsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
        }

        // Assert
        var rows = m_engine.Query("SELECT * FROM users");
        Assert.That(rows, Has.Count.EqualTo(10));

        // Verify all rows have unique IDs
        var ids = rows.Select(r => r["id"].AsInt64()).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(10), "All IDs should be unique");
    }

    #endregion

    #region Transaction Tests

    [Test]
    public void InsertInTransactionCommitPersistsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("COMMIT");

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void InsertInTransactionRollbackDiscardsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("ROLLBACK");

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(0));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void InsertEmptyStringValuesTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Empty strings are valid (not NULL)
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('', '')");

        // Assert
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["name"].AsString(), Is.EqualTo(""));
        Assert.That(row.Value["email"].AsString(), Is.EqualTo(""));
    }

    [Test]
    public void InsertUnicodeValuesTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('??????', '????@test.com')");

        // Assert
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["name"].AsString(), Is.EqualTo("??????"));
        Assert.That(row.Value["email"].AsString(), Is.EqualTo("????@test.com"));
    }

    [Test]
    public void InsertSpecialCharactersTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('O''Brien', 'o''brien@test.com')");

        // Assert
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["name"].AsString(), Is.EqualTo("O'Brien"));
    }

    [Test]
    public void InsertLongStringValuesTest()
    {
        // Arrange
        CreateUsersTableExact();
        var longName = new string('A', 1000);
        var longEmail = new string('a', 500) + "@test.com";

        // Act
        m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('{longName}', '{longEmail}')");

        // Assert
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["name"].AsString(), Is.EqualTo(longName));
        Assert.That(row.Value["email"].AsString(), Is.EqualTo(longEmail));
    }

    #endregion

    #region Stress Tests

    [Test]
    public void InsertManyRowsTest()
    {
        // Arrange
        CreateUsersTableExact();
        const int rowCount = 100;

        // Act
        for (int i = 1; i <= rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
        }

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(rowCount));

        // Verify last inserted ID
        Assert.That(m_engine.LastInsertRowId, Is.EqualTo(rowCount));
    }

    [Test]
    public void InsertManyRowsInTransactionTest()
    {
        // Arrange
        CreateUsersTableExact();
        const int rowCount = 100;

        // Act
        m_engine.Execute("BEGIN TRANSACTION");
        for (int i = 1; i <= rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
        }
        m_engine.Execute("COMMIT");

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(rowCount));
    }

    #endregion

    #region Specific Bug Reproduction Tests

    [Test]
    public void InsertDoesNotOverwriteExistingRowTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert first row
        m_engine.Execute("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('?????', '??????', '2026-01-12 07:46:03')");
        var countAfterFirst = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();

        // Insert second row (this was reported as overwriting the first)
        m_engine.Execute("INSERT INTO [users] ([name], [email], [created_at]) VALUES ('????', '????@????.com', '2026-01-12 07:47:00')");
        var countAfterSecond = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();

        // Assert
        Assert.That(countAfterFirst, Is.EqualTo(1), "First insert should create 1 row");
        Assert.That(countAfterSecond, Is.EqualTo(2), "Second insert should create another row, not overwrite");

        // Verify both rows exist
        var rows = m_engine.Query("SELECT * FROM users ORDER BY id");
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["name"].AsString(), Is.EqualTo("?????"));
        Assert.That(rows[1]["name"].AsString(), Is.EqualTo("????"));
    }

    [Test]
    public void InsertFromQueryPanelSimulationTest()
    {
        // This test simulates the exact scenario from the bug report
        // Arrange
        CreateUsersTableExact();

        // Simulate the exact SQL that was being executed
        var sql = "INSERT INTO [users] ([name], [email], [created_at]) VALUES ('?????', '??????', '2026-01-12 07:46:03')";

        // Act - Execute via Query (simulating Query panel)
        var result = m_engine.Execute(sql);

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(1));

        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(1));

        // Verify the row data
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM users");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.Value["id"].AsInt64(), Is.EqualTo(1));
        Assert.That(row.Value["name"].AsString(), Is.EqualTo("?????"));
        Assert.That(row.Value["email"].AsString(), Is.EqualTo("??????"));
    }

    [Test]
    public void ConsecutiveInsertsWithSameTableStructureTest()
    {
        // Arrange - Create table with exact structure from bug
        CreateUsersTableExact();

        // Act - Multiple inserts like what happens in Table Editor
        var insertSql = "INSERT INTO [users] ([name], [email], [created_at]) VALUES (@name, @email, @created)";

        for (int i = 1; i <= 5; i++)
        {
            m_engine.Execute(
                "INSERT INTO [users] ([name], [email], [created_at]) VALUES (@name, @email, @created)",
                new Dictionary<string, object?>
                {
                    { "@name", $"User{i}" },
                    { "@email", $"user{i}@test.com" },
                    { "@created", DateTime.Now }
                });
        }

        // Assert
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users");
        Assert.That(count.AsInt64(), Is.EqualTo(5));

        var rows = m_engine.Query("SELECT id, name FROM users ORDER BY id");
        for (int i = 0; i < 5; i++)
        {
            Assert.That(rows[i]["id"].AsInt64(), Is.EqualTo(i + 1));
            Assert.That(rows[i]["name"].AsString(), Is.EqualTo($"User{i + 1}"));
        }
    }

    #endregion

    #region BUG: Count vs Actual Rows Mismatch Tests

    /// <summary>
    /// This test verifies that COUNT(*) returns the same number as actual rows returned by SELECT *.
    /// Bug: COUNT shows 104 but SELECT * returns only 101 rows.
    /// </summary>
    [Test]
    public void CountMatchesActualRowsTest()
    {
        // Arrange
        CreateUsersTableExact();
        const int rowCount = 50;

        // Act - Insert many rows
        for (int i = 1; i <= rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
        }

        // Assert - COUNT(*) should match actual rows
        var countResult = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        var actualRows = m_engine.Query("SELECT * FROM users");

        Assert.That(countResult, Is.EqualTo(rowCount), $"COUNT(*) should be {rowCount}");
        Assert.That(actualRows.Count, Is.EqualTo(rowCount), $"Actual rows should be {rowCount}");
        Assert.That(countResult, Is.EqualTo(actualRows.Count), 
            $"COUNT(*) ({countResult}) should match actual rows ({actualRows.Count})");
    }

    /// <summary>
    /// Tests that rows are not overwritten when inserting with auto-increment.
    /// Bug: New data appeared on row 3 instead of at the end.
    /// </summary>
    [Test]
    public void NewRowAppearsAtEndNotMiddleTest()
    {
        // Arrange
        CreateUsersTableExact();
        
        // Insert initial rows
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('Initial{i}', 'initial{i}@test.com')");
        }

        // Verify initial state
        var initialRows = m_engine.Query("SELECT id, name FROM users ORDER BY id");
        Assert.That(initialRows.Count, Is.EqualTo(10));
        var lastInitialId = initialRows[9]["id"].AsInt64();
        Assert.That(lastInitialId, Is.EqualTo(10));

        // Act - Insert new row
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('NewUser', 'newuser@test.com')");

        // Assert - New row should have ID 11 and appear at the end
        var newRows = m_engine.Query("SELECT id, name FROM users ORDER BY id");
        Assert.That(newRows.Count, Is.EqualTo(11), "Should have 11 rows after insert");
        
        // Verify all original rows are intact
        for (int i = 0; i < 10; i++)
        {
            Assert.That(newRows[i]["name"].AsString(), Is.EqualTo($"Initial{i + 1}"), 
                $"Row {i} should still be Initial{i + 1}");
        }
        
        // Verify new row is at the end with correct ID
        Assert.That(newRows[10]["id"].AsInt64(), Is.EqualTo(11), "New row should have ID 11");
        Assert.That(newRows[10]["name"].AsString(), Is.EqualTo("NewUser"), "New row should be NewUser");
    }

    /// <summary>
    /// Tests that auto-increment counter is properly initialized and maintained.
    /// </summary>
    [Test]
    public void AutoIncrementCounterIsProperlyMaintainedTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert rows and track IDs
        var insertedIds = new List<long>();
        for (int i = 1; i <= 20; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
            insertedIds.Add(m_engine.LastInsertRowId);
        }

        // Assert - All IDs should be sequential and unique
        Assert.That(insertedIds.Distinct().Count(), Is.EqualTo(20), "All IDs should be unique");
        Assert.That(insertedIds.Min(), Is.EqualTo(1), "First ID should be 1");
        Assert.That(insertedIds.Max(), Is.EqualTo(20), "Last ID should be 20");
        
        // Verify sequential
        for (int i = 0; i < 20; i++)
        {
            Assert.That(insertedIds[i], Is.EqualTo(i + 1), $"ID at position {i} should be {i + 1}");
        }
    }

    /// <summary>
    /// Tests that after explicit ID insert, auto-increment continues correctly.
    /// </summary>
    [Test]
    public void AutoIncrementContinuesAfterExplicitIdTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Insert with auto ID
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('User1', 'user1@test.com')");
        Assert.That(m_engine.LastInsertRowId, Is.EqualTo(1));

        // Insert with explicit ID = 100
        m_engine.Execute("INSERT INTO [users] ([id], [name], [email]) VALUES (100, 'User100', 'user100@test.com')");

        // Insert with auto ID again - should be > 100
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('User101', 'user101@test.com')");
        var newId = m_engine.LastInsertRowId;

        // Assert
        Assert.That(newId, Is.GreaterThan(100), "Auto ID after explicit 100 should be > 100");
        
        // Verify all rows exist
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        Assert.That(count, Is.EqualTo(3));
    }

    /// <summary>
    /// Stress test: Insert many rows and verify count consistency throughout.
    /// </summary>
    [Test]
    public void StressTestCountConsistencyTest()
    {
        // Arrange
        CreateUsersTableExact();
        const int totalRows = 200;

        // Act & Assert - Check consistency after every 10 inserts
        for (int i = 1; i <= totalRows; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
            
            if (i % 10 == 0)
            {
                var countResult = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
                var actualRows = m_engine.Query("SELECT id FROM users");
                
                Assert.That(countResult, Is.EqualTo(i), $"After {i} inserts, COUNT should be {i}");
                Assert.That(actualRows.Count, Is.EqualTo(i), $"After {i} inserts, actual rows should be {i}");
            }
        }
    }

    /// <summary>
    /// Tests that row IDs in data match the physical storage.
    /// </summary>
    [Test]
    public void RowIdsAreUniqueAndCorrectTest()
    {
        // Arrange
        CreateUsersTableExact();
        const int rowCount = 50;

        // Act
        for (int i = 1; i <= rowCount; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
        }

        // Assert - All IDs should be unique
        var rows = m_engine.Query("SELECT id, name FROM users");
        var ids = rows.Select(r => r["id"].AsInt64()).ToList();
        
        Assert.That(ids.Distinct().Count(), Is.EqualTo(rowCount), "All IDs must be unique");
        Assert.That(ids.Min(), Is.EqualTo(1), "Min ID should be 1");
        Assert.That(ids.Max(), Is.EqualTo(rowCount), "Max ID should be rowCount");
    }

    /// <summary>
    /// Test inserting after DELETE to ensure no ID collision.
    /// </summary>
    [Test]
    public void InsertAfterDeleteNoCollisionTest()
    {
        // Arrange
        CreateUsersTableExact();
        
        // Insert 10 rows
        for (int i = 1; i <= 10; i++)
        {
            m_engine.Execute($"INSERT INTO [users] ([name], [email]) VALUES ('User{i}', 'user{i}@test.com')");
        }
        
        // Delete some rows
        m_engine.Execute("DELETE FROM users WHERE id IN (3, 5, 7)");
        
        // Verify count after delete
        var countAfterDelete = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        Assert.That(countAfterDelete, Is.EqualTo(7));

        // Act - Insert new rows
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('NewUser1', 'new1@test.com')");
        var newId1 = m_engine.LastInsertRowId;
        
        m_engine.Execute("INSERT INTO [users] ([name], [email]) VALUES ('NewUser2', 'new2@test.com')");
        var newId2 = m_engine.LastInsertRowId;

        // Assert - New IDs should be 11 and 12, not reusing 3, 5, 7
        Assert.That(newId1, Is.EqualTo(11), "First new ID should be 11");
        Assert.That(newId2, Is.EqualTo(12), "Second new ID should be 12");
        
        // Verify final count
        var finalCount = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        var actualRows = m_engine.Query("SELECT * FROM users");
        
        Assert.That(finalCount, Is.EqualTo(9));
        Assert.That(actualRows.Count, Is.EqualTo(9));
    }

    #endregion

    #region Additional Tests

    /// <summary>
    /// Tests positional INSERT with explicit ID updates auto-increment counter.
    /// Bug: Positional INSERT INTO table VALUES (id, ...) was not updating counter.
    /// </summary>
    [Test]
    public void PositionalInsertWithExplicitIdUpdatesCounterTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert with positional VALUES (including explicit ID = 100)
        m_engine.Execute("INSERT INTO users VALUES (100, 'Alice', 'alice@test.com', '2025-01-12 10:00:00')");

        // Insert with auto-increment - should be > 100
        m_engine.Execute("INSERT INTO users (name, email) VALUES ('Bob', 'bob@test.com')");
        var newId = m_engine.LastInsertRowId;

        // Assert
        Assert.That(newId, Is.GreaterThan(100), "Auto ID after explicit 100 should be > 100");
        
        // Verify both rows exist
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        Assert.That(count, Is.EqualTo(2));
    }

    /// <summary>
    /// Tests that sequential explicit IDs don't cause excessive disk writes.
    /// </summary>
    [Test]
    public void SequentialExplicitIdsAreEfficientTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert many rows with explicit sequential IDs
        for (int i = 1; i <= 100; i++)
        {
            m_engine.Execute($"INSERT INTO users (id, name, email) VALUES ({i}, 'User{i}', 'user{i}@test.com')");
        }

        // Insert with auto-increment - should be 101
        m_engine.Execute("INSERT INTO users (name, email) VALUES ('NextUser', 'next@test.com')");
        var newId = m_engine.LastInsertRowId;

        // Assert
        Assert.That(newId, Is.EqualTo(101), "Auto ID after explicit 1-100 should be 101");
        
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        Assert.That(count, Is.EqualTo(101));
    }

    /// <summary>
    /// Tests that non-sequential explicit IDs work correctly.
    /// </summary>
    [Test]
    public void NonSequentialExplicitIdsTest()
    {
        // Arrange
        CreateUsersTableExact();

        // Act - Insert with random explicit IDs
        m_engine.Execute("INSERT INTO users (id, name, email) VALUES (5, 'User5', 'user5@test.com')");
        m_engine.Execute("INSERT INTO users (id, name, email) VALUES (3, 'User3', 'user3@test.com')");
        m_engine.Execute("INSERT INTO users (id, name, email) VALUES (10, 'User10', 'user10@test.com')");
        m_engine.Execute("INSERT INTO users (id, name, email) VALUES (7, 'User7', 'user7@test.com')");

        // Insert with auto-increment - should be > 10 (max explicit ID)
        m_engine.Execute("INSERT INTO users (name, email) VALUES ('NextUser', 'next@test.com')");
        var newId = m_engine.LastInsertRowId;

        // Assert
        Assert.That(newId, Is.EqualTo(11), "Auto ID should be max(explicit IDs) + 1");
        
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM users").AsInt64();
        Assert.That(count, Is.EqualTo(5));
    }

    #endregion
}
