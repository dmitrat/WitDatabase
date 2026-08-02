using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using LiteDB;
using Microsoft.Data.Sqlite;
using OutWit.Database.AdoNet;

namespace OutWit.Database.Benchmarks;

#region LiteDB Document Classes for Joins

public class CustomerDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class ProductJoinDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Price { get; set; }
    public int CategoryId { get; set; }
}

public class OrderJoinDoc
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime OrderDate { get; set; }
}

public class CategoryDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion

/// <summary>
/// Benchmarks for JOIN query performance.
/// Tests various join patterns against different WitDb modes, SQLite and LiteDB.
/// Note: LiteDB is NoSQL and doesn't support SQL JOINs - manual joins in code are used.
/// </summary>
[Config(typeof(SqlEngineBenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class JoinBenchmarks : IDisposable
{
    #region Fields

    private WitDbConnection? m_witConn;
    private SqliteConnection? m_sqliteConn;
    private LiteDatabase? m_liteDb;
    private ILiteCollection<CustomerDoc>? m_customers;
    private ILiteCollection<ProductJoinDoc>? m_products;
    private ILiteCollection<OrderJoinDoc>? m_orders;
    private ILiteCollection<CategoryDoc>? m_categories;
    private string m_witPath = null!;
    private string m_sqlitePath = null!;
    private string m_liteDbPath = null!;

    #endregion

    #region Parameters

    [ParamsSource(nameof(OrdersCountValues))]
    public int OrdersCount { get; set; }

    public IEnumerable<int> OrdersCountValues => BenchmarkSweep.Sizes(100, 500);

    [ParamsSource(nameof(EngineModeValues))]
    public WitDbEngineMode EngineMode { get; set; }

    public IEnumerable<WitDbEngineMode> EngineModeValues => BenchmarkSweep.Modes(WitDbEngineMode.Default, WitDbEngineMode.BTree, WitDbEngineMode.Lsm, WitDbEngineMode.BTreeParallelAuto, WitDbEngineMode.LsmParallelAuto);

    #endregion

    #region Setup/Cleanup

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Use different path patterns for BTree (file .witdb) vs LSM (directory)
        var isLsm = EngineMode is WitDbEngineMode.Lsm or WitDbEngineMode.LsmParallelAuto;
        m_witPath = isLsm 
            ? BenchmarkPathHelper.GenerateUniquePath("wit_join_lsm")
            : BenchmarkPathHelper.GenerateUniquePath("wit_join_btree") + ".witdb";
        m_sqlitePath = BenchmarkPathHelper.GenerateUniquePath("sql_join") + ".db";
        m_liteDbPath = BenchmarkPathHelper.GenerateUniquePath("lite_join") + ".db";

        CleanupPaths();

        SetupWitDb();
        SetupSqlite();
        SetupLiteDb();
    }

    private void CleanupPaths()
    {
        BenchmarkPathHelper.SafeCleanup(m_witPath);
        BenchmarkPathHelper.SafeCleanup(m_witPath + "_indexes");
        BenchmarkPathHelper.SafeCleanup(m_sqlitePath);
        BenchmarkPathHelper.SafeCleanup(m_liteDbPath);
    }

    private void SetupWitDb()
    {
        var connStr = WitDbConnectionHelper.BuildConnectionString(m_witPath, EngineMode);
        m_witConn = new WitDbConnection(connStr);
        m_witConn.Open();

        using (var c = m_witConn.CreateCommand())
        {
            c.CommandText = @"CREATE TABLE Customers (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Country VARCHAR(50)
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Price DOUBLE,
                CategoryId INT
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                CustomerId INT,
                ProductId INT,
                Quantity INT,
                OrderDate DATETIME
            )";
            c.ExecuteNonQuery();

            c.CommandText = @"CREATE TABLE Categories (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(50)
            )";
            c.ExecuteNonQuery();

            c.CommandText = "CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Orders_ProductId ON Orders(ProductId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_CategoryId ON Products(CategoryId)";
            c.ExecuteNonQuery();
        }

        InsertTestDataWit();
    }

    private void InsertTestDataWit()
    {
        var tx = (WitDbTransaction)m_witConn!.BeginTransaction();
        using (var c = m_witConn.CreateCommand())
        {
            c.Transaction = tx;

            // Categories
            c.CommandText = "INSERT INTO Categories (Name) VALUES (@name)";
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            string[] categories = { "Electronics", "Clothing", "Food", "Books", "Sports" };
            foreach (var cat in categories)
            {
                pName.Value = cat;
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

            // Customers
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

            // Products
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

            // Orders
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
        m_sqliteConn = new SqliteConnection($"Data Source={m_sqlitePath}");
        m_sqliteConn.Open();

        using (var c = m_sqliteConn.CreateCommand())
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

            c.CommandText = "CREATE INDEX IX_Orders_CustomerId ON Orders(CustomerId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Orders_ProductId ON Orders(ProductId)";
            c.ExecuteNonQuery();
            c.CommandText = "CREATE INDEX IX_Products_CategoryId ON Products(CategoryId)";
            c.ExecuteNonQuery();
        }

        var tx = m_sqliteConn.BeginTransaction();
        using (var c = m_sqliteConn.CreateCommand())
        {
            c.Transaction = tx;

            string[] categories = { "Electronics", "Clothing", "Food", "Books", "Sports" };
            c.CommandText = "INSERT INTO Categories (Name) VALUES (@name)";
            var pName = c.CreateParameter(); pName.ParameterName = "@name"; c.Parameters.Add(pName);
            foreach (var cat in categories)
            {
                pName.Value = cat;
                c.ExecuteNonQuery();
            }
            c.Parameters.Clear();

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

    private void SetupLiteDb()
    {
        m_liteDb = new LiteDatabase(m_liteDbPath);
        m_customers = m_liteDb.GetCollection<CustomerDoc>("customers");
        m_products = m_liteDb.GetCollection<ProductJoinDoc>("products");
        m_orders = m_liteDb.GetCollection<OrderJoinDoc>("orders");
        m_categories = m_liteDb.GetCollection<CategoryDoc>("categories");

        m_customers.EnsureIndex(x => x.Country);
        m_orders.EnsureIndex(x => x.CustomerId);
        m_orders.EnsureIndex(x => x.ProductId);
        m_products.EnsureIndex(x => x.CategoryId);

        string[] categoryNames = { "Electronics", "Clothing", "Food", "Books", "Sports" };
        var cats = categoryNames.Select((name, i) => new CategoryDoc { Id = i + 1, Name = name }).ToList();
        m_categories.InsertBulk(cats);

        int customerCount = Math.Max(10, OrdersCount / 10);
        string[] countries = { "USA", "UK", "Germany", "France", "Japan" };
        var custs = Enumerable.Range(0, customerCount)
            .Select(i => new CustomerDoc { Id = i + 1, Name = $"Customer_{i}", Country = countries[i % countries.Length] })
            .ToList();
        m_customers.InsertBulk(custs);

        int productCount = Math.Max(20, OrdersCount / 5);
        var rnd = new Random(42);
        var prods = Enumerable.Range(0, productCount)
            .Select(i => new ProductJoinDoc
            {
                Id = i + 1,
                Name = $"Product_{i}",
                Price = Math.Round(rnd.NextDouble() * 1000, 2),
                CategoryId = (i % categoryNames.Length) + 1
            })
            .ToList();
        m_products.InsertBulk(prods);

        var baseDate = new DateTime(2024, 1, 1);
        var ords = Enumerable.Range(0, OrdersCount)
            .Select(i => new OrderJoinDoc
            {
                Id = i + 1,
                CustomerId = (i % customerCount) + 1,
                ProductId = (i % productCount) + 1,
                Quantity = rnd.Next(1, 10),
                OrderDate = baseDate.AddDays(i % 365)
            })
            .ToList();
        m_orders.InsertBulk(ords);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        m_witConn?.Dispose();
        m_sqliteConn?.Dispose();
        m_liteDb?.Dispose();

        CleanupPaths();
    }

    #endregion

    #region Benchmarks - INNER JOIN 2 tables

    [Benchmark(Description = "INNER JOIN 2 tables - WitDb")]
    public int InnerJoin2WitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    // No Baseline here on purpose. BenchmarkDotNet allows one baseline per class unless the
    // benchmarks are split into categories, so a single [Benchmark(Baseline = true)] made the Ratio
    // column compare every operation in the class against one unrelated operation - the January
    // report rated a 20-iteration seek "2.74x faster" than a 100-iteration one. Until the classes
    // carry [BenchmarkCategory] the honest report has no Ratio column at all; ratios are computed
    // per operation from the Mean column instead.
    [Benchmark(Description = "INNER JOIN 2 tables - SQLite")]
    public int InnerJoin2Sqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Manual JOIN 2 collections - LiteDB")]
    public int InnerJoin2LiteDb()
    {
        var customersDict = m_customers!.FindAll().ToDictionary(c => c.Id);
        int cnt = 0;
        foreach (var order in m_orders!.FindAll())
        {
            if (customersDict.TryGetValue(order.CustomerId, out var customer))
            {
                // Simulate accessing joined data
                _ = new { order.Id, customer.Name, order.Quantity };
                cnt++;
            }
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - 3-table JOIN

    [Benchmark(Description = "INNER JOIN 3 tables - WitDb")]
    public int InnerJoin3WitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, p.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id
            INNER JOIN Products p ON o.ProductId = p.Id";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "INNER JOIN 3 tables - SQLite")]
    public int InnerJoin3Sqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name AS CustomerName, p.Name AS ProductName, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id
            INNER JOIN Products p ON o.ProductId = p.Id";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Manual JOIN 3 collections - LiteDB")]
    public int InnerJoin3LiteDb()
    {
        var customersDict = m_customers!.FindAll().ToDictionary(c => c.Id);
        var productsDict = m_products!.FindAll().ToDictionary(p => p.Id);
        int cnt = 0;
        foreach (var order in m_orders!.FindAll())
        {
            if (customersDict.TryGetValue(order.CustomerId, out var customer) &&
                productsDict.TryGetValue(order.ProductId, out var product))
            {
                _ = new { order.Id, CustomerName = customer.Name, ProductName = product.Name, order.Quantity };
                cnt++;
            }
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - 4-table JOIN

    [Benchmark(Description = "INNER JOIN 4 tables - WitDb")]
    public int InnerJoin4WitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, p.Name, cat.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id
            INNER JOIN Products p ON o.ProductId = p.Id
            INNER JOIN Categories cat ON p.CategoryId = cat.Id";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "INNER JOIN 4 tables - SQLite")]
    public int InnerJoin4Sqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, p.Name, cat.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id
            INNER JOIN Products p ON o.ProductId = p.Id
            INNER JOIN Categories cat ON p.CategoryId = cat.Id";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Manual JOIN 4 collections - LiteDB")]
    public int InnerJoin4LiteDb()
    {
        var customersDict = m_customers!.FindAll().ToDictionary(c => c.Id);
        var productsDict = m_products!.FindAll().ToDictionary(p => p.Id);
        var categoriesDict = m_categories!.FindAll().ToDictionary(c => c.Id);
        int cnt = 0;
        foreach (var order in m_orders!.FindAll())
        {
            if (customersDict.TryGetValue(order.CustomerId, out var customer) &&
                productsDict.TryGetValue(order.ProductId, out var product) &&
                categoriesDict.TryGetValue(product.CategoryId, out var category))
            {
                _ = new { order.Id, CustomerName = customer.Name, ProductName = product.Name, CategoryName = category.Name, order.Quantity };
                cnt++;
            }
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - LEFT JOIN

    [Benchmark(Description = "LEFT JOIN - WitDb")]
    public int LeftJoinWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT c.Id, c.Name, o.Id
            FROM Customers c
            LEFT JOIN Orders o ON c.Id = o.CustomerId";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "LEFT JOIN - SQLite")]
    public int LeftJoinSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT c.Id, c.Name, o.Id
            FROM Customers c
            LEFT JOIN Orders o ON c.Id = o.CustomerId";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Manual LEFT JOIN - LiteDB")]
    public int LeftJoinLiteDb()
    {
        var ordersByCustomer = m_orders!.FindAll().ToLookup(o => o.CustomerId);
        int cnt = 0;
        foreach (var customer in m_customers!.FindAll())
        {
            var customerOrders = ordersByCustomer[customer.Id];
            if (customerOrders.Any())
            {
                foreach (var order in customerOrders)
                {
                    _ = new { customer.Id, customer.Name, OrderId = (int?)order.Id };
                    cnt++;
                }
            }
            else
            {
                _ = new { customer.Id, customer.Name, OrderId = (int?)null };
                cnt++;
            }
        }
        return cnt;
    }

    #endregion

    #region Benchmarks - JOIN with GROUP BY

    [Benchmark(Description = "JOIN with GROUP BY - WitDb")]
    public int JoinGroupByWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT c.Id, c.Name, COUNT(o.Id), SUM(o.Quantity)
            FROM Customers c
            INNER JOIN Orders o ON c.Id = o.CustomerId
            GROUP BY c.Id, c.Name";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "JOIN with GROUP BY - SQLite")]
    public int JoinGroupBySqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT c.Id, c.Name, COUNT(o.Id) AS OrderCount, SUM(o.Quantity) AS TotalQty
            FROM Customers c
            INNER JOIN Orders o ON c.Id = o.CustomerId
            GROUP BY c.Id, c.Name";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Manual JOIN + GroupBy - LiteDB")]
    public int JoinGroupByLiteDb()
    {
        var customersDict = m_customers!.FindAll().ToDictionary(c => c.Id);
        var result = m_orders!.FindAll()
            .Where(o => customersDict.ContainsKey(o.CustomerId))
            .GroupBy(o => o.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                CustomerName = customersDict[g.Key].Name,
                OrderCount = g.Count(),
                TotalQty = g.Sum(o => o.Quantity)
            })
            .ToList();
        return result.Count;
    }

    #endregion

    #region Benchmarks - JOIN with WHERE

    [Benchmark(Description = "JOIN with WHERE filter - WitDb")]
    public int JoinWhereWitDb()
    {
        int cnt = 0;
        using var c = m_witConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, p.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id
            INNER JOIN Products p ON o.ProductId = p.Id
            WHERE c.Country = 'USA' AND o.Quantity > 5";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "JOIN with WHERE filter - SQLite")]
    public int JoinWhereSqlite()
    {
        int cnt = 0;
        using var c = m_sqliteConn!.CreateCommand();
        c.CommandText = @"
            SELECT o.Id, c.Name, p.Name, o.Quantity
            FROM Orders o
            INNER JOIN Customers c ON o.CustomerId = c.Id
            INNER JOIN Products p ON o.ProductId = p.Id
            WHERE c.Country = 'USA' AND o.Quantity > 5";
        using var r = c.ExecuteReader();
        while (r.Read()) cnt++;
        return cnt;
    }

    [Benchmark(Description = "Manual JOIN with filter - LiteDB")]
    public int JoinWhereLiteDb()
    {
        var usaCustomers = m_customers!.Find(c => c.Country == "USA").ToDictionary(c => c.Id);
        var productsDict = m_products!.FindAll().ToDictionary(p => p.Id);
        int cnt = 0;
        foreach (var order in m_orders!.Find(o => o.Quantity > 5))
        {
            if (usaCustomers.TryGetValue(order.CustomerId, out var customer) &&
                productsDict.TryGetValue(order.ProductId, out var product))
            {
                _ = new { order.Id, CustomerName = customer.Name, ProductName = product.Name, order.Quantity };
                cnt++;
            }
        }
        return cnt;
    }

    #endregion

    #region IDisposable

    public void Dispose() => GlobalCleanup();

    #endregion
}
