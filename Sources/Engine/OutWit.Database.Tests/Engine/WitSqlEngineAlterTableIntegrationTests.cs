using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests;

/// <summary>
/// Integration tests for ALTER TABLE operations with real database (not mock).
/// Tests persistence, complex scenarios, and edge cases.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineAlterTableIntegrationTests
{
    #region Fields

    private string m_testDbPath = null!;
    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"witdb_test_{Guid.NewGuid():N}");
        
        // Ensure directory doesn't exist
        if (Directory.Exists(m_testDbPath))
        {
            Directory.Delete(m_testDbPath, recursive: true);
        }
        
        var database = WitDatabase.Create(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
        m_engine = null!;
        
        // Wait a bit for file handles to be released
        Thread.Sleep(100);
        
        if (Directory.Exists(m_testDbPath))
        {
            try
            {
                Directory.Delete(m_testDbPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors - files may be locked
            }
        }
    }

    #endregion

    #region Constraint Persistence Tests

    [Test]
    public void ConstraintsSurviveEngineRestartTest()
    {
        // Create table and add constraints
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255), Age INT)");
        m_engine.Execute("INSERT INTO Users (Id, Email, Age) VALUES (1, 'test@test.com', 25)");
        
        // Add CHECK constraint
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT CHK_Age CHECK (Age >= 0)");
        
        // Dispose and recreate engine
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify constraint still exists
        var table = m_engine.GetTable("Users");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
        Assert.That(table.NamedConstraints![0].Name, Is.EqualTo("CHK_Age"));
        
        // Verify data persisted
        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("test@test.com"));
        
        // Verify CHECK constraint is enforced after restart
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Users (Id, Email, Age) VALUES (2, 'other@test.com', -5)")); // Negative age
        Assert.That(ex!.Message, Does.Contain("CHECK"));
    }

    [Test]
    public void UniqueConstraintSurvivesEngineRestartTest()
    {
        // Create table and add UNIQUE constraint
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (1, 'test@test.com')");
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)");
        
        // Dispose and recreate engine
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify constraint still exists
        var table = m_engine.GetTable("Users");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
        Assert.That(table.NamedConstraints![0].Name, Is.EqualTo("UQ_Email"));
        Assert.That(table.NamedConstraints![0].Type, Is.EqualTo(ConstraintType.Unique));
        
        // Verify UNIQUE constraint is enforced after restart
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (2, 'test@test.com')")); // Duplicate email
        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    [Test]
    public void ForeignKeyConstraintPersistsTest()
    {
        m_engine.Execute("CREATE TABLE Categories (Id INT PRIMARY KEY, Name VARCHAR(100))");
        m_engine.Execute("INSERT INTO Categories (Id, Name) VALUES (1, 'Electronics')");
        
        m_engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, CategoryId INT, Name VARCHAR(100))");
        m_engine.Execute("INSERT INTO Products (Id, CategoryId, Name) VALUES (1, 1, 'Phone')");
        
        m_engine.Execute(@"ALTER TABLE Products ADD CONSTRAINT FK_Products_Categories 
            FOREIGN KEY (CategoryId) REFERENCES Categories (Id)");
        
        // Restart engine
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify FK constraint persisted
        var table = m_engine.GetTable("Products");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
        Assert.That(table.NamedConstraints![0].Type, Is.EqualTo(ConstraintType.ForeignKey));
        
        // Verify FK is enforced after restart
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Products (Id, CategoryId, Name) VALUES (2, 999, 'Invalid')"));
        Assert.That(ex!.Message, Does.Contain("FOREIGN KEY"));
    }

    #endregion

    #region Computed Column Persistence Tests

    [Test]
    public void StoredComputedColumnPersistsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                Id INT PRIMARY KEY,
                Quantity INT,
                UnitPrice DECIMAL,
                TotalPrice AS (Quantity * UnitPrice) STORED
            )");
        
        m_engine.Execute("INSERT INTO OrderItems (Id, Quantity, UnitPrice) VALUES (1, 5, 10.00)");
        m_engine.Execute("INSERT INTO OrderItems (Id, Quantity, UnitPrice) VALUES (2, 3, 25.00)");
        
        // Restart engine
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify computed values persisted
        var rows = m_engine.Query("SELECT * FROM OrderItems ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["TotalPrice"].AsDecimal(), Is.EqualTo(50.00m));
        Assert.That(rows[1]["TotalPrice"].AsDecimal(), Is.EqualTo(75.00m));
        
        // Verify column metadata persisted
        var table = m_engine.GetTable("OrderItems");
        var computedCol = table!.Columns.FirstOrDefault(c => c.Name == "TotalPrice");
        Assert.That(computedCol, Is.Not.Null);
        Assert.That(computedCol!.IsComputed, Is.True);
        Assert.That(computedCol.IsStored, Is.True);
    }

    [Test]
    public void VirtualComputedColumnMetadataPersistsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Employees (
                Id INT PRIMARY KEY,
                FirstName VARCHAR(100),
                LastName VARCHAR(100),
                FullName AS (FirstName || ' ' || LastName) VIRTUAL
            )");
        
        m_engine.Execute("INSERT INTO Employees (Id, FirstName, LastName) VALUES (1, 'John', 'Doe')");
        
        // Restart engine
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify virtual column is still evaluated correctly
        var rows = m_engine.Query("SELECT * FROM Employees WHERE Id = 1");
        Assert.That(rows[0]["FullName"].AsString(), Is.EqualTo("John Doe"));
        
        // Verify column metadata persisted
        var table = m_engine.GetTable("Employees");
        var computedCol = table!.Columns.FirstOrDefault(c => c.Name == "FullName");
        Assert.That(computedCol, Is.Not.Null);
        Assert.That(computedCol!.IsComputed, Is.True);
        Assert.That(computedCol.IsStored, Is.False);
    }

    #endregion

    #region ADD COLUMN with DEFAULT Persistence Tests

    [Test]
    public void AddColumnWithDefaultPersistsTest()
    {
        m_engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, Name VARCHAR(200))");
        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");
        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (2, 'Gadget')");
        
        m_engine.Execute("ALTER TABLE Products ADD COLUMN Price DECIMAL DEFAULT 9.99");
        
        // Restart engine
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify default values persisted
        var rows = m_engine.Query("SELECT * FROM Products ORDER BY Id");
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(9.99m));
        Assert.That(rows[1]["Price"].AsDecimal(), Is.EqualTo(9.99m));
        
        // Verify new inserts use default
        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (3, 'Tool')");
        rows = m_engine.Query("SELECT * FROM Products WHERE Id = 3");
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(9.99m));
    }

    #endregion

    #region Complex Scenario Tests

    [Test]
    public void CompleteEfCoreMigrationScenarioTest()
    {
        // Migration 1: Create initial schema
        m_engine.Execute(@"
            CREATE TABLE Categories (
                Id INT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL
            )");
        
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(200) NOT NULL,
                CategoryId INT
            )");
        
        // Insert initial data
        m_engine.Execute("INSERT INTO Categories (Name) VALUES ('Electronics')");
        m_engine.Execute("INSERT INTO Categories (Name) VALUES ('Books')");
        m_engine.Execute("INSERT INTO Products (Name, CategoryId) VALUES ('Phone', 1)");
        m_engine.Execute("INSERT INTO Products (Name, CategoryId) VALUES ('Laptop', 1)");
        
        // Migration 2: Add Price column with default
        m_engine.Execute("ALTER TABLE Products ADD COLUMN Price DECIMAL DEFAULT 0");
        
        // Migration 3: Add foreign key constraint
        m_engine.Execute(@"ALTER TABLE Products ADD CONSTRAINT FK_Products_Categories 
            FOREIGN KEY (CategoryId) REFERENCES Categories (Id)");
        
        // Migration 4: Add computed column for display
        m_engine.Execute("ALTER TABLE Products ADD COLUMN DisplayName AS (Name || ' ($' || Price || ')') STORED");
        
        // Migration 5: Add check constraint for price
        m_engine.Execute("ALTER TABLE Products ADD CONSTRAINT CHK_Price CHECK (Price >= 0)");
        
        // Restart engine (simulate app restart)
        m_engine.Dispose();
        var database = WitDatabase.Open(m_testDbPath);
        m_engine = new WitSqlEngine(database, ownsStore: true);
        
        // Verify everything works
        var products = m_engine.Query("SELECT * FROM Products ORDER BY Id");
        Assert.That(products, Has.Count.EqualTo(2));
        Assert.That(products[0]["DisplayName"].AsString(), Is.EqualTo("Phone ($0)"));
        
        // Update price and verify computed column updates
        m_engine.Execute("UPDATE Products SET Price = 999.99 WHERE Name = 'Phone'");
        products = m_engine.Query("SELECT * FROM Products WHERE Name = 'Phone'");
        Assert.That(products[0]["DisplayName"].AsString(), Is.EqualTo("Phone ($999.99)"));
        
        // Verify FK constraint works after restart
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Products (Name, CategoryId, Price) VALUES ('NewProduct', 999, 10)")); // Invalid FK
        Assert.That(ex!.Message, Does.Contain("FOREIGN KEY"));
        
        // Verify CHECK constraint works after restart
        ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Products (Name, CategoryId, Price) VALUES ('BadProduct', 1, -10)")); // Negative price
        Assert.That(ex!.Message, Does.Contain("CHECK"));
    }

    [Test]
    public void InsertSelectWithComputedColumnsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Source (
                Id INT PRIMARY KEY,
                Quantity INT,
                Price DECIMAL
            )");
        
        m_engine.Execute(@"
            CREATE TABLE Target (
                Id INT PRIMARY KEY,
                Quantity INT,
                Price DECIMAL,
                Total AS (Quantity * Price) STORED
            )");
        
        m_engine.Execute("INSERT INTO Source (Id, Quantity, Price) VALUES (1, 5, 10.00)");
        m_engine.Execute("INSERT INTO Source (Id, Quantity, Price) VALUES (2, 3, 20.00)");
        
        // INSERT ... SELECT should calculate computed columns
        m_engine.Execute("INSERT INTO Target (Id, Quantity, Price) SELECT Id, Quantity, Price FROM Source");
        
        var rows = m_engine.Query("SELECT * FROM Target ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Total"].AsDecimal(), Is.EqualTo(50.00m));
        Assert.That(rows[1]["Total"].AsDecimal(), Is.EqualTo(60.00m));
    }

    [Test]
    public void UpdateComputedColumnThrowsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Test (
                Id INT PRIMARY KEY,
                Value INT,
                Doubled AS (Value * 2) STORED
            )");
        
        m_engine.Execute("INSERT INTO Test (Id, Value) VALUES (1, 5)");
        
        // Direct UPDATE on computed column should fail
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("UPDATE Test SET Doubled = 20 WHERE Id = 1"));
        
        Assert.That(ex!.Message, Does.Contain("computed column"));
    }

    [Test]
    public void MultipleConstraintTypesOnSameTableTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id INT PRIMARY KEY,
                CustomerId INT,
                ProductId INT,
                Quantity INT,
                TotalPrice DECIMAL
            )");
        
        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Customers (Id) VALUES (1)");
        
        // Add CHECK constraints
        m_engine.Execute("ALTER TABLE Orders ADD CONSTRAINT CHK_Quantity CHECK (Quantity > 0)");
        m_engine.Execute("ALTER TABLE Orders ADD CONSTRAINT CHK_TotalPrice CHECK (TotalPrice >= 0)");
        
        // Add FK constraint
        m_engine.Execute(@"ALTER TABLE Orders ADD CONSTRAINT FK_Orders_Customers 
            FOREIGN KEY (CustomerId) REFERENCES Customers (Id)");
        
        var table = m_engine.GetTable("Orders");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(3));
        
        // Insert valid data
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, ProductId, Quantity, TotalPrice) VALUES (1, 1, 1, 5, 100)");
        
        // Violate CHECK (Quantity)
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Orders (Id, CustomerId, ProductId, Quantity, TotalPrice) VALUES (2, 1, 1, 0, 100)"));
        Assert.That(ex!.Message, Does.Contain("CHECK"));
        
        // Verify data integrity - only one row inserted
        var rows = m_engine.Query("SELECT * FROM Orders");
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void AddColumnToEmptyTableTest()
    {
        m_engine.Execute("CREATE TABLE Empty (Id INT PRIMARY KEY)");
        m_engine.Execute("ALTER TABLE Empty ADD COLUMN Name VARCHAR(100) DEFAULT 'default'");
        
        var table = m_engine.GetTable("Empty");
        Assert.That(table!.Columns, Has.Count.EqualTo(2));
        
        // New insert should use default
        m_engine.Execute("INSERT INTO Empty (Id) VALUES (1)");
        var rows = m_engine.Query("SELECT * FROM Empty");
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("default"));
    }

    [Test]
    public void DropConstraintAndReaddTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY, Value INT)");
        m_engine.Execute("ALTER TABLE Test ADD CONSTRAINT CHK_Value CHECK (Value > 0)");
        
        // Insert valid data
        m_engine.Execute("INSERT INTO Test (Id, Value) VALUES (1, 10)");
        
        // Drop constraint
        m_engine.Execute("ALTER TABLE Test DROP CONSTRAINT CHK_Value");
        
        // Now we can insert invalid data
        m_engine.Execute("INSERT INTO Test (Id, Value) VALUES (2, -5)");
        
        // Re-add constraint should fail due to existing invalid data
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("ALTER TABLE Test ADD CONSTRAINT CHK_Value CHECK (Value > 0)"));
        
        Assert.That(ex!.Message, Does.Contain("violated"));
    }

    [Test]
    public void ComputedColumnWithNullValuesTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Test (
                Id INT PRIMARY KEY,
                A INT,
                B INT,
                Sum AS (COALESCE(A, 0) + COALESCE(B, 0)) STORED
            )");
        
        m_engine.Execute("INSERT INTO Test (Id, A, B) VALUES (1, 10, 20)");
        m_engine.Execute("INSERT INTO Test (Id, A, B) VALUES (2, 10, NULL)");
        m_engine.Execute("INSERT INTO Test (Id, A, B) VALUES (3, NULL, NULL)");
        
        var rows = m_engine.Query("SELECT * FROM Test ORDER BY Id");
        Assert.That(rows[0]["Sum"].AsInt64(), Is.EqualTo(30));
        Assert.That(rows[1]["Sum"].AsInt64(), Is.EqualTo(10));
        Assert.That(rows[2]["Sum"].AsInt64(), Is.EqualTo(0));
    }

    [Test]
    public void LargeDataSetComputedColumnTest()
    {
        m_engine.Execute(@"
            CREATE TABLE LargeTable (
                Id INT PRIMARY KEY,
                Value INT,
                Computed AS (Value * 2) STORED
            )");
        
        // Insert many rows
        for (int i = 1; i <= 1000; i++)
        {
            m_engine.Execute($"INSERT INTO LargeTable (Id, Value) VALUES ({i}, {i})");
        }
        
        // Verify all computed values are correct
        var rows = m_engine.Query("SELECT * FROM LargeTable ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(1000));
        
        for (int i = 0; i < rows.Count; i++)
        {
            var expectedValue = (i + 1) * 2;
            Assert.That(rows[i]["Computed"].AsInt64(), Is.EqualTo(expectedValue), $"Row {i + 1} has wrong computed value");
        }
    }

    #endregion

    #region VIRTUAL Computed Column with Index Tests

    [Test]
    public void VirtualComputedColumnWorksWithIndexSeekTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(200),
                Price DECIMAL,
                NameUpper AS (UPPER(Name)) VIRTUAL
            )");
        
        m_engine.Execute("INSERT INTO Products (Id, Name, Price) VALUES (1, 'Apple', 1.50)");
        m_engine.Execute("INSERT INTO Products (Id, Name, Price) VALUES (2, 'Banana', 2.00)");
        m_engine.Execute("INSERT INTO Products (Id, Name, Price) VALUES (3, 'Cherry', 3.50)");
        
        // Create index on regular column
        m_engine.Execute("CREATE INDEX IX_Products_Price ON Products (Price)");
        
        // Query using index - VIRTUAL column should still be evaluated
        var rows = m_engine.Query("SELECT * FROM Products WHERE Price = 2.00");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Banana"));
        Assert.That(rows[0]["NameUpper"].AsString(), Is.EqualTo("BANANA"));
    }

    [Test]
    public void VirtualComputedColumnWorksWithIndexRangeScanTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(200),
                Price DECIMAL,
                DoubledPrice AS (Price * 2) VIRTUAL
            )");
        
        m_engine.Execute("INSERT INTO Products (Id, Name, Price) VALUES (1, 'Apple', 1.00)");
        m_engine.Execute("INSERT INTO Products (Id, Name, Price) VALUES (2, 'Banana', 2.00)");
        m_engine.Execute("INSERT INTO Products (Id, Name, Price) VALUES (3, 'Cherry', 3.00)");
        
        // Create index
        m_engine.Execute("CREATE INDEX IX_Products_Price ON Products (Price)");
        
        // Range query - VIRTUAL column should be evaluated for all results
        var rows = m_engine.Query("SELECT * FROM Products WHERE Price >= 1.50 AND Price <= 2.50 ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Banana"));
        Assert.That(rows[0]["DoubledPrice"].AsDecimal(), Is.EqualTo(4.00m));
    }

    #endregion
}
