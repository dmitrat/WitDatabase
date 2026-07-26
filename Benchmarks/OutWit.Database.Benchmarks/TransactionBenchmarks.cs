using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Account document for transaction benchmarks.
/// </summary>
public class AccountDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Balance { get; set; }
}

/// <summary>
/// Benchmarks for transaction performance.
/// Tests single transaction with multiple operations, concurrent reads, and mixed workloads.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class TransactionBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<AccountDoc>? m_liteCollection;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    #endregion

    #region Parameters

    [Params(100, 500)]
    public int OperationsPerTx { get; set; }

    // Default first: it is the configuration every ADO.NET and EF Core consumer actually gets, and
    // the four tuned modes below all disable MVCC, which is not the provider default.
    [Params(WitDbEngineMode.Default, WitDbEngineMode.BTree, WitDbEngineMode.Lsm, WitDbEngineMode.BTreeParallelAuto, WitDbEngineMode.LsmParallelAuto)]
    public WitDbEngineMode EngineMode { get; set; }

    #endregion

    #region Setup/Cleanup

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Use different path patterns for BTree (file .witdb) vs LSM (directory)
        var isLsm = EngineMode is WitDbEngineMode.Lsm or WitDbEngineMode.LsmParallelAuto;
        m_witPath = isLsm 
            ? BenchmarkPathHelper.GenerateUniquePath("wit_tx_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_tx_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_tx") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_tx") + ".db";
    }

    [IterationSetup]
    public void IterationSetup()
    {
        CleanupPaths();

        // WitDb
        var connStr = WitDbConnectionHelper.BuildConnectionString(m_witPath, EngineMode);
        m_witConn = new WitDbConnection(connStr);
        m_witConn.Open();

        using (var c = m_witConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS Accounts";
            c.ExecuteNonQuery();
            c.CommandText = @"
                CREATE TABLE Accounts (
                    Id BIGINT PRIMARY KEY AUTOINCREMENT,
                    Name VARCHAR(100),
                    Balance DOUBLE
                )";
            c.ExecuteNonQuery();
        }

        var tx = (WitDbTransaction)m_witConn.BeginTransaction();
        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

            for (int i = 0; i < 100; i++)
            {
                pn.Value = $"Account_{i}";
                pb.Value = 1000.0;
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();

        // SQLite
        m_sqliteConn = new SqliteConnection($"Data Source={m_sqlitePath}");
        m_sqliteConn.Open();

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.CommandText = "DROP TABLE IF EXISTS Accounts";
            c.ExecuteNonQuery();
            c.CommandText = @"
                CREATE TABLE Accounts (
                    Id INTEGER PRIMARY KEY,
                    Name TEXT,
                    Balance REAL
                )";
            c.ExecuteNonQuery();
        }

        var txS = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = txS;
            c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

            for (int i = 0; i < 100; i++)
            {
                pn.Value = $"Account_{i}";
                pb.Value = 1000.0;
                c.ExecuteNonQuery();
            }
        }
        txS.Commit();
        txS.Dispose();

        // LiteDB
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_liteCollection = m_liteDb.GetCollection<AccountDoc>("accounts");

        var docs = Enumerable.Range(0, 100)
            .Select(i => new AccountDoc { Id = i + 1, Name = $"Account_{i}", Balance = 1000.0 })
            .ToList();
        m_liteCollection.InsertBulk(docs);
    }

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_sqlitePath);
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        m_witConn?.Dispose(); m_witConn = null;
        m_sqliteConn?.Dispose(); m_sqliteConn = null;
        m_liteDb?.Dispose(); m_liteDb = null;
        m_liteCollection = null;

        CleanupPaths();
    }

    [GlobalCleanup]
    public void GlobalCleanup() => IterationCleanup();

    #endregion

    #region Benchmarks - Single Transaction with N Operations

    [Benchmark(Description = "Single Tx with N INSERTs - WitDb")]
    public void SingleTxInsertsWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

        for (int i = 0; i < OperationsPerTx; i++)
        {
            pn.Value = $"NewAccount_{i}";
            pb.Value = 500.0;
            c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "Single Tx with N INSERTs - SQLite", Baseline = true)]
    public void SingleTxInsertsSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

        for (int i = 0; i < OperationsPerTx; i++)
        {
            pn.Value = $"NewAccount_{i}";
            pb.Value = 500.0;
            c.ExecuteNonQuery();
        }
        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "Single Tx with N INSERTs - LiteDB")]
    public void SingleTxInsertsLiteDb()
    {
        m_liteDb!.BeginTrans();
        for (int i = 0; i < OperationsPerTx; i++)
        {
            m_liteCollection!.Insert(new AccountDoc
            {
                Name = $"NewAccount_{i}",
                Balance = 500.0
            });
        }
        m_liteDb.Commit();
    }

    #endregion

    #region Benchmarks - Transaction with Mixed Operations

    [Benchmark(Description = "Mixed Tx (INS/UPD/SEL) - WitDb")]
    public int MixedTxWitDb()
    {
        int readCount = 0;
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();

        using (var insertCmd = m_witConn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
            var pn = insertCmd.CreateParameter(); pn.ParameterName = "@n"; insertCmd.Parameters.Add(pn);
            var pb = insertCmd.CreateParameter(); pb.ParameterName = "@b"; insertCmd.Parameters.Add(pb);

            for (int i = 0; i < OperationsPerTx / 3; i++)
            {
                pn.Value = $"Mixed_{i}";
                pb.Value = 100.0;
                insertCmd.ExecuteNonQuery();
            }
        }

        using (var updateCmd = m_witConn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE Accounts SET Balance = Balance + 10 WHERE Id = @id";
            var pId = updateCmd.CreateParameter(); pId.ParameterName = "@id"; updateCmd.Parameters.Add(pId);

            for (int i = 1; i <= OperationsPerTx / 3; i++)
            {
                pId.Value = i;
                updateCmd.ExecuteNonQuery();
            }
        }

        using (var selectCmd = m_witConn.CreateCommand())
        {
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT * FROM Accounts WHERE Id = @id";
            var pId = selectCmd.CreateParameter(); pId.ParameterName = "@id"; selectCmd.Parameters.Add(pId);

            for (int i = 1; i <= OperationsPerTx / 3; i++)
            {
                pId.Value = i;
                using var r = selectCmd.ExecuteReader();
                if (r.Read()) readCount++;
            }
        }

        tx.Commit();
        tx.Dispose();
        return readCount;
    }

    [Benchmark(Description = "Mixed Tx (INS/UPD/SEL) - SQLite")]
    public int MixedTxSqlite()
    {
        int readCount = 0;
        var tx = m_sqliteConn!.BeginTransaction();

        using (var insertCmd = m_sqliteConn.CreateCommand())
        {
            insertCmd.Transaction = tx;
            insertCmd.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
            var pn = insertCmd.CreateParameter(); pn.ParameterName = "@n"; insertCmd.Parameters.Add(pn);
            var pb = insertCmd.CreateParameter(); pb.ParameterName = "@b"; insertCmd.Parameters.Add(pb);

            for (int i = 0; i < OperationsPerTx / 3; i++)
            {
                pn.Value = $"Mixed_{i}";
                pb.Value = 100.0;
                insertCmd.ExecuteNonQuery();
            }
        }

        using (var updateCmd = m_sqliteConn.CreateCommand())
        {
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE Accounts SET Balance = Balance + 10 WHERE Id = @id";
            var pId = updateCmd.CreateParameter(); pId.ParameterName = "@id"; updateCmd.Parameters.Add(pId);

            for (int i = 1; i <= OperationsPerTx / 3; i++)
            {
                pId.Value = i;
                updateCmd.ExecuteNonQuery();
            }
        }

        using (var selectCmd = m_sqliteConn.CreateCommand())
        {
            selectCmd.Transaction = tx;
            selectCmd.CommandText = "SELECT * FROM Accounts WHERE Id = @id";
            var pId = selectCmd.CreateParameter(); pId.ParameterName = "@id"; selectCmd.Parameters.Add(pId);

            for (int i = 1; i <= OperationsPerTx / 3; i++)
            {
                pId.Value = i;
                using var r = selectCmd.ExecuteReader();
                if (r.Read()) readCount++;
            }
        }

        tx.Commit();
        tx.Dispose();
        return readCount;
    }

    [Benchmark(Description = "Mixed Tx (INS/UPD/SEL) - LiteDB")]
    public int MixedTxLiteDb()
    {
        int readCount = 0;
        m_liteDb!.BeginTrans();

        for (int i = 0; i < OperationsPerTx / 3; i++)
        {
            m_liteCollection!.Insert(new AccountDoc
            {
                Name = $"Mixed_{i}",
                Balance = 100.0
            });
        }

        for (int i = 1; i <= OperationsPerTx / 3; i++)
        {
            var doc = m_liteCollection!.FindById(i);
            if (doc != null)
            {
                doc.Balance += 10;
                m_liteCollection.Update(doc);
            }
        }

        for (int i = 1; i <= OperationsPerTx / 3; i++)
        {
            var doc = m_liteCollection!.FindById(i);
            if (doc != null) readCount++;
        }

        m_liteDb.Commit();
        return readCount;
    }

    #endregion

    #region Benchmarks - Transaction Rollback

    [Benchmark(Description = "Tx Rollback (N ops) - WitDb")]
    public void TxRollbackWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using var c = m_witConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

        for (int i = 0; i < OperationsPerTx; i++)
        {
            pn.Value = $"Rollback_{i}";
            pb.Value = 999.0;
            c.ExecuteNonQuery();
        }
        tx.Rollback();
        tx.Dispose();
    }

    [Benchmark(Description = "Tx Rollback (N ops) - SQLite")]
    public void TxRollbackSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();
        using var c = m_sqliteConn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
        var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
        var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

        for (int i = 0; i < OperationsPerTx; i++)
        {
            pn.Value = $"Rollback_{i}";
            pb.Value = 999.0;
            c.ExecuteNonQuery();
        }
        tx.Rollback();
        tx.Dispose();
    }

    [Benchmark(Description = "Tx Rollback (N ops) - LiteDB")]
    public void TxRollbackLiteDb()
    {
        m_liteDb!.BeginTrans();
        for (int i = 0; i < OperationsPerTx; i++)
        {
            m_liteCollection!.Insert(new AccountDoc
            {
                Name = $"Rollback_{i}",
                Balance = 999.0
            });
        }
        m_liteDb.Rollback();
    }

    #endregion

    #region Benchmarks - Sequential Reads (no tx)

    [Benchmark(Description = "Sequential Reads x100 - WitDb")]
    public int SequentialReadsWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Accounts WHERE Id = @id";
        var p = c.CreateParameter(); p.ParameterName = "@id"; c.Parameters.Add(p);

        for (int i = 1; i <= 100; i++)
        {
            p.Value = i;
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Sequential Reads x100 - SQLite")]
    public int SequentialReadsSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = "SELECT * FROM Accounts WHERE Id = @id";
        var p = c.CreateParameter(); p.ParameterName = "@id"; c.Parameters.Add(p);

        for (int i = 1; i <= 100; i++)
        {
            p.Value = i;
            using var r = c.ExecuteReader();
            if (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Sequential Reads x100 - LiteDB")]
    public int SequentialReadsLiteDb()
    {
        int cnt = 0;
        for (int i = 1; i <= 100; i++)
        {
            var doc = m_liteCollection!.FindById(i);
            if (doc != null) cnt++;
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - Savepoint

    [Benchmark(Description = "Tx with Savepoint - WitDb")]
    public void TxSavepointWitDb()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();

        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES ('BeforeSP', 100)";
            c.ExecuteNonQuery();
        }

        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "SAVEPOINT sp1";
            c.ExecuteNonQuery();
        }

        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

            for (int i = 0; i < OperationsPerTx / 2; i++)
            {
                pn.Value = $"AfterSP_{i}";
                pb.Value = 200.0;
                c.ExecuteNonQuery();
            }
        }

        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "ROLLBACK TO SAVEPOINT sp1";
            c.ExecuteNonQuery();
        }

        tx.Commit();
        tx.Dispose();
    }

    [Benchmark(Description = "Tx with Savepoint - SQLite")]
    public void TxSavepointSqlite()
    {
        var tx = m_sqliteConn!.BeginTransaction();

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES ('BeforeSP', 100)";
            c.ExecuteNonQuery();
        }

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "SAVEPOINT sp1";
            c.ExecuteNonQuery();
        }

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Accounts (Name, Balance) VALUES (@n, @b)";
            var pn = c.CreateParameter(); pn.ParameterName = "@n"; c.Parameters.Add(pn);
            var pb = c.CreateParameter(); pb.ParameterName = "@b"; c.Parameters.Add(pb);

            for (int i = 0; i < OperationsPerTx / 2; i++)
            {
                pn.Value = $"AfterSP_{i}";
                pb.Value = 200.0;
                c.ExecuteNonQuery();
            }
        }

        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "ROLLBACK TO SAVEPOINT sp1";
            c.ExecuteNonQuery();
        }

        tx.Commit();
        tx.Dispose();
    }

    // Note: LiteDB doesn't support savepoints - transaction is atomic

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
