using OutWit.Database.Parser.Schema.AlterActions;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableConstraints;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests;

/// <summary>
/// Tests for DDL statement parsing: CREATE/DROP/ALTER TABLE, INDEX, VIEW, TRIGGER, SEQUENCE.
/// </summary>
[TestFixture]
public class DdlParserTests
{
    #region CREATE TABLE

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
    public void ParseCreateTableWithAllConstraintsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL UNIQUE,
                Price DECIMAL DEFAULT 0,
                CategoryId INT REFERENCES Categories(Id) ON DELETE CASCADE,
                IsActive BOOLEAN DEFAULT TRUE,
                CHECK (Price >= 0)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(5));
        
        var idConstraints = create.Columns[0].Constraints;
        Assert.That(idConstraints?.Any(c => c is ColumnConstraintPrimaryKey), Is.True);
        
        var nameConstraints = create.Columns[1].Constraints;
        Assert.That(nameConstraints?.Any(c => c is ColumnConstraintNotNull), Is.True);
        Assert.That(nameConstraints?.Any(c => c is ColumnConstraintUnique), Is.True);
        
        var priceConstraints = create.Columns[2].Constraints;
        Assert.That(priceConstraints?.Any(c => c is ColumnConstraintDefault), Is.True);
        
        var catConstraints = create.Columns[3].Constraints;
        Assert.That(catConstraints?.Any(c => c is ColumnConstraintReferences), Is.True);
    }

    [Test]
    public void ParseCreateTableWithMultipleCheckConstraintsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Products (
                Id INT PRIMARY KEY,
                Price DECIMAL CHECK (Price >= 0),
                Quantity INT CHECK (Quantity >= 0),
                CHECK (Price * Quantity <= 1000000)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        
        // Column-level CHECK constraints
        var priceConstraints = create.Columns[1].Constraints;
        Assert.That(priceConstraints?.Any(c => c is ColumnConstraintCheck), Is.True);
        
        var qtyConstraints = create.Columns[2].Constraints;
        Assert.That(qtyConstraints?.Any(c => c is ColumnConstraintCheck), Is.True);
        
        // Table-level CHECK constraint
        Assert.That(create.Constraints?.Any(c => c is TableConstraintCheck), Is.True);
    }

    [Test]
    public void ParseCreateTableWithTableConstraintsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE OrderItems (
                OrderId INT,
                ProductId INT,
                Quantity INT,
                PRIMARY KEY (OrderId, ProductId),
                FOREIGN KEY (OrderId) REFERENCES Orders(Id),
                UNIQUE (OrderId, ProductId)
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.Constraints, Is.Not.Null);
        Assert.That(create.Constraints!.Any(c => c is TableConstraintPrimaryKey), Is.True);
        Assert.That(create.Constraints!.Any(c => c is TableConstraintForeignKey), Is.True);
        Assert.That(create.Constraints!.Any(c => c is TableConstraintUnique), Is.True);
    }

    [Test]
    public void ParseCreateTableWithMultiColumnForeignKeyTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE OrderDetails (
                OrderId INT,
                LineNumber INT,
                ProductId INT,
                FOREIGN KEY (OrderId, LineNumber) REFERENCES OrderLines(OrderId, LineNo) ON DELETE CASCADE
            )");
        var create = (WitSqlStatementCreateTable)stmt;
        var fk = create.Constraints!.OfType<TableConstraintForeignKey>().First();
        Assert.That(fk.Columns, Has.Count.EqualTo(2));
        Assert.That(fk.ForeignColumns, Has.Count.EqualTo(2));
    }

    #endregion

    #region DROP/ALTER TABLE

    [Test]
    public void ParseDropTableTest()
    {
        var stmt = WitSql.ParseStatement("DROP TABLE Users");
        var drop = (WitSqlStatementDropTable)stmt;
        Assert.That(drop.TableName, Is.EqualTo("Users"));
        Assert.That(drop.IfExists, Is.False);
    }

    [Test]
    public void ParseDropTableIfExistsTest()
    {
        var stmt = WitSql.ParseStatement("DROP TABLE IF EXISTS TempData");
        var drop = (WitSqlStatementDropTable)stmt;
        Assert.That(drop.IfExists, Is.True);
    }

    [Test]
    public void ParseAlterTableAddColumnTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ADD COLUMN Age INT");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionAddColumn>());
    }

    [Test]
    public void ParseAlterTableDropColumnTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users DROP COLUMN Age");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionDropColumn>());
    }

    [Test]
    public void ParseAlterTableRenameTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users RENAME TO Accounts");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionRenameTable>());
    }

    [Test]
    public void ParseAlterTableRenameColumnTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users RENAME COLUMN Username TO Login");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionRenameColumn>());
    }

    [Test]
    public void ParseAlterTableAlterColumnTypeTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ALTER COLUMN Age TYPE BIGINT");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionAlterColumn>());
        var alterCol = (AlterActionAlterColumn)alter.Action;
        Assert.That(alterCol.ColumnName, Is.EqualTo("Age"));
        Assert.That(alterCol.NewType, Is.Not.Null);
        Assert.That(alterCol.NewType!.TypeName, Is.EqualTo("BIGINT"));
    }

    [Test]
    public void ParseAlterTableAlterColumnSetDefaultTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ALTER COLUMN Status SET DEFAULT 'active'");
        var alter = (WitSqlStatementAlterTable)stmt;
        var alterCol = (AlterActionAlterColumn)alter.Action;
        Assert.That(alterCol.NewDefault, Is.Not.Null);
    }

    [Test]
    public void ParseAlterTableAlterColumnDropDefaultTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ALTER COLUMN Status DROP DEFAULT");
        var alter = (WitSqlStatementAlterTable)stmt;
        var alterCol = (AlterActionAlterColumn)alter.Action;
        Assert.That(alterCol.DropDefault, Is.True);
    }

    [Test]
    public void ParseAlterTableAlterColumnSetNotNullTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ALTER COLUMN Email SET NOT NULL");
        var alter = (WitSqlStatementAlterTable)stmt;
        var alterCol = (AlterActionAlterColumn)alter.Action;
        Assert.That(alterCol.SetNotNull, Is.True);
    }

    [Test]
    public void ParseAlterTableAlterColumnDropNotNullTest()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ALTER COLUMN Nickname DROP NOT NULL");
        var alter = (WitSqlStatementAlterTable)stmt;
        var alterCol = (AlterActionAlterColumn)alter.Action;
        Assert.That(alterCol.SetNotNull, Is.False);
    }

    #endregion

    #region INDEX

    [Test]
    public void ParseCreateIndexTest()
    {
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Users_Email ON Users (Email)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.IndexName, Is.EqualTo("IX_Users_Email"));
        Assert.That(create.TableName, Is.EqualTo("Users"));
        Assert.That(create.IsUnique, Is.False);
    }

    [Test]
    public void ParseCreateUniqueIndexTest()
    {
        var stmt = WitSql.ParseStatement("CREATE UNIQUE INDEX IX_Users_Username ON Users (Username)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.IsUnique, Is.True);
    }

    [Test]
    public void ParseCreateIndexIfNotExistsTest()
    {
        var stmt = WitSql.ParseStatement("CREATE INDEX IF NOT EXISTS IX_Users_Email ON Users (Email)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.IfNotExists, Is.True);
    }

    [Test]
    public void ParseCreateIndexMultiColumnTest()
    {
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Orders ON Orders (UserId, OrderDate DESC)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(2));
        Assert.That(create.Columns[1].Descending, Is.True);
    }

    [Test]
    public void ParseDropIndexTest()
    {
        var stmt = WitSql.ParseStatement("DROP INDEX IF EXISTS IX_Users_Email");
        var drop = (WitSqlStatementDropIndex)stmt;
        Assert.That(drop.IndexName, Is.EqualTo("IX_Users_Email"));
        Assert.That(drop.IfExists, Is.True);
    }

    #endregion

    #region VIEW

    [Test]
    public void ParseCreateViewTest()
    {
        var stmt = WitSql.ParseStatement("CREATE VIEW ActiveUsers AS SELECT * FROM Users WHERE IsActive = TRUE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateView>());
        var create = (WitSqlStatementCreateView)stmt;
        Assert.That(create.ViewName, Is.EqualTo("ActiveUsers"));
        Assert.That(create.IfNotExists, Is.False);
    }

    [Test]
    public void ParseCreateViewIfNotExistsTest()
    {
        var stmt = WitSql.ParseStatement("CREATE VIEW IF NOT EXISTS V AS SELECT 1");
        var create = (WitSqlStatementCreateView)stmt;
        Assert.That(create.IfNotExists, Is.True);
    }

    [Test]
    public void ParseCreateViewWithColumnListTest()
    {
        var stmt = WitSql.ParseStatement(
            "CREATE VIEW UserSummary (UserId, UserName, OrderCount) AS SELECT Id, Name, COUNT(*) FROM Users");
        var create = (WitSqlStatementCreateView)stmt;
        Assert.That(create.ColumnNames, Has.Count.EqualTo(3));
        Assert.That(create.ColumnNames![0], Is.EqualTo("UserId"));
    }

    [Test]
    public void ParseDropViewTest()
    {
        var stmt = WitSql.ParseStatement("DROP VIEW IF EXISTS ActiveUsers");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDropView>());
        var drop = (WitSqlStatementDropView)stmt;
        Assert.That(drop.ViewName, Is.EqualTo("ActiveUsers"));
        Assert.That(drop.IfExists, Is.True);
    }

    #endregion

    #region TRIGGER

    [Test]
    public void ParseCreateTriggerTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER UpdateTimestamp
            BEFORE UPDATE ON Users
            FOR EACH ROW
            BEGIN
                UPDATE Users SET UpdatedAt = NOW() WHERE Id = 1;
            END");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTrigger>());
        var create = (WitSqlStatementCreateTrigger)stmt;
        Assert.That(create.TriggerName, Is.EqualTo("UpdateTimestamp"));
        Assert.That(create.Time, Is.EqualTo(TriggerTimingType.Before));
        Assert.That(create.Event, Is.EqualTo(TriggerEventType.Update));
        Assert.That(create.TableName, Is.EqualTo("Users"));
        Assert.That(create.ForEachRow, Is.True);
    }

    [Test]
    public void ParseCreateTriggerIfNotExistsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER IF NOT EXISTS AuditLog
            AFTER INSERT ON Orders
            BEGIN
                INSERT INTO AuditLog (ActionType) VALUES ('INSERT');
            END");
        var create = (WitSqlStatementCreateTrigger)stmt;
        Assert.That(create.IfNotExists, Is.True);
        Assert.That(create.Time, Is.EqualTo(TriggerTimingType.After));
        Assert.That(create.Event, Is.EqualTo(TriggerEventType.Insert));
    }

    [Test]
    public void ParseCreateTriggerWithWhenConditionTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER PreventNegativeBalance
            BEFORE UPDATE ON Accounts
            FOR EACH ROW
            WHEN (1 = 1)
            BEGIN
                SELECT 1;
            END");
        var create = (WitSqlStatementCreateTrigger)stmt;
        Assert.That(create.WhenCondition, Is.Not.Null);
    }

    [Test]
    public void ParseCreateTriggerInsteadOfTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER InsertIntoView
            INSTEAD OF INSERT ON ActiveUsersView
            BEGIN
                INSERT INTO Users (Name) VALUES ('test');
            END");
        var create = (WitSqlStatementCreateTrigger)stmt;
        Assert.That(create.Time, Is.EqualTo(TriggerTimingType.InsteadOf));
    }

    [Test]
    public void ParseCreateTriggerDeleteTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER AuditDelete
            AFTER DELETE ON Users
            BEGIN
                INSERT INTO DeleteLog (DeletedAt) VALUES (NOW());
            END");
        var create = (WitSqlStatementCreateTrigger)stmt;
        Assert.That(create.Event, Is.EqualTo(TriggerEventType.Delete));
    }

    [Test]
    public void ParseCreateTriggerUpdateOfColumnsTest()
    {
        var stmt = WitSql.ParseStatement(@"
            CREATE TRIGGER TrackPriceChange
            AFTER UPDATE OF Price, Quantity ON Products
            BEGIN
                INSERT INTO PriceHistory (ProductId) VALUES (1);
            END");
        var create = (WitSqlStatementCreateTrigger)stmt;
        Assert.That(create.Event, Is.EqualTo(TriggerEventType.Update));
        Assert.That(create.UpdateColumns, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseDropTriggerTest()
    {
        var stmt = WitSql.ParseStatement("DROP TRIGGER IF EXISTS UpdateTimestamp");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDropTrigger>());
        var drop = (WitSqlStatementDropTrigger)stmt;
        Assert.That(drop.TriggerName, Is.EqualTo("UpdateTimestamp"));
        Assert.That(drop.IfExists, Is.True);
    }

    #endregion

    #region SEQUENCE

    [Test]
    public void ParseCreateSequenceTest()
    {
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE order_seq START WITH 1000");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateSequence>());
        var create = (WitSqlStatementCreateSequence)stmt;
        Assert.That(create.SequenceName, Is.EqualTo("order_seq"));
        Assert.That(create.StartWith, Is.EqualTo(1000));
    }

    [Test]
    public void ParseCreateSequenceIfNotExistsTest()
    {
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE IF NOT EXISTS my_seq");
        var create = (WitSqlStatementCreateSequence)stmt;
        Assert.That(create.IfNotExists, Is.True);
        Assert.That(create.StartWith, Is.EqualTo(1)); // default
    }

    [Test]
    public void ParseDropSequenceTest()
    {
        var stmt = WitSql.ParseStatement("DROP SEQUENCE IF EXISTS order_seq");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDropSequence>());
        var drop = (WitSqlStatementDropSequence)stmt;
        Assert.That(drop.SequenceName, Is.EqualTo("order_seq"));
        Assert.That(drop.IfExists, Is.True);
    }

    [Test]
    public void ParseAlterSequenceTest()
    {
        var stmt = WitSql.ParseStatement("ALTER SEQUENCE order_seq RESTART WITH 5000");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementAlterSequence>());
        var alter = (WitSqlStatementAlterSequence)stmt;
        Assert.That(alter.SequenceName, Is.EqualTo("order_seq"));
        Assert.That(alter.RestartWith, Is.EqualTo(5000));
    }

    #endregion

    #region Data Types - Integer

    [Test]
    public void ParseAllIntegerTypesTest()
    {
        var types = new[] { "TINYINT", "SMALLINT", "INT", "INTEGER", "BIGINT", "INT8", "INT16", "INT32", "INT64" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseUnsignedIntegerTypesTest()
    {
        var types = new[] { "UTINYINT", "UINT8", "USMALLINT", "UINT16", "UINT", "UINT32", "UBIGINT", "UINT64", "ULONG" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    #endregion

    #region Data Types - Float

    [Test]
    public void ParseFloatTypesTest()
    {
        var types = new[] { "FLOAT", "REAL", "DOUBLE", "DECIMAL", "NUMERIC" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseFloat16HalfTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Col FLOAT16)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        
        stmt = WitSql.ParseStatement("CREATE TABLE T (Col HALF)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseFloat64DoubleTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Col FLOAT64)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseMoneyTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Price MONEY)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    #endregion

    #region Data Types - String

    [Test]
    public void ParseStringTypesTest()
    {
        var types = new[] { "TEXT", "VARCHAR(100)", "CHAR(10)", "NVARCHAR(255)" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseNtextTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Col NTEXT)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    #endregion

    #region Data Types - DateTime

    [Test]
    public void ParseDateTimeTypesTest()
    {
        var types = new[] { "DATE", "TIME", "DATETIME", "TIMESTAMP", "TIMESPAN" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseDateOnlyTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Col DATEONLY)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseTimeOnlyTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Col TIMEONLY)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseDateTimeOffsetTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Col DATETIMEOFFSET)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    #endregion

    #region Data Types - Other

    [Test]
    public void ParseGuidTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Id GUID PRIMARY KEY)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseUniqueIdentifierTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Id UNIQUEIDENTIFIER PRIMARY KEY)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseBooleanTypeTest()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Flag BOOLEAN DEFAULT TRUE)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseBinaryTypesTest()
    {
        var types = new[] { "BLOB", "BINARY(16)", "VARBINARY(1024)" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Data {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    #endregion
}
