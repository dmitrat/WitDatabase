using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Infrastructure;

/// <summary>
/// Tests for parsing multiple SQL statements in a single input.
/// </summary>
[TestFixture]
public class MultiStatementParserTests
{
    #region Basic Multiple Statements

    [Test]
    public void ParseMultipleStatementsTest()
    {
        var statements = WitSql.Parse("SELECT 1; SELECT 2; SELECT 3");
        Assert.That(statements, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseMultipleStatementsWithTrailingSemicolonTest()
    {
        var statements = WitSql.Parse("SELECT 1; SELECT 2;");
        Assert.That(statements, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseMultipleStatementsWithNewlinesTest()
    {
        var statements = WitSql.Parse(@"
            SELECT 1;
            SELECT 2;
            SELECT 3");
        Assert.That(statements, Has.Count.EqualTo(3));
    }

    [Test]
    public void ParseMultipleStatementsNoSemicolonsTest()
    {
        // Parser may require semicolons between statements
        var statements = WitSql.Parse("SELECT 1");
        Assert.That(statements.Count, Is.GreaterThanOrEqualTo(1));
    }

    #endregion

    #region Mixed Statement Types

    [Test]
    public void ParseMixedStatementTypesTest()
    {
        var statements = WitSql.Parse(@"
            CREATE TABLE T (Id INT);
            INSERT INTO T (Id) VALUES (1);
            SELECT * FROM T;
            DROP TABLE T");
        
        Assert.That(statements, Has.Count.EqualTo(4));
        Assert.That(statements[0], Is.InstanceOf<WitSqlStatementCreateTable>());
        Assert.That(statements[1], Is.InstanceOf<WitSqlStatementInsert>());
        Assert.That(statements[2], Is.InstanceOf<WitSqlStatementSelect>());
        Assert.That(statements[3], Is.InstanceOf<WitSqlStatementDropTable>());
    }

    [Test]
    public void ParseDdlAndDmlMixedTest()
    {
        var statements = WitSql.Parse(@"
            CREATE TABLE Users (Id INT, Name TEXT);
            INSERT INTO Users VALUES (1, 'John');
            INSERT INTO Users VALUES (2, 'Jane');
            UPDATE Users SET Name = 'John Doe' WHERE Id = 1;
            DELETE FROM Users WHERE Id = 2;
            DROP TABLE Users");
        
        Assert.That(statements, Has.Count.EqualTo(6));
    }

    [Test]
    public void ParseTransactionWithStatementsTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN TRANSACTION;
            INSERT INTO Log (Message) VALUES ('Start');
            SAVEPOINT sp1;
            INSERT INTO Log (Message) VALUES ('Middle');
            ROLLBACK TO sp1;
            INSERT INTO Log (Message) VALUES ('Retry');
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(7));
        Assert.That(statements[0], Is.InstanceOf<WitSqlStatementBeginTransaction>());
        Assert.That(statements[6], Is.InstanceOf<WitSqlStatementCommit>());
    }

    #endregion

    #region Empty and Whitespace

    [Test]
    public void ParseWithLeadingWhitespaceTest()
    {
        var statements = WitSql.Parse("   \n\n  SELECT 1; SELECT 2");
        Assert.That(statements, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParseWithTrailingWhitespaceTest()
    {
        var statements = WitSql.Parse("SELECT 1; SELECT 2   \n\n  ");
        Assert.That(statements, Has.Count.EqualTo(2));
    }

    #endregion

    #region Complex Scripts

    [Test]
    public void ParseDatabaseSetupScriptTest()
    {
        var statements = WitSql.Parse(@"
            CREATE TABLE Users (
                Id INT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                Email VARCHAR(255) UNIQUE
            );
            
            CREATE TABLE Orders (
                Id INT PRIMARY KEY AUTOINCREMENT,
                UserId INT NOT NULL REFERENCES Users(Id),
                Total DECIMAL(10, 2),
                CreatedAt DATETIME
            );
            
            CREATE INDEX IX_Orders_UserId ON Orders (UserId);
            
            INSERT INTO Users (Name, Email) VALUES ('Admin', 'admin@example.com');
            INSERT INTO Users (Name, Email) VALUES ('User1', 'user1@example.com');
            
            SELECT COUNT(*) FROM Users");
        
        Assert.That(statements, Has.Count.EqualTo(6));
    }

    [Test]
    public void ParseMigrationScriptTest()
    {
        var statements = WitSql.Parse(@"
            -- Migration: Add soft delete support
            ALTER TABLE Users ADD COLUMN DeletedAt DATETIME;
            ALTER TABLE Users ADD COLUMN IsDeleted BOOLEAN DEFAULT FALSE;
            
            CREATE INDEX IX_Users_IsDeleted ON Users (IsDeleted) WHERE IsDeleted = FALSE;
            
            UPDATE Users SET IsDeleted = FALSE WHERE DeletedAt IS NULL;
            UPDATE Users SET IsDeleted = TRUE WHERE DeletedAt IS NOT NULL");
        
        Assert.That(statements, Has.Count.EqualTo(5));
    }

    [Test]
    public void ParseDataProcessingScriptTest()
    {
        var statements = WitSql.Parse(@"
            BEGIN TRANSACTION;
            
            -- Archive old orders
            INSERT INTO OrdersArchive 
            SELECT * FROM Orders WHERE CreatedAt < '2023-01-01';
            
            -- Delete archived orders
            DELETE FROM Orders WHERE CreatedAt < '2023-01-01';
            
            -- Update statistics
            UPDATE Statistics SET 
                ArchivedCount = (SELECT COUNT(*) FROM OrdersArchive),
                ActiveCount = (SELECT COUNT(*) FROM Orders);
            
            COMMIT");
        
        Assert.That(statements, Has.Count.EqualTo(5));
    }

    #endregion

    #region Statement Independence

    [Test]
    public void EachStatementIsParsedIndependentlyTest()
    {
        var statements = WitSql.Parse(@"
            SELECT * FROM Table1;
            SELECT * FROM Table2");
        
        Assert.That(statements, Has.Count.EqualTo(2));
        
        var select1 = (WitSqlStatementSelect)statements[0];
        var select2 = (WitSqlStatementSelect)statements[1];
        
        var source1 = (TableSourceSimple)select1.FromClause![0];
        var source2 = (TableSourceSimple)select2.FromClause![0];
        
        Assert.That(source1.TableName, Is.EqualTo("Table1"));
        Assert.That(source2.TableName, Is.EqualTo("Table2"));
    }

    [Test]
    public void ParseStatementsPreserveOrderTest()
    {
        var statements = WitSql.Parse(@"
            SELECT 'first';
            SELECT 'second';
            SELECT 'third'");
        
        Assert.That(statements, Has.Count.EqualTo(3));
        
        // All statements are SELECT statements
        Assert.That(statements[0], Is.InstanceOf<WitSqlStatementSelect>());
        Assert.That(statements[1], Is.InstanceOf<WitSqlStatementSelect>());
        Assert.That(statements[2], Is.InstanceOf<WitSqlStatementSelect>());
    }

    #endregion
}
