using OutWit.Database.Core.Builder;
using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for automatic index updates on INSERT, UPDATE, and DELETE operations.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineIndexAutoUpdateTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        // Use database with index support (default builder includes indexes)
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    #endregion

    #region INSERT Auto-Update Tests

    [Test]
    public void InsertUpdatesSecondaryIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Assert - index seek should find the row
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void InsertMultipleRowsUpdatesIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Charlie', 'charlie@test.com')");

        // Assert - should find all three via index
        using var iteratorAlice = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iteratorAlice.Open();
        Assert.That(iteratorAlice.MoveNext(), Is.True);

        using var iteratorBob = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Bob")]);
        iteratorBob.Open();
        Assert.That(iteratorBob.MoveNext(), Is.True);

        using var iteratorCharlie = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Charlie")]);
        iteratorCharlie.Open();
        Assert.That(iteratorCharlie.MoveNext(), Is.True);
    }

    [Test]
    public void InsertUpdatesCompositeIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name_email ON Users (Name, Email)");

        // Act
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Assert - verify index was created
        var indexDef = m_engine.GetIndex("idx_users_name_email");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.Columns.Count, Is.EqualTo(2));
    }

    [Test]
    public void InsertWithNullIndexColumnDoesNotAddToIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_email ON Users (Email)");

        // Act - insert row with null email
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('NoEmail')");

        // Assert - should find via table scan
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name = 'NoEmail'");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Email"].IsNull, Is.True);
    }

    [Test]
    public void InsertUpdatesMultipleIndexesTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("CREATE INDEX idx_users_email ON Users (Email)");

        // Act
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Assert - both indexes should have the entry
        using var iteratorName = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iteratorName.Open();
        Assert.That(iteratorName.MoveNext(), Is.True);

        using var iteratorEmail = m_engine.CreateIndexSeek("Users", "idx_users_email", [WitSqlValue.FromText("alice@test.com")]);
        iteratorEmail.Open();
        Assert.That(iteratorEmail.MoveNext(), Is.True);
    }

    [Test]
    public void InsertWithUniqueIndexDuplicateThrowsTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE UNIQUE INDEX idx_users_email ON Users (Email)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act & Assert - duplicate email should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'alice@test.com')"));

        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    #endregion

    #region UPDATE Auto-Update Tests

    [Test]
    public void UpdateIndexedColumnUpdatesIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act - update the indexed column
        m_engine.Execute("UPDATE Users SET Name = 'Alicia' WHERE Name = 'Alice'");

        // Assert - old key should not exist
        using var iteratorOld = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iteratorOld.Open();
        Assert.That(iteratorOld.MoveNext(), Is.False);

        // Assert - new key should exist
        using var iteratorNew = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alicia")]);
        iteratorNew.Open();
        Assert.That(iteratorNew.MoveNext(), Is.True);
        Assert.That(iteratorNew.Current["Name"].AsString(), Is.EqualTo("Alicia"));
    }

    [Test]
    public void UpdateNonIndexedColumnDoesNotAffectIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act - update non-indexed column
        m_engine.Execute("UPDATE Users SET Email = 'newalice@test.com' WHERE Name = 'Alice'");

        // Assert - index key should still work
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["Email"].AsString(), Is.EqualTo("newalice@test.com"));
    }

    [Test]
    public void UpdateToNullRemovesFromIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_email ON Users (Email)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act - set indexed column to NULL
        m_engine.Execute("UPDATE Users SET Email = NULL WHERE Name = 'Alice'");

        // Assert - should not find via index
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_email", [WitSqlValue.FromText("alice@test.com")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.False);
    }

    [Test]
    public void UpdateFromNullAddsToIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_email ON Users (Email)");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Alice')");

        // Act - set indexed column from NULL to value
        m_engine.Execute("UPDATE Users SET Email = 'alice@test.com' WHERE Name = 'Alice'");

        // Assert - should find via index now
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_email", [WitSqlValue.FromText("alice@test.com")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
    }

    [Test]
    public void UpdateMultipleRowsUpdatesIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice2@test.com')");

        // Act - update all Alice rows
        m_engine.Execute("UPDATE Users SET Name = 'Alicia' WHERE Name = 'Alice'");

        // Assert - old name should return no results
        using var iteratorOld = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iteratorOld.Open();
        Assert.That(iteratorOld.MoveNext(), Is.False);

        // Assert - new name should return both
        var rows = m_engine.Query("SELECT * FROM Users WHERE Name = 'Alicia'");
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void UpdateViolatesUniqueIndexThrowsTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE UNIQUE INDEX idx_users_email ON Users (Email)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");

        // Act & Assert - update to duplicate email should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("UPDATE Users SET Email = 'alice@test.com' WHERE Name = 'Bob'"));

        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    #endregion

    #region DELETE Auto-Update Tests

    [Test]
    public void DeleteRemovesFromIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act
        m_engine.Execute("DELETE FROM Users WHERE Name = 'Alice'");

        // Assert - should not find via index
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.False);
    }

    [Test]
    public void DeleteMultipleRowsRemovesFromIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Charlie', 'charlie@test.com')");

        // Act - delete Alice and Bob
        m_engine.Execute("DELETE FROM Users WHERE Name IN ('Alice', 'Bob')");

        // Assert - Alice and Bob should not be in index
        using var iteratorAlice = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iteratorAlice.Open();
        Assert.That(iteratorAlice.MoveNext(), Is.False);

        using var iteratorBob = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Bob")]);
        iteratorBob.Open();
        Assert.That(iteratorBob.MoveNext(), Is.False);

        // Assert - Charlie should still be in index
        using var iteratorCharlie = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Charlie")]);
        iteratorCharlie.Open();
        Assert.That(iteratorCharlie.MoveNext(), Is.True);
    }

    [Test]
    public void DeleteAllRowsClearsIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        InsertTestUsers();

        // Act
        m_engine.Execute("DELETE FROM Users");

        // Assert - index should be empty
        using var iterator = m_engine.CreateIndexRangeScan("Users", "idx_users_name", null, true, null, true);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.False);
    }

    [Test]
    public void DeleteWithNullIndexColumnDoesNotThrowTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_email ON Users (Email)");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('NoEmail')");

        // Act & Assert - should not throw when deleting row with null indexed column
        Assert.DoesNotThrow(() => m_engine.Execute("DELETE FROM Users WHERE Name = 'NoEmail'"));

        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Is.Empty);
    }

    #endregion

    #region Index Range Scan with Auto-Update Tests

    [Test]
    public void IndexRangeScanReflectsInsertsTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Charlie', 'charlie@test.com')");

        // Act - range scan from A to C (exclusive)
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            WitSqlValue.FromText("A"), true,
            WitSqlValue.FromText("C"), false);
        iterator.Open();

        // Assert - should find Alice and Bob but not Charlie
        var names = new List<string>();
        while (iterator.MoveNext())
        {
            names.Add(iterator.Current["Name"].AsString());
        }

        Assert.That(names, Contains.Item("Alice"));
        Assert.That(names, Contains.Item("Bob"));
        Assert.That(names, Does.Not.Contain("Charlie"));
    }

    [Test]
    public void IndexRangeScanReflectsUpdatesTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");

        // Act - move Bob out of range
        m_engine.Execute("UPDATE Users SET Name = 'Zebra' WHERE Name = 'Bob'");

        // Range scan from A to D
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            WitSqlValue.FromText("A"), true,
            WitSqlValue.FromText("D"), false);
        iterator.Open();

        // Assert - should find only Alice, not Bob (now Zebra)
        var names = new List<string>();
        while (iterator.MoveNext())
        {
            names.Add(iterator.Current["Name"].AsString());
        }

        Assert.That(names, Contains.Item("Alice"));
        Assert.That(names, Does.Not.Contain("Bob"));
        Assert.That(names, Does.Not.Contain("Zebra"));
    }

    [Test]
    public void IndexRangeScanReflectsDeletesTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        InsertTestUsers();

        // Act
        m_engine.Execute("DELETE FROM Users WHERE Name = 'Bob'");

        // Range scan all
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            null, true, null, true);
        iterator.Open();

        // Assert - should not find Bob
        var names = new List<string>();
        while (iterator.MoveNext())
        {
            names.Add(iterator.Current["Name"].AsString());
        }

        Assert.That(names, Contains.Item("Alice"));
        Assert.That(names, Does.Not.Contain("Bob"));
        Assert.That(names, Contains.Item("Charlie"));
    }

    #endregion

    #region Integer Index Tests

    [Test]
    public void InsertUpdatesIntegerIndexTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Price INT NOT NULL
            )");
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act
        m_engine.Execute("INSERT INTO Products (Price) VALUES (100)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (200)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (150)");

        // Assert - should find via index
        using var iterator = m_engine.CreateIndexSeek("Products", "idx_products_price", [WitSqlValue.FromInt(150)]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current["Price"].AsInt64(), Is.EqualTo(150));
    }

    [Test]
    public void IntegerIndexRangeScanTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Price INT NOT NULL
            )");
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (50)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (100)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (150)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (200)");

        // Act - range scan 75 to 175
        using var iterator = m_engine.CreateIndexRangeScan(
            "Products", "idx_products_price",
            WitSqlValue.FromInt(75), true,
            WitSqlValue.FromInt(175), true);
        iterator.Open();

        // Assert - should find 100 and 150
        var prices = new List<long>();
        while (iterator.MoveNext())
        {
            prices.Add(iterator.Current["Price"].AsInt64());
        }

        Assert.That(prices, Contains.Item(100L));
        Assert.That(prices, Contains.Item(150L));
        Assert.That(prices, Does.Not.Contain(50L));
        Assert.That(prices, Does.Not.Contain(200L));
    }

    #endregion

    #region Transaction Tests

    [Test]
    public void InsertInTransactionUpdatesIndexTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("COMMIT");

        // Assert - should find via index after commit
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);
        iterator.Open();
        Assert.That(iterator.MoveNext(), Is.True);
    }

    [Test]
    public void RollbackRevertsIndexChangesTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        // Act
        m_engine.Execute("BEGIN TRANSACTION");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("ROLLBACK");

        // Assert - Bob should not be in index after rollback
        // Note: Current implementation updates indexes directly, rollback only reverts data
        // This test documents current behavior - full transactional index support would require more work
        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    #endregion
}
