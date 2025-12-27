using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Integration tests for WitSqlEngine index operations.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineIndexTests : WitSqlEngineTestsBase
{
    #region CreateIndex Tests

    [Test]
    public void CreateIndexStoresMetadataTest()
    {
        // Arrange
        CreateUsersTable();

        // Act
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_users_name");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.Name, Is.EqualTo("idx_users_name"));
        Assert.That(indexDef.TableName, Is.EqualTo("Users"));
        Assert.That(indexDef.Columns, Contains.Item("Name"));
        Assert.That(indexDef.IsUnique, Is.False);
    }

    [Test]
    public void CreateUniqueIndexStoresMetadataTest()
    {
        // Arrange
        CreateUsersTable();

        // Act
        m_engine.Execute("CREATE UNIQUE INDEX idx_users_email ON Users (Email)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_users_email");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.IsUnique, Is.True);
    }

    [Test]
    public void CreateIndexIfNotExistsDoesNotThrowTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act & Assert - should not throw
        Assert.DoesNotThrow(() => 
            m_engine.Execute("CREATE INDEX IF NOT EXISTS idx_users_name ON Users (Name)"));
    }

    [Test]
    public void CreateCompositeIndexTest()
    {
        // Arrange
        CreateUsersTable();

        // Act
        m_engine.Execute("CREATE INDEX idx_users_name_email ON Users (Name, Email)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_users_name_email");
        Assert.That(indexDef, Is.Not.Null);
        Assert.That(indexDef!.Columns.Count, Is.EqualTo(2));
        Assert.That(indexDef.Columns[0], Is.EqualTo("Name"));
        Assert.That(indexDef.Columns[1], Is.EqualTo("Email"));
    }

    #endregion

    #region DropIndex Tests

    [Test]
    public void DropIndexRemovesMetadataTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");
        
        // Verify it exists
        Assert.That(m_engine.GetIndex("idx_users_name"), Is.Not.Null);

        // Act
        m_engine.Execute("DROP INDEX idx_users_name");

        // Assert
        Assert.That(m_engine.GetIndex("idx_users_name"), Is.Null);
    }

    [Test]
    public void DropIndexIfExistsDoesNotThrowTest()
    {
        // Act & Assert - should not throw for non-existent index
        Assert.DoesNotThrow(() => 
            m_engine.Execute("DROP INDEX IF EXISTS idx_nonexistent"));
    }

    #endregion

    #region CreateIndexSeek Tests

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

    #region CreateIndexRangeScan Tests

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
    public void CreateIndexRangeScanWithUnboundedStartTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act
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

        // Act
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

    #endregion

    #region GetIndex Tests

    [Test]
    public void GetIndexReturnsNullForNonExistentTest()
    {
        // Act
        var indexDef = m_engine.GetIndex("nonexistent");

        // Assert
        Assert.That(indexDef, Is.Null);
    }

    [Test]
    public void GetIndexIsCaseInsensitiveTest()
    {
        // Arrange
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX idx_users_name ON Users (Name)");

        // Act & Assert
        Assert.That(m_engine.GetIndex("IDX_USERS_NAME"), Is.Not.Null);
        Assert.That(m_engine.GetIndex("idx_users_name"), Is.Not.Null);
        Assert.That(m_engine.GetIndex("Idx_Users_Name"), Is.Not.Null);
    }

    #endregion

    #region Index with Different Data Types Tests

    [Test]
    public void CreateIndexOnIntegerColumnTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY,
                Price INT NOT NULL
            )");

        // Act
        m_engine.Execute("CREATE INDEX idx_products_price ON Products (Price)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_products_price");
        Assert.That(indexDef, Is.Not.Null);
    }

    [Test]
    public void CreateIndexOnDateColumnTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Events (
                Id BIGINT PRIMARY KEY,
                EventDate DATE NOT NULL
            )");

        // Act
        m_engine.Execute("CREATE INDEX idx_events_date ON Events (EventDate)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_events_date");
        Assert.That(indexDef, Is.Not.Null);
    }

    [Test]
    public void CreateIndexOnBooleanColumnTest()
    {
        // Arrange
        m_engine.Execute(@"
            CREATE TABLE Tasks (
                Id BIGINT PRIMARY KEY,
                IsCompleted BOOLEAN NOT NULL
            )");

        // Act
        m_engine.Execute("CREATE INDEX idx_tasks_completed ON Tasks (IsCompleted)");

        // Assert
        var indexDef = m_engine.GetIndex("idx_tasks_completed");
        Assert.That(indexDef, Is.Not.Null);
    }

    #endregion
}
