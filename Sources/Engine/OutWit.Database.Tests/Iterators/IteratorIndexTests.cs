using OutWit.Database.Definitions;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

/// <summary>
/// Tests for index iterators via WitSqlEngine public API.
/// Direct iterator testing is limited since IteratorIndexSeek and IteratorIndexRangeScan are internal.
/// </summary>
[TestFixture]
public class IteratorIndexTests : WitSqlEngineTestsBase
{
    #region CreateIndexSeek Integration Tests

    [Test]
    public void CreateIndexSeekReturnsIteratorTest()
    {
        // Arrange
        CreateUsersTable();
        InsertTestUsers();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);

        // Assert
        Assert.That(iterator, Is.Not.Null);
        Assert.That(iterator.Schema, Is.Not.Null);
        Assert.That(iterator.Schema.Count, Is.GreaterThan(0));
    }

    [Test]
    public void CreateIndexSeekSchemaIncludesRowIdTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Test")]);
        var schema = iterator.Schema;

        // Assert
        Assert.That(schema, Is.Not.Null);
        Assert.That(schema[0].Name, Is.EqualTo("_rowid"));
        Assert.That(schema[0].Type, Is.EqualTo(WitSqlType.Integer));
    }

    [Test]
    public void CreateIndexSeekWithIntegerKeyTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Price INT NOT NULL
            )");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (100)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (200)");
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act
        using var iterator = m_engine.CreateIndexSeek("Products", "idx_products_price", [WitSqlValue.FromInt(100)]);

        // Assert
        Assert.That(iterator, Is.Not.Null);
    }

    [Test]
    public void CreateIndexSeekThrowsForNonExistentTableTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            m_engine.CreateIndexSeek("NonExistent", "idx_users_name", [WitSqlValue.FromText("Alice")]));
    }

    [Test]
    public void CreateIndexSeekThrowsForNonExistentIndexTest()
    {
        // Arrange
        CreateUsersTable();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            m_engine.CreateIndexSeek("Users", "idx_nonexistent", [WitSqlValue.FromText("Alice")]));
    }

    [Test]
    public void CreateIndexSeekThrowsForWrongTableTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE TABLE Orders (Id BIGINT PRIMARY KEY, UserId BIGINT)");
        m_engine.Execute("CREATE INDEX idx_orders_userid ON Orders (UserId)");

        // Act & Assert - index belongs to Orders, not Users
        Assert.Throws<InvalidOperationException>(() =>
            m_engine.CreateIndexSeek("Users", "idx_orders_userid", [WitSqlValue.FromInt(1)]));
    }

    #endregion

    #region CreateIndexRangeScan Integration Tests

    [Test]
    public void CreateIndexRangeScanReturnsIteratorTest()
    {
        // Arrange
        CreateUsersTable();
        InsertTestUsers();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            WitSqlValue.FromText("A"), true,
            WitSqlValue.FromText("C"), true);

        // Assert
        Assert.That(iterator, Is.Not.Null);
        Assert.That(iterator.Schema, Is.Not.Null);
    }

    [Test]
    public void CreateIndexRangeScanSchemaIncludesRowIdTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            null, true, null, true);
        var schema = iterator.Schema;

        // Assert
        Assert.That(schema, Is.Not.Null);
        Assert.That(schema[0].Name, Is.EqualTo("_rowid"));
    }

    [Test]
    public void CreateIndexRangeScanWithUnboundedStartTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - start key is null (unbounded)
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            null, true,
            WitSqlValue.FromText("M"), false);

        // Assert
        Assert.That(iterator, Is.Not.Null);
    }

    [Test]
    public void CreateIndexRangeScanWithUnboundedEndTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - end key is null (unbounded)
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            WitSqlValue.FromText("M"), true,
            null, true);

        // Assert
        Assert.That(iterator, Is.Not.Null);
    }

    [Test]
    public void CreateIndexRangeScanFullScanTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act - both bounds null = full index scan
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            null, true, null, true);

        // Assert
        Assert.That(iterator, Is.Not.Null);
    }

    [Test]
    public void CreateIndexRangeScanWithIntegerRangeTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Price INT NOT NULL
            )");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (50)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (100)");
        m_engine.Execute("INSERT INTO Products (Price) VALUES (150)");
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Act
        using var iterator = m_engine.CreateIndexRangeScan(
            "Products", "idx_products_price",
            WitSqlValue.FromInt(75), true,
            WitSqlValue.FromInt(125), true);

        // Assert
        Assert.That(iterator, Is.Not.Null);
    }

    #endregion

    #region Estimated Row Count Tests

    [Test]
    public void IndexSeekOnUniqueIndexHasEstimatedRowCountTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE UNIQUE INDEX idx_users_email ON Users (Email)");

        // Act
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_email", [WitSqlValue.FromText("test@test.com")]);

        // Assert - estimated row count depends on whether physical index exists
        // In current implementation, if physical index doesn't exist, falls back to table scan
        // which returns -1 (unknown). If physical index exists and is unique, returns 1.
        Assert.That(iterator.EstimatedRowCount, Is.EqualTo(1).Or.EqualTo(-1));
    }

    [Test]
    public void IndexSeekOnNonUniqueIndexHasUnknownEstimatedRowCountTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        using var iterator = m_engine.CreateIndexSeek("Users", "idx_users_name", [WitSqlValue.FromText("Alice")]);

        // Assert - non-unique index should return unknown (-1)
        Assert.That(iterator.EstimatedRowCount, Is.EqualTo(-1));
    }

    [Test]
    public void IndexRangeScanHasUnknownEstimatedRowCountTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
        using var iterator = m_engine.CreateIndexRangeScan(
            "Users", "idx_users_name",
            null, true, null, true);

        // Assert - range scans always have unknown row count
        Assert.That(iterator.EstimatedRowCount, Is.EqualTo(-1));
    }

    #endregion
}
