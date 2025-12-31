using NSubstitute;
using OutWit.Database.Definitions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Statements;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for SELECT statement execution.
/// </summary>
[TestFixture]
public class StatementExecutorSelectTests : StatementExecutorTestsBase
{
    #region Basic SELECT Tests

    [Test]
    public void SelectLiteralReturnsValueTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT 42") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void SelectMultipleLiteralsTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT 1, 'hello', 3.14") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[0][1].AsString(), Is.EqualTo("hello"));
        Assert.That(rows[0][2].AsDouble(), Is.EqualTo(3.14).Within(0.001));
    }

    [Test]
    public void SelectExpressionTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT 2 + 3 * 4") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(14)); // 2 + 12 = 14
    }

    [Test]
    public void SelectWithAliasTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT 42 AS answer") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        Assert.That(result.Columns, Has.Count.EqualTo(1));
        Assert.That(result.Columns[0].Name, Is.EqualTo("answer"));
    }

    #endregion

    #region SELECT FROM Table Tests

    [Test]
    public void SelectStarFromTableTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectSpecificColumnsTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT Name, Email FROM Users") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(result.Columns.Select(s => s.Name), Is.EquivalentTo(new[] { "Name", "Email" }));
    }

    #endregion

    #region SELECT with WHERE Tests

    [Test]
    public void SelectWithWhereEqualityTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Alice", "alice2@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Name = 'Alice'") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.All(r => r["Name"].AsString() == "Alice"), Is.True);
    }

    [Test]
    public void SelectWithWhereComparisonTest()
    {
        var table = CreateTableDef("Products",
            ("Id", WitDataType.Int64, true),
            ("Price", WitDataType.Decimal, false));
        m_database.GetTable("Products").Returns(table);
        m_database.CreateTableScan("Products").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Price", WitSqlValue.FromDecimal(10.0m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Price", WitSqlValue.FromDecimal(25.0m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Price", WitSqlValue.FromDecimal(50.0m)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Products WHERE Price > 20") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithWhereAndTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Alice", "alice2@example.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Name = 'Alice' AND Email LIKE '%test%'") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Email"].AsString(), Is.EqualTo("alice@test.com"));
    }

    [Test]
    public void SelectWithWhereOrTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Name = 'Alice' OR Name = 'Bob'") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithWhereInTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "alice@test.com"),
            CreateUserRow(2, "Bob", "bob@test.com"),
            CreateUserRow(3, "Charlie", "charlie@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Name IN ('Alice', 'Charlie')") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithWhereBetweenTest()
    {
        var table = CreateTableDef("Products",
            ("Id", WitDataType.Int64, true),
            ("Price", WitDataType.Decimal, false));
        m_database.GetTable("Products").Returns(table);
        m_database.CreateTableScan("Products").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Price", WitSqlValue.FromDecimal(10.0m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Price", WitSqlValue.FromDecimal(25.0m))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Price", WitSqlValue.FromDecimal(50.0m)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Products WHERE Price BETWEEN 15 AND 30") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Price"].AsDecimal(), Is.EqualTo(25.0m));
    }

    [Test]
    public void SelectWithWhereIsNullTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Name", WitSqlValue.FromText("Alice")), ("Email", WitSqlValue.FromText("alice@test.com"))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Name", WitSqlValue.FromText("Bob")), ("Email", WitSqlValue.Null))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Email IS NULL") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Bob"));
    }

    #endregion

    #region SELECT with ORDER BY Tests

    [Test]
    public void SelectWithOrderByAscTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(3, "Charlie", "c@test.com"),
            CreateUserRow(1, "Alice", "a@test.com"),
            CreateUserRow(2, "Bob", "b@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name ASC") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void SelectWithOrderByDescTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "a@test.com"),
            CreateUserRow(2, "Bob", "b@test.com"),
            CreateUserRow(3, "Charlie", "c@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users ORDER BY Name DESC") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Charlie"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    [Test]
    public void SelectWithOrderByMultipleColumnsTest()
    {
        var table = CreateTableDef("Items",
            ("Id", WitDataType.Int64, true),
            ("Category", WitDataType.StringVariable, false),
            ("Name", WitDataType.StringVariable, false));
        m_database.GetTable("Items").Returns(table);
        m_database.CreateTableScan("Items").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Category", WitSqlValue.FromText("B")), ("Name", WitSqlValue.FromText("Z"))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Category", WitSqlValue.FromText("A")), ("Name", WitSqlValue.FromText("Y"))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Category", WitSqlValue.FromText("A")), ("Name", WitSqlValue.FromText("X")))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Items ORDER BY Category ASC, Name ASC") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0]["Category"].AsString(), Is.EqualTo("A"));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("X"));
        Assert.That(rows[1]["Category"].AsString(), Is.EqualTo("A"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Y"));
        Assert.That(rows[2]["Category"].AsString(), Is.EqualTo("B"));
    }

    #endregion

    #region SELECT with LIMIT/OFFSET Tests

    [Test]
    public void SelectWithLimitTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "a@test.com"),
            CreateUserRow(2, "Bob", "b@test.com"),
            CreateUserRow(3, "Charlie", "c@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LIMIT 2") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void SelectWithLimitAndOffsetTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "a@test.com"),
            CreateUserRow(2, "Bob", "b@test.com"),
            CreateUserRow(3, "Charlie", "c@test.com"),
            CreateUserRow(4, "David", "d@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT * FROM Users LIMIT 2 OFFSET 1") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    #endregion

    #region SELECT DISTINCT Tests

    [Test]
    public void SelectDistinctTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "a@test.com"),
            CreateUserRow(2, "Alice", "b@test.com"),
            CreateUserRow(3, "Bob", "c@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT DISTINCT Name FROM Users") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));
    }

    #endregion

    #region SELECT with Aggregates Tests

    [Test]
    public void SelectCountStarTest()
    {
        var table = CreateUsersTable();
        m_database.GetTable("Users").Returns(table);
        m_database.CreateTableScan("Users").Returns(CreateMockIterator(
            CreateUserRow(1, "Alice", "a@test.com"),
            CreateUserRow(2, "Bob", "b@test.com"),
            CreateUserRow(3, "Charlie", "c@test.com")
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT COUNT(*) FROM Users") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void SelectSumTest()
    {
        var table = CreateTableDef("Orders",
            ("Id", WitDataType.Int64, true),
            ("Amount", WitDataType.Int32, false));
        m_database.GetTable("Orders").Returns(table);
        m_database.CreateTableScan("Orders").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Amount", WitSqlValue.FromInt(100))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Amount", WitSqlValue.FromInt(200))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Amount", WitSqlValue.FromInt(300)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT SUM(Amount) FROM Orders") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(600));
    }

    [Test]
    public void SelectAvgTest()
    {
        var table = CreateTableDef("Scores",
            ("Id", WitDataType.Int64, true),
            ("Score", WitDataType.Float64, false));
        m_database.GetTable("Scores").Returns(table);
        m_database.CreateTableScan("Scores").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Score", WitSqlValue.FromReal(80.0))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Score", WitSqlValue.FromReal(90.0))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Score", WitSqlValue.FromReal(100.0)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT AVG(Score) FROM Scores") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0][0].AsDouble(), Is.EqualTo(90.0).Within(0.001));
    }

    [Test]
    public void SelectMinMaxTest()
    {
        var table = CreateTableDef("NumericValues",
            ("Id", WitDataType.Int64, true),
            ("Value", WitDataType.Int32, false));
        m_database.GetTable("NumericValues").Returns(table);
        m_database.CreateTableScan("NumericValues").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Value", WitSqlValue.FromInt(5))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Value", WitSqlValue.FromInt(15))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Value", WitSqlValue.FromInt(10)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT MIN(Value), MAX(Value) FROM NumericValues") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows[0][0].AsInt64(), Is.EqualTo(5));
        Assert.That(rows[0][1].AsInt64(), Is.EqualTo(15));
    }

    #endregion

    #region SELECT with GROUP BY Tests

    [Test]
    public void SelectWithGroupByTest()
    {
        var table = CreateTableDef("Sales",
            ("Id", WitDataType.Int64, true),
            ("Category", WitDataType.StringVariable, false),
            ("Amount", WitDataType.Int32, false));
        m_database.GetTable("Sales").Returns(table);
        m_database.CreateTableScan("Sales").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Category", WitSqlValue.FromText("A")), ("Amount", WitSqlValue.FromInt(100))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Category", WitSqlValue.FromText("B")), ("Amount", WitSqlValue.FromInt(200))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Category", WitSqlValue.FromText("A")), ("Amount", WitSqlValue.FromInt(150)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("SELECT Category, SUM(Amount) FROM Sales GROUP BY Category") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(2));

        var rowA = rows.First(r => r["Category"].AsString() == "A");
        var rowB = rows.First(r => r["Category"].AsString() == "B");

        Assert.That(rowA[1].AsInt64(), Is.EqualTo(250));
        Assert.That(rowB[1].AsInt64(), Is.EqualTo(200));
    }

    [Test]
    public void SelectWithGroupByHavingTest()
    {
        var table = CreateTableDef("Sales",
            ("Id", WitDataType.Int64, true),
            ("Category", WitDataType.StringVariable, false),
            ("Amount", WitDataType.Int32, false));
        m_database.GetTable("Sales").Returns(table);
        m_database.CreateTableScan("Sales").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Category", WitSqlValue.FromText("A")), ("Amount", WitSqlValue.FromInt(100))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Category", WitSqlValue.FromText("B")), ("Amount", WitSqlValue.FromInt(200))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Category", WitSqlValue.FromText("A")), ("Amount", WitSqlValue.FromInt(150)))
        ));

        var executor = new StatementExecutor(m_context);
        // HAVING with aggregate function - should work correctly now
        var stmt = WitSql.ParseStatement("SELECT Category, SUM(Amount) AS Total FROM Sales GROUP BY Category HAVING SUM(Amount) > 200") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);

        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Category"].AsString(), Is.EqualTo("A"));
    }

    [Test]
    public void SelectWithGroupByOrderByAggregateTest()
    {
        // This is the query that fails in benchmarks:
        // SELECT Region, COUNT(*), SUM(Amount) FROM Sales GROUP BY Region ORDER BY SUM(Amount) DESC
        var table = CreateTableDef("Sales",
            ("Id", WitDataType.Int64, true),
            ("Region", WitDataType.StringVariable, false),
            ("Amount", WitDataType.Float64, false),
            ("Quantity", WitDataType.Int32, false));
        m_database.GetTable("Sales").Returns(table);
        m_database.CreateTableScan("Sales").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Region", WitSqlValue.FromText("North")), ("Amount", WitSqlValue.FromReal(100.0)), ("Quantity", WitSqlValue.FromInt(5))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Region", WitSqlValue.FromText("South")), ("Amount", WitSqlValue.FromReal(300.0)), ("Quantity", WitSqlValue.FromInt(10))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Region", WitSqlValue.FromText("North")), ("Amount", WitSqlValue.FromReal(200.0)), ("Quantity", WitSqlValue.FromInt(8))),
            CreateRow(("_rowid", WitSqlValue.FromInt(4)), ("Id", WitSqlValue.FromInt(4)), ("Region", WitSqlValue.FromText("East")), ("Amount", WitSqlValue.FromReal(50.0)), ("Quantity", WitSqlValue.FromInt(2)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            SELECT Region, COUNT(*), SUM(Amount) 
            FROM Sales 
            GROUP BY Region 
            ORDER BY SUM(Amount) DESC") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);
        var rows = result.ReadAll();

        // North: 100 + 200 = 300
        // South: 300
        // East: 50
        // ORDER BY SUM(Amount) DESC: South (300), North (300), East (50)
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Region"].AsString(), Is.EqualTo("South").Or.EqualTo("North")); // Both have 300
        Assert.That(rows[2]["Region"].AsString(), Is.EqualTo("East")); // Lowest sum
    }

    [Test]
    public void SelectComplexAggregationWithOrderByTest()
    {
        // Full benchmark query that fails
        var table = CreateTableDef("Sales",
            ("Id", WitDataType.Int64, true),
            ("Region", WitDataType.StringVariable, false),
            ("Amount", WitDataType.Float64, false),
            ("Quantity", WitDataType.Int32, false));
        m_database.GetTable("Sales").Returns(table);
        m_database.CreateTableScan("Sales").Returns(CreateMockIterator(
            CreateRow(("_rowid", WitSqlValue.FromInt(1)), ("Id", WitSqlValue.FromInt(1)), ("Region", WitSqlValue.FromText("North")), ("Amount", WitSqlValue.FromReal(100.0)), ("Quantity", WitSqlValue.FromInt(5))),
            CreateRow(("_rowid", WitSqlValue.FromInt(2)), ("Id", WitSqlValue.FromInt(2)), ("Region", WitSqlValue.FromText("South")), ("Amount", WitSqlValue.FromReal(300.0)), ("Quantity", WitSqlValue.FromInt(10))),
            CreateRow(("_rowid", WitSqlValue.FromInt(3)), ("Id", WitSqlValue.FromInt(3)), ("Region", WitSqlValue.FromText("North")), ("Amount", WitSqlValue.FromReal(200.0)), ("Quantity", WitSqlValue.FromInt(8)))
        ));

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            SELECT 
                Region,
                COUNT(*),
                SUM(Amount),
                AVG(Amount),
                MIN(Quantity),
                MAX(Quantity)
            FROM Sales
            GROUP BY Region
            ORDER BY SUM(Amount) DESC") as WitSqlStatementSelect;

        var result = executor.Execute(stmt!);
        var rows = result.ReadAll();

        Assert.That(rows, Has.Count.EqualTo(2));
        // North has SUM(Amount) = 300, South has 300 - order may vary for equal values
        // But both should be present
        var regions = rows.Select(r => r["Region"].AsString()).ToList();
        Assert.That(regions, Does.Contain("North"));
        Assert.That(regions, Does.Contain("South"));
    }

    #endregion
}
