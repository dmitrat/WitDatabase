using NSubstitute;
using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Base class for WitSqlEngine tests providing common test infrastructure.
/// </summary>
public abstract class WitSqlEngineTestsBase
{
    #region Fields

    protected Engine.WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public virtual void Setup()
    {
        var database = WitDatabase.CreateInMemory();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
    }

    [TearDown]
    public virtual void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Table Helpers

    /// <summary>
    /// Creates a simple test table.
    /// </summary>
    protected void CreateTestTable(string tableName = "TestTable")
    {
        m_engine.Execute($@"
            CREATE TABLE {tableName} (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Value INT
            )");
    }

    /// <summary>
    /// Creates a Users table for testing.
    /// </summary>
    protected void CreateUsersTable()
    {
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Email VARCHAR(255)
            )");
    }

    /// <summary>
    /// Inserts test users and returns their IDs.
    /// </summary>
    protected void InsertTestUsers()
    {
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Bob', 'bob@test.com')");
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Charlie', 'charlie@test.com')");
    }

    #endregion

    #region Row Helpers

    /// <summary>
    /// Creates a row with the specified columns.
    /// </summary>
    protected static WitSqlRow CreateRow(params (string name, WitSqlValue value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => c.value).ToArray();
        return new WitSqlRow(values, names);
    }

    #endregion
}
