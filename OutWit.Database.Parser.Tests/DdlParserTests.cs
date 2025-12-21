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
    public void ParseCreateTableBasic()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE Users (Id INT, Name TEXT)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.TableName, Is.EqualTo("Users"));
        Assert.That(create.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseCreateTableIfNotExists()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE IF NOT EXISTS Logs (Id INT)");
        var create = (WitSqlStatementCreateTable)stmt;
        Assert.That(create.IfNotExists, Is.True);
    }

    [Test]
    public void ParseCreateTableWithAllConstraints()
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
    public void ParseCreateTableWithTableConstraints()
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

    #endregion

    #region DROP/ALTER TABLE

    [Test]
    public void ParseDropTable()
    {
        var stmt = WitSql.ParseStatement("DROP TABLE Users");
        var drop = (WitSqlStatementDropTable)stmt;
        Assert.That(drop.TableName, Is.EqualTo("Users"));
        Assert.That(drop.IfExists, Is.False);
    }

    [Test]
    public void ParseDropTableIfExists()
    {
        var stmt = WitSql.ParseStatement("DROP TABLE IF EXISTS TempData");
        var drop = (WitSqlStatementDropTable)stmt;
        Assert.That(drop.IfExists, Is.True);
    }

    [Test]
    public void ParseAlterTableAddColumn()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ADD COLUMN Age INT");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionAddColumn>());
    }

    [Test]
    public void ParseAlterTableDropColumn()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users DROP COLUMN Age");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionDropColumn>());
    }

    [Test]
    public void ParseAlterTableRename()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users RENAME TO Accounts");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionRenameTable>());
    }

    [Test]
    public void ParseAlterTableRenameColumn()
    {
        var stmt = WitSql.ParseStatement("ALTER TABLE Users RENAME COLUMN Username TO Login");
        var alter = (WitSqlStatementAlterTable)stmt;
        Assert.That(alter.Action, Is.InstanceOf<AlterActionRenameColumn>());
    }

    #endregion

    #region INDEX

    [Test]
    public void ParseCreateIndex()
    {
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Users_Email ON Users (Email)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.IndexName, Is.EqualTo("IX_Users_Email"));
        Assert.That(create.TableName, Is.EqualTo("Users"));
        Assert.That(create.IsUnique, Is.False);
    }

    [Test]
    public void ParseCreateUniqueIndex()
    {
        var stmt = WitSql.ParseStatement("CREATE UNIQUE INDEX IX_Users_Username ON Users (Username)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.IsUnique, Is.True);
    }

    [Test]
    public void ParseCreateIndexMultiColumn()
    {
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Orders ON Orders (UserId, OrderDate DESC)");
        var create = (WitSqlStatementCreateIndex)stmt;
        Assert.That(create.Columns, Has.Count.EqualTo(2));
        Assert.That(create.Columns[1].Descending, Is.True);
    }

    [Test]
    public void ParseDropIndex()
    {
        var stmt = WitSql.ParseStatement("DROP INDEX IF EXISTS IX_Users_Email");
        var drop = (WitSqlStatementDropIndex)stmt;
        Assert.That(drop.IndexName, Is.EqualTo("IX_Users_Email"));
        Assert.That(drop.IfExists, Is.True);
    }

    #endregion

    #region VIEW

    [Test]
    public void ParseCreateView()
    {
        var stmt = WitSql.ParseStatement("CREATE VIEW ActiveUsers AS SELECT * FROM Users WHERE IsActive = TRUE");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateView>());
        var create = (WitSqlStatementCreateView)stmt;
        Assert.That(create.ViewName, Is.EqualTo("ActiveUsers"));
        Assert.That(create.IfNotExists, Is.False);
    }

    [Test]
    public void ParseCreateViewIfNotExists()
    {
        var stmt = WitSql.ParseStatement("CREATE VIEW IF NOT EXISTS V AS SELECT 1");
        var create = (WitSqlStatementCreateView)stmt;
        Assert.That(create.IfNotExists, Is.True);
    }

    [Test]
    public void ParseDropView()
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
    public void ParseCreateTrigger()
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
    public void ParseCreateTriggerIfNotExists()
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
    public void ParseDropTrigger()
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
    public void ParseCreateSequence()
    {
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE order_seq START WITH 1000");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateSequence>());
        var create = (WitSqlStatementCreateSequence)stmt;
        Assert.That(create.SequenceName, Is.EqualTo("order_seq"));
        Assert.That(create.StartWith, Is.EqualTo(1000));
    }

    [Test]
    public void ParseCreateSequenceIfNotExists()
    {
        var stmt = WitSql.ParseStatement("CREATE SEQUENCE IF NOT EXISTS my_seq");
        var create = (WitSqlStatementCreateSequence)stmt;
        Assert.That(create.IfNotExists, Is.True);
        Assert.That(create.StartWith, Is.EqualTo(1)); // default
    }

    [Test]
    public void ParseDropSequence()
    {
        var stmt = WitSql.ParseStatement("DROP SEQUENCE IF EXISTS order_seq");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementDropSequence>());
        var drop = (WitSqlStatementDropSequence)stmt;
        Assert.That(drop.SequenceName, Is.EqualTo("order_seq"));
        Assert.That(drop.IfExists, Is.True);
    }

    [Test]
    public void ParseAlterSequence()
    {
        var stmt = WitSql.ParseStatement("ALTER SEQUENCE order_seq RESTART WITH 5000");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementAlterSequence>());
        var alter = (WitSqlStatementAlterSequence)stmt;
        Assert.That(alter.SequenceName, Is.EqualTo("order_seq"));
        Assert.That(alter.RestartWith, Is.EqualTo(5000));
    }

    #endregion

    #region Data Types

    [Test]
    public void ParseAllIntegerTypes()
    {
        var types = new[] { "TINYINT", "SMALLINT", "INT", "INTEGER", "BIGINT", "INT8", "INT16", "INT32", "INT64" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseFloatTypes()
    {
        var types = new[] { "FLOAT", "REAL", "DOUBLE", "DECIMAL", "NUMERIC" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseStringTypes()
    {
        var types = new[] { "TEXT", "VARCHAR(100)", "CHAR(10)", "NVARCHAR(255)" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseDateTimeTypes()
    {
        var types = new[] { "DATE", "TIME", "DATETIME", "TIMESTAMP", "TIMESPAN" };
        foreach (var type in types)
        {
            var stmt = WitSql.ParseStatement($"CREATE TABLE T (Col {type})");
            Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
        }
    }

    [Test]
    public void ParseGuidType()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Id GUID PRIMARY KEY)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseBooleanType()
    {
        var stmt = WitSql.ParseStatement("CREATE TABLE T (Flag BOOLEAN DEFAULT TRUE)");
        Assert.That(stmt, Is.InstanceOf<WitSqlStatementCreateTable>());
    }

    [Test]
    public void ParseBinaryTypes()
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
