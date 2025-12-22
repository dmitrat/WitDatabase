using OutWit.Database.Parser.Schema.AlterActions;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableConstraints;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Ddl;

/// <summary>
/// Tests for CREATE TABLE statement parsing (SS2.1, SS13.3, SS20).
/// Covers: basic create, IF NOT EXISTS, column constraints, table constraints,
/// named constraints, computed columns.
/// </summary>
[TestFixture]
public class CreateTableParserTests
{
    #region Basic CREATE TABLE (SS2.1)

    [Test]
    public void ParseCreateTableBasicTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE Users (Id INT, Name TEXT)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.TableName, Is.EqualTo("Users"));
        Assert.That(create.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseCreateTableIfNotExistsTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE IF NOT EXISTS Logs (Id INT)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.IfNotExists, Is.True);
    }

    [Test]
    public void ParseCreateTableSingleColumnTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Value INT)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(1));
        Assert.That(create.Columns[0].Name, Is.EqualTo("Value"));
    }

    [Test]
    public void ParseCreateTableManyColumnsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Id INT,
                Name VARCHAR(100),
                Description TEXT,
                Price DECIMAL,
                Quantity INT,
                IsActive BOOLEAN,
                CreatedAt DATETIME
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(7));
    }

    #endregion

    #region Column Constraints (SS2.1)

    [Test]
    public void ParseNotNullConstraintTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Name VARCHAR(100) NOT NULL)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].Constraints?.Any(c => c is ColumnConstraintNotNull), Is.True);
    }

    [Test]
    public void ParseNullConstraintTest()
    {
        // NULL constraint is the default (absence of NOT NULL)
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Name VARCHAR(100) NULL)");
        var create = (WitSqlStatementCreateTable)stmt;
        // NULL doesn't create a separate constraint in this parser
        Assert.That(create.Columns[0].Name, Is.EqualTo("Name"));
    }

    [Test]
    public void ParsePrimaryKeyConstraintTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Id INT PRIMARY KEY)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].Constraints?.Any(c => c is ColumnConstraintPrimaryKey), Is.True);
    }

    [Test]
    public void ParsePrimaryKeyAutoIncrementTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Id BIGINT PRIMARY KEY AUTOINCREMENT)");
        var create = (WitSqlStatementCreateTable)stmt;
        var pkConstraint = create.Columns[0].Constraints?.OfType<ColumnConstraintPrimaryKey>().FirstOrDefault();
        Assert.That(pkConstraint, Is.Not.Null);
        Assert.That(pkConstraint!.AutoIncrement, Is.True);
    }

    [Test]
    public void ParseUniqueConstraintTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Email VARCHAR(255) UNIQUE)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].Constraints?.Any(c => c is ColumnConstraintUnique), Is.True);
    }

    [Test]
    public void ParseDefaultLiteralConstraintTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Status VARCHAR(20) DEFAULT 'pending')");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].Constraints?.Any(c => c is ColumnConstraintDefault), Is.True);
    }

    [Test]
    public void ParseDefaultExpressionConstraintTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (CreatedAt DATETIME DEFAULT (NOW()))");
        var create = (WitSqlStatementCreateTable)stmt;
        var defaultConstraint = create.Columns[0].Constraints?.OfType<ColumnConstraintDefault>().FirstOrDefault();
        Assert.That(defaultConstraint, Is.Not.Null);
    }

    [Test]
    public void ParseCheckConstraintTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Price DECIMAL CHECK (Price >= 0))");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].Constraints?.Any(c => c is ColumnConstraintCheck), Is.True);
    }

    [Test]
    public void ParseReferencesConstraintTest()
    {
        var stmt = WitSql.ParseStatement(
            "CREATE TABLE Orders (UserId INT REFERENCES Users(Id))");
        var create = (WitSqlStatementCreateTable)stmt;
        var refConstraint = create.Columns[0].Constraints?.OfType<ColumnConstraintReferences>().FirstOrDefault();
        Assert.That(refConstraint, Is.Not.Null);
        Assert.That(refConstraint!.ForeignTable, Is.EqualTo("Users"));
        Assert.That(refConstraint.ForeignColumn, Is.EqualTo("Id"));
    }

    [Test]
    public void ParseReferencesWithOnDeleteTest()
    {
        var stmt = WitSql.ParseStatement(
            "CREATE TABLE Orders (UserId INT REFERENCES Users(Id) ON DELETE CASCADE)");
        var create = (WitSqlStatementCreateTable)stmt;
        var refConstraint = create.Columns[0].Constraints?.OfType<ColumnConstraintReferences>().First();
        Assert.That(refConstraint.OnDelete, Is.EqualTo(ReferenceActionType.Cascade));
    }

    [Test]
    public void ParseReferencesWithOnUpdateTest()
    {
        var stmt = WitSql.ParseStatement(
            "CREATE TABLE Orders (UserId INT REFERENCES Users(Id) ON UPDATE SET NULL)");
        var create = (WitSqlStatementCreateTable)stmt;
        var refConstraint = create.Columns[0].Constraints?.OfType<ColumnConstraintReferences>().First();
        Assert.That(refConstraint.OnUpdate, Is.EqualTo(ReferenceActionType.SetNull));
    }

    [Test]
    public void ParseMultipleConstraintsOnColumnTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Name VARCHAR(100) NOT NULL UNIQUE
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var constraints = create.Columns[0].Constraints;
        Assert.That(constraints?.Any(c => c is ColumnConstraintNotNull), Is.True);
        Assert.That(constraints?.Any(c => c is ColumnConstraintUnique), Is.True);
    }

    [Test]
    public void ParseAllConstraintsOnColumnTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL UNIQUE,
                Price DECIMAL DEFAULT 0 CHECK (Price >= 0),
                CategoryId INT REFERENCES Categories(Id) ON DELETE CASCADE
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(4));
    }

    #endregion

    #region Table Constraints (SS2.1)

    [Test]
    public void ParseTablePrimaryKeyConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE OrderItems (
                OrderId INT,
                ProductId INT,
                PRIMARY KEY (OrderId, ProductId)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var pk = create.Constraints?.OfType<TableConstraintPrimaryKey>().FirstOrDefault();
        Assert.That(pk, Is.Not.Null);
        Assert.That(pk!.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseTableUniqueConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                FirstName VARCHAR(50),
                LastName VARCHAR(50),
                UNIQUE (FirstName, LastName)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var unique = create.Constraints?.OfType<TableConstraintUnique>().FirstOrDefault();
        Assert.That(unique, Is.Not.Null);
        Assert.That(unique!.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseTableForeignKeyConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Orders (
                Id INT,
                UserId INT,
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var fk = create.Constraints?.OfType<TableConstraintForeignKey>().FirstOrDefault();
        Assert.That(fk, Is.Not.Null);
        Assert.That(fk!.ForeignTable, Is.EqualTo("Users"));
    }

    [Test]
    public void ParseTableForeignKeyMultiColumnTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE OrderDetails (
                OrderId INT,
                LineNumber INT,
                ProductId INT,
                FOREIGN KEY (OrderId, LineNumber) REFERENCES OrderLines(OrderId, LineNo) ON DELETE CASCADE
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var fk = create.Constraints?.OfType<TableConstraintForeignKey>().First();
        Assert.That(fk.Columns, Has.Count.EqualTo(2));
        Assert.That(fk.ForeignColumns, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseTableCheckConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Price DECIMAL,
                Quantity INT,
                CHECK (Price * Quantity <= 1000000)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Constraints?.Any(c => c is TableConstraintCheck), Is.True);
    }

    #endregion

    #region Named Constraints (SS13.3)

    [Test]
    public void ParseNamedPrimaryKeyConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id INT,
                CONSTRAINT PK_Users PRIMARY KEY (Id)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var pk = create.Constraints?.OfType<TableConstraintPrimaryKey>().First();
        Assert.That(pk.Name, Is.EqualTo("PK_Users"));
    }

    [Test]
    public void ParseNamedUniqueConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Email VARCHAR(100),
                CONSTRAINT UQ_Users_Email UNIQUE (Email)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var uniq = create.Constraints?.OfType<TableConstraintUnique>().First();
        Assert.That(uniq.Name, Is.EqualTo("UQ_Users_Email"));
    }

    [Test]
    public void ParseNamedForeignKeyConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Orders (
                UserId INT,
                CONSTRAINT FK_Orders_Users FOREIGN KEY (UserId) REFERENCES Users(Id)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var fk = create.Constraints?.OfType<TableConstraintForeignKey>().First();
        Assert.That(fk.Name, Is.EqualTo("FK_Orders_Users"));
    }

    [Test]
    public void ParseNamedCheckConstraintTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Price DECIMAL,
                CONSTRAINT CK_Products_Price CHECK (Price >= 0)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var check = create.Constraints?.OfType<TableConstraintCheck>().First();
        Assert.That(check.Name, Is.EqualTo("CK_Products_Price"));
    }

    [Test]
    public void ParseUnnamedConstraintStillWorksTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id INT,
                PRIMARY KEY (Id)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var pk = create.Constraints?.OfType<TableConstraintPrimaryKey>().First();
        Assert.That(pk.Name, Is.Null);
    }

    #endregion

    #region Computed Columns (SS20)

    [Test]
    public void ParseComputedColumnVirtualTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Orders (
                Quantity INT,
                Price DECIMAL,
                Total AS (Quantity * Price)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var totalCol = create.Columns[2];
        Assert.That(totalCol.IsComputed, Is.True);
        Assert.That(totalCol.ComputedType, Is.EqualTo(ComputedColumnType.Virtual));
    }

    [Test]
    public void ParseComputedColumnStoredTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                FirstName VARCHAR(50),
                LastName VARCHAR(50),
                FullName AS (FirstName || ' ' || LastName) STORED
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var fullNameCol = create.Columns[2];
        Assert.That(fullNameCol.IsComputed, Is.True);
        Assert.That(fullNameCol.ComputedType, Is.EqualTo(ComputedColumnType.Stored));
    }

    [Test]
    public void ParseComputedColumnExplicitVirtualTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Price DECIMAL,
                Tax AS (Price * 0.1) VIRTUAL
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var taxCol = create.Columns[1];
        Assert.That(taxCol.ComputedType, Is.EqualTo(ComputedColumnType.Virtual));
    }

    [Test]
    public void ParseComputedColumnWithFunctionTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Email VARCHAR(100),
                EmailLower AS (LOWER(Email)) STORED
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var emailLowerCol = create.Columns[1];
        Assert.That(emailLowerCol.IsComputed, Is.True);
    }

    [Test]
    public void ParseMixedRegularAndComputedColumnsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Items (
                Id INT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Qty INT DEFAULT 0,
                Price DECIMAL,
                Total AS (Qty * Price) STORED
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns[0].IsComputed, Is.False);
        Assert.That(create.Columns[1].IsComputed, Is.False);
        Assert.That(create.Columns[4].IsComputed, Is.True);
    }

    #endregion
}
