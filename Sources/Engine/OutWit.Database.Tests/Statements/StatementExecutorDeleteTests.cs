using NSubstitute;
using OutWit.Database.Definitions;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for DELETE statement execution.
/// </summary>
[TestFixture]
public class StatementExecutorDeleteTests : StatementExecutorTestsBase
{
    #region Basic DELETE Tests

    [Test]
    public void DeleteAllRowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(3));
        m_database.Received(1).DeleteRow("Users", 1);
        m_database.Received(1).DeleteRow("Users", 2);
        m_database.Received(1).DeleteRow("Users", 3);
    }

    [Test]
    public void DeleteWithWhereClauseTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Name = 'Bob'");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).DeleteRow("Users", 2);
        m_database.DidNotReceive().DeleteRow("Users", 1);
        m_database.DidNotReceive().DeleteRow("Users", 3);
    }

    [Test]
    public void DeleteMultipleRowsWithConditionTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Alice", "alice2@test.com"),
            CreateUserRow(4, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Name = 'Alice'");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(1).DeleteRow("Users", 1);
        m_database.Received(1).DeleteRow("Users", 3);
    }

    [Test]
    public void DeleteNoMatchingRowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Id = 999");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(0));
        m_database.DidNotReceive().DeleteRow(Arg.Any<string>(), Arg.Any<long>());
    }

    #endregion

    #region DELETE with Complex WHERE Tests

    [Test]
    public void DeleteWithCompoundConditionTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Alice", "alice@example.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Name = 'Alice' AND Email LIKE '%test%'");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).DeleteRow("Users", 1);
    }

    [Test]
    public void DeleteWithInClauseTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com"),
            CreateUserRow(4, "David", "david@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Id IN (1, 3, 5)");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(1).DeleteRow("Users", 1);
        m_database.Received(1).DeleteRow("Users", 3);
    }

    [Test]
    public void DeleteWithBetweenTest()
    {
        var table = CreateTableDef("Orders",
            ("Id", WitDataType.Int64, true),
            ("Total", WitDataType.Decimal, false));
        m_database.GetTable("Orders").Returns(table);
        m_database.CreateTableScan("Orders").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Total", WitSqlValue.FromDecimal(50m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Total", WitSqlValue.FromDecimal(100m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Total", WitSqlValue.FromDecimal(150m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(4)), ("Id", WitSqlValue.FromInt(4)), ("Total", WitSqlValue.FromDecimal(200m)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Orders WHERE Total BETWEEN 75 AND 175");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(1).DeleteRow("Orders", 2);
        m_database.Received(1).DeleteRow("Orders", 3);
    }

    [Test]
    public void DeleteWithIsNullTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Name", WitSqlValue.FromText("Alice")), ("Email", WitSqlValue.FromText("alice@test.com"))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Name", WitSqlValue.FromText("Bob")), ("Email", WitSqlValue.Null)),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Name", WitSqlValue.FromText("Charlie")), ("Email", WitSqlValue.Null))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Email IS NULL");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(1).DeleteRow("Users", 2);
        m_database.Received(1).DeleteRow("Users", 3);
    }

    #endregion

    #region DELETE Changes Count Tests

    [Test]
    public void DeleteUpdatesLastChangesCountTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users WHERE Id <= 2");

        executor.Execute(stmt);

        Assert.That(m_context.LastChangesCount, Is.EqualTo(2));
    }

    #endregion

    #region DELETE from Empty Table Tests

    [Test]
    public void DeleteFromEmptyTableTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM Users");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(0));
        m_database.DidNotReceive().DeleteRow(Arg.Any<string>(), Arg.Any<long>());
    }

    #endregion

    #region DELETE Table Not Found Tests

    [Test]
    public void DeleteFromNonExistentTableTest()
    {
        m_database.GetTable("NonExistent").Returns((DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DELETE FROM NonExistent");

        // Should throw error for non-existent table
        Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
    }

    #endregion
}
