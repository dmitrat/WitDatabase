using System.Diagnostics;
using NUnit.Framework;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Performance tests for UPDATE/DELETE operations.
/// Tests single-row fast path and batch IN (...) fast path optimizations.
/// </summary>
[TestFixture]
[Category("Performance")]
public class DmlPerformanceTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDb_DmlPerf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, true); } catch { }
        }
    }

    #endregion

    #region Single Row UPDATE Fast Path Tests

    [Test]
    public void UpdateByPkFastPathTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        const int rowCount = 100;
        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Value) VALUES ({i}.0)");
        }

        var sw = Stopwatch.StartNew();
        using (var stmt = engine.Prepare("UPDATE T SET Value = @v WHERE Id = @id"))
        {
            for (int i = 1; i <= rowCount; i++)
            {
                stmt.SetParameter("id", i);
                stmt.SetParameter("v", i * 10.0);
                stmt.Execute();
            }
        }
        sw.Stop();

        var perRowMs = (double)sw.ElapsedMilliseconds / rowCount;
        TestContext.WriteLine($"UPDATE {rowCount} rows by PK: {sw.ElapsedMilliseconds}ms ({perRowMs:F3}ms/row)");
        
        Assert.That(perRowMs, Is.LessThan(5), $"UPDATE per row too slow: {perRowMs:F3}ms");
    }

    [Test]
    public void VerifyFastPathTriggersAfterUpdateTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("CREATE TABLE AuditLog (Id BIGINT PRIMARY KEY AUTOINCREMENT, OldValue DOUBLE, NewValue DOUBLE)");
        engine.Execute(@"
            CREATE TRIGGER tr_audit AFTER UPDATE ON T
            FOR EACH ROW
            BEGIN
                INSERT INTO AuditLog (OldValue, NewValue) VALUES (OLD.Value, NEW.Value);
            END
        ");
        
        engine.Execute("INSERT INTO T (Value) VALUES (100)");
        engine.Execute("UPDATE T SET Value = 200 WHERE Id = 1");
        
        var auditRows = engine.Query("SELECT * FROM AuditLog");
        Assert.That(auditRows.Count, Is.EqualTo(1));
        Assert.That(auditRows[0]["OldValue"].AsDouble(), Is.EqualTo(100));
        Assert.That(auditRows[0]["NewValue"].AsDouble(), Is.EqualTo(200));
    }

    #endregion

    #region Single Row DELETE Fast Path Tests

    [Test]
    public void DeleteByPkFastPathTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        const int rowCount = 100;
        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Value) VALUES ({i}.0)");
        }

        var sw = Stopwatch.StartNew();
        using (var stmt = engine.Prepare("DELETE FROM T WHERE Id = @id"))
        {
            for (int i = 1; i <= rowCount; i++)
            {
                stmt.SetParameter("id", i);
                stmt.Execute();
            }
        }
        sw.Stop();

        var countAfter = engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64();
        Assert.That(countAfter, Is.EqualTo(0));

        var perRowMs = (double)sw.ElapsedMilliseconds / rowCount;
        TestContext.WriteLine($"DELETE {rowCount} rows by PK: {sw.ElapsedMilliseconds}ms ({perRowMs:F3}ms/row)");
        
        Assert.That(perRowMs, Is.LessThan(1.0), $"DELETE too slow: {perRowMs:F3}ms/row");
    }

    [Test]
    public void VerifyDeleteFastPathTriggersAfterDeleteTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("CREATE TABLE DeleteLog (Id BIGINT PRIMARY KEY AUTOINCREMENT, DeletedId BIGINT)");
        engine.Execute(@"
            CREATE TRIGGER tr_delete AFTER DELETE ON T
            FOR EACH ROW
            BEGIN
                INSERT INTO DeleteLog (DeletedId) VALUES (OLD.Id);
            END
        ");
        
        engine.Execute("INSERT INTO T (Value) VALUES (100)");
        engine.Execute("DELETE FROM T WHERE Id = 1");
        
        var logRows = engine.Query("SELECT * FROM DeleteLog");
        Assert.That(logRows.Count, Is.EqualTo(1));
        Assert.That(logRows[0]["DeletedId"].AsInt64(), Is.EqualTo(1));
    }

    #endregion

    #region Batch UPDATE Fast Path Tests

    [Test]
    public void UpdateBatchWithInClauseFastPathTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        const int rowCount = 100;
        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Value) VALUES ({i}.0)");
        }

        var sw = Stopwatch.StartNew();
        engine.Execute("UPDATE T SET Value = 999 WHERE Id IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
        sw.Stop();

        var updated = engine.Query("SELECT * FROM T WHERE Value = 999");
        Assert.That(updated.Count, Is.EqualTo(10));

        TestContext.WriteLine($"UPDATE with IN (10 values): {sw.ElapsedMilliseconds}ms");
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), $"Batch UPDATE too slow: {sw.ElapsedMilliseconds}ms");
    }

    [Test]
    public void UpdateBatchWithParameterizedInClauseTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        for (int i = 0; i < 50; i++)
        {
            engine.Execute($"INSERT INTO T (Value) VALUES ({i}.0)");
        }

        using var stmt = engine.Prepare("UPDATE T SET Value = @newValue WHERE Id IN (@id1, @id2, @id3)");
        stmt.SetParameter("newValue", 888.0);
        stmt.SetParameter("id1", 1);
        stmt.SetParameter("id2", 5);
        stmt.SetParameter("id3", 10);
        stmt.Execute();

        var updated = engine.Query("SELECT * FROM T WHERE Value = 888");
        Assert.That(updated.Count, Is.EqualTo(3));
    }

    [Test]
    public void UpdateBatchTriggersAfterUpdateForEachRowTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("CREATE TABLE AuditLog (Id BIGINT PRIMARY KEY AUTOINCREMENT, UpdatedId BIGINT)");
        engine.Execute(@"
            CREATE TRIGGER tr_audit AFTER UPDATE ON T
            FOR EACH ROW
            BEGIN
                INSERT INTO AuditLog (UpdatedId) VALUES (NEW.Id);
            END
        ");
        
        engine.Execute("INSERT INTO T (Value) VALUES (1)");
        engine.Execute("INSERT INTO T (Value) VALUES (2)");
        engine.Execute("INSERT INTO T (Value) VALUES (3)");
        
        engine.Execute("UPDATE T SET Value = 999 WHERE Id IN (1, 2, 3)");
        
        var auditRows = engine.Query("SELECT * FROM AuditLog ORDER BY UpdatedId");
        Assert.That(auditRows.Count, Is.EqualTo(3));
        Assert.That(auditRows[0]["UpdatedId"].AsInt64(), Is.EqualTo(1));
        Assert.That(auditRows[1]["UpdatedId"].AsInt64(), Is.EqualTo(2));
        Assert.That(auditRows[2]["UpdatedId"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void UpdateBatchWithReturningClauseTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(50), Value DOUBLE)");
        
        engine.Execute("INSERT INTO T (Name, Value) VALUES ('A', 1)");
        engine.Execute("INSERT INTO T (Name, Value) VALUES ('B', 2)");
        engine.Execute("INSERT INTO T (Name, Value) VALUES ('C', 3)");
        
        var result = engine.Query("UPDATE T SET Value = Value * 10 WHERE Id IN (1, 3) RETURNING Id, Name, Value");
        
        Assert.That(result.Count, Is.EqualTo(2));
        
        var ids = result.Select(r => r["Id"].AsInt64()).OrderBy(x => x).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 1L, 3L }));
    }

    #endregion

    #region Batch DELETE Fast Path Tests

    [Test]
    public void DeleteBatchWithInClauseFastPathTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        const int rowCount = 100;
        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Value) VALUES ({i}.0)");
        }

        var sw = Stopwatch.StartNew();
        engine.Execute("DELETE FROM T WHERE Id IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)");
        sw.Stop();

        var countAfter = engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64();
        Assert.That(countAfter, Is.EqualTo(rowCount - 10));

        TestContext.WriteLine($"DELETE with IN (10 values): {sw.ElapsedMilliseconds}ms");
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(100), $"Batch DELETE too slow: {sw.ElapsedMilliseconds}ms");
    }

    [Test]
    public void DeleteBatchTriggersAfterDeleteForEachRowTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("CREATE TABLE DeleteLog (Id BIGINT PRIMARY KEY AUTOINCREMENT, DeletedId BIGINT)");
        engine.Execute(@"
            CREATE TRIGGER tr_delete AFTER DELETE ON T
            FOR EACH ROW
            BEGIN
                INSERT INTO DeleteLog (DeletedId) VALUES (OLD.Id);
            END
        ");
        
        engine.Execute("INSERT INTO T (Value) VALUES (100)");
        engine.Execute("INSERT INTO T (Value) VALUES (200)");
        engine.Execute("INSERT INTO T (Value) VALUES (300)");
        
        engine.Execute("DELETE FROM T WHERE Id IN (1, 2, 3)");
        
        var logRows = engine.Query("SELECT * FROM DeleteLog ORDER BY DeletedId");
        Assert.That(logRows.Count, Is.EqualTo(3));
        Assert.That(logRows[0]["DeletedId"].AsInt64(), Is.EqualTo(1));
        Assert.That(logRows[1]["DeletedId"].AsInt64(), Is.EqualTo(2));
        Assert.That(logRows[2]["DeletedId"].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void DeleteBatchWithReturningClauseTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(50), Value DOUBLE)");
        
        engine.Execute("INSERT INTO T (Name, Value) VALUES ('A', 1)");
        engine.Execute("INSERT INTO T (Name, Value) VALUES ('B', 2)");
        engine.Execute("INSERT INTO T (Name, Value) VALUES ('C', 3)");
        
        var result = engine.Query("DELETE FROM T WHERE Id IN (1, 3) RETURNING Id, Name");
        
        Assert.That(result.Count, Is.EqualTo(2));
        
        var ids = result.Select(r => r["Id"].AsInt64()).OrderBy(x => x).ToList();
        Assert.That(ids, Is.EqualTo(new[] { 1L, 3L }));
        
        var remaining = engine.Query("SELECT * FROM T");
        Assert.That(remaining.Count, Is.EqualTo(1));
        Assert.That(remaining[0]["Name"].AsString(), Is.EqualTo("B"));
    }

    [Test]
    public void DeleteBatchWithCascadeTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE Parent (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name VARCHAR(50))");
        engine.Execute("CREATE TABLE Child (Id BIGINT PRIMARY KEY AUTOINCREMENT, ParentId BIGINT REFERENCES Parent(Id) ON DELETE CASCADE, Value INT)");
        
        engine.Execute("INSERT INTO Parent (Name) VALUES ('P1')");
        engine.Execute("INSERT INTO Parent (Name) VALUES ('P2')");
        engine.Execute("INSERT INTO Parent (Name) VALUES ('P3')");
        
        engine.Execute("INSERT INTO Child (ParentId, Value) VALUES (1, 10)");
        engine.Execute("INSERT INTO Child (ParentId, Value) VALUES (1, 11)");
        engine.Execute("INSERT INTO Child (ParentId, Value) VALUES (2, 20)");
        engine.Execute("INSERT INTO Child (ParentId, Value) VALUES (3, 30)");
        
        engine.Execute("DELETE FROM Parent WHERE Id IN (1, 2)");
        
        var parents = engine.Query("SELECT * FROM Parent");
        Assert.That(parents.Count, Is.EqualTo(1));
        Assert.That(parents[0]["Name"].AsString(), Is.EqualTo("P3"));
        
        var children = engine.Query("SELECT * FROM Child");
        Assert.That(children.Count, Is.EqualTo(1));
        Assert.That(children[0]["ParentId"].AsInt64(), Is.EqualTo(3));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void UpdateBatchWithNonExistentIdsTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("INSERT INTO T (Value) VALUES (1)");
        engine.Execute("INSERT INTO T (Value) VALUES (2)");
        
        var result = engine.Execute("UPDATE T SET Value = 999 WHERE Id IN (1, 100, 200)");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        
        var updated = engine.Query("SELECT * FROM T WHERE Value = 999");
        Assert.That(updated.Count, Is.EqualTo(1));
        Assert.That(updated[0]["Id"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void DeleteBatchWithNonExistentIdsTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("INSERT INTO T (Value) VALUES (1)");
        engine.Execute("INSERT INTO T (Value) VALUES (2)");
        
        var result = engine.Execute("DELETE FROM T WHERE Id IN (2, 100, 200)");
        
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        
        var remaining = engine.Query("SELECT * FROM T");
        Assert.That(remaining.Count, Is.EqualTo(1));
        Assert.That(remaining[0]["Id"].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void BatchFastPathNotUsedWithBeforeTriggerTest()
    {
        // Verify batch fast path is NOT used when BEFORE trigger exists
        // Standard path should be used instead
        
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        engine.Execute("CREATE TABLE TriggerLog (Id BIGINT PRIMARY KEY AUTOINCREMENT, Message VARCHAR(100))");
        
        // BEFORE UPDATE trigger that logs but doesn't cancel
        engine.Execute(@"
            CREATE TRIGGER tr_before BEFORE UPDATE ON T
            FOR EACH ROW
            BEGIN
                INSERT INTO TriggerLog (Message) VALUES ('before update');
            END
        ");
        
        engine.Execute("INSERT INTO T (Value) VALUES (1)");
        engine.Execute("INSERT INTO T (Value) VALUES (2)");
        engine.Execute("INSERT INTO T (Value) VALUES (3)");
        
        // First test standard UPDATE by PK (single row) - should work with BEFORE trigger
        var result1 = engine.Execute("UPDATE T SET Value = 888 WHERE Id = 1");
        Assert.That(result1.RowsAffected, Is.EqualTo(1), "Single-row UPDATE with BEFORE trigger should work");
        
        // Clear log
        engine.Execute("DELETE FROM TriggerLog");
        
        // Now test batch UPDATE with IN clause - should use standard path (not batch fast path)
        var result2 = engine.Execute("UPDATE T SET Value = 999 WHERE Id IN (2, 3)");
        Assert.That(result2.RowsAffected, Is.EqualTo(2), "Batch UPDATE with BEFORE trigger should update 2 rows");
        
        // Verify trigger was executed for each row
        var logRows = engine.Query("SELECT * FROM TriggerLog");
        Assert.That(logRows.Count, Is.EqualTo(2), "BEFORE trigger should fire for each updated row");
        
        // Verify rows were actually updated
        var updatedRows = engine.Query("SELECT * FROM T WHERE Value = 999");
        Assert.That(updatedRows.Count, Is.EqualTo(2), "Two rows should have Value=999");
    }

    #endregion

    #region INSERT Performance Tests

    [Test]
    public void InsertAutoIncrementPerformanceTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        const int rowCount = 1000;
        var sw = Stopwatch.StartNew();
        
        for (int i = 0; i < rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Value) VALUES ({i}.0)");
        }
        
        sw.Stop();

        var countAfter = engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64();
        Assert.That(countAfter, Is.EqualTo(rowCount));

        var perRowMs = (double)sw.ElapsedMilliseconds / rowCount;
        TestContext.WriteLine($"INSERT {rowCount} rows (auto-increment): {sw.ElapsedMilliseconds}ms ({perRowMs:F3}ms/row)");
        
        // Should be less than 1ms per row for auto-increment
        Assert.That(perRowMs, Is.LessThan(1.0), $"INSERT auto-increment too slow: {perRowMs:F3}ms/row");
    }

    [Test]
    public void InsertExplicitIdPerformanceTest()
    {
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        const int rowCount = 1000;
        var sw = Stopwatch.StartNew();
        
        // Insert with explicit sequential IDs
        for (int i = 1; i <= rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Id, Value) VALUES ({i}, {i}.0)");
        }
        
        sw.Stop();

        var countAfter = engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64();
        Assert.That(countAfter, Is.EqualTo(rowCount));

        var perRowMs = (double)sw.ElapsedMilliseconds / rowCount;
        TestContext.WriteLine($"INSERT {rowCount} rows (explicit ID): {sw.ElapsedMilliseconds}ms ({perRowMs:F3}ms/row)");
        
        // Explicit ID may be slightly slower due to EnsureAutoIncrementAtLeast call,
        // but should still be less than 2ms per row
        Assert.That(perRowMs, Is.LessThan(2.0), $"INSERT explicit ID too slow: {perRowMs:F3}ms/row");
    }

    [Test]
    public void InsertExplicitIdWithReadLockOptimizationTest()
    {
        // Test that sequential explicit IDs benefit from read-lock optimization
        // (only the first insert should need a write lock, subsequent ones use read-lock fast path)
        
        var dbPath = Path.Combine(m_testDir, "test.witdb");
        using var db = new WitDatabaseBuilder()
            .WithFilePath(dbPath)
            .WithBTree()
            .WithTransactions()
            .Build();

        using var engine = new WitSqlEngine(db);
        
        engine.Execute("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT, Value DOUBLE)");
        
        // First insert with explicit ID = 1000 (will update counter)
        engine.Execute("INSERT INTO T (Id, Value) VALUES (1000, 1.0)");
        
        const int rowCount = 100;
        var sw = Stopwatch.StartNew();
        
        // Subsequent inserts with lower IDs should be fast (read-lock only, no disk write)
        for (int i = 1; i < rowCount; i++)
        {
            engine.Execute($"INSERT INTO T (Id, Value) VALUES ({i}, {i}.0)");
        }
        
        sw.Stop();

        var countAfter = engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64();
        Assert.That(countAfter, Is.EqualTo(rowCount));

        var perRowMs = (double)sw.ElapsedMilliseconds / (rowCount - 1);
        TestContext.WriteLine($"INSERT {rowCount - 1} rows (explicit ID < counter): {sw.ElapsedMilliseconds}ms ({perRowMs:F3}ms/row)");
        
        // These should be as fast as auto-increment since no counter update needed
        Assert.That(perRowMs, Is.LessThan(1.0), $"INSERT explicit ID (below counter) too slow: {perRowMs:F3}ms/row");
    }

    #endregion
}
