using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Comparison.Benchmarks;

/// <summary>
/// Benchmarks simulating real-world mixed workloads (OLTP-like scenarios).
/// Note: LiteDB is excluded as it doesn't support SQL queries.
/// </summary>
[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MixedWorkloadBenchmarks : IDisposable
{
    public enum SqlDatabaseType { WitDb, SQLite }
    
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;
    private Random _rnd = null!;

    [Params(1000)]
    public int InitialRows { get; set; }

    [Params(100)]
    public int OperationsPerIteration { get; set; }

    [Params(SqlDatabaseType.WitDb, SqlDatabaseType.SQLite)]
    public SqlDatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_mixed_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_mixed_{Guid.NewGuid():N}.db");
        _rnd = new Random(42);

        SetupWitDb();
        SetupSqlite();
    }

    private void SetupWitDb()
    {
        // Use LSM-Tree storage via connection string
        _witConn = new WitDbConnection($"Data Source={_witPath};Store=lsm;Transactions=true");
        _witConn.Open();

        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = @"CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Email VARCHAR(200),
                Balance DOUBLE,
                CreatedAt DATETIME
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId INT,
                Amount DOUBLE,
                Status VARCHAR(20),
                CreatedAt DATETIME
            )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Orders_UserId ON Orders(UserId)";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Orders_Status ON Orders(Status)";
            c.ExecuteNonQuery();
        }

        // Seed data
        var tx = (WitDbTransaction)_witConn.BeginTransaction();
        using (var c = _witConn.CreateCommand())
        {
            c.Transaction = tx;

            // Insert users
            c.CommandText = "INSERT INTO Users (Name, Email, Balance, CreatedAt) VALUES (@name, @email, @balance, @created)";
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pEmail = c.CreateParameter(); pEmail.ParameterName = "@email"; c.Parameters.Add(pEmail);
            var pBalance = c.CreateParameter(); pBalance.ParameterName = "@balance"; c.Parameters.Add(pBalance);
            var pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; c.Parameters.Add(pCreated);

            int userCount = InitialRows / 10;
            for (int i = 1; i <= userCount; i++)
            {
                pName.Value = $"User_{i}";
                pEmail.Value = $"user{i}@test.com";
                pBalance.Value = _rnd.NextDouble() * 10000;
                pCreated.Value = DateTime.UtcNow.AddDays(-_rnd.Next(365));
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert orders
            c.CommandText = "INSERT INTO Orders (UserId, Amount, Status, CreatedAt) VALUES (@userId, @amount, @status, @created)";
            var pUserId = c.CreateParameter(); pUserId.ParameterName = "@userId"; c.Parameters.Add(pUserId);
            var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; c.Parameters.Add(pAmount);
            var pStatus = c.CreateParameter(); pStatus.ParameterName = "@status"; c.Parameters.Add(pStatus);
            pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; c.Parameters.Add(pCreated);

            string[] statuses = { "pending", "completed", "cancelled", "refunded" };
            for (int i = 1; i <= InitialRows; i++)
            {
                pUserId.Value = _rnd.Next(1, userCount + 1);
                pAmount.Value = Math.Round(_rnd.NextDouble() * 500, 2);
                pStatus.Value = statuses[_rnd.Next(statuses.Length)];
                pCreated.Value = DateTime.UtcNow.AddDays(-_rnd.Next(365));
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private void SetupSqlite()
    {
        _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
        _sqliteConn.Open();

        using (var c = _sqliteConn.CreateCommand())
        {
            c.CommandText = @"CREATE TABLE Users (
                Id INTEGER PRIMARY KEY,
                Name TEXT,
                Email TEXT,
                Balance REAL,
                CreatedAt TEXT
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER,
                Amount REAL,
                Status TEXT,
                CreatedAt TEXT
            )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Orders_UserId ON Orders(UserId)";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Orders_Status ON Orders(Status)";
            c.ExecuteNonQuery();
        }

        var tx = _sqliteConn.BeginTransaction();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.Transaction = tx;

            // Insert users
            c.CommandText = "INSERT INTO Users (Name, Email, Balance, CreatedAt) VALUES (@name, @email, @balance, @created)";
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pEmail = c.CreateParameter(); pEmail.ParameterName = "@email"; c.Parameters.Add(pEmail);
            var pBalance = c.CreateParameter(); pBalance.ParameterName = "@balance"; c.Parameters.Add(pBalance);
            var pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; c.Parameters.Add(pCreated);

            int userCount = InitialRows / 10;
            for (int i = 1; i <= userCount; i++)
            {
                pName.Value = $"User_{i}";
                pEmail.Value = $"user{i}@test.com";
                pBalance.Value = _rnd.NextDouble() * 10000;
                pCreated.Value = DateTime.UtcNow.AddDays(-_rnd.Next(365)).ToString("O");
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert orders
            c.CommandText = "INSERT INTO Orders (UserId, Amount, Status, CreatedAt) VALUES (@userId, @amount, @status, @created)";
            var pUserId = c.CreateParameter(); pUserId.ParameterName = "@userId"; c.Parameters.Add(pUserId);
            var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; c.Parameters.Add(pAmount);
            var pStatus = c.CreateParameter(); pStatus.ParameterName = "@status"; c.Parameters.Add(pStatus);
            pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; c.Parameters.Add(pCreated);

            string[] statuses = { "pending", "completed", "cancelled", "refunded" };
            for (int i = 1; i <= InitialRows; i++)
            {
                pUserId.Value = _rnd.Next(1, userCount + 1);
                pAmount.Value = Math.Round(_rnd.NextDouble() * 500, 2);
                pStatus.Value = statuses[_rnd.Next(statuses.Length)];
                pCreated.Value = DateTime.UtcNow.AddDays(-_rnd.Next(365)).ToString("O");
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _witConn?.Dispose();
        _sqliteConn?.Dispose();
        try { Directory.Delete(_witPath, true); } catch { }
        try { File.Delete(_sqlitePath); } catch { }
    }

    /// <summary>
    /// 80% reads, 20% writes - typical web application workload.
    /// </summary>
    [Benchmark(Description = "OLTP 80/20 Read/Write")]
    public int OltpReadHeavy()
    {
        int cnt = 0;
        int userCount = InitialRows / 10;

        if (Database == SqlDatabaseType.WitDb)
        {
            for (int i = 0; i < OperationsPerIteration; i++)
            {
                if (_rnd.Next(100) < 80)
                {
                    // Read: get user
                    using var c = _witConn.CreateCommand();
                    c.CommandText = "SELECT * FROM Users WHERE Id = @id";
                    var p = c.CreateParameter(); p.ParameterName = "@id"; p.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(p);
                    using var r = c.ExecuteReader();
                    if (r.Read()) cnt++;
                }
                else
                {
                    // Write: insert order
                    using var c = _witConn.CreateCommand();
                    c.CommandText = "INSERT INTO Orders (UserId, Amount, Status, CreatedAt) VALUES (@userId, @amount, @status, @created)";
                    var pUserId = c.CreateParameter(); pUserId.ParameterName = "@userId"; pUserId.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(pUserId);
                    var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; pAmount.Value = Math.Round(_rnd.NextDouble() * 500, 2); c.Parameters.Add(pAmount);
                    var pStatus = c.CreateParameter(); pStatus.ParameterName = "@status"; pStatus.Value = "pending"; c.Parameters.Add(pStatus);
                    var pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; pCreated.Value = DateTime.UtcNow; c.Parameters.Add(pCreated);
                    c.ExecuteNonQuery();
                    cnt++;
                }
            }
        }
        else
        {
            for (int i = 0; i < OperationsPerIteration; i++)
            {
                if (_rnd.Next(100) < 80)
                {
                    using var c = _sqliteConn.CreateCommand();
                    c.CommandText = "SELECT * FROM Users WHERE Id = @id";
                    var p = c.CreateParameter(); p.ParameterName = "@id"; p.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(p);
                    using var r = c.ExecuteReader();
                    if (r.Read()) cnt++;
                }
                else
                {
                    using var c = _sqliteConn.CreateCommand();
                    c.CommandText = "INSERT INTO Orders (UserId, Amount, Status, CreatedAt) VALUES (@userId, @amount, @status, @created)";
                    var pUserId = c.CreateParameter(); pUserId.ParameterName = "@userId"; pUserId.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(pUserId);
                    var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; pAmount.Value = Math.Round(_rnd.NextDouble() * 500, 2); c.Parameters.Add(pAmount);
                    var pStatus = c.CreateParameter(); pStatus.ParameterName = "@status"; pStatus.Value = "pending"; c.Parameters.Add(pStatus);
                    var pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; pCreated.Value = DateTime.UtcNow.ToString("O"); c.Parameters.Add(pCreated);
                    c.ExecuteNonQuery();
                    cnt++;
                }
            }
        }
        return cnt;
    }

    /// <summary>
    /// 50% reads, 50% writes - balanced workload.
    /// </summary>
    [Benchmark(Description = "OLTP 50/50 Read/Write")]
    public int OltpBalanced()
    {
        int cnt = 0;
        int userCount = InitialRows / 10;

        if (Database == SqlDatabaseType.WitDb)
        {
            for (int i = 0; i < OperationsPerIteration; i++)
            {
                if (i % 2 == 0)
                {
                    using var c = _witConn.CreateCommand();
                    c.CommandText = "SELECT COUNT(*) FROM Orders WHERE UserId = @userId";
                    var p = c.CreateParameter(); p.ParameterName = "@userId"; p.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(p);
                    cnt += Convert.ToInt32(c.ExecuteScalar());
                }
                else
                {
                    using var c = _witConn.CreateCommand();
                    c.CommandText = "UPDATE Users SET Balance = Balance + @amount WHERE Id = @id";
                    var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; pAmount.Value = _rnd.NextDouble() * 100; c.Parameters.Add(pAmount);
                    var pId = c.CreateParameter(); pId.ParameterName = "@id"; pId.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(pId);
                    cnt += c.ExecuteNonQuery();
                }
            }
        }
        else
        {
            for (int i = 0; i < OperationsPerIteration; i++)
            {
                if (i % 2 == 0)
                {
                    using var c = _sqliteConn.CreateCommand();
                    c.CommandText = "SELECT COUNT(*) FROM Orders WHERE UserId = @userId";
                    var p = c.CreateParameter(); p.ParameterName = "@userId"; p.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(p);
                    cnt += Convert.ToInt32(c.ExecuteScalar());
                }
                else
                {
                    using var c = _sqliteConn.CreateCommand();
                    c.CommandText = "UPDATE Users SET Balance = Balance + @amount WHERE Id = @id";
                    var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; pAmount.Value = _rnd.NextDouble() * 100; c.Parameters.Add(pAmount);
                    var pId = c.CreateParameter(); pId.ParameterName = "@id"; pId.Value = _rnd.Next(1, userCount + 1); c.Parameters.Add(pId);
                    cnt += c.ExecuteNonQuery();
                }
            }
        }
        return cnt;
    }

    /// <summary>
    /// Report generation - complex queries.
    /// </summary>
    [Benchmark(Description = "Analytics Queries")]
    public int AnalyticsQueries()
    {
        int cnt = 0;

        if (Database == SqlDatabaseType.WitDb)
        {
            // Total revenue by user
            using (var c = _witConn.CreateCommand())
            {
                c.CommandText = @"
                    SELECT u.Id, u.Name, SUM(o.Amount)
                    FROM Users u
                    INNER JOIN Orders o ON u.Id = o.UserId
                    WHERE o.Status = 'completed'
                    GROUP BY u.Id, u.Name";
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }

            // Order status distribution
            using (var c = _witConn.CreateCommand())
            {
                c.CommandText = "SELECT Status, COUNT(*) FROM Orders GROUP BY Status";
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }

            // Top users by order count
            using (var c = _witConn.CreateCommand())
            {
                c.CommandText = @"
                    SELECT u.Name, COUNT(o.Id)
                    FROM Users u
                    INNER JOIN Orders o ON u.Id = o.UserId
                    GROUP BY u.Id, u.Name
                    ORDER BY COUNT(o.Id) DESC
                    LIMIT 10";
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }
        }
        else
        {
            using (var c = _sqliteConn.CreateCommand())
            {
                c.CommandText = @"
                    SELECT u.Id, u.Name, SUM(o.Amount) AS TotalRevenue
                    FROM Users u
                    INNER JOIN Orders o ON u.Id = o.UserId
                    WHERE o.Status = 'completed'
                    GROUP BY u.Id, u.Name";
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }

            using (var c = _sqliteConn.CreateCommand())
            {
                c.CommandText = "SELECT Status, COUNT(*) AS Cnt FROM Orders GROUP BY Status";
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }

            using (var c = _sqliteConn.CreateCommand())
            {
                c.CommandText = @"
                    SELECT u.Name, COUNT(o.Id) AS OrderCount
                    FROM Users u
                    INNER JOIN Orders o ON u.Id = o.UserId
                    GROUP BY u.Id, u.Name
                    ORDER BY OrderCount DESC
                    LIMIT 10";
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }
        }
        return cnt;
    }

    /// <summary>
    /// Batch operations in a single transaction.
    /// </summary>
    [Benchmark(Description = "Batch Operations")]
    public int BatchOperations()
    {
        int cnt = 0;
        int userCount = InitialRows / 10;

        if (Database == SqlDatabaseType.WitDb)
        {
            var tx = (WitDbTransaction)_witConn.BeginTransaction();
            
            // Batch insert
            using (var c = _witConn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "INSERT INTO Orders (UserId, Amount, Status, CreatedAt) VALUES (@userId, @amount, @status, @created)";
                var pUserId = c.CreateParameter(); pUserId.ParameterName = "@userId"; c.Parameters.Add(pUserId);
                var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; c.Parameters.Add(pAmount);
                var pStatus = c.CreateParameter(); pStatus.ParameterName = "@status"; c.Parameters.Add(pStatus);
                var pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; c.Parameters.Add(pCreated);

                for (int i = 0; i < OperationsPerIteration / 2; i++)
                {
                    pUserId.Value = _rnd.Next(1, userCount + 1);
                    pAmount.Value = Math.Round(_rnd.NextDouble() * 500, 2);
                    pStatus.Value = "pending";
                    pCreated.Value = DateTime.UtcNow;
                    cnt += c.ExecuteNonQuery();
                }
            }

            // Batch update
            using (var c = _witConn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "UPDATE Orders SET Status = 'completed' WHERE Status = 'pending' AND Id < @maxId";
                var p = c.CreateParameter(); p.ParameterName = "@maxId"; p.Value = InitialRows / 2; c.Parameters.Add(p);
                cnt += c.ExecuteNonQuery();
            }

            tx.Commit();
            tx.Dispose();
        }
        else
        {
            var tx = _sqliteConn.BeginTransaction();

            using (var c = _sqliteConn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "INSERT INTO Orders (UserId, Amount, Status, CreatedAt) VALUES (@userId, @amount, @status, @created)";
                var pUserId = c.CreateParameter(); pUserId.ParameterName = "@userId"; c.Parameters.Add(pUserId);
                var pAmount = c.CreateParameter(); pAmount.ParameterName = "@amount"; c.Parameters.Add(pAmount);
                var pStatus = c.CreateParameter(); pStatus.ParameterName = "@status"; c.Parameters.Add(pStatus);
                var pCreated = c.CreateParameter(); pCreated.ParameterName = "@created"; c.Parameters.Add(pCreated);

                for (int i = 0; i < OperationsPerIteration / 2; i++)
                {
                    pUserId.Value = _rnd.Next(1, userCount + 1);
                    pAmount.Value = Math.Round(_rnd.NextDouble() * 500, 2);
                    pStatus.Value = "pending";
                    pCreated.Value = DateTime.UtcNow.ToString("O");
                    cnt += c.ExecuteNonQuery();
                }
            }

            using (var c = _sqliteConn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = "UPDATE Orders SET Status = 'completed' WHERE Status = 'pending' AND Id < @maxId";
                var p = c.CreateParameter(); p.ParameterName = "@maxId"; p.Value = InitialRows / 2; c.Parameters.Add(p);
                cnt += c.ExecuteNonQuery();
            }

            tx.Commit();
            tx.Dispose();
        }
        return cnt;
    }

    public void Dispose() => GlobalCleanup();
}

/// <summary>
/// Benchmarks for index performance.
/// Note: LiteDB is excluded as it doesn't support SQL queries.
/// </summary>
[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class IndexBenchmarks : IDisposable
{
    public enum SqlDatabaseType { WitDb, SQLite }
    
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;
    private int[] _searchValues = null!;

    [Params(10000)]
    public int RowCount { get; set; }

    [Params(SqlDatabaseType.WitDb, SqlDatabaseType.SQLite)]
    public SqlDatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_idx_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_idx_{Guid.NewGuid():N}.db");

        // Pre-generate search values
        var rnd = new Random(42);
        _searchValues = Enumerable.Range(0, 1000).Select(_ => rnd.Next(1, RowCount + 1)).ToArray();

        SetupWitDb();
        SetupSqlite();
    }

    private void SetupWitDb()
    {
        // Use LSM-Tree storage via connection string
        _witConn = new WitDbConnection($"Data Source={_witPath};Store=lsm;Transactions=true");
        _witConn.Open();

        using (var c = _witConn.CreateCommand())
        {
            c.CommandText = @"CREATE TABLE Data (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Category INT,
                Value DOUBLE,
                Name VARCHAR(100)
            )";
            c.ExecuteNonQuery();

            // Create secondary index on Category
            c.CommandText = "CREATE INDEX IX_Data_Category ON Data(Category)";
            c.ExecuteNonQuery();
        }

        var tx = (WitDbTransaction)_witConn.BeginTransaction();
        using (var c = _witConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Data (Category, Value, Name) VALUES (@cat, @val, @name)";
            var pCat = c.CreateParameter(); pCat.ParameterName = "@cat"; c.Parameters.Add(pCat);
            var pVal = c.CreateParameter(); pVal.ParameterName = "@val"; c.Parameters.Add(pVal);
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);

            var rnd = new Random(42);
            for (int i = 0; i < RowCount; i++)
            {
                pCat.Value = rnd.Next(100);  // 100 categories
                pVal.Value = rnd.NextDouble() * 1000;
                pName.Value = $"Item_{i}";
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    private void SetupSqlite()
    {
        _sqliteConn = new SqliteConnection($"Data Source={_sqlitePath}");
        _sqliteConn.Open();

        using (var c = _sqliteConn.CreateCommand())
        {
            c.CommandText = @"CREATE TABLE Data (
                Id INTEGER PRIMARY KEY,
                Category INTEGER,
                Value REAL,
                Name TEXT
            )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Data_Category ON Data(Category)";
            c.ExecuteNonQuery();
        }

        var tx = _sqliteConn.BeginTransaction();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO Data (Category, Value, Name) VALUES (@cat, @val, @name)";
            var pCat = c.CreateParameter(); pCat.ParameterName = "@cat"; c.Parameters.Add(pCat);
            var pVal = c.CreateParameter(); pVal.ParameterName = "@val"; c.Parameters.Add(pVal);
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);

            var rnd = new Random(42);
            for (int i = 0; i < RowCount; i++)
            {
                pCat.Value = rnd.Next(100);
                pVal.Value = rnd.NextDouble() * 1000;
                pName.Value = $"Item_{i}";
                c.ExecuteNonQuery();
            }
        }
        tx.Commit();
        tx.Dispose();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _witConn?.Dispose();
        _sqliteConn?.Dispose();
        try { Directory.Delete(_witPath, true); } catch { }
        try { File.Delete(_sqlitePath); } catch { }
    }

    [Benchmark(Description = "Query by PK (indexed)")]
    public int QueryByPk()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT * FROM Data WHERE Id = @id";
            var p = c.CreateParameter(); p.ParameterName = "@id"; c.Parameters.Add(p);
            foreach (var id in _searchValues)
            {
                p.Value = id;
                using var r = c.ExecuteReader();
                if (r.Read()) cnt++;
            }
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT * FROM Data WHERE Id = @id";
            var p = c.CreateParameter(); p.ParameterName = "@id"; c.Parameters.Add(p);
            foreach (var id in _searchValues)
            {
                p.Value = id;
                using var r = c.ExecuteReader();
                if (r.Read()) cnt++;
            }
        }
        return cnt;
    }

    [Benchmark(Description = "Query by Secondary Index")]
    public int QueryBySecondaryIndex()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT * FROM Data WHERE Category = @cat";
            var p = c.CreateParameter(); p.ParameterName = "@cat"; c.Parameters.Add(p);
            for (int i = 0; i < 100; i++)  // Query each category once
            {
                p.Value = i;
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT * FROM Data WHERE Category = @cat";
            var p = c.CreateParameter(); p.ParameterName = "@cat"; c.Parameters.Add(p);
            for (int i = 0; i < 100; i++)
            {
                p.Value = i;
                using var r = c.ExecuteReader();
                while (r.Read()) cnt++;
            }
        }
        return cnt;
    }

    [Benchmark(Description = "Query Non-Indexed Column")]
    public int QueryNonIndexed()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT * FROM Data WHERE Value > @val AND Value < @val2";
            var p = c.CreateParameter(); p.ParameterName = "@val"; p.Value = 400.0; c.Parameters.Add(p);
            var p2 = c.CreateParameter(); p2.ParameterName = "@val2"; p2.Value = 600.0; c.Parameters.Add(p2);
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT * FROM Data WHERE Value > @val AND Value < @val2";
            var p = c.CreateParameter(); p.ParameterName = "@val"; p.Value = 400.0; c.Parameters.Add(p);
            var p2 = c.CreateParameter(); p2.ParameterName = "@val2"; p2.Value = 600.0; c.Parameters.Add(p2);
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "Aggregation with Index")]
    public int AggregationWithIndex()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = "SELECT Category, COUNT(*), AVG(Value) FROM Data GROUP BY Category";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = "SELECT Category, COUNT(*) AS Cnt, AVG(Value) AS AvgVal FROM Data GROUP BY Category";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    public void Dispose() => GlobalCleanup();
}
