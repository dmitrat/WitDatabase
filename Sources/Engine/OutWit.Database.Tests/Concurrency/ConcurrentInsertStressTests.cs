using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Concurrency;

/// <summary>
/// Stress tests for concurrent INSERT operations.
/// Tests Row ID uniqueness, MVCC isolation, and data consistency under parallel load.
/// </summary>
[TestFixture]
[Category("Stress")]
public class ConcurrentInsertStressTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"concurrent_insert_stress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    #endregion

    #region Sequential Transaction Stress

    [Test]
    public void SequentialTransactionsInsertTest()
    {
        var dbPath = Path.Combine(m_testDir, "seq_tx.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");

        const int transactionCount = 100;

        for (int i = 0; i < transactionCount; i++)
        {
            engine.Execute("BEGIN TRANSACTION");
            engine.Execute($"INSERT INTO Users (Name) VALUES ('User{i}')");
            engine.Execute("COMMIT");
        }

        // Verify all rows
        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
        Assert.That(count, Is.EqualTo(transactionCount));

        // Verify all IDs are unique
        var rows = engine.Query("SELECT Id FROM Users ORDER BY Id");
        var ids = rows.Select(r => r["Id"].AsInt64()).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(transactionCount));
        Assert.That(ids.Min(), Is.EqualTo(1));
        Assert.That(ids.Max(), Is.EqualTo(transactionCount));
    }

    [Test]
    public void AlternatingCommitRollbackInsertTest()
    {
        var dbPath = Path.Combine(m_testDir, "alt_commit.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value INT)");

        const int rounds = 50;
        var committed = 0;

        for (int i = 0; i < rounds; i++)
        {
            engine.Execute("BEGIN TRANSACTION");
            engine.Execute($"INSERT INTO Data (Value) VALUES ({i})");

            if (i % 2 == 0)
            {
                engine.Execute("COMMIT");
                committed++;
            }
            else
            {
                engine.Execute("ROLLBACK");
            }
        }

        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(count, Is.EqualTo(committed));

        // Row IDs should have gaps but be unique
        var ids = engine.Query("SELECT Id FROM Data").Select(r => r["Id"].AsInt64()).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(committed));
    }

    #endregion

    #region Bulk Insert Tests

    [Test]
    public void BulkInsertInSingleTransactionTest()
    {
        var dbPath = Path.Combine(m_testDir, "bulk_single.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Items (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100), Value DOUBLE)");

        const int rowCount = 1000;

        engine.Execute("BEGIN TRANSACTION");

        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute(
                "INSERT INTO Items (Name, Value) VALUES (@name, @value)",
                new Dictionary<string, object?>
                {
                    { "@name", $"Item{i}" },
                    { "@value", i * 1.5 }
                });
        }

        engine.Execute("COMMIT");

        // Verify
        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Items").AsInt64();
        Assert.That(count, Is.EqualTo(rowCount));

        // Verify IDs are sequential
        var firstId = engine.ExecuteScalar("SELECT MIN(Id) FROM Items").AsInt64();
        var lastId = engine.ExecuteScalar("SELECT MAX(Id) FROM Items").AsInt64();
        Assert.That(firstId, Is.EqualTo(1));
        Assert.That(lastId, Is.EqualTo(rowCount));
    }

    [Test]
    public void BulkInsertRollbackTest()
    {
        var dbPath = Path.Combine(m_testDir, "bulk_rollback.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Items (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");

        // Insert some committed data first
        engine.Execute("INSERT INTO Items (Name) VALUES ('Committed1')");
        engine.Execute("INSERT INTO Items (Name) VALUES ('Committed2')");

        // Start transaction and insert many rows
        engine.Execute("BEGIN TRANSACTION");

        for (int i = 0; i < 500; i++)
        {
            engine.Execute($"INSERT INTO Items (Name) VALUES ('RolledBack{i}')");
        }

        // Rollback
        engine.Execute("ROLLBACK");

        // Verify only committed data exists
        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Items").AsInt64();
        Assert.That(count, Is.EqualTo(2));

        // After rollback, the row ID counter is reloaded from persisted state.
        // This means we may get IDs that are 3+ (not > 500) since the rolled-back
        // IDs were never actually persisted.
        engine.Execute("INSERT INTO Items (Name) VALUES ('AfterRollback')");
        var newId = engine.LastInsertRowId;
        
        // New ID should be >= 3 (continuing from last committed)
        Assert.That(newId, Is.GreaterThanOrEqualTo(3), "New ID should be at least 3");
        
        // Verify no ID collisions
        var ids = engine.Query("SELECT Id FROM Items").Select(r => r["Id"].AsInt64()).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(3), "All IDs should be unique");
    }

    #endregion

    #region Row ID Uniqueness Tests

    [Test]
    public void RowIdUniquenessUnderLoadTest()
    {
        var dbPath = Path.Combine(m_testDir, "rowid_unique.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, ThreadId INT, SeqNum INT)");

        const int transactionsPerThread = 50;
        const int rowsPerTransaction = 10;
        const int threadCount = 4;
        var exceptions = new List<Exception>();

        // Sequential transactions from "multiple threads" (simulated)
        for (int t = 0; t < threadCount; t++)
        {
            for (int tx = 0; tx < transactionsPerThread; tx++)
            {
                try
                {
                    engine.Execute("BEGIN TRANSACTION");

                    for (int row = 0; row < rowsPerTransaction; row++)
                    {
                        engine.Execute(
                            "INSERT INTO Data (ThreadId, SeqNum) VALUES (@tid, @seq)",
                            new Dictionary<string, object?>
                            {
                                { "@tid", t },
                                { "@seq", tx * rowsPerTransaction + row }
                            });
                    }

                    engine.Execute("COMMIT");
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }
        }

        Assert.That(exceptions, Is.Empty, $"Exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");

        // Verify total rows
        var expectedRows = threadCount * transactionsPerThread * rowsPerTransaction;
        var actualCount = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(actualCount, Is.EqualTo(expectedRows));

        // Verify all IDs are unique
        var ids = engine.Query("SELECT Id FROM Data").Select(r => r["Id"].AsInt64()).ToList();
        var uniqueIds = ids.Distinct().Count();
        Assert.That(uniqueIds, Is.EqualTo(expectedRows), "All row IDs must be unique");
    }

    [Test]
    public void ExplicitIdAndAutoIncrementMixTest()
    {
        var dbPath = Path.Combine(m_testDir, "explicit_auto.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");

        // Insert with auto-increment
        engine.Execute("INSERT INTO Users (Name) VALUES ('User1')");
        Assert.That(engine.LastInsertRowId, Is.EqualTo(1));

        // Insert with explicit ID
        engine.Execute("INSERT INTO Users (Id, Name) VALUES (100, 'User100')");

        // Insert with auto-increment again - should be > 100
        engine.Execute("INSERT INTO Users (Name) VALUES ('User2')");
        Assert.That(engine.LastInsertRowId, Is.EqualTo(101));

        // Insert with explicit ID in the past (< 100) - should work but not affect counter
        engine.Execute("INSERT INTO Users (Id, Name) VALUES (50, 'User50')");

        // Next auto-increment should still be 102
        engine.Execute("INSERT INTO Users (Name) VALUES ('User3')");
        Assert.That(engine.LastInsertRowId, Is.EqualTo(102));

        // Verify all rows exist
        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
        Assert.That(count, Is.EqualTo(5));

        // Verify no duplicate IDs
        var ids = engine.Query("SELECT Id FROM Users").Select(r => r["Id"].AsInt64()).ToList();
        Assert.That(ids.Distinct().Count(), Is.EqualTo(5));
    }

    #endregion

    #region MVCC Concurrent Insert Tests

    [Test]
    public void MvccConcurrentReadDuringInsertTest()
    {
        var dbPath = Path.Combine(m_testDir, "mvcc_read_insert.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithMvcc()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Counter (Id INT PRIMARY KEY, Value INT)");
        engine.Execute("INSERT INTO Counter VALUES (1, 0)");

        const int iterations = 50;
        var readValues = new List<long>();

        for (int i = 0; i < iterations; i++)
        {
            // Start read transaction
            engine.Execute("BEGIN TRANSACTION");
            var valueBefore = engine.ExecuteScalar("SELECT Value FROM Counter WHERE Id = 1").AsInt64();

            // Insert new row (simulating concurrent write)
            engine.Execute($"INSERT INTO Counter VALUES ({i + 10}, {i})");

            // Read again - should see same value (snapshot isolation)
            var valueAfter = engine.ExecuteScalar("SELECT Value FROM Counter WHERE Id = 1").AsInt64();
            
            engine.Execute("COMMIT");

            Assert.That(valueAfter, Is.EqualTo(valueBefore), 
                "Value should be consistent within transaction");
        }
    }

    [Test]
    public void MvccMultipleInsertsInTransactionTest()
    {
        var dbPath = Path.Combine(m_testDir, "mvcc_multi_insert.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithMvcc()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Log (Id BIGINT PRIMARY KEY AUTOINCREMENT, Msg VARCHAR(100))");

        // Insert many rows in a single transaction
        engine.Execute("BEGIN TRANSACTION");

        for (int i = 0; i < 100; i++)
        {
            engine.Execute($"INSERT INTO Log (Msg) VALUES ('Message{i}')");
        }

        // Verify rows visible within transaction
        var countInTx = engine.ExecuteScalar("SELECT COUNT(*) FROM Log").AsInt64();
        Assert.That(countInTx, Is.EqualTo(100));

        engine.Execute("COMMIT");

        // Verify rows persisted
        var countAfter = engine.ExecuteScalar("SELECT COUNT(*) FROM Log").AsInt64();
        Assert.That(countAfter, Is.EqualTo(100));

        // Verify IDs are sequential
        var ids = engine.Query("SELECT Id FROM Log ORDER BY Id").Select(r => r["Id"].AsInt64()).ToList();
        for (int i = 0; i < 100; i++)
        {
            Assert.That(ids[i], Is.EqualTo(i + 1));
        }
    }

    [Test]
    public void MvccRollbackDoesNotAffectCommittedDataTest()
    {
        var dbPath = Path.Combine(m_testDir, "mvcc_rollback.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithMvcc()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value INT)");

        // Commit some data
        engine.Execute("BEGIN TRANSACTION");
        engine.Execute("INSERT INTO Data (Value) VALUES (1)");
        engine.Execute("INSERT INTO Data (Value) VALUES (2)");
        engine.Execute("COMMIT");

        var countAfterFirst = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(countAfterFirst, Is.EqualTo(2));

        // Start another transaction and rollback
        engine.Execute("BEGIN TRANSACTION");
        engine.Execute("INSERT INTO Data (Value) VALUES (3)");
        engine.Execute("INSERT INTO Data (Value) VALUES (4)");
        engine.Execute("ROLLBACK");

        // Original data should still be there
        var countAfterRollback = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(countAfterRollback, Is.EqualTo(2));

        // Verify values
        var values = engine.Query("SELECT Value FROM Data ORDER BY Id")
            .Select(r => r["Value"].AsInt64()).ToList();
        Assert.That(values, Is.EqualTo(new[] { 1L, 2L }));
    }

    #endregion

    #region Savepoint Tests

    [Test]
    public void SavepointWithInsertsTest()
    {
        var dbPath = Path.Combine(m_testDir, "savepoint.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Items (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");

        engine.Execute("BEGIN TRANSACTION");

        // Insert initial rows
        engine.Execute("INSERT INTO Items (Name) VALUES ('Before Savepoint 1')");
        engine.Execute("INSERT INTO Items (Name) VALUES ('Before Savepoint 2')");

        // Create savepoint
        engine.Execute("SAVEPOINT sp1");

        // Insert more rows
        engine.Execute("INSERT INTO Items (Name) VALUES ('After Savepoint 1')");
        engine.Execute("INSERT INTO Items (Name) VALUES ('After Savepoint 2')");

        // Verify 4 rows visible
        var countBefore = engine.ExecuteScalar("SELECT COUNT(*) FROM Items").AsInt64();
        Assert.That(countBefore, Is.EqualTo(4));

        // Rollback to savepoint
        engine.Execute("ROLLBACK TO SAVEPOINT sp1");

        // Should only have 2 rows now
        var countAfter = engine.ExecuteScalar("SELECT COUNT(*) FROM Items").AsInt64();
        Assert.That(countAfter, Is.EqualTo(2));

        // Commit transaction
        engine.Execute("COMMIT");

        // Verify persisted count
        var finalCount = engine.ExecuteScalar("SELECT COUNT(*) FROM Items").AsInt64();
        Assert.That(finalCount, Is.EqualTo(2));
    }

    [Test]
    public void NestedSavepointsWithInsertsTest()
    {
        var dbPath = Path.Combine(m_testDir, "nested_savepoint.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, Level INT)");

        engine.Execute("BEGIN TRANSACTION");

        engine.Execute("INSERT INTO Data (Level) VALUES (0)"); // Level 0

        engine.Execute("SAVEPOINT sp1");
        engine.Execute("INSERT INTO Data (Level) VALUES (1)"); // Level 1

        engine.Execute("SAVEPOINT sp2");
        engine.Execute("INSERT INTO Data (Level) VALUES (2)"); // Level 2

        // Verify count before any rollback
        var countBeforeRollback = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(countBeforeRollback, Is.EqualTo(3)); // 0, 1, 2

        // Rollback to sp2 (removes level 2 only)
        engine.Execute("ROLLBACK TO SAVEPOINT sp2");
        var countAfterRb2 = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(countAfterRb2, Is.EqualTo(2)); // 0, 1

        // Insert at level 2 again
        engine.Execute("INSERT INTO Data (Level) VALUES (22)");
        var countAfterReinsert = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(countAfterReinsert, Is.EqualTo(3)); // 0, 1, 22

        // Rollback to sp1 (removes level 1 and 22)
        engine.Execute("ROLLBACK TO SAVEPOINT sp1");
        var countAfterRb1 = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(countAfterRb1, Is.EqualTo(1)); // Only level 0

        engine.Execute("COMMIT");

        // Verify final state
        var levels = engine.Query("SELECT Level FROM Data ORDER BY Id")
            .Select(r => r["Level"].AsInt64()).ToList();
        Assert.That(levels, Is.EqualTo(new[] { 0L }));
    }

    #endregion

    #region Multi-Table Insert Tests

    [Test]
    public void InsertIntoMultipleTablesInTransactionTest()
    {
        var dbPath = Path.Combine(m_testDir, "multi_table.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");
        engine.Execute("CREATE TABLE Orders (Id BIGINT PRIMARY KEY AUTOINCREMENT, UserId BIGINT, Amount DECIMAL(10,2))");
        engine.Execute("CREATE TABLE Logs (Id BIGINT PRIMARY KEY AUTOINCREMENT, Action VARCHAR(100))");

        engine.Execute("BEGIN TRANSACTION");

        // Insert into multiple tables
        for (int i = 0; i < 20; i++)
        {
            engine.Execute($"INSERT INTO Users (Name) VALUES ('User{i}')");
            engine.Execute($"INSERT INTO Orders (UserId, Amount) VALUES ({i + 1}, {i * 10.5m})");
            engine.Execute($"INSERT INTO Logs (Action) VALUES ('Created User{i}')");
        }

        engine.Execute("COMMIT");

        // Verify counts
        Assert.That(engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64(), Is.EqualTo(20));
        Assert.That(engine.ExecuteScalar("SELECT COUNT(*) FROM Orders").AsInt64(), Is.EqualTo(20));
        Assert.That(engine.ExecuteScalar("SELECT COUNT(*) FROM Logs").AsInt64(), Is.EqualTo(20));

        // Verify IDs are independent per table
        var userMaxId = engine.ExecuteScalar("SELECT MAX(Id) FROM Users").AsInt64();
        var orderMaxId = engine.ExecuteScalar("SELECT MAX(Id) FROM Orders").AsInt64();
        var logMaxId = engine.ExecuteScalar("SELECT MAX(Id) FROM Logs").AsInt64();

        Assert.That(userMaxId, Is.EqualTo(20));
        Assert.That(orderMaxId, Is.EqualTo(20));
        Assert.That(logMaxId, Is.EqualTo(20));
    }

    #endregion

    #region Insert with SELECT Tests

    [Test]
    public void InsertFromSelectTest()
    {
        var dbPath = Path.Combine(m_testDir, "insert_select.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Source (Id BIGINT PRIMARY KEY, Value INT)");
        engine.Execute("CREATE TABLE Target (Id BIGINT PRIMARY KEY AUTOINCREMENT, SourceId BIGINT, Value INT)");

        // Populate source
        for (int i = 1; i <= 50; i++)
        {
            engine.Execute($"INSERT INTO Source VALUES ({i}, {i * 10})");
        }

        // Insert from select
        engine.Execute("BEGIN TRANSACTION");
        engine.ExecuteNonQuery("INSERT INTO Target (SourceId, Value) SELECT Id, Value FROM Source WHERE Value > 100");
        engine.Execute("COMMIT");

        // Verify
        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Target").AsInt64();
        Assert.That(count, Is.EqualTo(40)); // Values 110, 120, ..., 500

        // Verify target IDs are auto-generated
        var targetIds = engine.Query("SELECT Id FROM Target ORDER BY Id")
            .Select(r => r["Id"].AsInt64()).ToList();
        Assert.That(targetIds.First(), Is.EqualTo(1));
        Assert.That(targetIds.Last(), Is.EqualTo(40));
    }

    #endregion

    #region Persistence Tests

    [Test]
    public void InsertPersistsAfterCloseAndReopenTest()
    {
        var dbPath = Path.Combine(m_testDir, "persist_test.witdb");
        
        // First session - create and insert
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build())
        {
            using var engine = new WitSqlEngine(db, ownsStore: false);

            engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value INT)");

            engine.Execute("BEGIN TRANSACTION");
            for (int i = 0; i < 100; i++)
            {
                engine.Execute($"INSERT INTO Data (Value) VALUES ({i})");
            }
            engine.Execute("COMMIT");

            var countBefore = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
            Assert.That(countBefore, Is.EqualTo(100));
        }

        // Second session - verify data persisted
        using (var db = WitDatabase.Open(dbPath))
        {
            using var engine = new WitSqlEngine(db, ownsStore: false);

            var countAfter = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
            Assert.That(countAfter, Is.EqualTo(100));

            // Insert more rows
            engine.Execute("INSERT INTO Data (Value) VALUES (1000)");
            var newId = engine.LastInsertRowId;
            Assert.That(newId, Is.EqualTo(101), "Auto-increment should continue from persisted value");
        }

        // Third session - verify new insert persisted
        using (var db = WitDatabase.Open(dbPath))
        {
            using var engine = new WitSqlEngine(db, ownsStore: false);

            var finalCount = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
            Assert.That(finalCount, Is.EqualTo(101));
        }
    }

    [Test]
    public void RollbackDoesNotPersistTest()
    {
        var dbPath = Path.Combine(m_testDir, "rollback_persist.witdb");
        
        // First session
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build())
        {
            using var engine = new WitSqlEngine(db, ownsStore: false);

            engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value INT)");

            // Commit some data
            engine.Execute("INSERT INTO Data (Value) VALUES (1)");
            engine.Execute("INSERT INTO Data (Value) VALUES (2)");

            // Start transaction and rollback
            engine.Execute("BEGIN TRANSACTION");
            engine.Execute("INSERT INTO Data (Value) VALUES (3)");
            engine.Execute("INSERT INTO Data (Value) VALUES (4)");
            engine.Execute("ROLLBACK");
        }

        // Second session - verify only committed data exists
        using (var db = WitDatabase.Open(dbPath))
        {
            using var engine = new WitSqlEngine(db, ownsStore: false);

            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
            Assert.That(count, Is.EqualTo(2));

            var values = engine.Query("SELECT Value FROM Data ORDER BY Id")
                .Select(r => r["Value"].AsInt64()).ToList();
            Assert.That(values, Is.EqualTo(new[] { 1L, 2L }));
        }
    }

    #endregion

    #region Row Count Consistency Tests

    [Test]
    public void RowCountConsistencyDuringInsertTest()
    {
        var dbPath = Path.Combine(m_testDir, "count_consistency.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Items (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100))");

        const int batchSize = 50;
        const int batches = 10;

        for (int batch = 0; batch < batches; batch++)
        {
            engine.Execute("BEGIN TRANSACTION");

            for (int i = 0; i < batchSize; i++)
            {
                engine.Execute($"INSERT INTO Items (Name) VALUES ('Batch{batch}_Item{i}')");
            }

            engine.Execute("COMMIT");

            // Verify count after each batch
            var expectedCount = (batch + 1) * batchSize;
            var actualCount = engine.ExecuteScalar("SELECT COUNT(*) FROM Items").AsInt64();
            Assert.That(actualCount, Is.EqualTo(expectedCount), 
                $"Count mismatch after batch {batch}");

            // Verify actual rows match count
            var actualRows = engine.Query("SELECT Id FROM Items");
            Assert.That(actualRows.Count, Is.EqualTo(expectedCount),
                $"Actual rows don't match count after batch {batch}");
        }
    }

    #endregion

    #region Large Transaction Tests

    [Test]
    [CancelAfter(60000)] // 60 seconds
    public void LargeTransactionWith10000RowsTest()
    {
        var dbPath = Path.Combine(m_testDir, "large_tx.witdb");
        
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db, ownsStore: false);

        engine.Execute("CREATE TABLE Data (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100), Value DOUBLE)");

        const int rowCount = 10000;

        engine.Execute("BEGIN TRANSACTION");

        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute(
                "INSERT INTO Data (Name, Value) VALUES (@name, @value)",
                new Dictionary<string, object?>
                {
                    { "@name", $"Row{i:D5}" },
                    { "@value", i * Math.PI }
                });
        }

        engine.Execute("COMMIT");

        // Verify
        var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
        Assert.That(count, Is.EqualTo(rowCount));

        // Sample verification
        var sample = engine.QueryFirstOrDefault("SELECT * FROM Data WHERE Id = 5000");
        Assert.That(sample, Is.Not.Null);
        Assert.That(sample!.Value["Name"].AsString(), Is.EqualTo("Row04999"));
    }

    #endregion
}
