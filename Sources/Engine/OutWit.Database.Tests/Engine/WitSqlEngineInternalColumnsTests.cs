namespace OutWit.Database.Tests;

/// <summary>
/// Tests that verify internal columns like _rowid are not exposed in SELECT * results.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineInternalColumnsTests : WitSqlEngineTestsBase
{
    #region SELECT * Tests

    [Test]
    public void SelectStarDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Email VARCHAR(255)
            )");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        
        var rows = m_engine.Query("SELECT * FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        
        // _rowid should NOT be in the result
        Assert.That(rows[0].ColumnNames, Does.Not.Contain("_rowid"));
        
        // Regular columns should be present
        Assert.That(rows[0].ColumnNames, Does.Contain("Id"));
        Assert.That(rows[0].ColumnNames, Does.Contain("Name"));
        Assert.That(rows[0].ColumnNames, Does.Contain("Email"));
        
        // Should have exactly 3 columns (no _rowid)
        Assert.That(rows[0].ColumnCount, Is.EqualTo(3));
    }

    [Test]
    public void SelectStarFromJoinDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL
            )");
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT,
                Total DECIMAL
            )");
        m_engine.Execute("INSERT INTO Users (Name) VALUES ('Alice')");
        m_engine.Execute("INSERT INTO Orders (UserId, Total) VALUES (1, 100.00)");
        
        var rows = m_engine.Query(@"
            SELECT * FROM Users u
            INNER JOIN Orders o ON u.Id = o.UserId");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        
        // _rowid should NOT be in any form
        foreach (var colName in rows[0].ColumnNames)
        {
            Assert.That(colName, Does.Not.Contain("_rowid"), 
                $"Column '{colName}' should not contain _rowid");
        }
    }

    [Test]
    public void SelectStarWithAliasDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Price DECIMAL
            )");
        m_engine.Execute("INSERT INTO Products (Name, Price) VALUES ('Widget', 29.99)");
        
        var rows = m_engine.Query("SELECT p.* FROM Products p");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ColumnNames, Does.Not.Contain("_rowid"));
        Assert.That(rows[0].ColumnNames, Does.Not.Contain("p._rowid"));
    }

    [Test]
    public void SelectExplicitColumnsDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Items (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100)
            )");
        m_engine.Execute("INSERT INTO Items (Name) VALUES ('Test')");
        
        var rows = m_engine.Query("SELECT Id, Name FROM Items");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ColumnCount, Is.EqualTo(2));
        Assert.That(rows[0].ColumnNames, Does.Contain("Id"));
        Assert.That(rows[0].ColumnNames, Does.Contain("Name"));
    }

    #endregion

    #region Schema Tests

    [Test]
    public void SelectStarSchemaDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE TestTable (
                Id INT PRIMARY KEY,
                Value INT
            )");
        
        var result = m_engine.Execute("SELECT * FROM TestTable");
        
        // Schema should not include _rowid
        Assert.That(result.Columns.Select(c => c.Name), Does.Not.Contain("_rowid"));
    }

    #endregion

    #region INSERT/UPDATE/DELETE Still Work Tests

    [Test]
    public void UpdateWithWhereStillWorksTest()
    {
        // This test verifies that internal UPDATE operations still work
        // (they need _rowid internally but it shouldn't be exposed)
        m_engine.Execute(@"
            CREATE TABLE Data (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Value INT
            )");
        m_engine.Execute("INSERT INTO Data (Value) VALUES (100)");
        m_engine.Execute("UPDATE Data SET Value = 200 WHERE Id = 1");
        
        var rows = m_engine.Query("SELECT * FROM Data");
        Assert.That(rows[0]["Value"].AsInt64(), Is.EqualTo(200));
        Assert.That(rows[0].ColumnNames, Does.Not.Contain("_rowid"));
    }

    [Test]
    public void DeleteWithWhereStillWorksTest()
    {
        m_engine.Execute(@"
            CREATE TABLE ToDelete (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(50)
            )");
        m_engine.Execute("INSERT INTO ToDelete (Name) VALUES ('Keep')");
        m_engine.Execute("INSERT INTO ToDelete (Name) VALUES ('Delete')");
        m_engine.Execute("DELETE FROM ToDelete WHERE Name = 'Delete'");
        
        var rows = m_engine.Query("SELECT * FROM ToDelete");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Keep"));
        Assert.That(rows[0].ColumnNames, Does.Not.Contain("_rowid"));
    }

    #endregion

    #region Subquery Tests

    [Test]
    public void SelectStarFromSubqueryDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Value INT
            )");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (1)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (2)");
        
        var rows = m_engine.Query(@"
            SELECT * FROM (SELECT * FROM Numbers WHERE Value > 0) AS sub
            ORDER BY Value");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        
        foreach (var row in rows)
        {
            Assert.That(row.ColumnNames, Does.Not.Contain("_rowid"));
        }
    }

    #endregion

    #region DISTINCT Tests

    [Test]
    public void SelectDistinctStarDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Duplicates (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Category VARCHAR(20)
            )");
        m_engine.Execute("INSERT INTO Duplicates (Category) VALUES ('A')");
        m_engine.Execute("INSERT INTO Duplicates (Category) VALUES ('A')");
        m_engine.Execute("INSERT INTO Duplicates (Category) VALUES ('B')");
        
        // Note: DISTINCT * will still return all rows because Id is different
        // but _rowid should not be included in the comparison or output
        var rows = m_engine.Query("SELECT DISTINCT * FROM Duplicates ORDER BY Id");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        foreach (var row in rows)
        {
            Assert.That(row.ColumnNames, Does.Not.Contain("_rowid"));
        }
    }

    #endregion

    #region CTE Tests

    [Test]
    public void SelectStarFromCteDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE CteSource (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(50)
            )");
        m_engine.Execute("INSERT INTO CteSource (Name) VALUES ('Test')");
        
        var rows = m_engine.Query(@"
            WITH cte AS (SELECT * FROM CteSource)
            SELECT * FROM cte");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].ColumnNames, Does.Not.Contain("_rowid"));
    }

    #endregion

    #region UNION Tests

    [Test]
    public void SelectStarUnionDoesNotIncludeRowIdTest()
    {
        m_engine.Execute(@"
            CREATE TABLE TableA (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Value INT
            )");
        m_engine.Execute(@"
            CREATE TABLE TableB (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Value INT
            )");
        m_engine.Execute("INSERT INTO TableA (Value) VALUES (1)");
        m_engine.Execute("INSERT INTO TableB (Value) VALUES (2)");
        
        var rows = m_engine.Query(@"
            SELECT * FROM TableA
            UNION ALL
            SELECT * FROM TableB
            ORDER BY Value");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        foreach (var row in rows)
        {
            Assert.That(row.ColumnNames, Does.Not.Contain("_rowid"));
        }
    }

    #endregion
}
