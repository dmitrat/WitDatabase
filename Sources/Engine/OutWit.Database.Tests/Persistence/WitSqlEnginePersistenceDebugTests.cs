using OutWit.Database.Core.Builder;

namespace OutWit.Database.Tests.Persistence;

/// <summary>
/// Tests for debugging data persistence issues.
/// </summary>
[TestFixture]
public sealed class WitSqlEnginePersistenceDebugTests
{
    #region Fields

    private string? m_testDir;
    private string? m_testDbPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDbDebug_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_testDbPath = Path.Combine(m_testDir, "test.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDir != null && Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region Debug Tests

    [Test]
    public void DebugTablePersistenceWithExplicitFlushTest()
    {
        // Create database and table with explicit flush
        using (var database = WitDatabase.Create(m_testDbPath!))
        {
            using (var engine = new Engine.WitSqlEngine(database, ownsStore: false))
            {
                engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY, Value TEXT)");
                engine.Execute("INSERT INTO Test VALUES (1, 'TestValue')");
                
                // Verify table exists before closing
                var table = engine.GetTable("Test");
                Assert.That(table, Is.Not.Null, "Table should exist before close");
            }
            
            // Explicit flush
            database.Flush();
        }

        // Check file size
        var fileInfo = new FileInfo(m_testDbPath!);
        Console.WriteLine($"Database file size after create: {fileInfo.Length} bytes");

        // Reopen with WitDatabase.Open
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: false))
        {
            var table = engine.GetTable("Test");
            Assert.That(table, Is.Not.Null, "Table should exist after reopen");
            
            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Test").AsInt64();
            Assert.That(count, Is.EqualTo(1), "Should have 1 row after reopen");
        }
    }

    [Test]
    public void DebugSchemaKeysPersistenceTest()
    {
        byte[] schemaKey = System.Text.Encoding.UTF8.GetBytes("$schema:_tables");
        
        // Create database and check schema persistence
        using (var database = WitDatabase.Create(m_testDbPath!))
        {
            using (var engine = new Engine.WitSqlEngine(database, ownsStore: false))
            {
                engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY)");
            }
            
            // Check if schema key exists before flush
            var schemaDataBefore = database.Get(schemaKey);
            Console.WriteLine($"Schema data before flush: {(schemaDataBefore != null ? $"{schemaDataBefore.Length} bytes" : "null")}");
            
            database.Flush();
            
            // Check if schema key exists after flush
            var schemaDataAfter = database.Get(schemaKey);
            Console.WriteLine($"Schema data after flush: {(schemaDataAfter != null ? $"{schemaDataAfter.Length} bytes" : "null")}");
        }

        // Reopen and check
        using (var database = WitDatabase.Open(m_testDbPath!))
        {
            var schemaData = database.Get(schemaKey);
            Console.WriteLine($"Schema data after reopen: {(schemaData != null ? $"{schemaData.Length} bytes" : "null")}");
            Assert.That(schemaData, Is.Not.Null, "Schema data should persist after reopen");
        }
    }

    [Test]
    public void DebugLowLevelKeyValuePersistenceTest()
    {
        byte[] testKey = System.Text.Encoding.UTF8.GetBytes("test:key");
        byte[] testValue = System.Text.Encoding.UTF8.GetBytes("test:value");
        
        // Create database and store raw key-value
        using (var database = WitDatabase.Create(m_testDbPath!))
        {
            database.Put(testKey, testValue);
            
            // Check value exists before flush
            var valueBefore = database.Get(testKey);
            Assert.That(valueBefore, Is.Not.Null, "Value should exist before flush");
            
            database.Flush();
        }

        // Reopen and check
        using (var database = WitDatabase.Open(m_testDbPath!))
        {
            var value = database.Get(testKey);
            Assert.That(value, Is.Not.Null, "Value should persist after reopen");
            Assert.That(value, Is.EqualTo(testValue), "Value content should match");
        }
    }

    #endregion
}
