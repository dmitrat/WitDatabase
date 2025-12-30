using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Comparison.Benchmarks;

/// <summary>
/// Benchmarks comparing JOIN performance between WitDb and SQLite.
/// Note: LiteDB is excluded as it doesn't support SQL JOINs (NoSQL document database).
/// </summary>
[Config(typeof(ComparisonBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JoinBenchmarks : IDisposable
{
    public enum SqlDatabaseType { WitDb, SQLite }
    
    private WitDbConnection _witConn = null!;
    private SqliteConnection _sqliteConn = null!;
    private string _witPath = null!;
    private string _sqlitePath = null!;

    [Params(100, 1000)]
    public int OrdersCount { get; set; }

    [Params(SqlDatabaseType.WitDb, SqlDatabaseType.SQLite)]
    public SqlDatabaseType Database { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _witPath = Path.Combine(Path.GetTempPath(), $"wit_join_{Guid.NewGuid():N}");
        _sqlitePath = Path.Combine(Path.GetTempPath(), $"sql_join_{Guid.NewGuid():N}.db");

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
            // Customers table with AUTOINCREMENT
            c.CommandText = @"CREATE TABLE Customers (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Country VARCHAR(50)
            )";
            c.ExecuteNonQuery();

            // Products table with AUTOINCREMENT
            c.CommandText = @"CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Price DOUBLE,
                CategoryId INT
            )";
            c.ExecuteNonQuery();

            // Orders table with AUTOINCREMENT
            c.CommandText = @"CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                CustomerId INT,
                ProductId INT,
                Quantity INT,
                OrderDate DATETIME
            )";
            c.ExecuteNonQuery();

            // Categories table with AUTOINCREMENT
            c.CommandText = @"CREATE TABLE Categories (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(50)
            )";
            c.ExecuteNonQuery();

            // Create indexes for JOIN performance
            c.CommandText = "CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Orders_ProductId ON Orders(ProductId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_CategoryId ON Products(CategoryId)";
            c.ExecuteNonQuery();
        }

        // Insert data
        var tx = (WitDbTransaction)_witConn.BeginTransaction();
        using (var c = _witConn.CreateCommand())
        {
            c.Transaction = tx;

            // Insert categories
            c.CommandText = "INSERT INTO Categories (Name) VALUES (@name)";
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            string[] categories = { "Electronics", "Clothing", "Food", "Books", "Sports" };
            for (int i = 0; i < categories.Length; i++)
            {
                pName.Value = categories[i];
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert customers (10% of orders count, min 10)
            int customerCount = Math.Max(10, OrdersCount / 10);
            c.CommandText = "INSERT INTO Customers (Name, Country) VALUES (@name, @country)";
            pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pCountry = c.CreateParameter(); pCountry.ParameterName = "@country"; c.Parameters.Add(pCountry);
            string[] countries = { "USA", "UK", "Germany", "France", "Japan" };
            for (int i = 0; i < customerCount; i++)
            {
                pName.Value = $"Customer_{i}";
                pCountry.Value = countries[i % countries.Length];
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert products (20% of orders count, min 20)
            int productCount = Math.Max(20, OrdersCount / 5);
            c.CommandText = "INSERT INTO Products (Name, Price, CategoryId) VALUES (@name, @price, @catId)";
            pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pPrice = c.CreateParameter(); pPrice.ParameterName = "@price"; c.Parameters.Add(pPrice);
            var pCatId = c.CreateParameter(); pCatId.ParameterName = "@catId"; c.Parameters.Add(pCatId);
            var rnd = new Random(42);
            for (int i = 0; i < productCount; i++)
            {
                pName.Value = $"Product_{i}";
                pPrice.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pCatId.Value = (i % categories.Length) + 1;
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert orders
            c.CommandText = "INSERT INTO Orders (CustomerId, ProductId, Quantity, OrderDate) VALUES (@custId, @prodId, @qty, @date)";
            var pCustId = c.CreateParameter(); pCustId.ParameterName = "@custId"; c.Parameters.Add(pCustId);
            var pProdId = c.CreateParameter(); pProdId.ParameterName = "@prodId"; c.Parameters.Add(pProdId);
            var pQty = c.CreateParameter(); pQty.ParameterName = "@qty"; c.Parameters.Add(pQty);
            var pDate = c.CreateParameter(); pDate.ParameterName = "@date"; c.Parameters.Add(pDate);
            var baseDate = new DateTime(2024, 1, 1);
            for (int i = 0; i < OrdersCount; i++)
            {
                pCustId.Value = (i % customerCount) + 1;
                pProdId.Value = (i % productCount) + 1;
                pQty.Value = rnd.Next(1, 10);
                pDate.Value = baseDate.AddDays(i % 365);
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
            c.CommandText = @"CREATE TABLE Customers (
                Id INTEGER PRIMARY KEY,
                Name TEXT,
                Country TEXT
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Products (
                Id INTEGER PRIMARY KEY,
                Name TEXT,
                Price REAL,
                CategoryId INTEGER
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                CustomerId INTEGER,
                ProductId INTEGER,
                Quantity INTEGER,
                OrderDate TEXT
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY,
                Name TEXT
            )";
            c.ExecuteNonQuery();

            // Create indexes for JOIN performance
            c.CommandText = "CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Orders_ProductId ON Orders(ProductId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_CategoryId ON Products(CategoryId)";
            c.ExecuteNonQuery();
        }

        var tx = _sqliteConn.BeginTransaction();
        using (var c = _sqliteConn.CreateCommand())
        {
            c.Transaction = tx;

            // Insert categories
            c.CommandText = "INSERT INTO Categories (Name) VALUES (@name)";
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            string[] categories = { "Electronics", "Clothing", "Food", "Books", "Sports" };
            for (int i = 0; i < categories.Length; i++)
            {
                pName.Value = categories[i];
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert customers
            int customerCount = Math.Max(10, OrdersCount / 10);
            c.CommandText = "INSERT INTO Customers (Name, Country) VALUES (@name, @country)";
            pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pCountry = c.CreateParameter(); pCountry.ParameterName = "@country"; c.Parameters.Add(pCountry);
            string[] countries = { "USA", "UK", "Germany", "France", "Japan" };
            for (int i = 0; i < customerCount; i++)
            {
                pName.Value = $"Customer_{i}";
                pCountry.Value = countries[i % countries.Length];
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert products
            int productCount = Math.Max(20, OrdersCount / 5);
            c.CommandText = "INSERT INTO Products (Name, Price, CategoryId) VALUES (@name, @price, @catId)";
            pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            var pPrice = c.CreateParameter(); pPrice.ParameterName = "@price"; c.Parameters.Add(pPrice);
            var pCatId = c.CreateParameter(); pCatId.ParameterName = "@catId"; c.Parameters.Add(pCatId);
            var rnd = new Random(42);
            for (int i = 0; i < productCount; i++)
            {
                pName.Value = $"Product_{i}";
                pPrice.Value = Math.Round(rnd.NextDouble() * 1000, 2);
                pCatId.Value = (i % categories.Length) + 1;
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Insert orders
            c.CommandText = "INSERT INTO Orders (CustomerId, ProductId, Quantity, OrderDate) VALUES (@custId, @prodId, @qty, @date)";
            var pCustId = c.CreateParameter(); pCustId.ParameterName = "@custId"; c.Parameters.Add(pCustId);
            var pProdId = c.CreateParameter(); pProdId.ParameterName = "@prodId"; c.Parameters.Add(pProdId);
            var pQty = c.CreateParameter(); pQty.ParameterName = "@qty"; c.Parameters.Add(pQty);
            var pDate = c.CreateParameter(); pDate.ParameterName = "@date"; c.Parameters.Add(pDate);
            var baseDate = new DateTime(2024, 1, 1);
            for (int i = 0; i < OrdersCount; i++)
            {
                pCustId.Value = (i % customerCount) + 1;
                pProdId.Value = (i % productCount) + 1;
                pQty.Value = rnd.Next(1, 10);
                pDate.Value = baseDate.AddDays(i % 365).ToString("yyyy-MM-dd");
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

    [Benchmark(Description = "INNER JOIN 2 tables")]
    public int InnerJoin2Tables()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "INNER JOIN 3 tables")]
    public int InnerJoin3Tables()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name AS CustomerName, p.Name AS ProductName, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "INNER JOIN 4 tables")]
    public int InnerJoin4Tables()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, cat.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id
                INNER JOIN Categories cat ON p.CategoryId = cat.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, cat.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id
                INNER JOIN Categories cat ON p.CategoryId = cat.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "LEFT JOIN")]
    public int LeftJoin()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT c.Id, c.Name, o.Id
                FROM Customers c
                LEFT JOIN Orders o ON c.Id = o.CustomerId";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT c.Id, c.Name, o.Id
                FROM Customers c
                LEFT JOIN Orders o ON c.Id = o.CustomerId";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "JOIN with GROUP BY")]
    public int JoinWithGroupBy()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT c.Id, c.Name, COUNT(o.Id), SUM(o.Quantity)
                FROM Customers c
                INNER JOIN Orders o ON c.Id = o.CustomerId
                GROUP BY c.Id, c.Name";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT c.Id, c.Name, COUNT(o.Id) AS OrderCount, SUM(o.Quantity) AS TotalQty
                FROM Customers c
                INNER JOIN Orders o ON c.Id = o.CustomerId
                GROUP BY c.Id, c.Name";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "JOIN with ORDER BY")]
    public int JoinWithOrderBy()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id
                ORDER BY c.Name, o.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id
                ORDER BY c.Name, o.Id";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    [Benchmark(Description = "JOIN with WHERE filter")]
    public int JoinWithWhere()
    {
        int cnt = 0;
        if (Database == SqlDatabaseType.WitDb)
        {
            using var c = _witConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id
                WHERE c.Country = 'USA' AND o.Quantity > 5";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        else
        {
            using var c = _sqliteConn.CreateCommand();
            c.CommandText = @"
                SELECT o.Id, c.Name, p.Name, o.Quantity
                FROM Orders o
                INNER JOIN Customers c ON o.CustomerId = c.Id
                INNER JOIN Products p ON o.ProductId = p.Id
                WHERE c.Country = 'USA' AND o.Quantity > 5";
            using var r = c.ExecuteReader();
            while (r.Read()) cnt++;
        }
        return cnt;
    }

    public void Dispose() => GlobalCleanup();
}
