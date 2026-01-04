using OutWit.Database.AdoNet;
using OutWit.Database.Core.Builder;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.AdoNet.Tests.Parallel;

/// <summary>
/// Integration tests for parallel access via ADO.NET connections.
/// </summary>
[TestFixture]
public class WitDbConnectionParallelAccessTests : IDisposable
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"adonet_parallel_test_{Guid.NewGuid():N}");
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

    #region Connection String Parallel Mode Tests

    [Test]
    public void ConnectionStringWithParallelModeAutoTest()
    {
        var dbPath = Path.Combine(m_testDir, "parallel_auto.witdb");
        var cs = $"Data Source={dbPath};Parallel Mode=Auto;Transactions=false";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        // Create table and insert data
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Test (Id INTEGER PRIMARY KEY, Name TEXT)";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test1')";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Name FROM Test WHERE Id = 1";
            var result = cmd.ExecuteScalar();
            Assert.That(result, Is.EqualTo("Test1"));
        }
    }

    [Test]
    [Ignore("Parallel Mode=Buffered causes SQL parsing issues - requires investigation")]
    public void ConnectionStringWithMaxWritersTest()
    {
        var dbPath = Path.Combine(m_testDir, "max_writers.witdb");
        var cs = $"Data Source={dbPath};Parallel Mode=Buffered;Max Writers=4;Transactions=false";

        using var conn = new WitDbConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE Data (Key TEXT PRIMARY KEY, Value TEXT)";
            cmd.ExecuteNonQuery();
        }

        // Insert multiple records
        for (int i = 0; i < 10; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO Data (Key, Value) VALUES ('key{i}', 'value{i}')";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Data";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(10));
        }
    }

    #endregion

    #region Multi-Connection Parallel Tests

    [Test]
    [Ignore("Multiple file connections not supported for embedded database. Use single connection with parallel commands instead.")]
    public void MultipleConnectionsReadTest()
    {
        var dbPath = Path.Combine(m_testDir, "multi_read.witdb");
        var cs = $"Data Source={dbPath};Parallel Mode=Auto;Transactions=false";

        // Setup: Create table and insert data
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Data TEXT)";
            cmd.ExecuteNonQuery();

            for (int i = 0; i < 100; i++)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = $"INSERT INTO Items (Id, Data) VALUES ({i}, 'Data{i}')";
                insertCmd.ExecuteNonQuery();
            }
        }

        // Parallel reads from multiple connections
        const int readers = 4;
        const int readsPerReader = 25;
        var errors = new List<Exception>();
        var readCounts = new int[readers];

        var tasks = Enumerable.Range(0, readers).Select(readerId => Task.Run(() =>
        {
            try
            {
                using var conn = new WitDbConnection(cs);
                conn.Open();

                for (int i = 0; i < readsPerReader; i++)
                {
                    var id = (readerId * readsPerReader + i) % 100;
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT Data FROM Items WHERE Id = {id}";
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        Interlocked.Increment(ref readCounts[readerId]);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(errors, Is.Empty, $"Errors: {string.Join(", ", errors.Select(e => e.Message))}");
        Assert.That(readCounts.Sum(), Is.EqualTo(readers * readsPerReader));
    }

    [Test]
    [Ignore("Multiple file connections not supported for embedded database. Use single connection with parallel commands instead.")]
    public void MultipleConnectionsWriteTest()
    {
        var dbPath = Path.Combine(m_testDir, "multi_write.witdb");
        var cs = $"Data Source={dbPath};Parallel Mode=Latched;Transactions=false";

        // Setup
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Log (Id INTEGER PRIMARY KEY, ThreadId INTEGER, Seq INTEGER)";
            cmd.ExecuteNonQuery();
        }

        // Parallel writes from multiple connections
        const int writers = 4;
        const int writesPerWriter = 25;
        var errors = new List<Exception>();
        var nextId = 0;

        var tasks = Enumerable.Range(0, writers).Select(writerId => Task.Run(() =>
        {
            try
            {
                using var conn = new WitDbConnection(cs);
                conn.Open();

                for (int i = 0; i < writesPerWriter; i++)
                {
                    var id = Interlocked.Increment(ref nextId);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"INSERT INTO Log (Id, ThreadId, Seq) VALUES ({id}, {writerId}, {i})";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                lock (errors)
                {
                    errors.Add(ex);
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.That(errors, Is.Empty, $"Errors: {string.Join(", ", errors.Select(e => e.Message))}");

        // Verify all writes
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Log";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            Assert.That(count, Is.EqualTo(writers * writesPerWriter));
        }
    }

    #endregion

    #region Mixed Read/Write Tests

    [Test]
    [Ignore("Multiple file connections not supported for embedded database. Use single connection with parallel commands instead.")]
    public void ConcurrentReadWriteTest()
    {
        var dbPath = Path.Combine(m_testDir, "concurrent_rw.witdb");
        var cs = $"Data Source={dbPath};Parallel Mode=Auto;Transactions=false";

        // Setup
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Counter (Id INTEGER PRIMARY KEY, Value INTEGER)";
            cmd.ExecuteNonQuery();

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO Counter (Id, Value) VALUES (1, 0)";
            insertCmd.ExecuteNonQuery();
        }

        const int readers = 2;
        const int writers = 2;
        const int operations = 50;
        var errors = new List<Exception>();
        var readSuccess = 0;
        var writeSuccess = 0;

        var readerTasks = Enumerable.Range(0, readers).Select(_ => Task.Run(() =>
        {
            try
            {
                using var conn = new WitDbConnection(cs);
                conn.Open();

                for (int i = 0; i < operations; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM Counter WHERE Id = 1";
                    var result = cmd.ExecuteScalar();
                    if (result != null)
                    {
                        Interlocked.Increment(ref readSuccess);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        var writerTasks = Enumerable.Range(0, writers).Select(writerId => Task.Run(() =>
        {
            try
            {
                using var conn = new WitDbConnection(cs);
                conn.Open();

                for (int i = 0; i < operations; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"UPDATE Counter SET Value = Value + 1 WHERE Id = 1";
                    var affected = cmd.ExecuteNonQuery();
                    if (affected > 0)
                    {
                        Interlocked.Increment(ref writeSuccess);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(readerTasks.Concat(writerTasks).ToArray());

        Assert.That(errors, Is.Empty, $"Errors: {string.Join(", ", errors.Select(e => e.Message))}");
        Assert.That(readSuccess, Is.EqualTo(readers * operations));
        
        TestContext.WriteLine($"Read success: {readSuccess}");
        TestContext.WriteLine($"Write success: {writeSuccess}");
    }

    #endregion

    #region Stress Tests

    [Test]
    [Category("Stress")]
    [Ignore("Multiple file connections not supported for embedded database. Use single connection with parallel commands instead.")]
    public void HighConcurrencyStressTest()
    {
        var dbPath = Path.Combine(m_testDir, "stress.witdb");
        var cs = $"Data Source={dbPath};Parallel Mode=Auto;Transactions=false";

        // Setup
        using (var conn = new WitDbConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE Stress (Id INTEGER PRIMARY KEY, Data TEXT)";
            cmd.ExecuteNonQuery();
        }

        const int threads = 8;
        const int operationsPerThread = 100;
        var errors = new List<Exception>();
        var successCount = 0;
        var nextId = 0;

        var tasks = Enumerable.Range(0, threads).Select(threadId => Task.Run(() =>
        {
            try
            {
                using var conn = new WitDbConnection(cs);
                conn.Open();

                for (int i = 0; i < operationsPerThread; i++)
                {
                    // Mix of operations
                    if (i % 3 == 0)
                    {
                        // Insert
                        var id = Interlocked.Increment(ref nextId);
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"INSERT INTO Stress (Id, Data) VALUES ({id}, 'Thread{threadId}_Op{i}')";
                        cmd.ExecuteNonQuery();
                        Interlocked.Increment(ref successCount);
                    }
                    else if (i % 3 == 1)
                    {
                        // Select
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*) FROM Stress";
                        cmd.ExecuteScalar();
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        // Update (may affect 0 rows if id doesn't exist)
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"UPDATE Stress SET Data = 'Updated' WHERE Id = {i % 50}";
                        cmd.ExecuteNonQuery();
                        Interlocked.Increment(ref successCount);
                    }
                }
            }
            catch (Exception ex)
            {
                lock (errors) { errors.Add(ex); }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        TestContext.WriteLine($"Success count: {successCount}");
        TestContext.WriteLine($"Error count: {errors.Count}");

        Assert.That(errors, Is.Empty, $"Errors: {string.Join("\n", errors.Select(e => e.ToString()))}");
        Assert.That(successCount, Is.EqualTo(threads * operationsPerThread));
    }

    #endregion
}
