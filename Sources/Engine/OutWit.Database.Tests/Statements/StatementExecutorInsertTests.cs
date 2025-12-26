using NSubstitute;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using OutWit.Database.Values;
using DbDefinitions = OutWit.Database.Definitions;
using DbTypes = OutWit.Database.Types;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for INSERT statement execution.
/// </summary>
[TestFixture]
public class StatementExecutorInsertTests : StatementExecutorTestsBase
{
    #region Basic INSERT Tests

    [Test]
    public void InsertWithColumnsAndValuesTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).InsertRow("Users", Arg.Is<WitSqlRow>(r =>
            r["Name"].AsString() == "Alice" &&
            r["Email"].AsString() == "alice@test.com"));
    }

    [Test]
    public void InsertWithoutColumnNamesTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users VALUES (1, 'Bob', 'bob@test.com')");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        m_database.Received(1).InsertRow("Users", Arg.Any<WitSqlRow>());
    }

    [Test]
    public void InsertMultipleRowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L, 2L, 3L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email) VALUES ('Alice', 'a@test.com'), ('Bob', 'b@test.com'), ('Charlie', 'c@test.com')");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(3));
        m_database.Received(3).InsertRow("Users", Arg.Any<WitSqlRow>());
    }

    #endregion

    #region Auto-Increment Tests

    [Test]
    public void InsertAutoIncrementTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(42L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email) VALUES ('Alice', 'alice@test.com')");

        executor.Execute(stmt);

        Assert.That(m_context.LastInsertRowId, Is.EqualTo(42));
        m_database.Received(1).InsertRow("Users", Arg.Is<WitSqlRow>(r =>
            r["Id"].AsInt64() == 42));
    }

    [Test]
    public void InsertWithExplicitIdIgnoresAutoIncrementTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Id, Name, Email) VALUES (100, 'Alice', 'alice@test.com')");

        executor.Execute(stmt);

        m_database.Received(1).InsertRow("Users", Arg.Is<WitSqlRow>(r =>
            r["Id"].AsInt64() == 100));
    }

    #endregion

    #region Default Values Tests

    [Test]
    public void InsertWithDefaultValueTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Items",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "Name", Type = DbTypes.WitDataType.StringVariable, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Status", Type = DbTypes.WitDataType.StringVariable, DefaultValue = "'active'", Ordinal = 2 }
            ]
        };
        m_database.GetTable("Items").Returns(table);
        m_database.GetNextAutoIncrement("Items").Returns(1L);
        m_database.CreateTableScan("Items").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Items (Name) VALUES ('Test Item')");

        executor.Execute(stmt);

        m_database.Received(1).InsertRow("Items", Arg.Is<WitSqlRow>(r =>
            r["Name"].AsString() == "Test Item" &&
            r["Status"].AsString() == "active"));
    }

    [Test]
    public void InsertNullForMissingNullableColumnTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Items",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "Name", Type = DbTypes.WitDataType.StringVariable, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Description", Type = DbTypes.WitDataType.StringVariable, Nullable = true, Ordinal = 2 }
            ]
        };
        m_database.GetTable("Items").Returns(table);
        m_database.GetNextAutoIncrement("Items").Returns(1L);
        m_database.CreateTableScan("Items").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Items (Name) VALUES ('Test')");

        executor.Execute(stmt);

        m_database.Received(1).InsertRow("Items", Arg.Is<WitSqlRow>(r =>
            r["Description"].IsNull));
    }

    #endregion

    #region NOT NULL Constraint Tests

    [Test]
    public void InsertViolatesNotNullThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Email) VALUES ('test@test.com')");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("NOT NULL"));
        Assert.That(ex.Message, Does.Contain("Name"));
    }

    [Test]
    public void InsertExplicitNullViolatesNotNullThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email) VALUES (NULL, 'test@test.com')");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("NOT NULL"));
    }

    #endregion

    #region UNIQUE Constraint Tests

    [Test]
    public void InsertViolatesUniqueThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L, 2L);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("existing@test.com")),
                ("Age", WitSqlValue.FromInt(25))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email, Age) VALUES ('Bob', 'existing@test.com', 30)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
        Assert.That(ex.Message, Does.Contain("Email"));
    }

    [Test]
    public void InsertNullDoesNotViolateUniqueTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Items",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "Code", Type = DbTypes.WitDataType.StringVariable, IsUnique = true, Nullable = true, Ordinal = 1 }
            ]
        };
        m_database.GetTable("Items").Returns(table);
        // First row already exists with Id=1, so next auto-increment should return 2
        m_database.GetNextAutoIncrement("Items").Returns(2L);
        m_database.CreateTableScan("Items").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Code", WitSqlValue.Null))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Items (Code) VALUES (NULL)");

        // Should not throw - NULL doesn't violate UNIQUE
        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion

    #region CHECK Constraint Tests

    [Test]
    public void InsertViolatesCheckThrowsTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email, Age) VALUES ('Alice', 'alice@test.com', -5)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("CHECK"));
        Assert.That(ex.Message, Does.Contain("Age"));
    }

    [Test]
    public void InsertPassesCheckTest()
    {
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email, Age) VALUES ('Alice', 'alice@test.com', 25)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion

    #region FOREIGN KEY Constraint Tests

    [Test]
    public void InsertViolatesForeignKeyThrowsTest()
    {
        var usersTable = CreateUsersTable();
        var ordersTable = CreateOrdersTableWithFK();

        m_database.GetTable("Users").Returns(usersTable);
        m_database.GetTable("Orders").Returns(ordersTable);
        m_database.GetNextAutoIncrement("Orders").Returns(1L);
        m_database.CreateTableScan("Orders").Returns(CreateEmptyIterator());
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator()); // No users exist

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Orders (UserId, Total) VALUES (999, 100.00)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("FOREIGN KEY"));
    }

    [Test]
    public void InsertPassesForeignKeyTest()
    {
        var usersTable = CreateUsersTable();
        var ordersTable = CreateOrdersTableWithFK();

        m_database.GetTable("Users").Returns(usersTable);
        m_database.GetTable("Orders").Returns(ordersTable);
        m_database.GetNextAutoIncrement("Orders").Returns(1L);
        m_database.CreateTableScan("Orders").Returns(CreateEmptyIterator());
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Orders (UserId, Total) VALUES (1, 100.00)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    [Test]
    public void InsertNullForeignKeyIsAllowedTest()
    {
        var usersTable = CreateUsersTable();
        var ordersTable = new DbDefinitions.DefinitionTable
        {
            Name = "Orders",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "UserId", Type = DbTypes.WitDataType.Int64, Nullable = true, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Total", Type = DbTypes.WitDataType.Decimal, Ordinal = 2 }
            ],
            ForeignKeys =
            [
                new DbDefinitions.DefinitionForeignKey
                {
                    Columns = ["UserId"],
                    ForeignTable = "Users",
                    ForeignColumns = ["Id"]
                }
            ]
        };

        m_database.GetTable("Users").Returns(usersTable);
        m_database.GetTable("Orders").Returns(ordersTable);
        m_database.GetNextAutoIncrement("Orders").Returns(1L);
        m_database.CreateTableScan("Orders").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Orders (UserId, Total) VALUES (NULL, 50.00)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion

    #region INSERT ... SELECT Tests

    [Test]
    public void InsertFromSelectTest()
    {
        var sourceTable = CreateUsersTable();
        var targetTable = new DbDefinitions.DefinitionTable
        {
            Name = "UsersArchive",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "Name", Type = DbTypes.WitDataType.StringVariable, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Email", Type = DbTypes.WitDataType.StringVariable, Ordinal = 2 }
            ]
        };

        m_database.GetTable("Users").Returns(sourceTable);
        m_database.GetTable("UsersArchive").Returns(targetTable);
        m_database.GetNextAutoIncrement("UsersArchive").Returns(1L, 2L);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com")
        ));
        m_database.CreateTableScan("UsersArchive").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO UsersArchive SELECT * FROM Users");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(2).InsertRow("UsersArchive", Arg.Any<WitSqlRow>());
    }

    #endregion

    #region Table Not Found Tests

    [Test]
    public void InsertIntoNonExistentTableThrowsTest()
    {
        m_database.GetTable("NonExistent").Returns((DbDefinitions.DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO NonExistent (Name) VALUES ('Test')");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    #endregion

    #region Changes Count Tests

    [Test]
    public void InsertUpdatesLastChangesCountTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L, 2L, 3L);
        m_database.CreateTableScan("Users").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Name, Email) VALUES ('A', 'a@test.com'), ('B', 'b@test.com'), ('C', 'c@test.com')");

        executor.Execute(stmt);

        Assert.That(m_context.LastChangesCount, Is.EqualTo(3));
    }

    #endregion
}
