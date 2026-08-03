using NUnit.Framework;
using System.Data;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// Tests for real-world usage scenarios of WitDbConnection.
/// </summary>
[TestFixture]
public class WitDbConnectionScenariosTests
{
    #region Fields

    private string? m_testDbPath;
    private string? m_testLsmPath;

    /// <summary>
    /// A directory of its own for the <c>ChangeDatabase</c> tests, which need the database to be
    /// <b>named</b> <c>mydb</c> rather than to contain anything.
    /// </summary>
    /// <remarks>
    /// They used to write <c>Data Source=mydb.witdb</c> - a relative path, so the file landed in the
    /// test runner's working directory and was never deleted. The one on this machine was created in
    /// December 2025 and had been reopened by every run since; when 12.0.0 started refusing a database
    /// whose transaction model does not match the connection string, all three failed, because that
    /// seven-month-old file was written without MVCC. A test that shares a database with its own
    /// history is a test whose subject can change without anyone touching it.
    /// </remarks>
    private string? m_changeDatabaseDirectory;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbScenario_{Guid.NewGuid():N}.witdb");
        m_testLsmPath = Path.Combine(Path.GetTempPath(), $"WitDbScenario_LSM_{Guid.NewGuid():N}");
        m_changeDatabaseDirectory = Path.Combine(Path.GetTempPath(), $"WitDbScenario_Name_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_changeDatabaseDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDbPath != null && File.Exists(m_testDbPath))
        {
            try { File.Delete(m_testDbPath); } catch { }
        }
        
        if (m_testLsmPath != null && Directory.Exists(m_testLsmPath))
        {
            try { Directory.Delete(m_testLsmPath, recursive: true); } catch { }
        }

        if (m_changeDatabaseDirectory != null && Directory.Exists(m_changeDatabaseDirectory))
        {
            try { Directory.Delete(m_changeDatabaseDirectory, recursive: true); } catch { }
        }
    }

    /// <summary>A database called <c>mydb</c>, in a directory nothing else writes to.</summary>
    private string NamedDatabase() => Path.Combine(m_changeDatabaseDirectory!, "mydb.witdb");

    #endregion

    #region Full Workflow Scenarios

    [Test]
    public void FullWorkflowWithEncryptionAndMvccTest()
    {
        var connectionString = $"Data Source={m_testDbPath};Encryption=aes-gcm;Password=SecurePass123;MVCC=true;Isolation Level=Snapshot";

        // Create and populate database
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.Snapshot);
            cmd.Transaction = tx;

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();

            tx.Commit();
        }

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.EqualTo(2L));
        }
    }

    /// <summary>
    /// Diagnostic test to isolate Encryption + MVCC persistence issue.
    /// Tests encryption WITHOUT MVCC to verify encryption persistence works.
    /// </summary>
    [Test]
    public void DiagnosticEncryptionWithoutMvccPersistsTest()
    {
        var connectionString = $"Data Source={m_testDbPath};Encryption=aes-gcm;Password=SecurePass123;MVCC=false;Transactions=false";

        // Create and populate database
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();
        }

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.EqualTo(2L), "Encryption without MVCC should persist data");
        }
    }

    /// <summary>
    /// Diagnostic test to isolate Encryption + MVCC persistence issue.
    /// Tests MVCC WITHOUT encryption to verify MVCC persistence works.
    /// </summary>
    [Test]
    public void DiagnosticMvccWithoutEncryptionPersistsTest()
    {
        var connectionString = $"Data Source={m_testDbPath};MVCC=true;Isolation Level=Snapshot";

        // Create and populate database
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.Snapshot);
            cmd.Transaction = tx;

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();

            tx.Commit();
        }

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.EqualTo(2L), "MVCC without encryption should persist data");
        }
    }

    /// <summary>
    /// Diagnostic test: Encryption + MVCC combined.
    /// This is the problematic scenario - now without [Ignore] to see exact error.
    /// </summary>
    [Test]
    public void DiagnosticEncryptionWithMvccPersistsTest()
    {
        var connectionString = $"Data Source={m_testDbPath};Encryption=aes-gcm;Password=SecurePass123;MVCC=true;Isolation Level=Snapshot";

        // Create and populate database
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.Snapshot);
            cmd.Transaction = tx;

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();

            tx.Commit();
            
            // Verify data is there before close
            cmd.Transaction = null;
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var countBeforeClose = cmd.ExecuteScalar();
            Assert.That(countBeforeClose, Is.EqualTo(2L), "Data should exist before connection close");
        }

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.EqualTo(2L), "Encryption + MVCC should persist data after reopen");
        }
    }

    /// <summary>
    /// Diagnostic test: Encryption + MVCC without Snapshot isolation.
    /// Tests if the issue is specific to Snapshot isolation level.
    /// </summary>
    [Test]
    public void DiagnosticEncryptionWithMvccReadCommittedPersistsTest()
    {
        var connectionString = $"Data Source={m_testDbPath};Encryption=aes-gcm;Password=SecurePass123;MVCC=true;Isolation Level=ReadCommitted";

        // Create and populate database
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.ReadCommitted);
            cmd.Transaction = tx;

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();

            tx.Commit();
        }

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.EqualTo(2L), "Encryption + MVCC with ReadCommitted should persist data");
        }
    }

    /// <summary>
    /// Diagnostic test: Encryption + MVCC without explicit transaction.
    /// Tests if the issue is related to explicit transaction usage.
    /// </summary>
    [Test]
    public void DiagnosticEncryptionWithMvccNoExplicitTransactionPersistsTest()
    {
        var connectionString = $"Data Source={m_testDbPath};Encryption=aes-gcm;Password=SecurePass123;MVCC=true";

        // Create and populate database WITHOUT explicit transaction
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();
        }

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.EqualTo(2L), "Encryption + MVCC without explicit tx should persist data");
        }
    }

    [Test]
    public void LsmStoreWithEncryptionTest()
    {
        var connectionString = $"Data Source={m_testLsmPath};Store=lsm;Encryption=aes-gcm;Password=LsmPassword;LSM MemTable Size=1048576";

        using var connection = new WitDbConnection(connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Data (DataKey VARCHAR(100) PRIMARY KEY, DataValue TEXT)";
        cmd.ExecuteNonQuery();

        // Insert multiple records to test LSM behavior
        for (int i = 0; i < 100; i++)
        {
            cmd.CommandText = $"INSERT INTO Data VALUES ('key{i}', 'value{i}')";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "SELECT COUNT(*) FROM Data";
        var count = cmd.ExecuteScalar();

        Assert.That(count, Is.EqualTo(100L));
    }

    #endregion

    #region Default Behavior Scenarios

    [Test]
    public void DefaultsWorkCorrectlyTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        // Verify we can execute queries (database is properly initialized with defaults)
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO Test VALUES (1)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Id FROM Test WHERE Id = 1";
        var result = cmd.ExecuteScalar();
        Assert.That(result, Is.EqualTo(1L));
    }

    [Test]
    public void DefaultStoreIsBTreeTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Value VARCHAR(100))";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO Test VALUES (1, 'test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Value FROM Test WHERE Id = 1";
        var result = cmd.ExecuteScalar();
        Assert.That(result, Is.EqualTo("test"));

        Assert.That(File.Exists(m_testDbPath), Is.True);
    }

    [Test]
    public void DefaultMvccIsEnabledTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
        cmd.ExecuteNonQuery();

        // Snapshot isolation requires MVCC
        using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.Snapshot);
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Test VALUES (1)";
        cmd.ExecuteNonQuery();
        tx.Commit();

        cmd.Transaction = null;
        cmd.CommandText = "SELECT COUNT(*) FROM Test";
        var count = cmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(1L));
    }

    [Test]
    public void DefaultTransactionsEnabledTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
        cmd.ExecuteNonQuery();

        using var tx = (WitDbTransaction)connection.BeginTransaction();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Test VALUES (1)";
        cmd.ExecuteNonQuery();

        // Rollback
        tx.Rollback();

        cmd.Transaction = null;
        cmd.CommandText = "SELECT COUNT(*) FROM Test";
        var count = cmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(0L), "Rollback should have undone the insert");
    }

    #endregion

    #region Partial Configuration Scenarios

    [Test]
    public void OnlyOverrideCacheSizeTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Cache Size=100");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
        cmd.ExecuteNonQuery();

        // Transactions should work (default enabled)
        using var tx = (WitDbTransaction)connection.BeginTransaction();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Test VALUES (1)";
        cmd.ExecuteNonQuery();
        tx.Commit();

        cmd.Transaction = null;
        cmd.CommandText = "SELECT COUNT(*) FROM Test";
        var count = cmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(1L));
    }

    [Test]
    public void ExplicitDefaultsSameAsOmittingTest()
    {
        var explicitDefaults = $"Data Source={m_testDbPath};Store=btree;MVCC=true;Transactions=true;Isolation Level=ReadCommitted";

        using var connection = new WitDbConnection(explicitDefaults);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY)";
        cmd.ExecuteNonQuery();

        using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.Snapshot);
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Test VALUES (1)";
        cmd.ExecuteNonQuery();
        tx.Commit();

        cmd.Transaction = null;
        cmd.CommandText = "SELECT COUNT(*) FROM Test";
        var count = cmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(1L));
    }

    #endregion

    #region Command Execution Scenarios

    [Test]
    public void CreateCommandReturnsWitDbCommandTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();

        Assert.That(command, Is.InstanceOf<WitDbCommand>());
        Assert.That(command.Connection, Is.SameAs(connection));
    }

    [Test]
    public void ExecuteSimpleQueryTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 + 1";
        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.EqualTo(2L));
    }

    [Test]
    public void CrudOperationsTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var cmd = connection.CreateCommand();

        // Create
        cmd.CommandText = "CREATE TABLE Products (Id INT PRIMARY KEY, Name VARCHAR(100), Price DECIMAL(10,2))";
        cmd.ExecuteNonQuery();

        // Insert
        cmd.CommandText = "INSERT INTO Products VALUES (1, 'Widget', 9.99)";
        cmd.ExecuteNonQuery();

        // Read
        cmd.CommandText = "SELECT Name FROM Products WHERE Id = 1";
        var name = cmd.ExecuteScalar();
        Assert.That(name, Is.EqualTo("Widget"));

        // Update
        cmd.CommandText = "UPDATE Products SET Price = 19.99 WHERE Id = 1";
        cmd.ExecuteNonQuery();

        // Verify update
        cmd.CommandText = "SELECT Price FROM Products WHERE Id = 1";
        var price = cmd.ExecuteScalar();
        Assert.That(price, Is.EqualTo(19.99m).Or.EqualTo(19.99));

        // Delete
        cmd.CommandText = "DELETE FROM Products WHERE Id = 1";
        cmd.ExecuteNonQuery();

        // Verify delete
        cmd.CommandText = "SELECT COUNT(*) FROM Products";
        var count = cmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(0L));
    }

    #endregion

    #region Schema Scenarios

    [Test]
    public void GetSchemaReturnsMetaDataCollectionsTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        var schema = connection.GetSchema();

        Assert.That(schema, Is.Not.Null);
        Assert.That(schema.Rows.Count, Is.GreaterThan(0));
    }

    [Test]
    public void GetSchemaTablesReturnsTableInfoTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE TestTable (Id INT PRIMARY KEY)";
        cmd.ExecuteNonQuery();

        var schema = connection.GetSchema("Tables");

        Assert.That(schema, Is.Not.Null);
    }

    #endregion

    #region ChangeDatabase Scenarios

    [Test]
    public void ChangeDatabaseToSameNameSucceedsTest()
    {
        using var connection = new WitDbConnection($"Data Source={NamedDatabase()}");
        connection.Open();

        Assert.DoesNotThrow(() => connection.ChangeDatabase("mydb"));
    }

    [Test]
    public void ChangeDatabaseToMainSucceedsTest()
    {
        using var connection = new WitDbConnection($"Data Source={NamedDatabase()}");
        connection.Open();

        Assert.DoesNotThrow(() => connection.ChangeDatabase("main"));
    }

    [Test]
    public void ChangeDatabaseToDifferentNameThrowsTest()
    {
        using var connection = new WitDbConnection($"Data Source={NamedDatabase()}");
        connection.Open();

        Assert.Throws<NotSupportedException>(() => connection.ChangeDatabase("otherdb"));
    }

    #endregion

    #region High Performance Scenarios

    [Test]
    public void HighPerformanceWriteHeavyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testLsmPath};Store=lsm;Transactions=false;MVCC=false");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Logs (Id INT PRIMARY KEY, Message TEXT, CreatedAt BIGINT)";
        cmd.ExecuteNonQuery();

        // Bulk insert
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < 1000; i++)
        {
            cmd.CommandText = $"INSERT INTO Logs VALUES ({i}, 'Log message {i}', {timestamp + i})";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "SELECT COUNT(*) FROM Logs";
        var count = cmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(1000L));
    }

    [Test]
    public void ConcurrentReadsTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:;MVCC=true");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Data (Id INT PRIMARY KEY, Value INT)";
        cmd.ExecuteNonQuery();

        for (int i = 0; i < 100; i++)
        {
            cmd.CommandText = $"INSERT INTO Data VALUES ({i}, {i * 10})";
            cmd.ExecuteNonQuery();
        }

        // Multiple read transactions
        var tasks = new List<Task<long>>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                using var readCmd = connection.CreateCommand();
                readCmd.CommandText = "SELECT SUM(Value) FROM Data";
                return Convert.ToInt64(readCmd.ExecuteScalar());
            }));
        }

        Task.WaitAll(tasks.ToArray());

        foreach (var task in tasks)
        {
            Assert.That(task.Result, Is.EqualTo(49500L));
        }
    }

    /// <summary>
    /// Diagnostic test: Use Core API directly with MVCC (no ADO.NET).
    /// This isolates whether the problem is in Core or ADO.NET.
    /// </summary>
    [Test]
    public void DiagnosticCoreMvccDirectPersistsTest()
    {
        // Create with MVCC using Core API directly (like the ProviderMetadataTests)
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(m_testDbPath)
            .WithBTree()
            .WithMvcc()
            .Build())
        {
            db.Put("key1"u8, "value1"u8);
            db.Put("key2"u8, "value2"u8);
        }

        // Reopen and verify
        using (var db = WitDatabase.Open(m_testDbPath))
        {
            Assert.That(db.SupportsMvcc, Is.True, "Reopened database should use MVCC");
            Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()), "key1 should persist");
            Assert.That(db.Get("key2"u8), Is.EqualTo("value2"u8.ToArray()), "key2 should persist");
        }
    }

    /// <summary>
    /// Diagnostic test: Use Core API with MVCC transaction.
    /// </summary>
    [Test]
    public void DiagnosticCoreMvccWithTransactionPersistsTest()
    {
        // Create with MVCC using Core API with transaction
        using (var db = new WitDatabaseBuilder()
            .WithFilePath(m_testDbPath)
            .WithBTree()
            .WithMvcc()
            .Build())
        {
            using var tx = db.BeginTransaction();
            tx.Put("key1"u8, "value1"u8);
            tx.Put("key2"u8, "value2"u8);
            tx.Commit();
        }

        // Reopen and verify
        using (var db = WitDatabase.Open(m_testDbPath))
        {
            Assert.That(db.SupportsMvcc, Is.True, "Reopened database should use MVCC");
            Assert.That(db.Get("key1"u8), Is.EqualTo("value1"u8.ToArray()), "key1 should persist");
            Assert.That(db.Get("key2"u8), Is.EqualTo("value2"u8.ToArray()), "key2 should persist");
        }
    }

    /// <summary>
    /// Diagnostic test: MVCC with explicit transaction - step by step tracing.
    /// </summary>
    [Test]
    public void DiagnosticMvccWithExplicitTransactionDetailedTest()
    {
        var connectionString = $"Data Source={m_testDbPath};MVCC=true";

        // Create and populate database WITH explicit transaction
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();
            var engine = connection.Engine!;
            var database = engine.GetType().GetField("m_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine) as WitDatabase;

            TestContext.WriteLine($"Database SupportsMvcc: {database?.SupportsMvcc}");

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            // Count before transaction
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            TestContext.WriteLine($"Count before transaction: {cmd.ExecuteScalar()}");

            // Begin transaction
            using var tx = (WitDbTransaction)connection.BeginTransaction(IsolationLevel.Snapshot);
            cmd.Transaction = tx;
            
            TestContext.WriteLine($"Transaction started. Engine.CurrentTransaction type: {engine.CurrentTransaction?.GetType().Name}");

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();

            // Count within transaction (before commit)
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var countInTx = cmd.ExecuteScalar();
            TestContext.WriteLine($"Count within transaction (before commit): {countInTx}");

            // Commit
            TestContext.WriteLine("Committing transaction...");
            tx.Commit();
            TestContext.WriteLine($"Transaction committed. Engine.CurrentTransaction is null: {engine.CurrentTransaction == null}");

            // Count after commit (within same connection)
            cmd.Transaction = null;
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var countAfterCommit = cmd.ExecuteScalar();
            TestContext.WriteLine($"Count after commit (same connection): {countAfterCommit}");
            
            // Check keys in store (raw scan)
            TestContext.WriteLine("Raw store scan:");
            var store = database!.Store;
            if (store is OutWit.Database.Core.Transactions.MvccTransactionalStore mvccStore)
            {
                var innerStore = mvccStore.MvccStore.InnerStore;
                foreach (var (key, value) in innerStore.Scan(null, null))
                {
                    var keyStr = System.Text.Encoding.UTF8.GetString(key);
                    if (keyStr.StartsWith("t:Users"))
                    {
                        // Try to parse as MVCC record
                        if (OutWit.Database.Core.Mvcc.MvccRecord.TryDeserialize(value, out var record))
                        {
                            TestContext.WriteLine($"  Key: {keyStr.Substring(0, Math.Min(30, keyStr.Length))}...");
                            TestContext.WriteLine($"    CreateTs: {record.CreateTimestamp}");
                            TestContext.WriteLine($"    CommitTs: {record.CommitTimestamp}");
                            TestContext.WriteLine($"    TxId: {record.TransactionId}");
                            TestContext.WriteLine($"    IsCommitted: {record.IsCommitted}");
                            TestContext.WriteLine($"    DeleteTs: {record.DeleteTimestamp}");
                        }
                    }
                }
            }

            // Flush explicitly
            TestContext.WriteLine("Flushing...");
            database.Flush();
        }

        TestContext.WriteLine("Connection closed. Reopening...");

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();
            var engine = connection.Engine!;
            var database = engine.GetType().GetField("m_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine) as WitDatabase;
            
            // Check raw store after reopen
            TestContext.WriteLine("Raw store scan after reopen:");
            var store = database!.Store;
            if (store is OutWit.Database.Core.Transactions.MvccTransactionalStore mvccStore)
            {
                var innerStore = mvccStore.MvccStore.InnerStore;
                foreach (var (key, value) in innerStore.Scan(null, null))
                {
                    var keyStr = System.Text.Encoding.UTF8.GetString(key);
                    if (keyStr.StartsWith("t:Users"))
                    {
                        if (OutWit.Database.Core.Mvcc.MvccRecord.TryDeserialize(value, out var record))
                        {
                            TestContext.WriteLine($"  Key: {keyStr.Substring(0, Math.Min(30, keyStr.Length))}...");
                            TestContext.WriteLine($"    CreateTs: {record.CreateTimestamp}");
                            TestContext.WriteLine($"    CommitTs: {record.CommitTimestamp}");
                            TestContext.WriteLine($"    TxId: {record.TransactionId}");
                            TestContext.WriteLine($"    IsCommitted: {record.IsCommitted}");
                            TestContext.WriteLine($"    IsVisibleAsOf(MaxValue): {record.IsVisibleAsOf(long.MaxValue)}");
                        }
                    }
                }
                
                // Check what MvccKeyValueStore.Scan returns
                TestContext.WriteLine("MvccKeyValueStore.Scan (through MVCC layer):");
                int mvccCount = 0;
                foreach (var (key, value) in mvccStore.MvccStore.Scan(null, null))
                {
                    var keyStr = System.Text.Encoding.UTF8.GetString(key);
                    if (keyStr.StartsWith("t:Users"))
                    {
                        mvccCount++;
                        TestContext.WriteLine($"  MvccStore found: {keyStr.Substring(0, Math.Min(20, keyStr.Length))}...");
                    }
                }
                TestContext.WriteLine($"  Total Users rows via MvccStore: {mvccCount}");
                
                // Check TimestampManager state
                TestContext.WriteLine($"TimestampManager.CurrentTimestamp: {mvccStore.TimestampManager.CurrentTimestamp}");
            }
            
            // Check WitDatabase.Scan
            TestContext.WriteLine("WitDatabase.Scan:");
            int dbScanCount = 0;
            foreach (var (key, value) in database.Scan())
            {
                var keyStr = System.Text.Encoding.UTF8.GetString(key);
                if (keyStr.StartsWith("t:Users"))
                {
                    dbScanCount++;
                    TestContext.WriteLine($"  WitDatabase found: {keyStr.Substring(0, Math.Min(20, keyStr.Length))}...");
                }
            }
            TestContext.WriteLine($"  Total Users rows via WitDatabase.Scan: {dbScanCount}");
            
            // Check SchemaCatalog
            var schema = engine.GetType().GetField("m_schema", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine);
            if (schema != null)
            {
                var schemaType = schema.GetType();
                var getTableMethod = schemaType.GetMethod("GetTable", new[] { typeof(string) });
                var tableDef = getTableMethod?.Invoke(schema, new object[] { "Users" });
                TestContext.WriteLine($"SchemaCatalog.GetTable('Users'): {tableDef?.GetType().Name ?? "null"}");
                
                var getRowCountMethod = schemaType.GetMethod("GetRowCount", new[] { typeof(string) });
                var rowCount = getRowCountMethod?.Invoke(schema, new object[] { "Users" });
                TestContext.WriteLine($"SchemaCatalog.GetRowCount('Users'): {rowCount}");
                
                // Check table names
                var tableNamesProperty = schemaType.GetProperty("TableNames");
                var tableNames = tableNamesProperty?.GetValue(schema) as IEnumerable<string>;
                TestContext.WriteLine($"SchemaCatalog.TableNames: {string.Join(", ", tableNames ?? Array.Empty<string>())}");
            }
            
            // Check raw store for $schema:_rowcount:Users key
            TestContext.WriteLine("Looking for $schema:_rowcount:Users in raw store:");
            if (store is OutWit.Database.Core.Transactions.MvccTransactionalStore mvccStore2)
            {
                var innerStore2 = mvccStore2.MvccStore.InnerStore;
                foreach (var (key, value) in innerStore2.Scan(null, null))
                {
                    var keyStr = System.Text.Encoding.UTF8.GetString(key);
                    if (keyStr.Contains("rowcount") || keyStr.Contains("_rowcount"))
                    {
                        TestContext.WriteLine($"  Found key: {keyStr.Substring(0, Math.Min(50, keyStr.Length))}... (len={key.Length})");
                        
                        // Try to parse as MVCC record
                        if (OutWit.Database.Core.Mvcc.MvccRecord.TryDeserialize(value, out var record))
                        {
                            var valueInt = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(record.Value);
                            TestContext.WriteLine($"    Value (as long): {valueInt}, TxId: {record.TransactionId}, Committed: {record.IsCommitted}");
                        }
                        else
                        {
                            // Maybe raw value?
                            if (value.Length == 8)
                            {
                                var valueInt = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(value);
                                TestContext.WriteLine($"    Raw value (as long): {valueInt}");
                            }
                        }
                    }
                }
            }
            
            // Try direct Get via MvccKeyValueStore
            TestContext.WriteLine("Direct MvccKeyValueStore.Get for $schema:_rowcount:Users:");
            if (store is OutWit.Database.Core.Transactions.MvccTransactionalStore mvccStore3)
            {
                var rowCountKey = System.Text.Encoding.UTF8.GetBytes("$schema:_rowcount:Users");
                var rowCountValue = mvccStore3.MvccStore.Get(rowCountKey);
                if (rowCountValue != null && rowCountValue.Length >= 8)
                {
                    var valueInt = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(rowCountValue);
                    TestContext.WriteLine($"  MvccKeyValueStore.Get returned: {valueInt}");
                }
                else
                {
                    TestContext.WriteLine($"  MvccKeyValueStore.Get returned: null or invalid");
                }
            }
            
            // Try direct table scan via Engine
            TestContext.WriteLine("Trying engine.CreateTableScan('Users'):");
            try
            {
                var createTableScanMethod = engine.GetType().GetMethod("CreateTableScan", new[] { typeof(string) });
                if (createTableScanMethod != null)
                {
                    var tableScan = createTableScanMethod.Invoke(engine, new object[] { "Users" });
                    TestContext.WriteLine($"  CreateTableScan returned: {tableScan?.GetType().Name ?? "null"}");
                }
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"  CreateTableScan error: {ex.Message}");
            }

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var count = cmd.ExecuteScalar();
            TestContext.WriteLine($"Count after reopen: {count}");
            
            Assert.That(count, Is.EqualTo(2L), "MVCC with explicit transaction should persist data");
        }
    }

    /// <summary>
    /// Detailed diagnostic test to understand MVCC persistence issue through ADO.NET SQL.
    /// </summary>
    [Test]
    public void DiagnosticMvccAdoNetDetailedTest()
    {
        var connectionString = $"Data Source={m_testDbPath};MVCC=true";

        // Create and populate database
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();
            var engine = connection.Engine!;
            var database = engine.GetType().GetField("m_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine) as WitDatabase;

            TestContext.WriteLine($"Database type: {database?.GetType().Name}");
            TestContext.WriteLine($"SupportsMvcc: {database?.SupportsMvcc}");
            TestContext.WriteLine($"SupportsTransactions: {database?.SupportsTransactions}");
            
            // Get underlying store
            var store = database?.Store;
            TestContext.WriteLine($"Store type: {store?.GetType().Name}");

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(100))";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (1, 'Alice')";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "INSERT INTO Users VALUES (2, 'Bob')";
            cmd.ExecuteNonQuery();

            // Verify data is there before close
            cmd.CommandText = "SELECT COUNT(*) FROM Users";
            var countBeforeClose = cmd.ExecuteScalar();
            TestContext.WriteLine($"Count before close: {countBeforeClose}");
            
            // Check what's in the store
            var allKeys = new List<string>();
            foreach (var (key, value) in database!.Scan())
            {
                allKeys.Add(System.Text.Encoding.UTF8.GetString(key));
            }
            TestContext.WriteLine($"Keys in store before close: {string.Join(", ", allKeys)}");

            // Flush explicitly
            TestContext.WriteLine("Flushing...");
            database.Flush();
        }

        TestContext.WriteLine("Connection closed. Reopening...");

        // Reopen and verify
        using (var connection = new WitDbConnection(connectionString))
        {
            connection.Open();
            var engine = connection.Engine!;
            var database = engine.GetType().GetField("m_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(engine) as WitDatabase;
            
            TestContext.WriteLine($"Reopened - Database type: {database?.GetType().Name}");
            TestContext.WriteLine($"Reopened - SupportsMvcc: {database?.SupportsMvcc}");
            
            // Check what's in the store after reopen
            var allKeys = new List<string>();
            foreach (var (key, value) in database!.Scan())
            {
                allKeys.Add(System.Text.Encoding.UTF8.GetString(key));
            }
            TestContext.WriteLine($"Keys in store after reopen: {string.Join(", ", allKeys)}");

            using var cmd = connection.CreateCommand();
            
            // Check if table exists
            try
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Users";
                var count = cmd.ExecuteScalar();
                TestContext.WriteLine($"Count after reopen: {count}");
                Assert.That(count, Is.EqualTo(2L), "MVCC should persist data");
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Error querying: {ex.Message}");
                throw;
            }
        }
    }

    #endregion
}
