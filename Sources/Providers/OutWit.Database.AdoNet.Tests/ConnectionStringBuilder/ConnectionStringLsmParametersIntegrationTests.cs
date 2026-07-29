using NUnit.Framework;
using OutWit.Database.AdoNet;

namespace OutWit.Database.AdoNet.Tests.ConnectionStringBuilder;

/// <summary>
/// Integration tests to verify LSM parameters from connection string are properly applied.
/// </summary>
[TestFixture]
public class ConnectionStringLsmParametersIntegrationTests : IDisposable
{
    private string m_testDir = null!;

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDB_LsmParams_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        Dispose();
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            if (Directory.Exists(m_testDir))
                Directory.Delete(m_testDir, recursive: true);
        }
        catch { }
    }

    #region SyncWrites Parameter Tests

    [Test]
    public void SyncWritesFalseInConnectionStringWorksTest()
    {
        var lsmPath = Path.Combine(m_testDir, "syncwrites_false");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        // Create table and insert data
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY, Val INT)";
            cmd.ExecuteNonQuery();
        }

        // Insert multiple rows - this should be fast with SyncWrites=false
        using var tx = (WitDbTransaction)conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO T (Id, Val) VALUES (@id, @val)";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "@id";
            cmd.Parameters.Add(pId);
            var pVal = cmd.CreateParameter();
            pVal.ParameterName = "@val";
            cmd.Parameters.Add(pVal);

            for (int i = 0; i < 100; i++)
            {
                pId.Value = i;
                pVal.Value = i * 10;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();

        // Verify data was inserted
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM T";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(100));
        }
    }

    [Test]
    public void SyncWritesTrueInConnectionStringWorksTest()
    {
        var lsmPath = Path.Combine(m_testDir, "syncwrites_true");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;SyncWrites=true";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        // Create table and insert a few rows (not many because SyncWrites=true is slow)
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY, Val INT)";
            cmd.ExecuteNonQuery();
        }

        using var tx = (WitDbTransaction)conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO T (Id, Val) VALUES (@id, @val)";
            var pId = cmd.CreateParameter();
            pId.ParameterName = "@id";
            cmd.Parameters.Add(pId);
            var pVal = cmd.CreateParameter();
            pVal.ParameterName = "@val";
            cmd.Parameters.Add(pVal);

            for (int i = 0; i < 10; i++)
            {
                pId.Value = i;
                pVal.Value = i * 10;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();

        // Verify data was inserted
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM T";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(10));
        }
    }

    [Test]
    public void SyncWritesDefaultIsFalseTest()
    {
        // Without explicit SyncWrites, default should be false (fast)
        var lsmPath = Path.Combine(m_testDir, "syncwrites_default");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY AUTOINCREMENT, Val INT)";
            cmd.ExecuteNonQuery();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var tx = (WitDbTransaction)conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO T (Val) VALUES (@val)";
            var pVal = cmd.CreateParameter();
            pVal.ParameterName = "@val";
            cmd.Parameters.Add(pVal);

            for (int i = 0; i < 1000; i++)
            {
                pVal.Value = i;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
        sw.Stop();

        // The elapsed time is REPORTED, not asserted.
        //
        // This assertion used to read `Is.LessThan(5)` seconds, on the reasoning that SyncWrites=true
        // would take ~10s and so a 5s ceiling proves the default is false. It measures the machine,
        // not the default: on a loaded CI runner the same 1000 inserts took 5.9s and 7.5s and failed
        // the build, having passed on the five preceding runs with nothing relevant changed. There
        // was never any margin - the threshold sat between the two behaviours it was meant to
        // separate.
        //
        // The claim in the test's name is a CONFIGURATION fact, so it is asserted as one below.
        TestContext.Out.WriteLine($"1000 inserts took {sw.Elapsed.TotalSeconds:F2}s (diagnostic only)");

        Assert.Multiple(() =>
        {
            // What "the default is false" actually means: the connection string above sets no
            // SyncWrites parameter, and the parameter is only applied when present, so the value
            // falls through to LsmOptions' own default.
            Assert.That(new Core.LSM.LsmOptions().SyncWrites, Is.False,
                "SyncWrites must default to false, which is what makes the unqualified connection " +
                "string above fast");

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM T";
            Assert.That(Convert.ToInt64(cmd.ExecuteScalar()), Is.EqualTo(1000),
                "and the writes must actually have landed");
        });
    }

    #endregion

    #region Other LSM Parameters Tests

    [Test]
    public void EnableWalParameterWorksTest()
    {
        var lsmPath = Path.Combine(m_testDir, "enable_wal");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;EnableWal=true";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY, Val INT)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO T (Id, Val) VALUES (1, 100)";
            cmd.ExecuteNonQuery();
        }

        // Verify WAL file was created
        var walFiles = Directory.GetFiles(lsmPath, "*.log");
        Assert.That(walFiles.Length, Is.GreaterThan(0), "WAL file should be created when EnableWal=true");
    }

    [Test]
    public void DisableWalParameterWorksTest()
    {
        var lsmPath = Path.Combine(m_testDir, "disable_wal");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;EnableWal=false";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY, Val INT)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO T (Id, Val) VALUES (1, 100)";
            cmd.ExecuteNonQuery();
        }

        // Note: WAL file might not be created when EnableWal=false
        // But we should still be able to read/write
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Val FROM T WHERE Id = 1";
            var val = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.That(val, Is.EqualTo(100));
        }
    }

    [Test]
    public void MemTableSizeParameterWorksTest()
    {
        var lsmPath = Path.Combine(m_testDir, "memtable_size");
        // Set small MemTableSize to trigger SSTable creation
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;MemTableSize=1024";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY AUTOINCREMENT, Data VARCHAR(1000))";
            cmd.ExecuteNonQuery();
        }

        // Insert data to exceed MemTableSize
        using var tx = (WitDbTransaction)conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO T (Data) VALUES (@data)";
            var pData = cmd.CreateParameter();
            pData.ParameterName = "@data";
            cmd.Parameters.Add(pData);

            for (int i = 0; i < 100; i++)
            {
                pData.Value = new string('X', 100);
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();

        // SSTable files should be created
        var sstFiles = Directory.GetFiles(lsmPath, "sst_*.sst");
        Assert.That(sstFiles.Length, Is.GreaterThan(0), "SSTable files should be created when MemTableSize is exceeded");
    }

    #endregion

    #region Case Sensitivity Tests

    [Test]
    [TestCase("SyncWrites=false")]
    [TestCase("syncwrites=false")]
    [TestCase("SYNCWRITES=false")]
    [TestCase("syncWrites=false")]
    public void SyncWritesIsCaseInsensitiveTest(string syncWritesPart)
    {
        var lsmPath = Path.Combine(m_testDir, $"case_{syncWritesPart.GetHashCode():X}");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;{syncWritesPart}";

        using var conn = new WitDbConnection(connectionString);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY, Val INT)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO T (Id, Val) VALUES (1, 100)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Val FROM T WHERE Id = 1";
            var val = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.That(val, Is.EqualTo(100));
        }
    }

    #endregion

    #region Boolean Format Tests

    [Test]
    [TestCase("SyncWrites=false")]
    [TestCase("SyncWrites=False")]
    [TestCase("SyncWrites=FALSE")]
    [TestCase("SyncWrites=0")]
    [TestCase("SyncWrites=no")]
    [TestCase("SyncWrites=off")]
    [TestCase("SyncWrites=true")]
    [TestCase("SyncWrites=True")]
    [TestCase("SyncWrites=TRUE")]
    [TestCase("SyncWrites=1")]
    [TestCase("SyncWrites=yes")]
    [TestCase("SyncWrites=on")]
    public void SyncWritesBooleanFormatsWorkTest(string syncWritesPart)
    {
        var lsmPath = Path.Combine(m_testDir, $"bool_{syncWritesPart.GetHashCode():X}");
        var connectionString = $"Data Source={lsmPath};Store=lsm;Transactions=true;MVCC=false;{syncWritesPart}";

        using var conn = new WitDbConnection(connectionString);
        
        // Should not throw
        Assert.DoesNotThrow(() => conn.Open());

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY)";
            cmd.ExecuteNonQuery();
        }

        // Just verify we can insert - actual SyncWrites behavior is tested elsewhere
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO T (Id) VALUES (1)";
            cmd.ExecuteNonQuery();
        }
    }

    #endregion
}
