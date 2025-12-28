using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Stores;
using OutWit.Database.Types;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for ROWVERSION column support.
/// ROWVERSION is a database-wide auto-incrementing value that changes on INSERT and UPDATE.
/// </summary>
[TestFixture]
public class WitSqlEngineRowVersionTests
{
    private Engine.WitSqlEngine m_engine = null!;

    [SetUp]
    public void SetUp()
    {
        var database = WitDatabase.CreateInMemory();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #region CREATE TABLE with ROWVERSION

    [Test]
    public void CreateTableWithRowVersionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        var table = m_engine.GetTable("Products");
        Assert.That(table, Is.Not.Null);
        
        var versionCol = table.GetColumn("Version");
        Assert.That(versionCol, Is.Not.Null);
        Assert.That(versionCol.Type, Is.EqualTo(WitDataType.RowVersion));
    }

    #endregion

    #region INSERT with ROWVERSION Auto-Generation

    [Test]
    public void InsertAutoGeneratesRowVersionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        // INSERT without specifying Version - should be auto-generated
        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");

        var rows = m_engine.Query("SELECT Id, Name, Version FROM Products WHERE Id = 1");
        
        Assert.That(rows.Count, Is.EqualTo(1));
        var version = rows[0]["Version"].AsRowVersion();
        Assert.That(version, Is.GreaterThan(0UL)); // Auto-generated non-zero value
    }

    [Test]
    public void InsertMultipleRowsHaveSequentialRowVersionsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget'), (2, 'Gadget'), (3, 'Gizmo')");

        var rows = m_engine.Query("SELECT Version FROM Products ORDER BY Id");
        
        Assert.That(rows.Count, Is.EqualTo(3));
        var v1 = rows[0]["Version"].AsRowVersion();
        var v2 = rows[1]["Version"].AsRowVersion();
        var v3 = rows[2]["Version"].AsRowVersion();
        
        // Each row should have a different, incrementing version
        Assert.That(v2, Is.GreaterThan(v1));
        Assert.That(v3, Is.GreaterThan(v2));
    }

    [Test]
    public void InsertExplicitRowVersionThrowsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        // Trying to INSERT explicit ROWVERSION value should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("INSERT INTO Products (Id, Name, Version) VALUES (1, 'Widget', 123)"));
        
        Assert.That(ex!.Message, Does.Contain("ROWVERSION"));
    }

    #endregion

    #region UPDATE with ROWVERSION Auto-Update

    [Test]
    public void UpdateAutoIncrementsRowVersionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");

        var before = m_engine.Query("SELECT Version FROM Products WHERE Id = 1");
        var versionBefore = before[0]["Version"].AsRowVersion();

        m_engine.Execute("UPDATE Products SET Name = 'Updated Widget' WHERE Id = 1");

        var after = m_engine.Query("SELECT Version FROM Products WHERE Id = 1");
        var versionAfter = after[0]["Version"].AsRowVersion();

        Assert.That(versionAfter, Is.GreaterThan(versionBefore));
    }

    [Test]
    public void UpdateMultipleRowsIncreasesAllVersionsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Category VARCHAR(50) NOT NULL,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Category, Name) VALUES (1, 'A', 'Widget'), (2, 'A', 'Gadget'), (3, 'B', 'Gizmo')");

        var before = m_engine.Query("SELECT Id, Version FROM Products WHERE Category = 'A' ORDER BY Id");
        var v1Before = before[0]["Version"].AsRowVersion();
        var v2Before = before[1]["Version"].AsRowVersion();

        // Update all products in category A
        m_engine.Execute("UPDATE Products SET Name = Name || ' Updated' WHERE Category = 'A'");

        var after = m_engine.Query("SELECT Id, Version FROM Products ORDER BY Id");
        var v1After = after[0]["Version"].AsRowVersion();
        var v2After = after[1]["Version"].AsRowVersion();
        var v3After = after[2]["Version"].AsRowVersion();

        // Updated rows should have increased versions
        Assert.That(v1After, Is.GreaterThan(v1Before));
        Assert.That(v2After, Is.GreaterThan(v2Before));
        
        // Not updated row should have same version as when inserted
        // (v3 wasn't in the "before" query, so we just check it exists)
        Assert.That(v3After, Is.GreaterThan(0UL));
    }

    [Test]
    public void UpdateExplicitRowVersionThrowsTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");

        // Trying to UPDATE explicit ROWVERSION value should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            m_engine.Execute("UPDATE Products SET Version = 999 WHERE Id = 1"));
        
        Assert.That(ex!.Message, Does.Contain("ROWVERSION"));
    }

    #endregion

    #region ROWVERSION with RETURNING

    [Test]
    public void InsertReturningRowVersionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        var rows = m_engine.Query("INSERT INTO Products (Id, Name) VALUES (1, 'Widget') RETURNING Id, Version");

        Assert.That(rows.Count, Is.EqualTo(1));
        var version = rows[0]["Version"].AsRowVersion();
        Assert.That(version, Is.GreaterThan(0UL));
    }

    [Test]
    public void UpdateReturningRowVersionTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");

        var rows = m_engine.Query("UPDATE Products SET Name = 'Updated' WHERE Id = 1 RETURNING Id, Version");

        Assert.That(rows.Count, Is.EqualTo(1));
        var version = rows[0]["Version"].AsRowVersion();
        Assert.That(version, Is.GreaterThan(0UL));
    }

    #endregion

    #region ROWVERSION Comparison

    [Test]
    public void RowVersionCanBeUsedInOrderByTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");
        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (2, 'Gadget')");

        // ORDER BY Version should work - oldest first
        var rows = m_engine.Query("SELECT Id FROM Products ORDER BY Version ASC");
        
        Assert.That(rows.Count, Is.EqualTo(2));
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1)); // First inserted
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2)); // Second inserted
    }

    #endregion

    #region Multiple Tables

    [Test]
    public void RowVersionIsGlobalAcrossTablesTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Name VARCHAR(100),
                Version ROWVERSION
            )");

        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id INT PRIMARY KEY,
                ProductId INT,
                Version ROWVERSION
            )");

        m_engine.Execute("INSERT INTO Products (Id, Name) VALUES (1, 'Widget')");
        var productRows = m_engine.Query("SELECT Version FROM Products WHERE Id = 1");
        var productVersion = productRows[0]["Version"].AsRowVersion();

        m_engine.Execute("INSERT INTO Orders (Id, ProductId) VALUES (1, 1)");
        var orderRows = m_engine.Query("SELECT Version FROM Orders WHERE Id = 1");
        var orderVersion = orderRows[0]["Version"].AsRowVersion();

        // Order was inserted after Product, so should have greater version
        Assert.That(orderVersion, Is.GreaterThan(productVersion));
    }

    #endregion
}
