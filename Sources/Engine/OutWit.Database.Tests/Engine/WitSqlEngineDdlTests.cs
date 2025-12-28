namespace OutWit.Database.Tests;

/// <summary>
/// Tests for WitSqlEngine DDL operations (CREATE, DROP, ALTER TABLE).
/// </summary>
[TestFixture]
public sealed class WitSqlEngineDdlTests : WitSqlEngineTestsBase
{
    #region Create Table Tests

    [Test]
    public void CreateTableCreatesTableTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY, Name TEXT)");
        
        var table = m_engine.GetTable("Test");
        
        Assert.That(table, Is.Not.Null);
        Assert.That(table!.Name, Is.EqualTo("Test"));
        Assert.That(table.Columns, Has.Count.EqualTo(2));
    }

    [Test]
    public void CreateTableIfNotExistsDoesNotThrowWhenExistsTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY)");
        
        Assert.DoesNotThrow(() => 
            m_engine.Execute("CREATE TABLE IF NOT EXISTS Test (Id INT PRIMARY KEY)"));
    }

    [Test]
    public void CreateTableWithAutoIncrementTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name TEXT)");
        
        m_engine.Execute("INSERT INTO Test (Name) VALUES ('Test1')");
        m_engine.Execute("INSERT INTO Test (Name) VALUES ('Test2')");
        
        var rows = m_engine.Query("SELECT * FROM Test ORDER BY Id");
        
        Assert.That(rows[0]["Id"].AsInt64(), Is.EqualTo(1));
        Assert.That(rows[1]["Id"].AsInt64(), Is.EqualTo(2));
    }

    [Test]
    public void CreateTableWithDefaultValuesTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY, Status VARCHAR(20) DEFAULT 'active')");
        
        m_engine.Execute("INSERT INTO Test (Id) VALUES (1)");
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM Test WHERE Id = 1");
        
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Status"].AsString(), Is.EqualTo("active"));
    }

    #endregion

    #region Drop Table Tests

    [Test]
    public void DropTableRemovesTableTest()
    {
        m_engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY)");
        m_engine.Execute("DROP TABLE Test");
        
        var table = m_engine.GetTable("Test");
        
        Assert.That(table, Is.Null);
    }

    [Test]
    public void DropTableIfExistsDoesNotThrowWhenNotExistsTest()
    {
        Assert.DoesNotThrow(() => 
            m_engine.Execute("DROP TABLE IF EXISTS NonExistent"));
    }

    #endregion

    #region Alter Table - Add Column Tests

    [Test]
    public void AlterTableAddColumnAddsColumnTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("ALTER TABLE Users ADD COLUMN Age INT");
        
        var table = m_engine.GetTable("Users");
        var ageColumn = table!.Columns.FirstOrDefault(c => c.Name == "Age");
        
        Assert.That(ageColumn, Is.Not.Null);
    }

    [Test]
    public void AlterTableAddColumnWithDefaultPopulatesExistingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("ALTER TABLE Users ADD COLUMN Status VARCHAR(20) DEFAULT 'active'");
        
        var rows = m_engine.Query("SELECT Status FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(r => r["Status"].AsString() == "active"), Is.True);
    }

    [Test]
    public void AlterTableAddColumnWithNullDefaultTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("ALTER TABLE Users ADD COLUMN Notes TEXT");
        
        var rows = m_engine.Query("SELECT Notes FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(r => r["Notes"].IsNull), Is.True);
    }

    [Test]
    public void AlterTableAddColumnWithIntegerDefaultTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("ALTER TABLE Users ADD COLUMN Score INT DEFAULT 100");
        
        var rows = m_engine.Query("SELECT Score FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(r => r["Score"].AsInt64() == 100), Is.True);
    }

    [Test]
    public void AlterTableAddColumnWithExpressionDefaultTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        // Test with computed expression
        m_engine.Execute("ALTER TABLE Users ADD COLUMN Priority INT DEFAULT (1 + 2)");
        
        var rows = m_engine.Query("SELECT Priority FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(r => r["Priority"].AsInt64() == 3), Is.True);
    }

    [Test]
    public void AlterTableAddColumnOnEmptyTableTest()
    {
        CreateUsersTable();
        
        // Add column to empty table - should succeed without populating anything
        m_engine.Execute("ALTER TABLE Users ADD COLUMN Status VARCHAR(20) DEFAULT 'active'");
        
        var table = m_engine.GetTable("Users");
        var statusColumn = table!.Columns.FirstOrDefault(c => c.Name == "Status");
        
        Assert.That(statusColumn, Is.Not.Null);
        Assert.That(statusColumn!.DefaultValue, Is.EqualTo("'active'"));
        
        // New inserts should use default
        m_engine.Execute("INSERT INTO Users (Name, Email) VALUES ('Test', 'test@test.com')");
        
        var row = m_engine.QueryFirstOrDefault("SELECT Status FROM Users");
        Assert.That(row!.Value["Status"].AsString(), Is.EqualTo("active"));
    }

    [Test]
    public void AlterTableAddNotNullColumnWithDefaultTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("ALTER TABLE Users ADD COLUMN IsActive BOOLEAN NOT NULL DEFAULT TRUE");
        
        var rows = m_engine.Query("SELECT IsActive FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows.All(r => r["IsActive"].AsBool() == true), Is.True);
    }

    [Test]
    public void AlterTableAddColumnWithNowDefaultGeneratesTimestampsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        // Add column with NOW() - use parentheses as required by parser for function calls
        m_engine.Execute("ALTER TABLE Users ADD COLUMN CreatedAt DATETIME DEFAULT (NOW())");
        
        var rows = m_engine.Query("SELECT CreatedAt FROM Users ORDER BY Id");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        // All rows should have a non-null timestamp
        Assert.That(rows.All(r => !r["CreatedAt"].IsNull), Is.True);
        // All timestamps should be close to now
        var now = DateTime.UtcNow;
        Assert.That(rows.All(r => (now - r["CreatedAt"].AsDateTime()).TotalMinutes < 1), Is.True);
    }

    [Test]
    public void AlterTableAddColumnWithNewGuidDefaultGeneratesUniqueGuidsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        // Use parentheses for function call
        m_engine.Execute("ALTER TABLE Users ADD COLUMN TrackingId GUID DEFAULT (NEWGUID())");
        
        var rows = m_engine.Query("SELECT TrackingId FROM Users");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        // All should have non-empty GUIDs
        Assert.That(rows.All(r => r["TrackingId"].AsGuid() != Guid.Empty), Is.True);
        // All GUIDs should be unique
        var guids = rows.Select(r => r["TrackingId"].AsGuid()).ToList();
        Assert.That(guids.Distinct().Count(), Is.EqualTo(3));
    }

    #endregion

    #region Drop Column Tests

    [Test]
    public void AlterTableDropColumnRemovesColumnTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("ALTER TABLE Users DROP COLUMN Email");
        
        var table = m_engine.GetTable("Users");
        var emailColumn = table!.Columns.FirstOrDefault(c => c.Name == "Email");
        
        Assert.That(emailColumn, Is.Null);
    }

    #endregion

    #region Alter Table - Rename Tests

    [Test]
    public void AlterTableRenameTableRenamesTableTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("ALTER TABLE Users RENAME TO Accounts");
        
        Assert.That(m_engine.GetTable("Users"), Is.Null);
        Assert.That(m_engine.GetTable("Accounts"), Is.Not.Null);
        
        var rows = m_engine.Query("SELECT * FROM Accounts");
        Assert.That(rows, Has.Count.EqualTo(3));
    }

    [Test]
    public void AlterTableRenameColumnRenamesColumnTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("ALTER TABLE Users RENAME COLUMN Name TO FullName");
        
        var table = m_engine.GetTable("Users");
        var nameColumn = table!.Columns.FirstOrDefault(c => c.Name == "Name");
        var fullNameColumn = table.Columns.FirstOrDefault(c => c.Name == "FullName");
        
        Assert.That(nameColumn, Is.Null);
        Assert.That(fullNameColumn, Is.Not.Null);
    }

    #endregion

    #region Index Tests

    [Test]
    public void CreateIndexCreatesIndexTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("CREATE INDEX IX_Users_Name ON Users (Name)");
        
        // Index creation should not throw
        Assert.Pass();
    }

    [Test]
    public void CreateUniqueIndexCreatesUniqueIndexTest()
    {
        CreateUsersTable();
        
        m_engine.Execute("CREATE UNIQUE INDEX IX_Users_Email ON Users (Email)");
        
        Assert.Pass();
    }

    [Test]
    public void DropIndexDropsIndexTest()
    {
        CreateUsersTable();
        m_engine.Execute("CREATE INDEX IX_Users_Name ON Users (Name)");
        
        Assert.DoesNotThrow(() => 
            m_engine.Execute("DROP INDEX IX_Users_Name"));
    }

    #endregion

    #region View Tests

    [Test]
    public void CreateViewCreatesViewTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute("CREATE VIEW ActiveUsers AS SELECT * FROM Users WHERE Name != 'Bob'");
        
        var view = m_engine.GetView("ActiveUsers");
        Assert.That(view, Is.Not.Null);
    }

    [Test]
    public void DropViewDropsViewTest()
    {
        CreateUsersTable();
        m_engine.Execute("CREATE VIEW TestView AS SELECT * FROM Users");
        
        m_engine.Execute("DROP VIEW TestView");
        
        var view = m_engine.GetView("TestView");
        Assert.That(view, Is.Null);
    }

    #endregion

    #region Sequence Tests

    [Test]
    public void CreateSequenceCreatesSequenceTest()
    {
        m_engine.Execute("CREATE SEQUENCE test_seq START WITH 100");
        
        var seq = m_engine.GetSequence("test_seq");
        Assert.That(seq, Is.Not.Null);
    }

    [Test]
    public void NextValIncrementsSequenceTest()
    {
        m_engine.Execute("CREATE SEQUENCE test_seq START WITH 1");
        
        var val1 = m_engine.NextVal("test_seq");
        var val2 = m_engine.NextVal("test_seq");
        
        Assert.That(val1, Is.EqualTo(1));
        Assert.That(val2, Is.EqualTo(2));
    }

    [Test]
    public void CurrValReturnsCurrentValueTest()
    {
        m_engine.Execute("CREATE SEQUENCE test_seq START WITH 1");
        
        m_engine.NextVal("test_seq");
        var current = m_engine.CurrVal("test_seq");
        var currentAgain = m_engine.CurrVal("test_seq");
        
        Assert.That(current, Is.EqualTo(1));
        Assert.That(currentAgain, Is.EqualTo(1));
    }

    [Test]
    public void DropSequenceDropsSequenceTest()
    {
        m_engine.Execute("CREATE SEQUENCE test_seq START WITH 1");
        m_engine.Execute("DROP SEQUENCE test_seq");
        
        var seq = m_engine.GetSequence("test_seq");
        Assert.That(seq, Is.Null);
    }

    #endregion

    #region Trigger Tests

    [Test]
    public void CreateTriggerCreatesTriggerTest()
    {
        CreateUsersTable();
        m_engine.Execute(@"
            CREATE TABLE AuditLog (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Action TEXT,
                UserId BIGINT
            )");
        
        m_engine.Execute(@"
            CREATE TRIGGER Users_Insert_Audit
            AFTER INSERT ON Users
            FOR EACH ROW
            BEGIN
                INSERT INTO AuditLog (Action, UserId) VALUES ('INSERT', NEW.Id);
            END");
        
        var trigger = m_engine.GetTrigger("Users_Insert_Audit");
        Assert.That(trigger, Is.Not.Null);
    }

    [Test]
    public void DropTriggerDropsTriggerTest()
    {
        CreateUsersTable();
        m_engine.Execute(@"
            CREATE TRIGGER TestTrigger
            AFTER INSERT ON Users
            FOR EACH ROW
            BEGIN
                SELECT 1;
            END");
        
        m_engine.Execute("DROP TRIGGER TestTrigger");
        
        var trigger = m_engine.GetTrigger("TestTrigger");
        Assert.That(trigger, Is.Null);
    }

    #endregion
}
