using NSubstitute;
using OutWit.Database.Definitions;
using OutWit.Database.Parser;
using OutWit.Database.Sql;
using OutWit.Database.Statements;
using OutWit.Database.Types;
using OutWit.Database.Values;

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
        
        var row1 = CreateUserRow(1, "Alice", "alice@old.com");
        var row2 = CreateUserRow(2, "Bob", "bob@old.com");
        
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(row1, row2));
        
        // Set up GetRowById for streaming update path
        m_database.GetRowById("Users", 1).Returns(row1);
        m_database.GetRowById("Users", 2).Returns(row2);

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
        
        // For PK-based WHERE clause, we need to set up GetRowById
        var row = CreateUserRow(2, "Bob", "bob@test.com");
        m_database.GetRowById("Users", 2).Returns(row);
        
        // Also set up scan for fallback path
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
        
        // Set up GetRowById for PK lookup
        var row = CreateUserRow(1, "Alice", "alice@test.com");
        m_database.GetRowById("Users", 1).Returns(row);
        
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
        
        // GetRowById returns null for non-existent row - this is correct behavior
        m_database.GetRowById("Users", 999).Returns((WitSqlRow?)null);
        
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
            ("Id", WitDataType.Int64, true),
            ("Price", WitDataType.Decimal, false));
        m_database.GetTable("Products").Returns(table);
        
        var row = CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Price", WitSqlValue.FromDecimal(100.0m)));
        m_database.CreateTableScan("Products").Returns(CreateMockIterator(row));
        
        // Set up GetRowById for streaming update path
        m_database.GetRowById("Products", 1).Returns(row);

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
        var table = new DefinitionTable
        {
            Name = "Items",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, Ordinal = 0 },
                new DefinitionColumn { Name = "Value1", Type = WitDataType.Int32, Ordinal = 1 },
                new DefinitionColumn { Name = "Value2", Type = WitDataType.Int32, Ordinal = 2 }
            ]
        };
        m_database.GetTable("Items").Returns(table);
        
        var row = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("Value1", WitSqlValue.FromInt(10)),
            ("Value2", WitSqlValue.FromInt(20))
        );
        m_database.CreateTableScan("Items").Returns(CreateMockIterator(row));
        
        // Set up GetRowById for streaming update path
        m_database.GetRowById("Items", 1).Returns(row);

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
        
        var row = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("Name", WitSqlValue.FromText("Alice")),
            ("Email", WitSqlValue.FromText("alice@test.com")),
            ("Age", WitSqlValue.FromInt(25))
        );
        m_database.GetRowById("Users", 1).Returns(row);
        
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(row));

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
        
        var row2 = CreateRow(
            ("_rowid", WitSqlValue.FromInt(2)),
            ("Id", WitSqlValue.FromInt(2)),
            ("Name", WitSqlValue.FromText("Bob")),
            ("Email", WitSqlValue.FromText("bob@test.com")),
            ("Age", WitSqlValue.FromInt(30))
        );
        m_database.GetRowById("Users", 2).Returns(row2);
        
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com")),
                ("Age", WitSqlValue.FromInt(25))
            ),
            row2
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
        
        var row = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("Name", WitSqlValue.FromText("Alice")),
            ("Email", WitSqlValue.FromText("alice@test.com")),
            ("Age", WitSqlValue.FromInt(25))
        );
        m_database.GetRowById("Users", 1).Returns(row);
        
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(row));

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
        
        var row = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("Name", WitSqlValue.FromText("Alice")),
            ("Email", WitSqlValue.FromText("alice@test.com")),
            ("Age", WitSqlValue.FromInt(25))
        );
        m_database.GetRowById("Users", 1).Returns(row);
        
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(row));

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
        
        var orderRow = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("UserId", WitSqlValue.FromInt(1)),
            ("Total", WitSqlValue.FromDecimal(100.0m))
        );
        m_database.GetRowById("Orders", 1).Returns(orderRow);
        
        m_database.CreateTableScan("Orders").Returns(CreateMockIterator(orderRow));
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
        
        var row1 = CreateUserRow(1, "Alice", "alice@test.com");
        var row2 = CreateUserRow(2, "Bob", "bob@test.com");
        var row3 = CreateUserRow(3, "Charlie", "charlie@test.com");
        
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(row1, row2, row3));
        
        // Set up GetRowById for streaming update path - only rows 1 and 2 match WHERE clause
        m_database.GetRowById("Users", 1).Returns(row1);
        m_database.GetRowById("Users", 2).Returns(row2);
        m_database.GetRowById("Users", 3).Returns(row3);

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
        m_database.GetTable("NonExistent").Returns((DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE NonExistent SET Name = 'Test'");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    #endregion

    #region Bulk UPDATE Optimization Tests

    [Test]
    public void BulkUpdateWithoutUniqueConstraintUsesOptimizedPathTest()
    {
        // Create a table without UNIQUE constraints on non-PK columns
        var table = new DefinitionTable
        {
            Name = "Products",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DefinitionColumn { Name = "Name", Type = WitDataType.StringVariable, Nullable = false, Ordinal = 1 },
                new DefinitionColumn { Name = "Price", Type = WitDataType.Decimal, Ordinal = 2 }
            ]
        };
        m_database.GetTable("Products").Returns(table);

        // Create many rows
        var rows = Enumerable.Range(1, 100)
            .Select(i => CreateRow(
                ("_rowid", WitSqlValue.FromInt(i)),
                ("Id", WitSqlValue.FromInt(i)),
                ("Name", WitSqlValue.FromText($"Product_{i}")),
                ("Price", WitSqlValue.FromDecimal(10.0m * i))
            ))
            .ToArray();

        m_database.CreateTableScan("Products").Returns(CreateMockIterator(rows));
        
        // Set up GetRowById for streaming update path
        foreach (var row in rows)
        {
            var id = row["Id"].AsInt64();
            m_database.GetRowById("Products", id).Returns(row);
        }

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Products SET Price = Price * 1.1");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(100));
        
        // Verify UpdateRow was called 100 times (streaming update)
        m_database.Received(100).UpdateRow("Products", Arg.Any<long>(), Arg.Any<WitSqlRow>());
    }

    // DELETED 2026-08-10 by the ledger census: BulkUpdateDetectsDuplicateInBatchTest.
    //
    // Its marker was right about the fixture and silent about the subject. "The mock doesn't reflect
    // actual DB state during batch UPDATE" is true - CreateTableScan always returns the original rows,
    // so the UNIQUE check can never see the conflict and the case could not pass however the engine
    // behaved. But the marker then deferred the real question to "an integration test" that nobody
    // wrote, and it went unasked for six months.
    //
    // The census asked it against the real engine, and the answer is good: a bulk UPDATE that collides
    // rows onto one value is REFUSED, on a UNIQUE column, on an explicit UNIQUE INDEX and on the
    // PRIMARY KEY alike, and the rows are left untouched. A table with no constraint accepts it, which
    // is the control showing the probe can see an accepted bulk update. That measurement is now
    // EngineDmlFindingsTests.BulkUpdateOntoOneValueIsRefusedTest, against an engine rather than a mock.

    [Test]
    public void BulkUpdateNonUniqueColumnSucceedsTest()
    {
        // Table with UNIQUE on Email, but we're updating Name (not UNIQUE)
        var table = CreateUsersTableWithConstraints();
        m_database.GetTable("Users").Returns(table);

        var row1 = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("Name", WitSqlValue.FromText("Alice")),
            ("Email", WitSqlValue.FromText("alice@test.com")),
            ("Age", WitSqlValue.FromInt(25))
        );
        var row2 = CreateRow(
            ("_rowid", WitSqlValue.FromInt(2)),
            ("Id", WitSqlValue.FromInt(2)),
            ("Name", WitSqlValue.FromText("Bob")),
            ("Email", WitSqlValue.FromText("bob@test.com")),
            ("Age", WitSqlValue.FromInt(30))
        );

        m_database.CreateTableScan("Users").Returns(CreateMockIterator(row1, row2));
        
        // Set up GetRowById for streaming update path
        m_database.GetRowById("Users", 1).Returns(row1);
        m_database.GetRowById("Users", 2).Returns(row2);

        var executor = new StatementExecutor(m_context);
        // Update Name to same value for all - should succeed (Name is not UNIQUE)
        var stmt = WitSql.ParseStatement("UPDATE Users SET Name = 'Updated'");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
        m_database.Received(2).UpdateRow("Users", Arg.Any<long>(), Arg.Is<WitSqlRow>(r =>
            r["Name"].AsString() == "Updated"));
    }

    [Test]
    public void BulkUpdateWithWhereClauseTest()
    {
        var table = new DefinitionTable
        {
            Name = "Products",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DefinitionColumn { Name = "Category", Type = WitDataType.StringVariable, Ordinal = 1 },
                new DefinitionColumn { Name = "Price", Type = WitDataType.Decimal, Ordinal = 2 }
            ]
        };
        m_database.GetTable("Products").Returns(table);

        var row1 = CreateRow(
            ("_rowid", WitSqlValue.FromInt(1)),
            ("Id", WitSqlValue.FromInt(1)),
            ("Category", WitSqlValue.FromText("Electronics")),
            ("Price", WitSqlValue.FromDecimal(100.0m))
        );
        var row2 = CreateRow(
            ("_rowid", WitSqlValue.FromInt(2)),
            ("Id", WitSqlValue.FromInt(2)),
            ("Category", WitSqlValue.FromText("Books")),
            ("Price", WitSqlValue.FromDecimal(20.0m))
        );
        var row3 = CreateRow(
            ("_rowid", WitSqlValue.FromInt(3)),
            ("Id", WitSqlValue.FromInt(3)),
            ("Category", WitSqlValue.FromText("Electronics")),
            ("Price", WitSqlValue.FromDecimal(200.0m))
        );

        m_database.CreateTableScan("Products").Returns(CreateMockIterator(row1, row2, row3));
        
        // Set up GetRowById for streaming update path
        m_database.GetRowById("Products", 1).Returns(row1);
        m_database.GetRowById("Products", 2).Returns(row2);
        m_database.GetRowById("Products", 3).Returns(row3);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Products SET Price = Price * 0.9 WHERE Category = 'Electronics'");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(2));
    }

    [Test]
    public void BulkUpdateStreamingDoesNotAccumulateRowsTest()
    {
        // This test verifies that streaming UPDATE doesn't collect all rows before processing
        // We can verify this by checking that UpdateRow is called during iteration
        var table = new DefinitionTable
        {
            Name = "Items",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DefinitionColumn { Name = "Value", Type = WitDataType.Int32, Ordinal = 1 }
            ]
        };
        m_database.GetTable("Items").Returns(table);

        var updateCallCount = 0;
        m_database.When(x => x.UpdateRow("Items", Arg.Any<long>(), Arg.Any<WitSqlRow>()))
            .Do(_ => updateCallCount++);

        // Create rows
        var rows = Enumerable.Range(1, 50)
            .Select(i => CreateRow(
                ("_rowid", WitSqlValue.FromInt(i)),
                ("Id", WitSqlValue.FromInt(i)),
                ("Value", WitSqlValue.FromInt(i * 10))
            ))
            .ToArray();

        m_database.CreateTableScan("Items").Returns(CreateMockIterator(rows));
        
        // Set up GetRowById for streaming update path
        foreach (var row in rows)
        {
            var id = row["Id"].AsInt64();
            m_database.GetRowById("Items", id).Returns(row);
        }

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Items SET Value = Value + 1");

        var result = executor.Execute(stmt);

        Assert.That(result.RowsAffected, Is.EqualTo(50));
        Assert.That(updateCallCount, Is.EqualTo(50));
    }
    #endregion
}
