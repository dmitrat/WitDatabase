using OutWit.Database.Core.Builder;
using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for implicit PRIMARY KEY index creation.
/// When a table has PRIMARY KEY (not AUTOINCREMENT), an implicit UNIQUE INDEX is created
/// to enable O(log n) uniqueness checks instead of O(n) full table scans.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineImplicitPkIndexTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        // Use database with index support
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    #endregion

    #region Implicit PK Index Creation Tests

    [Test]
    public void CreateTableWithExplicitPkCreatesImplicitIndexTest()
    {
        // Arrange & Act
        m_engine.Execute(@"
            CREATE TABLE Products (
                SKU VARCHAR(50) PRIMARY KEY,
                Name VARCHAR(200) NOT NULL
            )");

        // Assert - implicit PK index should exist
        var indexDef = m_engine.GetIndex("_PK_Products");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsUnique, Is.True);
        Assert.That(indexDef.IsPrimaryKey, Is.True);
        Assert.That(indexDef.IsImplicit, Is.True);
        Assert.That(indexDef.Columns, Contains.Item("SKU"));
    }

    [Test]
    public void CreateTableWithAutoIncrementPkDoesNotCreateImplicitIndexTest()
    {
        // Arrange & Act
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL
            )");

        // Assert - no implicit PK index for AUTOINCREMENT
        var indexDef = m_engine.GetIndex("_PK_Users");
        Assert.That(indexDef, Is.Null);
    }

    [Test]
    public void CreateTableWithCompositePkCreatesImplicitIndexTest()
    {
        // Arrange & Act
        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                OrderId INT,
                ProductId INT,
                Quantity INT NOT NULL,
                PRIMARY KEY (OrderId, ProductId)
            )");

        // Assert - implicit PK index should exist with both columns
        var indexDef = m_engine.GetIndex("_PK_OrderItems");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsUnique, Is.True);
        Assert.That(indexDef.IsPrimaryKey, Is.True);
        Assert.That(indexDef.IsImplicit, Is.True);
        Assert.That(indexDef.Columns.Count, Is.EqualTo(2));
        Assert.That(indexDef.Columns, Contains.Item("OrderId"));
        Assert.That(indexDef.Columns, Contains.Item("ProductId"));
    }

    [Test]
    public void CreateTableWithNoPkDoesNotCreateImplicitIndexTest()
    {
        // Arrange & Act
        m_engine.Execute(@"
            CREATE TABLE Logs (
                Id INT,
                Message TEXT,
                CreatedAt DATETIME
            )");

        // Assert - no implicit PK index when there's no PK
        var indexDef = m_engine.GetIndex("_PK_Logs");
        Assert.That(indexDef, Is.Null);
    }

    [Test]
    public void CreateTableWithIntegerPkNoAutoIncrementCreatesImplicitIndexTest()
    {
        // Arrange & Act - INTEGER PK without explicit AUTOINCREMENT
        m_engine.Execute(@"
            CREATE TABLE Items (
                Id INT PRIMARY KEY,
                Name VARCHAR(100)
            )");

        // Assert - implicit index should be created (INTEGER PK without explicit AUTOINCREMENT is NOT auto-increment)
        // Note: SQLite treats INTEGER PRIMARY KEY as AUTOINCREMENT by default, but WitDB requires explicit AUTOINCREMENT
        var indexDef = m_engine.GetIndex("_PK_Items");
        Assert.That(indexDef, Is.Not.Null);
    }

    #endregion

    #region Drop Table Tests

    [Test]
    public void DropTableRemovesImplicitPkIndexTest()
    {
        // Arrange
        m_engine.Execute("CREATE TABLE Products (SKU VARCHAR(50) PRIMARY KEY, Name VARCHAR(200))");
        
        // Verify index exists
        Assert.That(m_engine.GetIndex("_PK_Products"), Is.Not.Null);

        // Act
        m_engine.Execute("DROP TABLE Products");

        // Assert - implicit PK index should be removed
        Assert.That(m_engine.GetIndex("_PK_Products"), Is.Null);
    }

    #endregion

    #region Performance Tests

    [Test]
    public void InsertWithExplicitPkUsesImplicitIndexForUniqueCheckTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                SKU VARCHAR(50) PRIMARY KEY,
                Name VARCHAR(200) NOT NULL
            )");

        // Act - insert many rows (should use implicit index for uniqueness check)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 500; i++)
        {
            m_engine.Execute(
                "INSERT INTO Products (SKU, Name) VALUES (@sku, @name)",
                new Dictionary<string, object?>
                {
                    { "@sku", $"SKU-{i:D5}" },
                    { "@name", $"Product {i}" }
                });
        }
        sw.Stop();

        // Assert - verify all rows inserted
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM Products").AsInt64();
        Assert.That(count, Is.EqualTo(500));

        // Log performance for manual verification
        TestContext.Out.WriteLine($"Inserted 500 rows with explicit PK in {sw.ElapsedMilliseconds} ms");
        TestContext.Out.WriteLine($"Per row: {sw.Elapsed.TotalMilliseconds / 500:F4} ms");
        
        // The implicit index should make this much faster than O(n²)
        // 500 rows without index would require ~125K row scans total
        // With index it's ~4500 index lookups (O(n log n))
    }

    [Test]
    public void InsertDuplicatePkWithImplicitIndexThrowsTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                SKU VARCHAR(50) PRIMARY KEY,
                Name VARCHAR(200) NOT NULL
            )");
        
        m_engine.Execute("INSERT INTO Products (SKU, Name) VALUES ('SKU-001', 'Product 1')");

        // Act & Assert - duplicate PK should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Products (SKU, Name) VALUES ('SKU-001', 'Product 2')"));
        
        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    [Test]
    public void InsertCompositePkDuplicateThrowsTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                OrderId INT,
                ProductId INT,
                Quantity INT NOT NULL,
                PRIMARY KEY (OrderId, ProductId)
            )");
        
        m_engine.Execute("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (1, 100, 5)");

        // Act & Assert - duplicate composite PK should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (1, 100, 10)"));
        
        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    [Test]
    public void InsertCompositePkPartialMatchAllowedTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                OrderId INT,
                ProductId INT,
                Quantity INT NOT NULL,
                PRIMARY KEY (OrderId, ProductId)
            )");
        
        m_engine.Execute("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (1, 100, 5)");

        // Act - different ProductId should be allowed (partial match)
        Assert.DoesNotThrow(() =>
            m_engine.Execute("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (1, 101, 10)"));
        
        // Different OrderId should also be allowed
        Assert.DoesNotThrow(() =>
            m_engine.Execute("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (2, 100, 15)"));

        // Verify all rows inserted
        var count = m_engine.ExecuteScalar("SELECT COUNT(*) FROM OrderItems").AsInt64();
        Assert.That(count, Is.EqualTo(3));
    }

    #endregion

    #region Index Seek Tests

    [Test]
    public void ImplicitPkIndexCanBeUsedForSeekTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                SKU VARCHAR(50) PRIMARY KEY,
                Name VARCHAR(200) NOT NULL
            )");
        
        m_engine.Execute("INSERT INTO Products (SKU, Name) VALUES ('SKU-001', 'Widget')");
        m_engine.Execute("INSERT INTO Products (SKU, Name) VALUES ('SKU-002', 'Gadget')");
        m_engine.Execute("INSERT INTO Products (SKU, Name) VALUES ('SKU-003', 'Gizmo')");

        // Act - use implicit PK index for seek
        using var iterator = m_engine.CreateIndexSeek("Products", "_PK_Products", [WitSqlValue.FromText("SKU-002")]);
        iterator.Open();
        
        // Assert - should find the row
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["SKU"].AsString(), Is.EqualTo("SKU-002"));
        Assert.That(iterator.Current["Name"].AsString(), Is.EqualTo("Gadget"));
        Assert.That(iterator.MoveNext(), Is.False); // Only one match
    }

    #endregion

    #region INFORMATION_SCHEMA Tests

    [Test]
    public void ImplicitPkIndexNotShownInInformationSchemaTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                SKU VARCHAR(50) PRIMARY KEY,
                Name VARCHAR(200) NOT NULL
            )");
        
        // Create an explicit index for comparison
        m_engine.Execute("CREATE INDEX IX_Products_Name ON Products(Name)");

        // Act - query INFORMATION_SCHEMA.INDEXES
        var indexes = m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = 'Products'");

        // Assert - implicit index should NOT be shown, explicit index SHOULD be shown
        var indexNames = indexes.Select(r => r["INDEX_NAME"].AsString()).ToList();
        Assert.That(indexNames, Does.Not.Contain("_PK_Products")); // Implicit - hidden
        Assert.That(indexNames, Does.Contain("IX_Products_Name")); // Explicit - shown
    }

    #endregion
}
