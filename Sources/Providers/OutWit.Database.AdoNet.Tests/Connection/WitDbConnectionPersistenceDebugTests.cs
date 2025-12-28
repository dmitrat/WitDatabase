using NUnit.Framework;
using System.Data;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Providers;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// Debug tests for WitDbConnection persistence issues.
/// </summary>
[TestFixture]
public class WitDbConnectionPersistenceDebugTests
{
    #region Fields

    private string? m_testDbPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbPersistDebug_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDbPath != null && File.Exists(m_testDbPath))
        {
            try { File.Delete(m_testDbPath); } catch { }
        }
    }

    #endregion

    #region Debug Tests

    [Test]
    public void DebugRoundTripWithAdoNetOnlyTest()
    {
        var connectionString = $"Data Source={m_testDbPath}";
        
        Console.WriteLine("=== Step 1: Create with ADO.NET ===");
        using (var conn1 = new WitDbConnection(connectionString))
        {
            conn1.Open();
            using var cmd = conn1.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Value TEXT)";
            var createResult = cmd.ExecuteNonQuery();
            Console.WriteLine($"CREATE TABLE result: {createResult}");
            
            cmd.CommandText = "INSERT INTO Test VALUES (1, 'AdoNetValue')";
            var insertResult = cmd.ExecuteNonQuery();
            Console.WriteLine($"INSERT result: {insertResult}");
            
            // Verify before close
            cmd.CommandText = "SELECT Value FROM Test WHERE Id = 1";
            var val = cmd.ExecuteScalar();
            Console.WriteLine($"Value before close: {val}");
        }

        Console.WriteLine($"\nFile exists: {File.Exists(m_testDbPath)}");
        Console.WriteLine($"File size: {new FileInfo(m_testDbPath!).Length}");

        Console.WriteLine("\n=== Step 2: Reopen with ADO.NET ===");
        using (var conn2 = new WitDbConnection(connectionString))
        {
            conn2.Open();
            
            using var cmd = conn2.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Test WHERE Id = 1";
            try
            {
                var val = cmd.ExecuteScalar();
                Console.WriteLine($"Value after reopen: {val}");
                Assert.That(val, Is.EqualTo("AdoNetValue"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Assert.Fail($"Failed to read persisted data: {ex.Message}");
            }
        }
    }

    [Test]
    public void DebugCheckSchemaAfterAdoNetCreateTest()
    {
        var connectionString = $"Data Source={m_testDbPath}";
        byte[] schemaKey = System.Text.Encoding.UTF8.GetBytes("$schema:_tables");
        
        Console.WriteLine("=== Step 1: Create with ADO.NET ===");
        using (var conn1 = new WitDbConnection(connectionString))
        {
            conn1.Open();
            using var cmd = conn1.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Value TEXT)";
            cmd.ExecuteNonQuery();
            
            cmd.CommandText = "INSERT INTO Test VALUES (1, 'AdoNetValue')";
            cmd.ExecuteNonQuery();
        }

        Console.WriteLine($"File exists: {File.Exists(m_testDbPath)}");
        Console.WriteLine($"File size: {new FileInfo(m_testDbPath!).Length}");

        // Now open with WitDatabase.Open to check schema
        Console.WriteLine("\n=== Step 2: Check schema with WitDatabase.Open ===");
        using (var database = WitDatabase.Open(m_testDbPath!))
        {
            var schemaData = database.Get(schemaKey);
            Console.WriteLine($"Schema data: {(schemaData != null ? $"{schemaData.Length} bytes" : "NULL")}");
            
            if (schemaData != null)
            {
                // Try to use engine to check tables
                using var engine = new WitSqlEngine(database, ownsStore: false);
                var table = engine.GetTable("Test");
                Console.WriteLine($"Table 'Test' exists: {table != null}");
                
                if (table != null)
                {
                    var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Test").AsInt64();
                    Console.WriteLine($"Row count: {count}");
                }
            }
        }
    }

    [Test]
    public void DebugCompareBuilderUsageTest()
    {
        Console.WriteLine("=== Test: WitDatabase.Create vs WitDatabaseBuilder ===");
        
        // WitDatabase.Create internally uses builder - let's replicate it
        Console.WriteLine("\n--- Using WitDatabase.Create ---");
        using (var db1 = WitDatabase.Create(m_testDbPath!))
        {
            db1.Put("test:key"u8, "test:value"u8);
            db1.Flush();
        }
        
        var size1 = new FileInfo(m_testDbPath!).Length;
        Console.WriteLine($"File size after WitDatabase.Create: {size1}");
        
        File.Delete(m_testDbPath!);
        
        // Now test with manual builder like WitDbConnection does
        Console.WriteLine("\n--- Using WitDatabaseBuilder manually ---");
        var builder = new WitDatabaseBuilder();
        builder.WithFilePath(m_testDbPath!);
        // Note: No explicit WithBTree() - just like WitDbConnection
        
        using (var db2 = builder.Build())
        {
            db2.Put("test:key"u8, "test:value"u8);
            db2.Flush();
        }
        
        var size2 = new FileInfo(m_testDbPath!).Length;
        Console.WriteLine($"File size after WitDatabaseBuilder: {size2}");
        
        // Now reopen
        Console.WriteLine("\n--- Reopen with WitDatabase.Open ---");
        using (var db3 = WitDatabase.Open(m_testDbPath!))
        {
            var value = db3.Get("test:key"u8);
            Console.WriteLine($"Value after reopen: {(value != null ? System.Text.Encoding.UTF8.GetString(value) : "NULL")}");
        }
    }

    [Test]
    public void DebugCompareStoreUsageTest()
    {
        byte[] schemaKey = System.Text.Encoding.UTF8.GetBytes("$schema:_tables");
        
        // Test: Use WitDatabase.Store directly (like SchemaCatalog does)
        Console.WriteLine("=== Test: Direct Store usage (like SchemaCatalog) ===");
        
        var builder = new WitDatabaseBuilder();
        builder.WithFilePath(m_testDbPath!);
        builder.WithBTree();
        builder.WithTransactions();
        
        using (var database = builder.Build())
        {
            // Write to Store directly (this is how SchemaCatalog works!)
            var store = database.Store;
            Console.WriteLine($"Store type: {store.GetType().Name}");
            
            store.Put(schemaKey.AsSpan(), "test:schema:data"u8);
            
            // Check before flush
            var before = store.Get(schemaKey.AsSpan());
            Console.WriteLine($"Before flush (from Store): {(before != null ? System.Text.Encoding.UTF8.GetString(before) : "NULL")}");
            
            // Flush via WitDatabase (not Store!)
            database.Flush();
            
            // Check after flush but before close
            var after = store.Get(schemaKey.AsSpan());
            Console.WriteLine($"After flush (from Store): {(after != null ? System.Text.Encoding.UTF8.GetString(after) : "NULL")}");
        }

        Console.WriteLine($"File size: {new FileInfo(m_testDbPath!).Length}");

        // Reopen
        Console.WriteLine("\n=== Reopen ===");
        using (var database = WitDatabase.Open(m_testDbPath!))
        {
            var value = database.Get(schemaKey);
            Console.WriteLine($"After reopen (via WitDatabase.Get): {(value != null ? System.Text.Encoding.UTF8.GetString(value) : "NULL")}");
            
            var storeValue = database.Store.Get(schemaKey.AsSpan());
            Console.WriteLine($"After reopen (via Store.Get): {(storeValue != null ? System.Text.Encoding.UTF8.GetString(storeValue) : "NULL")}");
        }
    }

    [Test]
    public void DebugSimpleExecuteNonQueryTest()
    {
        var connectionString = $"Data Source={m_testDbPath}";
        
        Console.WriteLine("=== Test: Simple ExecuteNonQuery check ===");
        using (var conn = new WitDbConnection(connectionString))
        {
            conn.Open();
            
            using var cmd = conn.CreateCommand();
            
            // Test 1: Create table
            cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
            try
            {
                var result = cmd.ExecuteNonQuery();
                Console.WriteLine($"CREATE TABLE returned: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CREATE TABLE failed: {ex.Message}");
                throw;
            }
            
            // Test 2: Check if table exists (within same session)
            cmd.CommandText = "SELECT COUNT(*) FROM Test";
            try
            {
                var count = cmd.ExecuteScalar();
                Console.WriteLine($"SELECT COUNT(*) returned: {count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SELECT failed: {ex.Message}");
                throw;
            }
        }
        
        Console.WriteLine($"File size after close: {new FileInfo(m_testDbPath!).Length}");
    }

    [Test]
    public void DebugWitDbConnectionCreateNewDatabaseTest()
    {
        var connectionString = $"Data Source={m_testDbPath}";
        byte[] testKey = System.Text.Encoding.UTF8.GetBytes("test:key");
        byte[] testValue = System.Text.Encoding.UTF8.GetBytes("test:value");
        
        Console.WriteLine("=== Step 1: Create database via connection string ===");
        
        // Parse connection string to see what it creates
        var csBuilder = new WitDbConnectionStringBuilder(connectionString);
        Console.WriteLine($"DataSource: {csBuilder.DataSource}");
        Console.WriteLine($"Mode: {csBuilder.Mode}");
        Console.WriteLine($"Store: {csBuilder.Store ?? "(null - default btree)"}");
        Console.WriteLine($"Transactions: {csBuilder.Transactions}");
        Console.WriteLine($"Mvcc: {csBuilder.Mvcc}");
        
        // Create connection and write data
        using (var conn = new WitDbConnection(connectionString))
        {
            conn.Open();
            
            // Write directly to database (bypassing SQL engine schema)
            // This tests if the underlying storage works
            Console.WriteLine("\n=== Writing raw key-value data ===");
            // Can't access database directly from WitDbConnection easily...
            
            // Let's test via SQL instead
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
            cmd.ExecuteNonQuery();
            Console.WriteLine("Table created");
        }

        Console.WriteLine($"\nFile exists: {File.Exists(m_testDbPath)}");
        Console.WriteLine($"File size: {new FileInfo(m_testDbPath!).Length}");
        
        // Check file header directly to see metadata flags
        Console.WriteLine("\n=== Step 2: Check file header for metadata ===");
        var detection = StorageDetector.Detect(m_testDbPath!);
        Console.WriteLine($"Detection - Exists: {detection.Exists}");
        Console.WriteLine($"Detection - StoreType: {detection.StoreType}");
        Console.WriteLine($"Detection - HasTransactions: {detection.HasTransactions}");
        Console.WriteLine($"Detection - HasMvcc: {detection.HasMvcc}");
        Console.WriteLine($"Detection - HasFileLocking: {detection.HasFileLocking}");
        
        // Now check what was actually written
        Console.WriteLine("\n=== Step 3: Open with WitDatabase.Open and check ===");
        using (var database = WitDatabase.Open(m_testDbPath!))
        {
            Console.WriteLine($"Database opened successfully");
            Console.WriteLine($"Store type: {database.Store.GetType().Name}");
            Console.WriteLine($"SupportsMvcc: {database.SupportsMvcc}");
            
            // Try to scan all keys
            var count = 0;
            foreach (var (key, value) in database.Scan())
            {
                var keyStr = System.Text.Encoding.UTF8.GetString(key);
                Console.WriteLine($"  Key: {keyStr} ({key.Length} bytes), Value: {value.Length} bytes");
                count++;
            }
            Console.WriteLine($"Total keys: {count}");
        }
    }

    #endregion
}
