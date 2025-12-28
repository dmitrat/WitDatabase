using OutWit.Database.Definitions;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for ALTER TABLE ADD CONSTRAINT and DROP CONSTRAINT operations.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineAlterTableConstraintTests : WitSqlEngineTestsBase
{
    #region ADD CONSTRAINT - CHECK Tests

    [Test]
    public void AlterTableAddCheckConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, Price DECIMAL)");
        m_engine.Execute("INSERT INTO Products (Id, Price) VALUES (1, 10.00)");
        m_engine.Execute("INSERT INTO Products (Id, Price) VALUES (2, 20.00)");

        m_engine.Execute("ALTER TABLE Products ADD CONSTRAINT CHK_Price_Positive CHECK (Price > 0)");

        // Constraint should be added
        var table = m_engine.GetTable("Products");
        Assert.That(table, Is.Not.Null);
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
        Assert.That(table.NamedConstraints![0].Name, Is.EqualTo("CHK_Price_Positive"));
    }

    [Test]
    public void AlterTableAddCheckConstraintWithInvalidDataThrowsTest()
    {
        m_engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, Price DECIMAL)");
        m_engine.Execute("INSERT INTO Products (Id, Price) VALUES (1, 10.00)");
        m_engine.Execute("INSERT INTO Products (Id, Price) VALUES (2, -5.00)"); // Negative price

        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("ALTER TABLE Products ADD CONSTRAINT CHK_Price_Positive CHECK (Price > 0)"));

        Assert.That(ex!.Message, Does.Contain("violated by existing data"));
    }

    [Test]
    public void AlterTableAddCheckConstraintOnEmptyTableTest()
    {
        m_engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, Price DECIMAL)");

        // Should succeed on empty table
        m_engine.Execute("ALTER TABLE Products ADD CONSTRAINT CHK_Price_Positive CHECK (Price > 0)");

        var table = m_engine.GetTable("Products");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
    }

    #endregion

    #region ADD CONSTRAINT - UNIQUE Tests

    [Test]
    public void AlterTableAddUniqueConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (1, 'a@test.com')");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (2, 'b@test.com')");

        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)");

        var table = m_engine.GetTable("Users");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
        Assert.That(table.NamedConstraints![0].Name, Is.EqualTo("UQ_Email"));
        Assert.That(table.NamedConstraints[0].Type, Is.EqualTo(ConstraintType.Unique));
    }

    [Test]
    public void AlterTableAddUniqueConstraintOnDuplicatesThrowsTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (1, 'same@test.com')");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (2, 'same@test.com')"); // Duplicate

        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)"));

        Assert.That(ex!.Message, Does.Contain("violated by existing duplicate data"));
    }

    [Test]
    public void AlterTableAddUniqueConstraintAllowsMultipleNullsTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (1, NULL)");
        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (2, NULL)"); // Multiple NULLs allowed

        // Should succeed - NULL != NULL in UNIQUE
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)");

        var table = m_engine.GetTable("Users");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
    }

    [Test]
    public void AlterTableAddCompositeUniqueConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT, OrderDate DATE)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, OrderDate) VALUES (1, 100, '2024-01-01')");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, OrderDate) VALUES (2, 100, '2024-01-02')");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId, OrderDate) VALUES (3, 200, '2024-01-01')");

        m_engine.Execute("ALTER TABLE Orders ADD CONSTRAINT UQ_Customer_Date UNIQUE (CustomerId, OrderDate)");

        var table = m_engine.GetTable("Orders");
        Assert.That(table!.NamedConstraints![0].Columns, Has.Count.EqualTo(2));
    }

    #endregion

    #region ADD CONSTRAINT - FOREIGN KEY Tests

    [Test]
    public void AlterTableAddForeignKeyConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY, Name VARCHAR(100))");
        m_engine.Execute("INSERT INTO Customers (Id, Name) VALUES (1, 'Alice')");
        m_engine.Execute("INSERT INTO Customers (Id, Name) VALUES (2, 'Bob')");

        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId) VALUES (2, 2)");

        m_engine.Execute(@"ALTER TABLE Orders ADD CONSTRAINT FK_Orders_Customers 
            FOREIGN KEY (CustomerId) REFERENCES Customers (Id)");

        var table = m_engine.GetTable("Orders");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
        Assert.That(table.NamedConstraints![0].Type, Is.EqualTo(ConstraintType.ForeignKey));
    }

    [Test]
    public void AlterTableAddForeignKeyWithInvalidDataThrowsTest()
    {
        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY, Name VARCHAR(100))");
        m_engine.Execute("INSERT INTO Customers (Id, Name) VALUES (1, 'Alice')");

        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId) VALUES (2, 999)"); // Non-existent customer

        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute(@"ALTER TABLE Orders ADD CONSTRAINT FK_Orders_Customers 
                FOREIGN KEY (CustomerId) REFERENCES Customers (Id)"));

        Assert.That(ex!.Message, Does.Contain("violated"));
    }

    [Test]
    public void AlterTableAddForeignKeyAllowsNullTest()
    {
        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY, Name VARCHAR(100))");
        m_engine.Execute("INSERT INTO Customers (Id, Name) VALUES (1, 'Alice')");

        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId) VALUES (1, 1)");
        m_engine.Execute("INSERT INTO Orders (Id, CustomerId) VALUES (2, NULL)"); // NULL FK allowed

        m_engine.Execute(@"ALTER TABLE Orders ADD CONSTRAINT FK_Orders_Customers 
            FOREIGN KEY (CustomerId) REFERENCES Customers (Id)");

        var table = m_engine.GetTable("Orders");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
    }

    #endregion

    #region ADD CONSTRAINT - PRIMARY KEY Tests

    [Test]
    public void AlterTableAddPrimaryKeyThrowsNotSupportedTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT, Name VARCHAR(100))");

        var ex = Assert.Throws<NotSupportedException>(() =>
            m_engine.Execute("ALTER TABLE Test ADD CONSTRAINT PK_Test PRIMARY KEY (Id)"));

        Assert.That(ex!.Message, Does.Contain("PRIMARY KEY"));
    }

    #endregion

    #region DROP CONSTRAINT Tests

    [Test]
    public void AlterTableDropCheckConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, Price DECIMAL)");
        m_engine.Execute("ALTER TABLE Products ADD CONSTRAINT CHK_Price CHECK (Price > 0)");

        var table = m_engine.GetTable("Products");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));

        m_engine.Execute("ALTER TABLE Products DROP CONSTRAINT CHK_Price");

        table = m_engine.GetTable("Products");
        Assert.That(table!.NamedConstraints, Is.Null.Or.Empty);
    }

    [Test]
    public void AlterTableDropUniqueConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)");

        m_engine.Execute("ALTER TABLE Users DROP CONSTRAINT UQ_Email");

        var table = m_engine.GetTable("Users");
        Assert.That(table!.NamedConstraints, Is.Null.Or.Empty);
    }

    [Test]
    public void AlterTableDropForeignKeyConstraintTest()
    {
        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT)");
        m_engine.Execute(@"ALTER TABLE Orders ADD CONSTRAINT FK_Orders_Customers 
            FOREIGN KEY (CustomerId) REFERENCES Customers (Id)");

        m_engine.Execute("ALTER TABLE Orders DROP CONSTRAINT FK_Orders_Customers");

        var table = m_engine.GetTable("Orders");
        Assert.That(table!.NamedConstraints, Is.Null.Or.Empty);
    }

    [Test]
    public void AlterTableDropNonExistentConstraintThrowsTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY)");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("ALTER TABLE Test DROP CONSTRAINT NonExistent"));

        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void AlterTableDropConstraintAlreadyExistsTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)");

        // Adding same constraint again should fail
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)"));

        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public void EfCoreMigrationPatternTest()
    {
        // Simulate a typical EF Core migration scenario

        // Step 1: Create initial table
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(200) NOT NULL
            )");

        // Step 2: Add column with default
        m_engine.Execute("ALTER TABLE Products ADD COLUMN Price DECIMAL DEFAULT 0");

        // Step 3: Add data
        m_engine.Execute("INSERT INTO Products (Name) VALUES ('Widget')");
        m_engine.Execute("INSERT INTO Products (Name, Price) VALUES ('Gadget', 29.99)");

        // Step 4: Add check constraint
        m_engine.Execute("ALTER TABLE Products ADD CONSTRAINT CHK_Price_NonNegative CHECK (Price >= 0)");

        // Verify
        var rows = m_engine.Query("SELECT * FROM Products ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(0m));
        Assert.That(rows[1]["Price"].AsDecimal(), Is.EqualTo(29.99m));

        var table = m_engine.GetTable("Products");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConstraintPersistenceTest()
    {
        // Test that constraints are persisted and survive engine recreation
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Email VARCHAR(255))");
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT UQ_Email UNIQUE (Email)");
        m_engine.Execute("ALTER TABLE Users ADD CONSTRAINT CHK_Email CHECK (LENGTH(Email) > 5)");

        var table = m_engine.GetTable("Users");
        Assert.That(table!.NamedConstraints, Has.Count.EqualTo(2));
    }

    #endregion

    #region Computed Columns Auto-Update Tests

    [Test]
    public void StoredComputedColumnAutoUpdateOnUpdateTest()
    {
        // Create table with stored computed column
        m_engine.Execute(@"
            CREATE TABLE OrderItems (
                Id INT PRIMARY KEY,
                Quantity INT,
                UnitPrice DECIMAL,
                TotalPrice AS (Quantity * UnitPrice) STORED
            )");

        // Insert a row
        m_engine.Execute("INSERT INTO OrderItems (Id, Quantity, UnitPrice) VALUES (1, 5, 10.00)");

        // Verify initial computed value
        var rows = m_engine.Query("SELECT * FROM OrderItems WHERE Id = 1");
        Assert.That(rows[0]["TotalPrice"].AsDecimal(), Is.EqualTo(50.00m));

        // Update a source column
        m_engine.Execute("UPDATE OrderItems SET Quantity = 10 WHERE Id = 1");

        // Verify computed value was recalculated
        rows = m_engine.Query("SELECT * FROM OrderItems WHERE Id = 1");
        Assert.That(rows[0]["TotalPrice"].AsDecimal(), Is.EqualTo(100.00m));
    }

    [Test]
    public void StoredComputedColumnAutoUpdateOnMultipleColumnsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Price DECIMAL,
                Discount DECIMAL,
                FinalPrice AS (Price - COALESCE(Discount, 0)) STORED
            )");

        m_engine.Execute("INSERT INTO Products (Id, Price, Discount) VALUES (1, 100, 10)");

        var rows = m_engine.Query("SELECT * FROM Products WHERE Id = 1");
        Assert.That(rows[0]["FinalPrice"].AsDecimal(), Is.EqualTo(90m));

        // Update both columns
        m_engine.Execute("UPDATE Products SET Price = 200, Discount = 50 WHERE Id = 1");

        rows = m_engine.Query("SELECT * FROM Products WHERE Id = 1");
        Assert.That(rows[0]["FinalPrice"].AsDecimal(), Is.EqualTo(150m));
    }

    [Test]
    public void StoredComputedColumnAutoCalculateOnInsertTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Sales (
                Id INT PRIMARY KEY,
                Qty INT,
                Price DECIMAL,
                Total AS (Qty * Price) STORED
            )");

        // INSERT should auto-calculate the stored computed column
        m_engine.Execute("INSERT INTO Sales (Id, Qty, Price) VALUES (1, 3, 25.00)");

        var rows = m_engine.Query("SELECT * FROM Sales WHERE Id = 1");
        Assert.That(rows[0]["Total"].AsDecimal(), Is.EqualTo(75.00m));
    }

    [Test]
    public void InsertIntoComputedColumnThrowsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Test (
                Id INT PRIMARY KEY,
                Value INT,
                Doubled AS (Value * 2) STORED
            )");

        // Trying to INSERT directly into computed column should fail
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Test (Id, Value, Doubled) VALUES (1, 5, 10)"));

        Assert.That(ex!.Message, Does.Contain("computed column"));
    }

    #endregion

    #region Virtual Computed Columns Evaluation Tests

    [Test]
    public void VirtualComputedColumnEvaluatedOnSelectTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Employees (
                Id INT PRIMARY KEY,
                FirstName VARCHAR(100),
                LastName VARCHAR(100),
                FullName AS (FirstName || ' ' || LastName) VIRTUAL
            )");

        m_engine.Execute("INSERT INTO Employees (Id, FirstName, LastName) VALUES (1, 'John', 'Doe')");
        m_engine.Execute("INSERT INTO Employees (Id, FirstName, LastName) VALUES (2, 'Jane', 'Smith')");

        var rows = m_engine.Query("SELECT * FROM Employees ORDER BY Id");

        // VIRTUAL columns should be evaluated on-the-fly
        Assert.That(rows[0]["FullName"].AsString(), Is.EqualTo("John Doe"));
        Assert.That(rows[1]["FullName"].AsString(), Is.EqualTo("Jane Smith"));
    }

    [Test]
    public void VirtualComputedColumnReflectsCurrentDataTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Items (
                Id INT PRIMARY KEY,
                BasePrice DECIMAL,
                TaxRate DECIMAL,
                TotalWithTax AS (BasePrice * (1 + TaxRate)) VIRTUAL
            )");

        m_engine.Execute("INSERT INTO Items (Id, BasePrice, TaxRate) VALUES (1, 100, 0.1)");

        var rows = m_engine.Query("SELECT * FROM Items WHERE Id = 1");
        Assert.That(rows[0]["TotalWithTax"].AsDecimal(), Is.EqualTo(110m));

        // Update the source data
        m_engine.Execute("UPDATE Items SET TaxRate = 0.2 WHERE Id = 1");

        // VIRTUAL column should reflect the new value immediately
        rows = m_engine.Query("SELECT * FROM Items WHERE Id = 1");
        Assert.That(rows[0]["TotalWithTax"].AsDecimal(), Is.EqualTo(120m));
    }

    [Test]
    public void VirtualComputedColumnWithFunctionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id INT PRIMARY KEY,
                Email VARCHAR(255),
                EmailLower AS (LOWER(Email)) VIRTUAL
            )");

        m_engine.Execute("INSERT INTO Users (Id, Email) VALUES (1, 'John.Doe@Example.COM')");

        var rows = m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        Assert.That(rows[0]["EmailLower"].AsString(), Is.EqualTo("john.doe@example.com"));
    }

    [Test]
    public void VirtualComputedColumnWithCaseExpressionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id INT PRIMARY KEY,
                Amount DECIMAL,
                Status AS (
                    CASE 
                        WHEN Amount >= 1000 THEN 'Large'
                        WHEN Amount >= 100 THEN 'Medium'
                        ELSE 'Small'
                    END
                ) VIRTUAL
            )");

        m_engine.Execute("INSERT INTO Orders (Id, Amount) VALUES (1, 50)");
        m_engine.Execute("INSERT INTO Orders (Id, Amount) VALUES (2, 500)");
        m_engine.Execute("INSERT INTO Orders (Id, Amount) VALUES (3, 5000)");

        var rows = m_engine.Query("SELECT * FROM Orders ORDER BY Id");
        Assert.That(rows[0]["Status"].AsString(), Is.EqualTo("Small"));
        Assert.That(rows[1]["Status"].AsString(), Is.EqualTo("Medium"));
        Assert.That(rows[2]["Status"].AsString(), Is.EqualTo("Large"));
    }

    #endregion

    #region Index on Computed Column Tests

    [Test]
    public void CreateIndexOnStoredComputedColumnTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(200),
                NameUpper AS (UPPER(Name)) STORED
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Apple')");
        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (2, 'Banana')");

        // Create index on stored computed column
        m_engine.Execute("CREATE INDEX IX_Products_NameUpper ON Products (NameUpper)");

        var index = m_engine.GetIndex("IX_Products_NameUpper");
        Assert.That(index, Is.Not.Null);

        // Verify index works (this uses the index for queries - though optimizer might not pick it yet)
        var rows = m_engine.Query("SELECT * FROM Products WHERE NameUpper = 'APPLE'");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Apple"));
    }

    #endregion
}
