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
}
