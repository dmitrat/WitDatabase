using NSubstitute;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using OutWit.Database.Values;
using DbDefinitions = OutWit.Database.Definitions;
using DbTypes = OutWit.Database.Types;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for constraint validation during DML operations.
/// </summary>
[TestFixture]
public class StatementExecutorConstraintTests : StatementExecutorTestsBase
{
    #region Primary Key Constraint Tests

    [Test]
    public void InsertDuplicatePrimaryKeyThrowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.GetNextAutoIncrement("Users").Returns(1L);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com"))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Users (Id, Name, Email) VALUES (1, 'Bob', 'bob@test.com')");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("PRIMARY KEY").Or.Contains("UNIQUE"));
    }

    [Test]
    public void UpdateToDuplicatePrimaryKeyThrowsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("Name", WitSqlValue.FromText("Alice")),
                ("Email", WitSqlValue.FromText("alice@test.com"))
            ),
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(2)),
                ("Id", WitSqlValue.FromInt(2)),
                ("Name", WitSqlValue.FromText("Bob")),
                ("Email", WitSqlValue.FromText("bob@test.com"))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("UPDATE Users SET Id = 1 WHERE Id = 2");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("UNIQUE").Or.Contains("PRIMARY KEY"));
    }

    #endregion

    #region Composite Primary Key Tests

    [Test]
    public void InsertDuplicateCompositePrimaryKeyThrowsTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "OrderItems",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "OrderId", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "ProductId", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Quantity", Type = DbTypes.WitDataType.Int32, Ordinal = 2 }
            ],
            PrimaryKey = ["OrderId", "ProductId"]
        };
        m_database.GetTable("OrderItems").Returns(table);
        m_database.CreateTableScan("OrderItems").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("OrderId", WitSqlValue.FromInt(1)),
                ("ProductId", WitSqlValue.FromInt(100)),
                ("Quantity", WitSqlValue.FromInt(2))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (1, 100, 5)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("UNIQUE").Or.Contains("PRIMARY KEY"));
    }

    [Test]
    public void InsertPartiallyMatchingCompositePrimaryKeySucceedsTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "OrderItems",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "OrderId", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "ProductId", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Quantity", Type = DbTypes.WitDataType.Int32, Ordinal = 2 }
            ],
            PrimaryKey = ["OrderId", "ProductId"]
        };
        m_database.GetTable("OrderItems").Returns(table);
        m_database.CreateTableScan("OrderItems").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("OrderId", WitSqlValue.FromInt(1)),
                ("ProductId", WitSqlValue.FromInt(100)),
                ("Quantity", WitSqlValue.FromInt(2))
            )
        ));

        var executor = new StatementExecutor(m_context);
        // Same OrderId, different ProductId - should succeed
        var stmt = WitSql.ParseStatement("INSERT INTO OrderItems (OrderId, ProductId, Quantity) VALUES (1, 200, 3)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion

    #region Composite Unique Constraint Tests

    [Test]
    public void InsertViolatesCompositeUniqueThrowsTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Employees",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "DepartmentId", Type = DbTypes.WitDataType.Int64, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "EmployeeNumber", Type = DbTypes.WitDataType.StringVariable, Ordinal = 2 }
            ],
            UniqueConstraints = [["DepartmentId", "EmployeeNumber"]]
        };
        m_database.GetTable("Employees").Returns(table);
        m_database.GetNextAutoIncrement("Employees").Returns(1L, 2L);
        m_database.CreateTableScan("Employees").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("Id", WitSqlValue.FromInt(1)),
                ("DepartmentId", WitSqlValue.FromInt(10)),
                ("EmployeeNumber", WitSqlValue.FromText("E001"))
            )
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Employees (DepartmentId, EmployeeNumber) VALUES (10, 'E001')");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("UNIQUE"));
    }

    #endregion

    #region Table-Level CHECK Constraint Tests

    [Test]
    public void InsertViolatesTableLevelCheckThrowsTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Dates",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "StartDate", Type = DbTypes.WitDataType.Int32, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "EndDate", Type = DbTypes.WitDataType.Int32, Ordinal = 2 }
            ],
            CheckExpressions = ["EndDate >= StartDate"]
        };
        m_database.GetTable("Dates").Returns(table);
        m_database.GetNextAutoIncrement("Dates").Returns(1L);
        m_database.CreateTableScan("Dates").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Dates (StartDate, EndDate) VALUES (100, 50)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("CHECK"));
    }

    [Test]
    public void InsertPassesTableLevelCheckTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Dates",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "StartDate", Type = DbTypes.WitDataType.Int32, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "EndDate", Type = DbTypes.WitDataType.Int32, Ordinal = 2 }
            ],
            CheckExpressions = ["EndDate >= StartDate"]
        };
        m_database.GetTable("Dates").Returns(table);
        m_database.GetNextAutoIncrement("Dates").Returns(1L);
        m_database.CreateTableScan("Dates").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("INSERT INTO Dates (StartDate, EndDate) VALUES (50, 100)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion

    #region Composite Foreign Key Tests

    [Test]
    public void InsertViolatesCompositeForeignKeyThrowsTest()
    {
        var parentTable = new DbDefinitions.DefinitionTable
        {
            Name = "Categories",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "TenantId", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "CategoryId", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "Name", Type = DbTypes.WitDataType.StringVariable, Ordinal = 2 }
            ],
            PrimaryKey = ["TenantId", "CategoryId"]
        };

        var childTable = new DbDefinitions.DefinitionTable
        {
            Name = "Products",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "TenantId", Type = DbTypes.WitDataType.Int64, Ordinal = 1 },
                new DbDefinitions.DefinitionColumn { Name = "CategoryId", Type = DbTypes.WitDataType.Int64, Ordinal = 2 }
            ],
            ForeignKeys =
            [
                new DbDefinitions.DefinitionForeignKey
                {
                    Columns = ["TenantId", "CategoryId"],
                    ForeignTable = "Categories",
                    ForeignColumns = ["TenantId", "CategoryId"]
                }
            ]
        };

        m_database.GetTable("Categories").Returns(parentTable);
        m_database.GetTable("Products").Returns(childTable);
        m_database.GetNextAutoIncrement("Products").Returns(1L);
        m_database.CreateTableScan("Products").Returns(CreateEmptyIterator());
        m_database.CreateTableScan("Categories").Returns(CreateMockIterator(
            CreateRow(
                ("_rowid", WitSqlValue.FromInt(1)),
                ("TenantId", WitSqlValue.FromInt(1)),
                ("CategoryId", WitSqlValue.FromInt(100)),
                ("Name", WitSqlValue.FromText("Electronics"))
            )
        ));

        var executor = new StatementExecutor(m_context);
        // TenantId=1 exists but CategoryId=999 does not
        var stmt = WitSql.ParseStatement("INSERT INTO Products (TenantId, CategoryId) VALUES (1, 999)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("FOREIGN KEY"));
    }

    #endregion

    #region NULL Value in Constraint Tests

    [Test]
    public void CheckConstraintSkippedForNullValueTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Products",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "Price", Type = DbTypes.WitDataType.Decimal, Nullable = true, CheckExpression = "Price > 0", Ordinal = 1 }
            ]
        };
        m_database.GetTable("Products").Returns(table);
        m_database.GetNextAutoIncrement("Products").Returns(1L);
        m_database.CreateTableScan("Products").Returns(CreateEmptyIterator());

        var executor = new StatementExecutor(m_context);
        // NULL value should skip CHECK constraint (SQL standard)
        var stmt = WitSql.ParseStatement("INSERT INTO Products (Price) VALUES (NULL)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    [Test]
    public void UniqueConstraintAllowsMultipleNullsTest()
    {
        var table = new DbDefinitions.DefinitionTable
        {
            Name = "Products",
            Columns =
            [
                new DbDefinitions.DefinitionColumn { Name = "Id", Type = DbTypes.WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DbDefinitions.DefinitionColumn { Name = "SKU", Type = DbTypes.WitDataType.StringVariable, IsUnique = true, Nullable = true, Ordinal = 1 }
            ]
        };
        m_database.GetTable("Products").Returns(table);
        // First row already exists with Id=1, so next auto-increment should return 2
        m_database.GetNextAutoIncrement("Products").Returns(2L);
        m_database.CreateTableScan("Products").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("SKU", WitSqlValue.Null))
        ));

        var executor = new StatementExecutor(m_context);
        // Second NULL should be allowed (SQL standard)
        var stmt = WitSql.ParseStatement("INSERT INTO Products (SKU) VALUES (NULL)");

        var result = executor.Execute(stmt);
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion
}
