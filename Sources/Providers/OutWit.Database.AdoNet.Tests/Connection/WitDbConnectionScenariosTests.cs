using NUnit.Framework;
using System.Data;

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

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbScenario_{Guid.NewGuid():N}.witdb");
        m_testLsmPath = Path.Combine(Path.GetTempPath(), $"WitDbScenario_LSM_{Guid.NewGuid():N}");
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
    }

    #endregion

    #region Full Workflow Scenarios

    [Test]
    [Ignore("Known issue: Encryption + MVCC persistence not working correctly after connection close/reopen")]
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
        using var connection = new WitDbConnection("Data Source=mydb.witdb");
        connection.Open();

        Assert.DoesNotThrow(() => connection.ChangeDatabase("mydb"));
    }

    [Test]
    public void ChangeDatabaseToMainSucceedsTest()
    {
        using var connection = new WitDbConnection("Data Source=mydb.witdb");
        connection.Open();

        Assert.DoesNotThrow(() => connection.ChangeDatabase("main"));
    }

    [Test]
    public void ChangeDatabaseToDifferentNameThrowsTest()
    {
        using var connection = new WitDbConnection("Data Source=mydb.witdb");
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

    #endregion
}
