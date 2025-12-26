using NSubstitute;
using OutWit.Database.Parser;
using OutWit.Database.Statements;
using DbDefinitions = OutWit.Database.Definitions;
using DbTypes = OutWit.Database.Types;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Tests for DDL statement execution (CREATE/DROP/ALTER TABLE, INDEX, VIEW).
/// </summary>
[TestFixture]
public class StatementExecutorDdlTests : StatementExecutorTestsBase
{
    #region CREATE TABLE Tests

    [Test]
    public void CreateTableBasicTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR NOT NULL,
                Email VARCHAR
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.Name == "Users" &&
            t.Columns.Count == 3 &&
            t.Columns[0].Name == "Id" &&
            t.Columns[0].IsPrimaryKey &&
            t.Columns[1].Name == "Name" &&
            !t.Columns[1].Nullable &&
            t.Columns[2].Name == "Email"
        ));
    }

    [Test]
    public void CreateTableWithAllDataTypesTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE DataTypes (
                IntCol INTEGER,
                BigIntCol BIGINT,
                SmallIntCol SMALLINT,
                FloatCol FLOAT,
                DoubleCol DOUBLE,
                DecimalCol DECIMAL,
                BoolCol BOOLEAN,
                TextCol TEXT,
                VarCharCol VARCHAR,
                BlobCol BLOB,
                DateCol DATE,
                TimeCol TIME,
                DateTimeCol DATETIME,
                GuidCol GUID
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.Name == "DataTypes" &&
            t.Columns.Count == 14
        ));
    }

    [Test]
    public void CreateTableWithDefaultValueTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Items (
                Id INTEGER PRIMARY KEY,
                Status VARCHAR DEFAULT 'active',
                Count INTEGER DEFAULT 0
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.Name == "Items" &&
            t.Columns[1].DefaultValue == "'active'" &&
            t.Columns[2].DefaultValue == "0"
        ));
    }

    [Test]
    public void CreateTableWithUniqueConstraintTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY,
                Email VARCHAR UNIQUE
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.Columns[1].IsUnique
        ));
    }

    [Test]
    public void CreateTableWithCheckConstraintTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Users (
                Id INTEGER PRIMARY KEY,
                Age INTEGER CHECK (Age >= 0 AND Age <= 150)
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.Columns[1].CheckExpression != null
        ));
    }

    [Test]
    public void CreateTableWithForeignKeyTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER REFERENCES Users(Id)
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.Columns[1].ForeignKey != null &&
            t.Columns[1].ForeignKey.ForeignTable == "Users" &&
            t.Columns[1].ForeignKey.ForeignColumns![0] == "Id"
        ));
    }

    [Test]
    public void CreateTableWithCompositePrimaryKeyTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE OrderItems (
                OrderId INTEGER,
                ProductId INTEGER,
                Quantity INTEGER,
                PRIMARY KEY (OrderId, ProductId)
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.PrimaryKey != null &&
            t.PrimaryKey.Count == 2 &&
            t.PrimaryKey.Contains("OrderId") &&
            t.PrimaryKey.Contains("ProductId")
        ));
    }

    [Test]
    public void CreateTableWithTableLevelConstraintsTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement(@"
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER,
                Total DECIMAL,
                CHECK (Total >= 0),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            )
        ");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t =>
            t.CheckExpressions != null &&
            t.CheckExpressions.Count > 0 &&
            t.ForeignKeys != null &&
            t.ForeignKeys.Count > 0
        ));
    }

    [Test]
    public void CreateTableIfNotExistsAlreadyExistsTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE TABLE IF NOT EXISTS Users (Id INTEGER PRIMARY KEY)");

        var result = executor.Execute(stmt);

        // Should not call CreateTable since table exists
        m_database.DidNotReceive().CreateTable(Arg.Any<DbDefinitions.DefinitionTable>());
    }

    [Test]
    public void CreateTableIfNotExistsDoesNotExistTest()
    {
        m_database.GetTable("NewTable").Returns((DbDefinitions.DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE TABLE IF NOT EXISTS NewTable (Id INTEGER PRIMARY KEY)");

        executor.Execute(stmt);

        m_database.Received(1).CreateTable(Arg.Is<DbDefinitions.DefinitionTable>(t => t.Name == "NewTable"));
    }

    #endregion

    #region DROP TABLE Tests

    [Test]
    public void DropTableTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP TABLE Users");

        executor.Execute(stmt);

        m_database.Received(1).DropTable("Users");
    }

    [Test]
    public void DropTableNotFoundThrowsTest()
    {
        m_database.GetTable("NonExistent").Returns((DbDefinitions.DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP TABLE NonExistent");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void DropTableIfExistsNotFoundTest()
    {
        m_database.GetTable("NonExistent").Returns((DbDefinitions.DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP TABLE IF EXISTS NonExistent");

        // Should not throw
        executor.Execute(stmt);

        m_database.DidNotReceive().DropTable(Arg.Any<string>());
    }

    [Test]
    public void DropTableIfExistsFoundTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP TABLE IF EXISTS Users");

        executor.Execute(stmt);

        m_database.Received(1).DropTable("Users");
    }

    #endregion

    #region ALTER TABLE Tests

    [Test]
    public void AlterTableAddColumnTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ADD COLUMN Age INTEGER");

        executor.Execute(stmt);

        m_database.Received(1).AddColumn("Users", Arg.Is<DbDefinitions.DefinitionColumn>(c =>
            c.Name == "Age" &&
            c.Type == DbTypes.WitDataType.Int32
        ));
    }

    [Test]
    public void AlterTableAddColumnWithConstraintsTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("ALTER TABLE Users ADD COLUMN Status VARCHAR NOT NULL DEFAULT 'active'");

        executor.Execute(stmt);

        m_database.Received(1).AddColumn("Users", Arg.Is<DbDefinitions.DefinitionColumn>(c =>
            c.Name == "Status" &&
            !c.Nullable &&
            c.DefaultValue == "'active'"
        ));
    }

    [Test]
    public void AlterTableDropColumnTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("ALTER TABLE Users DROP COLUMN Email");

        executor.Execute(stmt);

        m_database.Received(1).DropColumn("Users", "Email");
    }

    [Test]
    public void AlterTableRenameTableTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("ALTER TABLE Users RENAME TO Accounts");

        executor.Execute(stmt);

        m_database.Received(1).RenameTable("Users", "Accounts");
    }

    [Test]
    public void AlterTableRenameColumnTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("ALTER TABLE Users RENAME COLUMN Email TO EmailAddress");

        executor.Execute(stmt);

        m_database.Received(1).RenameColumn("Users", "Email", "EmailAddress");
    }

    #endregion

    #region CREATE INDEX Tests

    [Test]
    public void CreateIndexTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Users_Email ON Users (Email)");

        executor.Execute(stmt);

        m_database.Received(1).CreateIndex(Arg.Is<DbDefinitions.DefinitionIndex>(i =>
            i.Name == "IX_Users_Email" &&
            i.TableName == "Users" &&
            i.Columns.Contains("Email") &&
            !i.IsUnique
        ));
    }

    [Test]
    public void CreateUniqueIndexTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE UNIQUE INDEX IX_Users_Email ON Users (Email)");

        executor.Execute(stmt);

        m_database.Received(1).CreateIndex(Arg.Is<DbDefinitions.DefinitionIndex>(i =>
            i.IsUnique
        ));
    }

    [Test]
    public void CreateCompositeIndexTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Users_NameEmail ON Users (Name, Email)");

        executor.Execute(stmt);

        m_database.Received(1).CreateIndex(Arg.Is<DbDefinitions.DefinitionIndex>(i =>
            i.Columns.Count == 2 &&
            i.Columns[0] == "Name" &&
            i.Columns[1] == "Email"
        ));
    }

    [Test]
    public void CreateIndexWithDescTest()
    {
        m_database.GetTable("Users").Returns(CreateUsersTable());

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE INDEX IX_Users_Name ON Users (Name DESC)");

        executor.Execute(stmt);

        m_database.Received(1).CreateIndex(Arg.Is<DbDefinitions.DefinitionIndex>(i =>
            i.ColumnDescending != null &&
            i.ColumnDescending[0] == true
        ));
    }

    [Test]
    public void CreateIndexIfNotExistsTableNotFoundThrowsTest()
    {
        m_database.GetTable("NonExistent").Returns((DbDefinitions.DefinitionTable?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE INDEX IF NOT EXISTS IX_Test ON NonExistent (Col)");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    #endregion

    #region DROP INDEX Tests

    [Test]
    public void DropIndexTest()
    {
        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP INDEX IX_Users_Email");

        executor.Execute(stmt);

        m_database.Received(1).DropIndex("IX_Users_Email");
    }

    #endregion

    #region CREATE VIEW Tests

    [Test]
    public void CreateViewTest()
    {
        m_database.GetView("ActiveUsers").Returns((DbDefinitions.DefinitionView?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE VIEW ActiveUsers AS SELECT * FROM Users WHERE Status = 'active'");

        executor.Execute(stmt);

        m_database.Received(1).CreateView("ActiveUsers", Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>());
    }

    [Test]
    public void CreateViewWithColumnAliasesTest()
    {
        m_database.GetView("UserSummary").Returns((DbDefinitions.DefinitionView?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE VIEW UserSummary (UserId, UserName) AS SELECT Id, Name FROM Users");

        executor.Execute(stmt);

        m_database.Received(1).CreateView("UserSummary", Arg.Any<string>(),
            Arg.Is<IReadOnlyList<string>?>(cols => cols != null && cols.Contains("UserId") && cols.Contains("UserName")));
    }

    [Test]
    public void CreateViewAlreadyExistsThrowsTest()
    {
        m_database.GetView("ExistingView").Returns(new DbDefinitions.DefinitionView { Name = "ExistingView", SelectSql = "SELECT 1" });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE VIEW ExistingView AS SELECT 1");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void CreateViewIfNotExistsAlreadyExistsTest()
    {
        m_database.GetView("ExistingView").Returns(new DbDefinitions.DefinitionView { Name = "ExistingView", SelectSql = "SELECT 1" });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("CREATE VIEW IF NOT EXISTS ExistingView AS SELECT 1");

        // Should not throw
        executor.Execute(stmt);

        m_database.DidNotReceive().CreateView(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>());
    }

    #endregion

    #region DROP VIEW Tests

    [Test]
    public void DropViewTest()
    {
        m_database.GetView("MyView").Returns(new DbDefinitions.DefinitionView { Name = "MyView", SelectSql = "SELECT 1" });

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP VIEW MyView");

        executor.Execute(stmt);

        m_database.Received(1).DropView("MyView");
    }

    [Test]
    public void DropViewNotFoundThrowsTest()
    {
        m_database.GetView("NonExistent").Returns((DbDefinitions.DefinitionView?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP VIEW NonExistent");

        var ex = Assert.Throws<InvalidOperationException>(() => executor.Execute(stmt));
        Assert.That(ex!.Message, Does.Contain("does not exist"));
    }

    [Test]
    public void DropViewIfExistsNotFoundTest()
    {
        m_database.GetView("NonExistent").Returns((DbDefinitions.DefinitionView?)null);

        var executor = new StatementExecutor(m_context);
        var stmt = WitSql.ParseStatement("DROP VIEW IF EXISTS NonExistent");

        // Should not throw
        executor.Execute(stmt);

        m_database.DidNotReceive().DropView(Arg.Any<string>());
    }

    #endregion
}
