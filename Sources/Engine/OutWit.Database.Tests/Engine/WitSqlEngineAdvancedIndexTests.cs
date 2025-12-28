using OutWit.Database.Core.Builder;
using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for advanced index features: partial indexes (WHERE clause),
/// expression indexes (functional indexes), and covering indexes (INCLUDE columns).
/// </summary>
[TestFixture]
public sealed class WitSqlEngineAdvancedIndexTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    #endregion

    #region Partial Index Tests

    [Test]
    public void CreatePartialIndexStoresWhereExpressionTest()
    {
        // Arrange
        CreateUsersTable();

        // Act
        m_engine.Execute("CREATE INDEX idx_active_users ON Users (Name) WHERE Email IS NOT NULL");

        // Assert
        var indexDef = m_engine.GetIndex("idx_active_users");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsFiltered, Is.True);
        Assert.That(indexDef.WhereExpression, Is.Not.Null);
    }

    [Test]
    public void InsertMatchingPartialIndexConditionAddsToIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");

        // Act - insert row that matches condition
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Assert - should find via index
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void InsertNotMatchingPartialIndexConditionDoesNotAddToIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");

        // Act - insert row that does NOT match condition (Email is NULL)
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('NoEmail')");

        // Assert - should NOT find via index
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("NoEmail")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.False);

        // But should find via table scan
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name = 'NoEmail'");
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    [Test]
    public void UpdateToMatchPartialIndexConditionAddsToIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Alice')"); // No email initially

        // Act - update to match condition
        m_engine.Execute("UPDATE Users SET Email = 'alice@test.com' WHERE Name = 'Alice'");

        // Assert - should now find via index
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
    }

    [Test]
    public void UpdateToNotMatchPartialIndexConditionRemovesFromIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act - update to NOT match condition
        m_engine.Execute("UPDATE Users SET Email = NULL WHERE Name = 'Alice'");

        // Assert - should NOT find via index anymore
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.False);
    }

    [Test]
    public void PartialIndexWithEqualityConditionTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Status TEXT NOT NULL,
                CustomerName TEXT NOT NULL,
                Total DECIMAL NOT NULL
            )");
        m_engine.Execute("CREATE INDEX idx_pending_orders ON Orders (CustomerName) WHERE Status = 'pending'");

        // Act - insert orders with different statuses
        m_engine.Execute("INSERT INTO Orders (Status, CustomerName, Total) VALUES ('pending', 'Alice', 100)");
        m_engine.Execute("INSERT INTO Orders (Status, CustomerName, Total) VALUES ('completed', 'Bob', 200)");
        m_engine.Execute("INSERT INTO Orders (Status, CustomerName, Total) VALUES ('pending', 'Charlie', 150)");

        // Assert - only pending orders in index
        using var iterator = m_engine.CreateIndexSeek("Orders", "idx_pending_orders", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);

        using var iteratorBob = m_engine.CreateIndexSeek("Orders", "idx_pending_orders", [WitSqlValue.FromText("Bob")]);
        iteratorBob.Open();
        Assert.That(iteratorBob.MoveNext(), Is.False); // Bob's order is completed
    }

    [Test]
    public void CreatePartialIndexOnExistingDataBuildsCorrectlyTest()
    {
        // Arrange - insert data BEFORE creating partial index
        CreateUsersTable();
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('NoEmail')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");

        // Act - create partial index
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");

        // Assert - should find only rows with email
        using var iteratorAlice = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iteratorAlice.Open();
        Assert.That(iteratorAlice.MoveNext(), Is.True);

        using var iteratorNoEmail = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("NoEmail")]);
        iteratorNoEmail.Open();
        Assert.That(iteratorNoEmail.MoveNext(), Is.False);

        using var iteratorBob = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Bob")]);
        iteratorBob.Open();
        Assert.That(iteratorBob.MoveNext(), Is.True);
    }

    [Test]
    public void PartialIndexWithComparisonConditionTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Price INT NOT NULL
            )");
        m_engine.Execute("CREATE INDEX idx_expensive ON Products (Name) WHERE Price > 100");

        // Act
        m_engine.Execute("INSERT INTO Products (Name, Price) VALUES ('Cheap', 50)");
        m_engine.Execute("INSERT INTO Products (Name, Price) VALUES ('Expensive', 200)");
        m_engine.Execute("INSERT INTO Products (Name, Price) VALUES ('Medium', 100)"); // Not > 100

        // Assert
        using var iteratorCheap = m_engine.CreateIndexSeek("Products", "idx_expensive", [WitSqlValue.FromText("Cheap")]);
        iteratorCheap.Open();
        Assert.That(iteratorCheap.MoveNext(), Is.False);

        using var iteratorExpensive = m_engine.CreateIndexSeek("Products", "idx_expensive", [WitSqlValue.FromText("Expensive")]);
        iteratorExpensive.Open();
        Assert.That(iteratorExpensive.MoveNext(), Is.True);

        using var iteratorMedium = m_engine.CreateIndexSeek("Products", "idx_expensive", [WitSqlValue.FromText("Medium")]);
        iteratorMedium.Open();
        Assert.That(iteratorMedium.MoveNext(), Is.False); // 100 is not > 100
    }

    #endregion

    #region Covering Index Tests

    [Test]
    public void CreateCoveringIndexStoresIncludeColumnsTest()
    {
        // Arrange
        CreateUsersTable();

        // Act
        m_engine.Execute("CREATE INDEX idx_name_include_email ON Users (Name) INCLUDE (Email)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_name_include_email");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsCovering, Is.True);
        Assert.That(indexDef.IncludeColumns, Contains.Item("Email"));
    }

    [Test]
    public void CoveringIndexWithMultipleIncludeColumnsTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Employees (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Department TEXT,
                Salary DECIMAL,
                HireDate DATE
            )");

        // Act
        m_engine.Execute("CREATE INDEX idx_dept ON Employees (Department) INCLUDE (Name, Salary)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_dept");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsCovering, Is.True);
        Assert.That(indexDef.IncludeColumns, Has.Count.EqualTo(2));
        Assert.That(indexDef.IncludeColumns, Contains.Item("Name"));
        Assert.That(indexDef.IncludeColumns, Contains.Item("Salary"));
    }

    [Test]
    public void InsertWithCoveringIndexSucceedsTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_name_include_email ON Users (Name) INCLUDE (Email)");

        // Act
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Assert - should find via index
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_name_include_email", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["Email"].AsString(), Is.EqualTo("alice@test.com"));
    }

    [Test]
    public void CoveringIndexCoversColumnsTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_name_include_email ON Users (Name) INCLUDE (Email)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_name_include_email", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        
        // Assert - verify index definition has covering info
        var indexDef = m_engine.GetIndex("idx_name_include_email");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsCovering, Is.True);
        
        // Key column + include columns should cover typical queries
        var coveredColumns = new HashSet<string>(indexDef.Columns, StringComparer.OrdinalIgnoreCase);
        foreach (var col in indexDef.IncludeColumns!)
        {
            coveredColumns.Add(col);
        }
        
        Assert.That(coveredColumns, Contains.Item("Name"));
        Assert.That(coveredColumns, Contains.Item("Email"));
    }

    [Test]
    public void CoveringIndexDoesNotCoverNonIncludedColumnsTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Employees (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Department TEXT,
                Salary DECIMAL
            )");
        m_engine.Execute("CREATE INDEX idx_dept ON Employees (Department) INCLUDE (Name)");
        m_engine.Execute("INSERT INTO Employees (Name, Department, Salary) VALUES ('Alice', 'Engineering', 100000)");

        // Act
        var indexDef = m_engine.GetIndex("idx_dept");
        
        // Assert - verify covering capability via definition
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsCovering, Is.True);
        
        // Build set of covered columns
        var coveredColumns = new HashSet<string>(indexDef.Columns, StringComparer.OrdinalIgnoreCase);
        foreach (var col in indexDef.IncludeColumns!)
        {
            coveredColumns.Add(col);
        }
        
        Assert.That(coveredColumns, Contains.Item("Department"));
        Assert.That(coveredColumns, Contains.Item("Name"));
        Assert.That(coveredColumns, Does.Not.Contain("Salary")); // Salary not included
    }

    [Test]
    public void CreateCoveringIndexOnExistingDataTest()
    {
        // Arrange - insert data BEFORE creating covering index
        CreateUsersTable();
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");

        // Act
        m_engine.Execute("CREATE INDEX idx_name_include_email ON Users (Name) INCLUDE (Email)");

        // Assert - should find all rows
        using var iteratorAlice = m_engine.CreateIndexSeek("Users", "idx_name_include_email", [WitSqlValue.FromText("Alice")]);
        iteratorAlice.Open();
        Assert.That(iteratorAlice.MoveNext(), Is.True);

        using var iteratorBob = m_engine.CreateIndexSeek("Users", "idx_name_include_email", [WitSqlValue.FromText("Bob")]);
        iteratorBob.Open();
        Assert.That(iteratorBob.MoveNext(), Is.True);
    }

    #endregion

    #region Partial + Covering Index Combined Tests

    [Test]
    public void CoveringPartialIndexTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Status TEXT NOT NULL,
                CustomerName TEXT NOT NULL,
                Total DECIMAL NOT NULL,
                Notes TEXT
            )");
        
        // Covering partial index: index CustomerName for pending orders, include Total
        m_engine.Execute("CREATE INDEX idx_pending ON Orders (CustomerName) INCLUDE (Total) WHERE Status = 'pending'");

        // Act
        m_engine.Execute("INSERT INTO Orders (Status, CustomerName, Total, Notes) VALUES ('pending', 'Alice', 100, 'Note1')");
        m_engine.Execute("INSERT INTO Orders (Status, CustomerName, Total, Notes) VALUES ('completed', 'Bob', 200, 'Note2')");

        // Assert
        var indexDef = m_engine.GetIndex("idx_pending");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsFiltered, Is.True);
        Assert.That(indexDef.IsCovering, Is.True);

        // Alice should be in index (pending)
        using var iteratorAlice = m_engine.CreateIndexSeek("Orders", "idx_pending", [WitSqlValue.FromText("Alice")]);
        iteratorAlice.Open();
        Assert.That(iteratorAlice.MoveNext(), Is.True);

        // Bob should NOT be in index (completed)
        using var iteratorBob = m_engine.CreateIndexSeek("Orders", "idx_pending", [WitSqlValue.FromText("Bob")]);
        iteratorBob.Open();
        Assert.That(iteratorBob.MoveNext(), Is.False);
    }

    #endregion

    #region Delete Tests for Partial Index

    [Test]
    public void DeleteMatchingPartialIndexConditionRemovesFromIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Verify in index
        using var iteratorBefore = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iteratorBefore.Open();
        Assert.That(iteratorBefore.MoveNext(), Is.True);
        iteratorBefore.Dispose();

        // Act
        m_engine.Execute("DELETE FROM Users WHERE Name = 'Alice'");

        // Assert - should not find via index after delete
        using var iteratorAfter = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iteratorAfter.Open();
        Assert.That(iteratorAfter.MoveNext(), Is.False);
    }

    [Test]
    public void DeleteNotMatchingPartialIndexConditionDoesNotAffectIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_with_email ON Users (Name) WHERE Email IS NOT NULL");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('NoEmail')"); // Not in index

        // Act - delete the row NOT in index
        m_engine.Execute("DELETE FROM Users WHERE Name = 'NoEmail'");

        // Assert - Alice should still be in index
        using var iteratorAlice = m_engine.CreateIndexSeek("Users", "idx_with_email", [WitSqlValue.FromText("Alice")]);
        iteratorAlice.Open();
        Assert.That(iteratorAlice.MoveNext(), Is.True);
    }

    #endregion
}
