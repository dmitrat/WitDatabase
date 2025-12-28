using NUnit.Framework;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for TRUNCATE TABLE and MERGE statements.
/// </summary>
[TestFixture]
public class WitSqlEngineTruncateMergeTests : WitSqlEngineTestsBase
{
    [SetUp]
    public override void Setup()
    {
        base.Setup();
        CreateTestTables();
        InsertTestData();
    }

    private void CreateTestTables()
    {
        // Target table for MERGE tests
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT UNIQUE NOT NULL,
                Name TEXT NOT NULL,
                Price DECIMAL(10,2) DEFAULT 0,
                Stock INTEGER DEFAULT 0,
                Category TEXT DEFAULT 'General'
            )");

        // Source table for MERGE tests
        m_engine.Execute(@"
            CREATE TABLE ProductUpdates (
                Sku TEXT NOT NULL,
                Name TEXT NOT NULL,
                Price DECIMAL(10,2),
                Stock INTEGER,
                Category TEXT
            )");

        // Table with index for TRUNCATE tests
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER,
                Quantity INTEGER,
                Status TEXT DEFAULT 'pending'
            )");

        m_engine.Execute("CREATE INDEX IX_Orders_ProductId ON Orders (ProductId)");
        m_engine.Execute("CREATE INDEX IX_Orders_Status ON Orders (Status)");
    }

    private void InsertTestData()
    {
        // Products
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock, Category) VALUES ('SKU001', 'Widget', 29.99, 100, 'Hardware')");
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock, Category) VALUES ('SKU002', 'Gadget', 49.99, 50, 'Electronics')");
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock, Category) VALUES ('SKU003', 'Gizmo', 19.99, 200, 'Hardware')");

        // Orders
        m_engine.Execute("INSERT INTO Orders (ProductId, Quantity, Status) VALUES (1, 5, 'shipped')");
        m_engine.Execute("INSERT INTO Orders (ProductId, Quantity, Status) VALUES (2, 3, 'pending')");
        m_engine.Execute("INSERT INTO Orders (ProductId, Quantity, Status) VALUES (1, 10, 'pending')");
        m_engine.Execute("INSERT INTO Orders (ProductId, Quantity, Status) VALUES (3, 7, 'delivered')");
    }

    #region TRUNCATE Tests

    [Test]
    public void TruncateRemovesAllRowsTest()
    {
        // Verify initial data
        var beforeResult = m_engine.Execute("SELECT COUNT(*) FROM Products");
        beforeResult.Read();
        Assert.That(beforeResult.CurrentRow[0].AsInt64(), Is.EqualTo(3));

        // TRUNCATE
        var truncateResult = m_engine.Execute("TRUNCATE TABLE Products");
        Assert.That(truncateResult.RowsAffected, Is.EqualTo(0)); // TRUNCATE returns 0

        // Verify all rows removed
        var afterResult = m_engine.Execute("SELECT COUNT(*) FROM Products");
        afterResult.Read();
        Assert.That(afterResult.CurrentRow[0].AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void TruncateResetsAutoIncrementTest()
    {
        // Insert some rows
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock) VALUES ('SKU004', 'NewProduct', 99.99, 10)");
        
        // Verify Id > 3
        var maxIdBefore = m_engine.Execute("SELECT MAX(Id) FROM Products");
        maxIdBefore.Read();
        Assert.That(maxIdBefore.CurrentRow[0].AsInt64(), Is.GreaterThanOrEqualTo(4));

        // TRUNCATE
        m_engine.Execute("TRUNCATE TABLE Products");

        // Insert new row - should get Id = 1
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget', 29.99, 100)");
        
        var newIdResult = m_engine.Execute("SELECT Id FROM Products WHERE Sku = 'SKU001'");
        newIdResult.Read();
        Assert.That(newIdResult.CurrentRow[0].AsInt64(), Is.EqualTo(1));
    }

    [Test]
    public void TruncateClearsSecondaryIndexesTest()
    {
        // TRUNCATE the Orders table (has indexes)
        m_engine.Execute("TRUNCATE TABLE Orders");

        // Verify all rows removed
        var afterResult = m_engine.Execute("SELECT COUNT(*) FROM Orders");
        afterResult.Read();
        Assert.That(afterResult.CurrentRow[0].AsInt64(), Is.EqualTo(0));

        // Insert new rows - indexes should work correctly
        m_engine.Execute("INSERT INTO Orders (ProductId, Quantity, Status) VALUES (1, 2, 'new')");
        
        // Query using indexed column should work
        var queryResult = m_engine.Execute("SELECT * FROM Orders WHERE Status = 'new'");
        var rows = queryResult.ReadAll();
        Assert.That(rows.Count, Is.EqualTo(1));
    }

    [Test]
    public void TruncateNonExistentTableThrowsTest()
    {
        Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("TRUNCATE TABLE NonExistentTable"));
    }

    [Test]
    public void TruncateEmptyTableSucceedsTest()
    {
        // First truncate to make sure it's empty
        m_engine.Execute("TRUNCATE TABLE Products");

        // Truncate again - should not throw
        Assert.DoesNotThrow(() =>
            m_engine.Execute("TRUNCATE TABLE Products"));
    }

    #endregion

    #region MERGE Tests - Basic Operations

    [Test]
    public void MergeWhenMatchedUpdateTest()
    {
        // Add source data
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget Pro', 39.99, 150)");

        // MERGE - update matching row
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(1));

        // Verify update
        var result = m_engine.Execute("SELECT Name, Price, Stock FROM Products WHERE Sku = 'SKU001'");
        result.Read();
        Assert.That(result.CurrentRow[0].AsString(), Is.EqualTo("Widget Pro"));
        Assert.That(result.CurrentRow[1].AsDecimal(), Is.EqualTo(39.99m));
        Assert.That(result.CurrentRow[2].AsInt64(), Is.EqualTo(150));
    }

    [Test]
    public void MergeWhenNotMatchedInsertTest()
    {
        // Add source data - one existing, one new
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget', 29.99, 100)");  // Exists
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU004', 'NewItem', 79.99, 25)");  // New

        // MERGE - insert non-matching rows
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN NOT MATCHED THEN
                INSERT (Sku, Name, Price, Stock) VALUES (s.Sku, s.Name, s.Price, s.Stock)");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(1)); // Only SKU004 inserted

        // Verify new row inserted
        var result = m_engine.Execute("SELECT * FROM Products WHERE Sku = 'SKU004'");
        result.Read();
        Assert.That(result.CurrentRow["Name"].AsString(), Is.EqualTo("NewItem"));
        Assert.That(result.CurrentRow["Price"].AsDecimal(), Is.EqualTo(79.99m));
        Assert.That(result.CurrentRow["Stock"].AsInt64(), Is.EqualTo(25));
    }

    [Test]
    public void MergeWhenMatchedDeleteTest()
    {
        // Add source data indicating items to delete
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU003', 'Gizmo', 0, 0)");

        // MERGE - delete matching rows
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED THEN DELETE");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(1));

        // Verify row deleted
        var result = m_engine.Execute("SELECT COUNT(*) FROM Products WHERE Sku = 'SKU003'");
        result.Read();
        Assert.That(result.CurrentRow[0].AsInt64(), Is.EqualTo(0));

        // Other rows should remain
        var totalResult = m_engine.Execute("SELECT COUNT(*) FROM Products");
        totalResult.Read();
        Assert.That(totalResult.CurrentRow[0].AsInt64(), Is.EqualTo(2));
    }

    #endregion

    #region MERGE Tests - Complex Conditions

    [Test]
    public void MergeWhenMatchedUpdateWithConditionTest()
    {
        // Add source data - one match with low stock, one with high stock
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget New', 35.00, 10)");  // Stock < current
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU002', 'Gadget New', 55.00, 200)"); // Stock > current

        // MERGE - only update if source stock > target stock
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND s.Stock > t.Stock THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(1)); // Only SKU002 updated

        // SKU001 should be unchanged
        var result1 = m_engine.Execute("SELECT Name, Stock FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsString(), Is.EqualTo("Widget"));

        // SKU002 should be updated
        var result2 = m_engine.Execute("SELECT Name, Stock FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsString(), Is.EqualTo("Gadget New"));
        Assert.That(result2.CurrentRow[1].AsInt64(), Is.EqualTo(200));
    }

    [Test]
    public void MergeWithComplexAndConditionTest()
    {
        // Add source data with various scenarios
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU001', 'Widget Premium', 45.00, 150, 'Hardware')");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU002', 'Gadget Pro', 75.00, 80, 'Electronics')");

        // MERGE - only update if stock increased AND price increased
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND s.Stock > t.Stock AND s.Price > t.Price THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(2)); // Both match conditions

        // Verify both updated
        var result1 = m_engine.Execute("SELECT Name, Price, Stock FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsString(), Is.EqualTo("Widget Premium"));
        Assert.That(result1.CurrentRow[1].AsDecimal(), Is.EqualTo(45.00m));
        Assert.That(result1.CurrentRow[2].AsInt64(), Is.EqualTo(150));

        var result2 = m_engine.Execute("SELECT Name, Price, Stock FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsString(), Is.EqualTo("Gadget Pro"));
        Assert.That(result2.CurrentRow[1].AsDecimal(), Is.EqualTo(75.00m));
        Assert.That(result2.CurrentRow[2].AsInt64(), Is.EqualTo(80));
    }

    [Test]
    public void MergeWithOrConditionTest()
    {
        // Add source data
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU001', 'Widget Lite', 15.00, 50, 'Hardware')"); // Price < current
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU002', 'Gadget New', 49.99, 100, 'Electronics')"); // Stock > current
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU003', 'Gizmo Same', 19.99, 200, 'Hardware')"); // Neither

        // MERGE - update if price decreased OR stock increased
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND (s.Price < t.Price OR s.Stock > t.Stock) THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(2)); // SKU001 (price <), SKU002 (stock >), NOT SKU003

        // SKU001 - updated (price decreased)
        var result1 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsString(), Is.EqualTo("Widget Lite"));

        // SKU002 - updated (stock increased)
        var result2 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsString(), Is.EqualTo("Gadget New"));

        // SKU003 - NOT updated (neither condition)
        var result3 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU003'");
        result3.Read();
        Assert.That(result3.CurrentRow[0].AsString(), Is.EqualTo("Gizmo"));
    }

    [Test]
    public void MergeWithComparisonExpressionTest()
    {
        // Add source data
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget Bulk', 25.00, 300)");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU002', 'Gadget Single', 60.00, 30)");

        // MERGE - only update if source has at least double the stock
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND s.Stock >= t.Stock * 2 THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(1)); // Only SKU001 (300 >= 100*2)

        // SKU001 - updated (300 >= 200)
        var result1 = m_engine.Execute("SELECT Name, Stock FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsString(), Is.EqualTo("Widget Bulk"));

        // SKU002 - NOT updated (30 < 100)
        var result2 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsString(), Is.EqualTo("Gadget"));
    }

    [Test]
    public void MergeWithCategoryMatchConditionTest()
    {
        // Add source data
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU001', 'Widget New', 35.00, 120, 'Hardware')");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock, Category) VALUES ('SKU002', 'Gadget New', 55.00, 80, 'Software')"); // Category mismatch

        // MERGE - only update if categories match
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND t.Category = s.Category THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(1)); // Only SKU001 (both Hardware)

        // SKU001 - updated (category match)
        var result1 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsString(), Is.EqualTo("Widget New"));

        // SKU002 - NOT updated (category mismatch: Electronics vs Software)
        var result2 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsString(), Is.EqualTo("Gadget"));
    }

    [Test]
    public void MergeMultipleWhenMatchedClausesWithConditionsTest()
    {
        // Add source data
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget Clearance', 10.00, 5)");   // Low stock, low price
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU002', 'Gadget Premium', 100.00, 200)");  // High stock, high price
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU003', 'Gizmo Standard', 25.00, 150)");   // Normal

        // MERGE - delete if stock < 10, update otherwise
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND s.Stock < 10 THEN
                DELETE
            WHEN MATCHED THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(3)); // 1 delete + 2 updates

        // SKU001 - deleted
        var result1 = m_engine.Execute("SELECT COUNT(*) FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsInt64(), Is.EqualTo(0));

        // SKU002 - updated
        var result2 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsString(), Is.EqualTo("Gadget Premium"));

        // SKU003 - updated
        var result3 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU003'");
        result3.Read();
        Assert.That(result3.CurrentRow[0].AsString(), Is.EqualTo("Gizmo Standard"));
    }

    #endregion

    #region MERGE Tests - Multiple WHEN Clauses

    [Test]
    public void MergeMultipleWhenClausesTest()
    {
        // Add source data - mix of existing and new
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget Updated', 34.99, 120)"); // Update
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU005', 'Brand New', 99.99, 10)");       // Insert

        // MERGE with both WHEN MATCHED and WHEN NOT MATCHED
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock
            WHEN NOT MATCHED THEN
                INSERT (Sku, Name, Price, Stock) VALUES (s.Sku, s.Name, s.Price, s.Stock)");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(2)); // 1 update + 1 insert

        // Verify update
        var updateResult = m_engine.Execute("SELECT Name, Price FROM Products WHERE Sku = 'SKU001'");
        updateResult.Read();
        Assert.That(updateResult.CurrentRow[0].AsString(), Is.EqualTo("Widget Updated"));
        Assert.That(updateResult.CurrentRow[1].AsDecimal(), Is.EqualTo(34.99m));

        // Verify insert
        var insertResult = m_engine.Execute("SELECT Name, Price FROM Products WHERE Sku = 'SKU005'");
        var insertRows = insertResult.ReadAll();
        Assert.That(insertRows.Count, Is.EqualTo(1));
        Assert.That(insertRows[0][0].AsString(), Is.EqualTo("Brand New"));
    }

    [Test]
    public void MergeWithSubquerySourceTest()
    {
        // Create a staging table
        m_engine.Execute(@"
            CREATE TABLE StagingProducts (
                Sku TEXT NOT NULL,
                Name TEXT NOT NULL,
                Price DECIMAL(10,2),
                Stock INTEGER,
                IsActive BOOLEAN DEFAULT TRUE
            )");

        m_engine.Execute("INSERT INTO StagingProducts (Sku, Name, Price, Stock, IsActive) VALUES ('SKU001', 'Widget V2', 31.99, 110, TRUE)");
        m_engine.Execute("INSERT INTO StagingProducts (Sku, Name, Price, Stock, IsActive) VALUES ('SKU006', 'Inactive', 0, 0, FALSE)");
        m_engine.Execute("INSERT INTO StagingProducts (Sku, Name, Price, Stock, IsActive) VALUES ('SKU007', 'Active New', 44.99, 30, TRUE)");
        m_engine.Execute("INSERT INTO StagingProducts (Sku, Name, Price, Stock, IsActive) VALUES ('SKU008', 'Another Widget', 35.00, 200, TRUE)");

        // MERGE using subquery - only active products
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING (SELECT Sku, Name, Price, Stock FROM StagingProducts WHERE IsActive = TRUE) AS s
            ON t.Sku = s.Sku
            WHEN MATCHED THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock
            WHEN NOT MATCHED THEN
                INSERT (Sku, Name, Price, Stock) VALUES (s.Sku, s.Name, s.Price, s.Stock)");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(3)); // SKU001 update, SKU007 insert, SKU008 insert

        // Verify SKU001 updated
        var result1 = m_engine.Execute("SELECT Name FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsString(), Is.EqualTo("Widget V2"));

        // Verify SKU007 inserted
        var result2 = m_engine.Execute("SELECT COUNT(*) FROM Products WHERE Sku = 'SKU007'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsInt64(), Is.EqualTo(1));

        // Verify SKU008 inserted
        var result3 = m_engine.Execute("SELECT COUNT(*) FROM Products WHERE Sku = 'SKU008'");
        result3.Read();
        Assert.That(result3.CurrentRow[0].AsInt64(), Is.EqualTo(1));

        // Verify SKU006 NOT inserted (IsActive = FALSE)
        var result4 = m_engine.Execute("SELECT COUNT(*) FROM Products WHERE Sku = 'SKU006'");
        result4.Read();
        Assert.That(result4.CurrentRow[0].AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void MergeWithSubqueryAndComplexConditionTest()
    {
        // Create a staging table with aggregated data
        m_engine.Execute(@"
            CREATE TABLE SalesData (
                Sku TEXT NOT NULL,
                TotalSold INTEGER DEFAULT 0,
                Revenue DECIMAL(10,2) DEFAULT 0
            )");

        m_engine.Execute("INSERT INTO SalesData (Sku, TotalSold, Revenue) VALUES ('SKU001', 500, 15000.00)"); // High sales
        m_engine.Execute("INSERT INTO SalesData (Sku, TotalSold, Revenue) VALUES ('SKU002', 100, 5000.00)");  // Low sales
        m_engine.Execute("INSERT INTO SalesData (Sku, TotalSold, Revenue) VALUES ('SKU003', 300, 6000.00)");  // Medium sales

        // MERGE - only increase stock for products with high sales (TotalSold > 200)
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING SalesData AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND s.TotalSold > 200 THEN
                UPDATE SET Stock = t.Stock + 100");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(2)); // SKU001 and SKU003

        // SKU001 - stock increased
        var result1 = m_engine.Execute("SELECT Stock FROM Products WHERE Sku = 'SKU001'");
        result1.Read();
        Assert.That(result1.CurrentRow[0].AsInt64(), Is.EqualTo(200)); // 100 + 100

        // SKU002 - stock unchanged (TotalSold = 100)
        var result2 = m_engine.Execute("SELECT Stock FROM Products WHERE Sku = 'SKU002'");
        result2.Read();
        Assert.That(result2.CurrentRow[0].AsInt64(), Is.EqualTo(50)); // unchanged

        // SKU003 - stock increased
        var result3 = m_engine.Execute("SELECT Stock FROM Products WHERE Sku = 'SKU003'");
        result3.Read();
        Assert.That(result3.CurrentRow[0].AsInt64(), Is.EqualTo(300)); // 200 + 100
    }

    #endregion

    #region MERGE Tests - Error Cases

    [Test]
    public void MergeTargetTableNotFoundThrowsTest()
    {
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget', 29.99, 100)");

        Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute(@"
                MERGE INTO NonExistentTable AS t
                USING ProductUpdates AS s
                ON t.Sku = s.Sku
                WHEN MATCHED THEN
                    UPDATE SET Name = s.Name"));
    }

    [Test]
    public void MergeNoMatchingWhenClauseTest()
    {
        // Add source data that exists
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget', 29.99, 100)");

        // MERGE with only WHEN NOT MATCHED - existing row won't be affected
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN NOT MATCHED THEN
                INSERT (Sku, Name, Price, Stock) VALUES (s.Sku, s.Name, s.Price, s.Stock)");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(0)); // No changes - row exists
    }

    #endregion

    #region Integration Tests

    [Test]
    public void MergeWithComputedColumnsTest()
    {
        // Create table with computed column (separate table, no conflict with existing Products)
        m_engine.Execute(@"
            CREATE TABLE ProductsWithComputed (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT UNIQUE NOT NULL,
                Name TEXT NOT NULL,
                Price DECIMAL(10,2) DEFAULT 0,
                Stock INTEGER DEFAULT 0,
                TotalValue AS (Price * Stock) STORED
            )");

        // Insert initial data
        m_engine.Execute("INSERT INTO ProductsWithComputed (Sku, Name, Price, Stock) VALUES ('COMP001', 'Widget', 10.00, 100)");

        // Verify computed column
        var initial = m_engine.Execute("SELECT TotalValue FROM ProductsWithComputed WHERE Sku = 'COMP001'");
        initial.Read();
        Assert.That(initial.CurrentRow[0].AsDecimal(), Is.EqualTo(1000.00m));

        // Create source for MERGE
        m_engine.Execute(@"
            CREATE TABLE ComputedUpdates (
                Sku TEXT NOT NULL,
                Name TEXT,
                Price DECIMAL(10,2),
                Stock INTEGER
            )");
        m_engine.Execute("INSERT INTO ComputedUpdates (Sku, Name, Price, Stock) VALUES ('COMP001', 'Widget', 20.00, 50)");

        // MERGE update - computed column should recalculate
        m_engine.Execute(@"
            MERGE INTO ProductsWithComputed AS t
            USING ComputedUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED THEN
                UPDATE SET Price = s.Price, Stock = s.Stock");

        // Verify computed column was recalculated
        var updated = m_engine.Execute("SELECT TotalValue FROM ProductsWithComputed WHERE Sku = 'COMP001'");
        updated.Read();
        Assert.That(updated.CurrentRow[0].AsDecimal(), Is.EqualTo(1000.00m)); // 20 * 50 = 1000
    }

    [Test]
    public void TruncateAndMergeSequenceTest()
    {
        // Truncate existing Products data from Setup
        m_engine.Execute("TRUNCATE TABLE Products");

        // Verify empty
        var countBefore = m_engine.Execute("SELECT COUNT(*) FROM Products");
        countBefore.Read();
        Assert.That(countBefore.CurrentRow[0].AsInt64(), Is.EqualTo(0));

        // Clear ProductUpdates and add source data for MERGE
        m_engine.Execute("TRUNCATE TABLE ProductUpdates");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('NEW001', 'New Widget', 35.00, 200)");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('NEW002', 'Gizmo', 15.00, 500)");

        // MERGE into empty table - all should be inserts
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN NOT MATCHED THEN
                INSERT (Sku, Name, Price, Stock) VALUES (s.Sku, s.Name, s.Price, s.Stock)");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(2));

        // Verify Products table
        var rows = m_engine.Query("SELECT Sku, Name FROM Products ORDER BY Sku");
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows[0]["Sku"].AsString(), Is.EqualTo("NEW001"));
        Assert.That(rows[1]["Sku"].AsString(), Is.EqualTo("NEW002"));
    }

    [Test]
    public void UpsertWithReturningAndSubsequentSelectTest()
    {
        // SKU001 already exists from InsertTestData(), so UPSERT will update it
        var upsertResult = m_engine.Execute(@"
            INSERT INTO Products (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget Pro', 39.99, 200)
            ON CONFLICT (Sku) DO UPDATE SET Name = EXCLUDED.Name, Price = EXCLUDED.Price, Stock = EXCLUDED.Stock
            RETURNING Id, Sku, Name, Price, Stock");

        var rows = upsertResult.ReadAll();
        Assert.That(rows.Count, Is.EqualTo(1));
        var id = rows[0]["Id"].AsInt64();
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Widget Pro"));

        // Verify with subsequent SELECT
        var selectResult = m_engine.Query($"SELECT * FROM Products WHERE Id = {id}");
        Assert.That(selectResult.Count, Is.EqualTo(1));
        Assert.That(selectResult[0]["Name"].AsString(), Is.EqualTo("Widget Pro"));
        Assert.That(selectResult[0]["Price"].AsDecimal(), Is.EqualTo(39.99m));
    }

    [Test]
    public void MergeAllActionsInOneStatementTest()
    {
        // Clear Products and set up fresh data
        m_engine.Execute("TRUNCATE TABLE Products");
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock, Category) VALUES ('ACT001', 'ToUpdate', 10.00, 100, 'Hardware')");
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock, Category) VALUES ('ACT002', 'ToDelete', 20.00, 0, 'Hardware')");
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock, Category) VALUES ('ACT003', 'ToKeep', 30.00, 50, 'Hardware')");

        // Clear ProductUpdates and set up source data
        m_engine.Execute("TRUNCATE TABLE ProductUpdates");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('ACT001', 'Updated', 15.00, 150)");
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('ACT002', 'DeleteMe', 0, 0)"); // Stock = 0 -> delete
        m_engine.Execute("INSERT INTO ProductUpdates (Sku, Name, Price, Stock) VALUES ('ACT004', 'NewProduct', 25.00, 75)"); // New

        // MERGE with all action types
        var mergeResult = m_engine.Execute(@"
            MERGE INTO Products AS t
            USING ProductUpdates AS s
            ON t.Sku = s.Sku
            WHEN MATCHED AND s.Stock = 0 THEN
                DELETE
            WHEN MATCHED THEN
                UPDATE SET Name = s.Name, Price = s.Price, Stock = s.Stock
            WHEN NOT MATCHED THEN
                INSERT (Sku, Name, Price, Stock) VALUES (s.Sku, s.Name, s.Price, s.Stock)");

        Assert.That(mergeResult.RowsAffected, Is.EqualTo(3)); // 1 update + 1 delete + 1 insert

        // Verify results
        var products = m_engine.Query("SELECT Sku, Name, Stock FROM Products ORDER BY Sku");
        Assert.That(products.Count, Is.EqualTo(3)); // ACT001 updated, ACT002 deleted, ACT003 unchanged, ACT004 inserted

        // ACT001 - updated
        Assert.That(products[0]["Sku"].AsString(), Is.EqualTo("ACT001"));
        Assert.That(products[0]["Name"].AsString(), Is.EqualTo("Updated"));
        Assert.That(products[0]["Stock"].AsInt64(), Is.EqualTo(150));

        // ACT003 - unchanged (was not in source)
        Assert.That(products[1]["Sku"].AsString(), Is.EqualTo("ACT003"));
        Assert.That(products[1]["Name"].AsString(), Is.EqualTo("ToKeep"));

        // ACT004 - inserted
        Assert.That(products[2]["Sku"].AsString(), Is.EqualTo("ACT004"));
        Assert.That(products[2]["Name"].AsString(), Is.EqualTo("NewProduct"));
    }

    #endregion
}
