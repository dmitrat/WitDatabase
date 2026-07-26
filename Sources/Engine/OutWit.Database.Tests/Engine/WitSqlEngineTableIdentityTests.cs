using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Regression tests for table identity and the data lifecycle of DDL.
/// </summary>
/// <remarks>
/// Two defects found by the July 2026 audit. Both were invisible to a test that asserts on
/// <c>COUNT(*)</c>, because that reads the schema catalog's row counter rather than the stored rows,
/// and the counter stayed plausible while the data did not.
/// <list type="number">
/// <item>Row keys were built as <c>t:{tableName}:</c> from the caller-supplied string while the
/// catalog resolves names case-insensitively, so <c>INSERT INTO users</c> after
/// <c>CREATE TABLE Users</c> wrote into a key space nothing could read, and
/// <c>TRUNCATE TABLE users</c> deleted nothing while resetting the rowid counter - so the next
/// inserts overwrote live rows.</item>
/// <item><c>DROP TABLE</c> deleted neither the rows nor the secondary indexes, so a table recreated
/// under the same name silently served the dropped table's contents.</item>
/// </list>
/// Every assertion here reads rows back rather than counting them.
/// </remarks>
[TestFixture]
public sealed class WitSqlEngineTableIdentityTests : WitSqlEngineTestsBase
{
    #region Case-Insensitive Table Identity

    [Test]
    public void InsertWithDifferingCaseWritesToTheSameTableTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");

        m_engine.Execute("INSERT INTO users (Id, Name) VALUES (1, 'lower')");
        m_engine.Execute("INSERT INTO Users (Id, Name) VALUES (2, 'proper')");
        m_engine.Execute("INSERT INTO USERS (Id, Name) VALUES (3, 'upper')");

        var names = SelectStrings("SELECT Name FROM Users ORDER BY Id");

        Assert.That(names, Is.EqualTo(new[] { "lower", "proper", "upper" }),
            "All three rows must land in one table and be readable");
    }

    [Test]
    public void RowInsertedWithDifferingCaseIsFoundByPrimaryKeyTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
        m_engine.Execute("INSERT INTO users (Id, Name) VALUES (1, 'lower')");

        var names = SelectStrings("SELECT Name FROM Users WHERE Id = 1");

        Assert.That(names, Is.EqualTo(new[] { "lower" }));
    }

    [Test]
    public void RowCountAgreesWithTheStoredRowsAfterMixedCaseInsertsTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
        m_engine.Execute("INSERT INTO users (Id, Name) VALUES (1, 'a')");
        m_engine.Execute("INSERT INTO Users (Id, Name) VALUES (2, 'b')");

        var counted = SelectLong("SELECT COUNT(*) FROM Users");
        var actual = SelectStrings("SELECT Name FROM Users").Length;

        Assert.That(counted, Is.EqualTo(actual),
            "COUNT(*) must agree with the number of rows a SELECT returns");
    }

    [Test]
    public void UpdateWithDifferingCaseAffectsTheStoredRowTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
        m_engine.Execute("INSERT INTO Users (Id, Name) VALUES (1, 'before')");

        m_engine.Execute("UPDATE users SET Name = 'after' WHERE Id = 1");

        Assert.That(SelectStrings("SELECT Name FROM Users"), Is.EqualTo(new[] { "after" }));
    }

    [Test]
    public void DeleteWithDifferingCaseRemovesTheStoredRowTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
        m_engine.Execute("INSERT INTO Users (Id, Name) VALUES (1, 'a')");

        m_engine.Execute("DELETE FROM users WHERE Id = 1");

        Assert.That(SelectStrings("SELECT Name FROM Users"), Is.Empty);
    }

    [Test]
    public void TruncateWithDifferingCaseRemovesTheStoredRowsTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
        m_engine.Execute("INSERT INTO Users (Id, Name) VALUES (1, 'a')");
        m_engine.Execute("INSERT INTO Users (Id, Name) VALUES (2, 'b')");

        m_engine.Execute("TRUNCATE TABLE users");

        Assert.Multiple(() =>
        {
            Assert.That(SelectStrings("SELECT Name FROM Users"), Is.Empty,
                "TRUNCATE must delete the rows, not only reset the counter");
            Assert.That(SelectLong("SELECT COUNT(*) FROM Users"), Is.Zero);
        });
    }

    #endregion

    #region Drop Table Data Lifecycle

    [Test]
    public void RecreatedTableDoesNotServeTheDroppedTablesRowsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V VARCHAR(20) NOT NULL)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 'old')");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 'older')");

        m_engine.Execute("DROP TABLE T");
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V VARCHAR(20) NOT NULL)");

        Assert.Multiple(() =>
        {
            Assert.That(SelectStrings("SELECT V FROM T"), Is.Empty,
                "A recreated table must not contain the dropped table's rows");
            Assert.That(SelectStrings("SELECT V FROM T WHERE Id = 1"), Is.Empty);
            Assert.That(SelectLong("SELECT COUNT(*) FROM T"), Is.Zero);
        });
    }

    [Test]
    public void DropTableWithDifferingCaseRemovesTheRowsTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, V VARCHAR(20) NOT NULL)");
        m_engine.Execute("INSERT INTO Users (Id, V) VALUES (1, 'old')");

        m_engine.Execute("DROP TABLE users");
        m_engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, V VARCHAR(20) NOT NULL)");

        Assert.That(SelectStrings("SELECT V FROM Users"), Is.Empty);
    }

    [Test]
    public void DroppedTableIndexesAreGoneSoTheyCanBeRecreatedTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 100)");

        m_engine.Execute("DROP TABLE T");

        Assert.Multiple(() =>
        {
            Assert.That(m_engine.GetIndex("IX_T_V"), Is.Null,
                "DROP TABLE must drop the table's indexes too");
            Assert.DoesNotThrow(
                () => m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL)"),
                "Recreating the table must not collide with a leftover implicit PK index");
            Assert.DoesNotThrow(
                () => m_engine.Execute("CREATE INDEX IX_T_V ON T (V)"),
                "Recreating the index must not collide with a leftover definition");
        });
    }

    [Test]
    public void RecreatedIndexDoesNotResolveDroppedRowsTest()
    {
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 100)");

        m_engine.Execute("DROP TABLE T");
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT NOT NULL)");
        m_engine.Execute("CREATE INDEX IX_T_V ON T (V)");

        Assert.That(SelectStrings("SELECT Id FROM T WHERE V = 100"), Is.Empty,
            "An indexed lookup must not resurrect rows from the dropped table");
    }

    #endregion

    #region Helper Methods

    private string[] SelectStrings(string sql)
    {
        return m_engine.Query(sql)
            .Select(row => row[0].IsNull ? string.Empty : row[0].AsString())
            .ToArray();
    }

    private long SelectLong(string sql)
    {
        return m_engine.Query(sql).Select(row => row[0].AsInt64()).First();
    }

    #endregion
}
