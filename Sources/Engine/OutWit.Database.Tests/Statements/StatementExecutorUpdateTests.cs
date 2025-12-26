using NSubstitute;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using OutWit.Database.Values;
using DbDefinitions = OutWit.Database.Definitions;
using DbTypes = OutWit.Database.Types;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for UPDATE statement execution.
/// </summary>
[TestFixture]
public class StatementExecutorUpdateTests : StatementExecutorTestsBase
{
    #region Basic UPDATE Tests

    [Test]
    public void UpdateAllRowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@old.com"),
            CreateUserRow(2, "Bob", "bob@old.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Email = 'updated@test.com'");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(2).UpdateRow("Users", Arg.Any<long>(), Arg.Is<WitSqlRow>(r =>
            r["Email"].AsString() == "updated@test.com"));
    }

    [Test]
    public void UpdateWithWhereClauseTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Name = 'Updated' WHERE Id = 2");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).UpdateRow("Users", 2, Arg.Is<WitSqlRow>(r =>
            r["Name"].AsString() == "Updated"));
    }

    [Test]
    public void UpdateMultipleColumnsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Name = 'New Name', Email = 'new@test.com' WHERE Id = 1");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).UpdateRow("Users", 1, Arg.Is<WitSqlRow>(r =>
            r["Name"].AsString() == "New Name" &&
            r["Email"].AsString() == "new@test.com"));
    }

    [Test]
    public void UpdateNoMatchingRowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Name = 'Test' WHERE Id = 999");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(0));
        m_database.DidNotReceive().UpdateRow(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<WitSqlRow>());
    }

    #endregion

    #region UPDATE with Expressions Tests

    [Test]
    public void UpdateWithExpressionTest()
    {
        var table = CreateTableDef("Products",
            ("Id", DbTypes.WitDataType.Int64, true),
            ("Price", DbTypes.WitDataType.Decimal, false));
        m_database.GetTable("Products").Returns(table);
        m_database.CreateTableScan("Products").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Price", WitSqlValue.FromDecimal(100.0m)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Products SET Price = Price * 1.10");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).UpdateRow("Products", 1, Arg.Is<WitSqlRow>(r =>
            r["Price"].AsDecimal() == 110.0m));
    }

    [Test]
    public void UpdateWithColumnReferenceTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Items",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "Value1", Type = DbTypes.WitDataType.Int32, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Value2", Type = DbTypes.WitDataType.Int32, Ordinal = 2 }
            ]
        };
        m_database.GetTable("Items").Returns(table);
        m_database.CreateTableScan("Items").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Value1", WitSqlValue.FromInt(10)),
                ("Value2", WitSqlValue.FromInt(20))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Items SET Value1 = Value2");

        var result = executor.Execute(stmt);

        m_database.Received(1).UpdateRow("Items", 1, Arg.Is<WitSqlRow>(r =>
            r["Value1"].AsInt64() == 20));
    }

    #endregion

    #region UPDATE Constraint Validation Tests

    [Test]
    public void UpdateViolatesNotNullThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com")),
                ("Age", WitSqlValue.FromInt(25))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Name = NULL WHERE Id = 1");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("NOT NULL"));
    }

    [Test]
    public void UpdateViolatesUniqueThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com")),
                ("Age", WitSqlValue.FromInt(25))
            ),
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(2)),
                ("Id", WitSqlValue.FromInt(2)),
                ("Name", WitSqlValue.FromText("Bob")),
                ("Email", WitSqlValue.FromText("bob@test.com")),
                ("Age", WitSqlValue.FromInt(30))
            )
        ));

        var executor = new StatementExecutor(m_context);
        // Try to set Bob's email to Alice's email
        var stmt = WitSql.ParseStatement("UPDATE Users SET Email = 'alice@test.com' WHERE Id = 2");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    [Test]
    public void UpdateSameValueDoesNotViolateUniqueTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com")),
                ("Age", WitSqlValue.FromInt(25))
            )
        ));

        var executor = new StatementExecutor(m_context);
        // Update to same value should work
        var stmt = WitSql.ParseStatement("UPDATE Users SET Email = 'alice@test.com' WHERE Id = 1");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    [Test]
    public void UpdateViolatesCheckThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com")),
                ("Age", WitSqlValue.FromInt(25))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Age = 200 WHERE Id = 1");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("CHECK"));
    }

    [Test]
    public void UpdateViolatesForeignKeyThrowsTest()
    {
        var usersTable = CreateUsersTable();
        var ordersTable = CreateOrdersTableWithFK();

        m_database.GetTable("Users").Returns(usersTable);
        m_database.GetTable("Orders").Returns(ordersTable);
        m_database.CreateTableScan("Orders").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("UserId", WitSqlValue.FromInt(1)),
                ("Total", WitSqlValue.FromDecimal(100.0m))
            )
        ));
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Orders SET UserId = 999 WHERE Id = 1");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("FOREIGN KEY"));
    }

    #endregion

    #region UPDATE Changes Count Tests

    [Test]
    public void UpdateUpdatesLastChangesCountTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Name = 'Updated' WHERE Id <= 2");

        executor.Execute(stmt);

        Assert.That(m_context.LastChangesCount, Is.EqualTo(2));
    }

    #endregion

    #region UPDATE Table Not Found Tests

    [Test]
    public void UpdateNonExistentTableThrowsTest()
    {
        m_database.GetTable("NonExistent").Returns((DbDefinitions.DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE NonExistent SET Name = 'Test'");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    #endregion
}
