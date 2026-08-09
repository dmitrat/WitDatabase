using System.Data;
using OutWit.Database.AdoNet;

namespace OutWit.Database.AdoNet.Tests.Concurrency;

/// <summary>
/// Stress tests for concurrent INSERT operations via ADO.NET.
/// Tests connection pooling, parallel execution, and data consistency.
/// </summary>
[TestFixture]
[Category("Stress")]
public class ConcurrentInsertAdoNetStressTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"adonet_concurrent_insert_{Guid.NewGuid():N}");
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

    #region Sequential Insert Tests

    [Test]
    public void SequentialInsertsWithSingleConnectionTest()
    {
        var dbPath = Path.Combine(m_testDir, "seq_single.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Value REAL)";
            cmd.ExecuteNonQuery();
        }

        const int rowCount = 200;

        for (int i = 0; i < rowCount; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Items (Name, Value) VALUES (@name, @value)";
            cmd.Parameters.AddWithValue("@name", $"Item{i}");
            cmd.Parameters.AddWithValue("@value", i * 1.5);
            cmd.ExecuteNonQuery();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(rowCount));
        }

        // Verify IDs
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT MIN(Id), MAX(Id) FROM Items";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            Assert.That(reader.GetInt64(0), Is.EqualTo(1));
            Assert.That(reader.GetInt64(1), Is.EqualTo(rowCount));
        }
    }

    [Test]
    public void SequentialInsertsWithMultipleConnectionsTest()
    {
        var dbPath = Path.Combine(m_testDir, "seq_multi.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        // First connection - create table and insert some data
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Data (Id INTEGER PRIMARY KEY AUTOINCREMENT, Value INT)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 50; i++)
            {
                cmd.CommandText = $"INSERT INTO Data (Value) VALUES ({i})";
                cmd.ExecuteNonQuery();
            }
        }

        // Second connection - insert more data
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();

            for (int i = 50; i < 100; i++)
            {
                cmd.CommandText = $"INSERT INTO Data (Value) VALUES ({i})";
                cmd.ExecuteNonQuery();
            }
        }

        // Third connection - verify
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Data";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(100));

            cmd.CommandText = "SELECT MAX(Id) FROM Data";
            var maxId = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(maxId, Is.EqualTo(100));
        }
    }

    #endregion

    #region Transaction Tests

    [Test]
    public void InsertInTransactionCommitTest()
    {
        var dbPath = Path.Combine(m_testDir, "tx_commit.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)";
            cmd.ExecuteNonQuery();
        }

        using (var tx = (WitDbTransaction)conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            for (int i = 0; i < 100; i++)
            {
                cmd.CommandText = $"INSERT INTO Items (Name) VALUES ('Item{i}')";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(100));
        }
    }

    [Test]
    public void InsertInTransactionRollbackTest()
    {
        var dbPath = Path.Combine(m_testDir, "tx_rollback.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)";
            cmd.ExecuteNonQuery();
        }

        // Insert committed data
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Items (Name) VALUES ('Committed')";
            cmd.ExecuteNonQuery();
        }

        // Insert and rollback
        using (var tx = (WitDbTransaction)conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            for (int i = 0; i < 50; i++)
            {
                cmd.CommandText = $"INSERT INTO Items (Name) VALUES ('RolledBack{i}')";
                cmd.ExecuteNonQuery();
            }

            tx.Rollback();
        }

        // Verify only committed data exists
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(1));

            cmd.CommandText = "SELECT Name FROM Items";
            var name = cmd.ExecuteScalar()?.ToString();
            Assert.That(name, Is.EqualTo("Committed"));
        }
    }

    [Test]
    public void MultipleTransactionsSequentialTest()
    {
        var dbPath = Path.Combine(m_testDir, "multi_tx.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Log (Id INTEGER PRIMARY KEY AUTOINCREMENT, TxId INT, Seq INT)";
            cmd.ExecuteNonQuery();
        }

        const int transactionCount = 20;
        const int rowsPerTransaction = 10;

        for (int txId = 0; txId < transactionCount; txId++)
        {
            using var tx = (WitDbTransaction)conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            for (int seq = 0; seq < rowsPerTransaction; seq++)
            {
                cmd.CommandText = $"INSERT INTO Log (TxId, Seq) VALUES ({txId}, {seq})";
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Log";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(transactionCount * rowsPerTransaction));

            // Verify IDs are unique
            cmd.CommandText = "SELECT COUNT(DISTINCT Id) FROM Log";
            var distinctCount = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(distinctCount, Is.EqualTo(transactionCount * rowsPerTransaction));
        }
    }

    #endregion

    #region Bulk Insert Tests

    [Test]
    public void BulkInsertWithPreparedStatementTest()
    {
        var dbPath = Path.Combine(m_testDir, "bulk_prepared.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTOINCREMENT, SKU TEXT, Name TEXT, Price REAL)";
            cmd.ExecuteNonQuery();
        }

        const int rowCount = 1000;

        using (var tx = (WitDbTransaction)conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Products (SKU, Name, Price) VALUES (@sku, @name, @price)";
            
            var pSku = cmd.CreateParameter(); pSku.ParameterName = "@sku"; cmd.Parameters.Add(pSku);
            var pName = cmd.CreateParameter(); pName.ParameterName = "@name"; cmd.Parameters.Add(pName);
            var pPrice = cmd.CreateParameter(); pPrice.ParameterName = "@price"; cmd.Parameters.Add(pPrice);

            for (int i = 0; i < rowCount; i++)
            {
                pSku.Value = $"SKU-{i:D6}";
                pName.Value = $"Product {i}";
                pPrice.Value = 10.00 + i * 0.10;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Products";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(rowCount));
        }
    }

    [Test]
    public void BulkInsertWithoutTransactionTest()
    {
        var dbPath = Path.Combine(m_testDir, "bulk_no_tx.witdb");
        var cs = $"Data Source={dbPath};Transactions=false"; // Auto-commit mode

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Value INT)";
            cmd.ExecuteNonQuery();
        }

        const int rowCount = 100;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Items (Value) VALUES (@v)";
            var pv = cmd.CreateParameter(); pv.ParameterName = "@v"; cmd.Parameters.Add(pv);

            for (int i = 0; i < rowCount; i++)
            {
                pv.Value = i;
                cmd.ExecuteNonQuery();
            }
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(rowCount));
        }
    }

    #endregion

    #region Row ID Tracking Tests

    [Test]
    public void LastInsertRowIdTest()
    {
        var dbPath = Path.Combine(m_testDir, "last_rowid.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)";
            cmd.ExecuteNonQuery();
        }

        var insertedIds = new List<long>();

        for (int i = 0; i < 20; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Users (Name) VALUES (@name) RETURNING Id";
            cmd.Parameters.AddWithValue("@name", $"User{i}");
            
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            insertedIds.Add(id);
        }

        // Verify IDs are sequential
        Assert.That(insertedIds, Is.EqualTo(Enumerable.Range(1, 20).Select(i => (long)i)));
    }

    [Test]
    public void ExplicitIdInsertTest()
    {
        var dbPath = Path.Combine(m_testDir, "explicit_id.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Data (Id INTEGER PRIMARY KEY AUTOINCREMENT, Value INT)";
            cmd.ExecuteNonQuery();
        }

        // Insert with auto-increment
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Data (Value) VALUES (1)";
            cmd.ExecuteNonQuery();
        }

        // Insert with explicit high ID
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Data (Id, Value) VALUES (100, 2)";
            cmd.ExecuteNonQuery();
        }

        // Next auto-increment should be > 100
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Data (Value) VALUES (3) RETURNING Id";
            var newId = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(newId, Is.EqualTo(101));
        }
    }

    #endregion

    #region Concurrency Tests

    [Test]
    public void SequentialConnectionsPreserveAutoIncrementTest()
    {
        var dbPath = Path.Combine(m_testDir, "seq_conn_autoinc.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        // Connection 1 - create and insert
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Data (Id INTEGER PRIMARY KEY AUTOINCREMENT, Value INT)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 50; i++)
            {
                cmd.CommandText = $"INSERT INTO Data (Value) VALUES ({i})";
                cmd.ExecuteNonQuery();
            }
        }

        // Connection 2 - insert more
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Data (Value) VALUES (999) RETURNING Id";
            var newId = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(newId, Is.EqualTo(51), "Auto-increment should continue from persisted value");
        }
    }

    [Test]
    public void SavepointInsertTest()
    {
        var dbPath = Path.Combine(m_testDir, "savepoint.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)";
            cmd.ExecuteNonQuery();
        }

        using (var tx = (WitDbTransaction)conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            // Insert before savepoint
            cmd.CommandText = "INSERT INTO Items (Name) VALUES ('Before')";
            cmd.ExecuteNonQuery();

            // Create savepoint (if supported)
            cmd.CommandText = "SAVEPOINT sp1";
            cmd.ExecuteNonQuery();

            // Insert after savepoint
            cmd.CommandText = "INSERT INTO Items (Name) VALUES ('After1')";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO Items (Name) VALUES ('After2')";
            cmd.ExecuteNonQuery();

            // Verify 3 rows visible
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var countBefore = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(countBefore, Is.EqualTo(3));

            // Rollback to savepoint
            cmd.CommandText = "ROLLBACK TO SAVEPOINT sp1";
            cmd.ExecuteNonQuery();

            // Verify only 1 row visible
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var countAfter = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(countAfter, Is.EqualTo(1));

            tx.Commit();
        }

        // Verify final state
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Items";
            var finalCount = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(finalCount, Is.EqualTo(1));
        }
    }

    #endregion

    #region Data Types Insert Tests

    [Test]
    public void InsertVariousDataTypesTest()
    {
        var dbPath = Path.Combine(m_testDir, "datatypes.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE AllTypes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    IntVal INT,
                    BigIntVal BIGINT,
                    DoubleVal DOUBLE,
                    DecimalVal DECIMAL(10,2),
                    BoolVal BOOLEAN,
                    TextVal TEXT,
                    DateVal DATETIME,
                    GuidVal GUID,
                    BlobVal BLOB
                )";
            cmd.ExecuteNonQuery();
        }

        var testGuid = Guid.NewGuid();
        var testDate = new DateTime(2024, 6, 15, 12, 30, 45, DateTimeKind.Utc);
        var testBlob = new byte[] { 1, 2, 3, 4, 5 };

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO AllTypes (IntVal, BigIntVal, DoubleVal, DecimalVal, BoolVal, TextVal, DateVal, GuidVal, BlobVal)
                VALUES (@int, @bigint, @double, @decimal, @bool, @text, @date, @guid, @blob)";

            cmd.Parameters.AddWithValue("@int", 42);
            cmd.Parameters.AddWithValue("@bigint", 9876543210L);
            cmd.Parameters.AddWithValue("@double", 3.14159);
            cmd.Parameters.AddWithValue("@decimal", 123.45m);
            cmd.Parameters.AddWithValue("@bool", true);
            cmd.Parameters.AddWithValue("@text", "Hello, World!");
            cmd.Parameters.AddWithValue("@date", testDate);
            cmd.Parameters.AddWithValue("@guid", testGuid);
            cmd.Parameters.AddWithValue("@blob", testBlob);

            cmd.ExecuteNonQuery();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM AllTypes WHERE Id = 1";
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read(), Is.True);

            Assert.That(reader.GetInt32(reader.GetOrdinal("IntVal")), Is.EqualTo(42));
            Assert.That(reader.GetInt64(reader.GetOrdinal("BigIntVal")), Is.EqualTo(9876543210L));
            Assert.That(reader.GetDouble(reader.GetOrdinal("DoubleVal")), Is.EqualTo(3.14159).Within(0.00001));
            Assert.That(reader.GetDecimal(reader.GetOrdinal("DecimalVal")), Is.EqualTo(123.45m));
            Assert.That(reader.GetBoolean(reader.GetOrdinal("BoolVal")), Is.True);
            Assert.That(reader.GetString(reader.GetOrdinal("TextVal")), Is.EqualTo("Hello, World!"));
            Assert.That(reader.GetGuid(reader.GetOrdinal("GuidVal")), Is.EqualTo(testGuid));
        }
    }

    [Test]
    public void InsertNullValuesTest()
    {
        var dbPath = Path.Combine(m_testDir, "nulls.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Nullable (Id INTEGER PRIMARY KEY, Value TEXT NULL, Count INT NULL)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Nullable (Id, Value, Count) VALUES (1, NULL, NULL)";
            cmd.ExecuteNonQuery();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM Nullable WHERE Id = 1";
            using var reader = cmd.ExecuteReader();
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.IsDBNull(reader.GetOrdinal("Value")), Is.True);
            Assert.That(reader.IsDBNull(reader.GetOrdinal("Count")), Is.True);
        }
    }

    #endregion

    #region Large Transaction Tests

    [Test]
    [CancelAfter(60000)]
    public void LargeTransactionTest()
    {
        var dbPath = Path.Combine(m_testDir, "large_tx.witdb");
        var cs = $"Data Source={dbPath};Transactions=true";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE BigTable (Id INTEGER PRIMARY KEY AUTOINCREMENT, Data TEXT)";
            cmd.ExecuteNonQuery();
        }

        const int rowCount = 5000;
        var data = new string('X', 100); // 100 chars per row

        using (var tx = (WitDbTransaction)conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO BigTable (Data) VALUES (@d)";
            var pd = cmd.CreateParameter(); pd.ParameterName = "@d"; pd.Value = data; cmd.Parameters.Add(pd);

            for (int i = 0; i < rowCount; i++)
            {
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // Verify
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM BigTable";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(rowCount));
        }
    }

    #endregion
}
