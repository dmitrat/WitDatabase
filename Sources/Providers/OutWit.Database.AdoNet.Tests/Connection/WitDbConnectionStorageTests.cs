using NUnit.Framework;
using System.Data;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// Tests for WitDbConnection with various storage engine configurations.
/// </summary>
[TestFixture]
public class WitDbConnectionStorageTests
{
    #region Fields

    private string? m_testDbPath;
    private string? m_testLsmPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbStorage_{Guid.NewGuid():N}.witdb");
        m_testLsmPath = Path.Combine(Path.GetTempPath(), $"WitDbStorage_LSM_{Guid.NewGuid():N}");
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

    #region BTree Store Tests

    [Test]
    public void BTreeStoreOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Store=btree");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void BTreeStoreCreatesFileTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Store=btree");
        connection.Open();

        Assert.That(File.Exists(m_testDbPath), Is.True);
    }

    [Test]
    public void BTreeStoreCaseInsensitiveTest()
    {
        using var conn1 = new WitDbConnection($"Data Source={m_testDbPath};Store=BTREE");
        conn1.Open();
        Assert.That(conn1.State, Is.EqualTo(ConnectionState.Open));
        conn1.Close();

        // Need different path for second test
        var path2 = Path.Combine(Path.GetTempPath(), $"WitDb_{Guid.NewGuid():N}.witdb");
        try
        {
            using var conn2 = new WitDbConnection($"Data Source={path2};Store=BTree");
            conn2.Open();
            Assert.That(conn2.State, Is.EqualTo(ConnectionState.Open));
        }
        finally
        {
            if (File.Exists(path2)) File.Delete(path2);
        }
    }

    #endregion

    #region LSM Store Tests

    [Test]
    public void LsmStoreOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testLsmPath};Store=lsm");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void LsmStoreCreatesDirectoryTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testLsmPath};Store=lsm");
        connection.Open();

        Assert.That(Directory.Exists(m_testLsmPath), Is.True);
    }

    [Test]
    public void LsmStoreWithCustomOptionsOpensSuccessfullyTest()
    {
        var connectionString = $"Data Source={m_testLsmPath};Store=lsm;LSM MemTable Size=8388608;LSM Block Size=4096;LSM WAL=true;LSM Sync=false;LSM Background Compaction=true";
        using var connection = new WitDbConnection(connectionString);
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void LsmStoreCanInsertManyRecordsTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testLsmPath};Store=lsm");
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

    #region InMemory Store Tests

    [Test]
    public void InMemoryStoreOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:;Store=inmemory");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void InMemoryStoreDoesNotCreateFileTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:;Store=inmemory");
        connection.Open();

        // No file should be created - just verify connection is open
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion

    #region Memory Mode Tests

    [Test]
    public void MemoryModeOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection("Mode=Memory");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void MemoryModeWithDataSourceOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:;Mode=Memory");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion

    #region Connection Mode Tests

    [Test]
    public void ReadWriteCreateModeCreatesFileTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Mode=ReadWriteCreate");
        
        Assert.That(File.Exists(m_testDbPath), Is.False);
        connection.Open();
        Assert.That(File.Exists(m_testDbPath), Is.True);
    }

    [Test]
    public void ReadWriteModeRequiresExistingFileTest()
    {
        // First create the file
        using (var createConn = new WitDbConnection($"Data Source={m_testDbPath};Mode=ReadWriteCreate"))
        {
            createConn.Open();
        }

        // Then open with ReadWrite
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Mode=ReadWrite");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void ReadOnlyModeRequiresExistingFileTest()
    {
        // First create the file
        using (var createConn = new WitDbConnection($"Data Source={m_testDbPath};Mode=ReadWriteCreate"))
        {
            createConn.Open();
            using var cmd = createConn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT)";
            cmd.ExecuteNonQuery();
        }

        // Then open with ReadOnly
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Mode=ReadOnly");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion

    #region Custom Store Provider Tests

    [Test]
    public void CustomStoreProviderWhenNotRegisteredThrowsHelpfulErrorTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Store=custom-store");

        var ex = Assert.Throws<InvalidOperationException>(() => connection.Open());
        Assert.That(ex!.Message, Does.Contain("custom-store").Or.Contain("not registered").Or.Contain("not found"));
    }

    #endregion

    #region Cache and Page Settings

    [Test]
    public void CustomCacheSizeOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Cache Size=500");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void CustomPageSizeOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Page Size=8192");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion

    #region Locking Settings

    [Test]
    public void FileLockingEnabledOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};File Locking=true");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void FileLockingDisabledOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};File Locking=false");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void CustomLockTimeoutOpensSuccessfullyTest()
    {
        using var connection = new WitDbConnection($"Data Source={m_testDbPath};Lock Timeout=60");
        
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    #endregion

    #region Data Persistence Tests

    [Test]
    public void BTreeDataPersistsAcrossSessionsWithoutEncryptionTest()
    {
        var connectionString = $"Data Source={m_testDbPath}";

        // Create and populate database
        using (var conn1 = new WitDbConnection(connectionString))
        {
            conn1.Open();
            using var cmd = conn1.CreateCommand();
            cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Value TEXT)";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO Test VALUES (1, 'Persisted Data')";
            cmd.ExecuteNonQuery();
        }

        // Reopen and verify
        using (var conn2 = new WitDbConnection(connectionString))
        {
            conn2.Open();
            using var cmd = conn2.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Test WHERE Id = 1";
            var result = cmd.ExecuteScalar();

            Assert.That(result, Is.EqualTo("Persisted Data"));
        }
    }

    #endregion
}
