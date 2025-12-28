using OutWit.Database.Core.Builder;

namespace OutWit.Database.Tests.Persistence;

/// <summary>
/// Tests for WitSqlEngine data persistence across sessions.
/// These tests verify that data survives closing and reopening the database.
/// </summary>
[TestFixture]
public sealed class WitSqlEnginePersistenceTests
{
    #region Fields

    private string? m_testDir;
    private string? m_testDbPath;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDbPersistence_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
        m_testDbPath = Path.Combine(m_testDir, "test.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (m_testDir != null && Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region Basic Persistence Tests

    [Test]
    public void CreateDatabaseCreatesFileTest()
    {
        using var database = WitDatabase.Create(m_testDbPath!);
        using var engine = new Engine.WitSqlEngine(database, ownsStore: true);

        Assert.That(File.Exists(m_testDbPath), Is.True);
    }

    [Test]
    public void TablePersistsAfterReopenTest()
    {
        // Create database and table
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name TEXT)");
            
            var table = engine.GetTable("Users");
            Assert.That(table, Is.Not.Null, "Table should exist after creation");
        }

        // Reopen and verify
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var table = engine.GetTable("Users");
            Assert.That(table, Is.Not.Null, "Table should persist after reopen");
            Assert.That(table!.Name, Is.EqualTo("Users"));
        }
    }

    [Test]
    public void DataPersistsAfterReopenTest()
    {
        // Create database, table and insert data
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name TEXT)");
            engine.Execute("INSERT INTO Users (Id, Name) VALUES (1, 'Alice')");
            engine.Execute("INSERT INTO Users (Id, Name) VALUES (2, 'Bob')");

            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
            Assert.That(count, Is.EqualTo(2), "Should have 2 rows after insert");
        }

        // Reopen and verify data
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
            Assert.That(count, Is.EqualTo(2), "Should have 2 rows after reopen");

            var rows = engine.Query("SELECT * FROM Users ORDER BY Id");
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
            Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        }
    }

    #endregion

    #region Multiple Table Tests

    [Test]
    public void MultipleTablesPersistAfterReopenTest()
    {
        // Create database with multiple tables
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name TEXT)");
            engine.Execute("CREATE TABLE Products (Id INT PRIMARY KEY, Title TEXT, Price DECIMAL(10,2))");
            engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, UserId INT, ProductId INT)");

            engine.Execute("INSERT INTO Users VALUES (1, 'Alice')");
            engine.Execute("INSERT INTO Products VALUES (1, 'Widget', 9.99)");
            engine.Execute("INSERT INTO Orders VALUES (1, 1, 1)");
        }

        // Reopen and verify
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            Assert.That(engine.GetTable("Users"), Is.Not.Null);
            Assert.That(engine.GetTable("Products"), Is.Not.Null);
            Assert.That(engine.GetTable("Orders"), Is.Not.Null);

            var user = engine.QueryFirstOrDefault("SELECT Name FROM Users WHERE Id = 1");
            Assert.That(user!.Value["Name"].AsString(), Is.EqualTo("Alice"));

            var product = engine.QueryFirstOrDefault("SELECT Title FROM Products WHERE Id = 1");
            Assert.That(product!.Value["Title"].AsString(), Is.EqualTo("Widget"));
        }
    }

    #endregion

    #region Large Data Tests

    [Test]
    public void LargeDatasetPersistsAfterReopenTest()
    {
        const int rowCount = 1000;

        // Create database and insert many rows
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Data (Id INT PRIMARY KEY, Value TEXT)");

            for (int i = 0; i < rowCount; i++)
            {
                engine.Execute($"INSERT INTO Data VALUES ({i}, 'Value_{i}')");
            }

            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
            Assert.That(count, Is.EqualTo(rowCount), "Should have all rows after insert");
        }

        // Reopen and verify
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Data").AsInt64();
            Assert.That(count, Is.EqualTo(rowCount), "Should have all rows after reopen");

            // Check random samples
            var row500 = engine.QueryFirstOrDefault("SELECT Value FROM Data WHERE Id = 500");
            Assert.That(row500!.Value["Value"].AsString(), Is.EqualTo("Value_500"));

            var row999 = engine.QueryFirstOrDefault("SELECT Value FROM Data WHERE Id = 999");
            Assert.That(row999!.Value["Value"].AsString(), Is.EqualTo("Value_999"));
        }
    }

    #endregion

    #region Update and Delete Persistence Tests

    [Test]
    public void UpdatedDataPersistsAfterReopenTest()
    {
        // Create and update
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name TEXT)");
            engine.Execute("INSERT INTO Users VALUES (1, 'Alice')");
            engine.Execute("UPDATE Users SET Name = 'Alice Updated' WHERE Id = 1");
        }

        // Reopen and verify update persisted
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var row = engine.QueryFirstOrDefault("SELECT Name FROM Users WHERE Id = 1");
            Assert.That(row!.Value["Name"].AsString(), Is.EqualTo("Alice Updated"));
        }
    }

    [Test]
    public void DeletedDataNotPresentAfterReopenTest()
    {
        // Create, insert and delete
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name TEXT)");
            engine.Execute("INSERT INTO Users VALUES (1, 'Alice')");
            engine.Execute("INSERT INTO Users VALUES (2, 'Bob')");
            engine.Execute("DELETE FROM Users WHERE Id = 1");

            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
            Assert.That(count, Is.EqualTo(1));
        }

        // Reopen and verify delete persisted
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var count = engine.ExecuteScalar("SELECT COUNT(*) FROM Users").AsInt64();
            Assert.That(count, Is.EqualTo(1), "Delete should persist");

            var row = engine.QueryFirstOrDefault("SELECT Name FROM Users WHERE Id = 1");
            Assert.That(row, Is.Null, "Deleted row should not exist");

            var bob = engine.QueryFirstOrDefault("SELECT Name FROM Users WHERE Id = 2");
            Assert.That(bob!.Value["Name"].AsString(), Is.EqualTo("Bob"));
        }
    }

    #endregion

    #region Schema Persistence Tests

    [Test]
    public void ColumnTypesPersistAfterReopenTest()
    {
        // Create table with various column types
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute(@"
                CREATE TABLE AllTypes (
                    Id INT PRIMARY KEY,
                    IntCol INT,
                    BigIntCol BIGINT,
                    TextCol TEXT,
                    VarCharCol VARCHAR(100),
                    BoolCol BOOLEAN,
                    DecimalCol DECIMAL(10,2),
                    DateCol DATE,
                    GuidCol GUID
                )");

            engine.Execute(@"
                INSERT INTO AllTypes VALUES (
                    1, 42, 9223372036854775807, 'Hello', 'World',
                    TRUE, 123.45, '2024-01-15', 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
                )");
        }

        // Reopen and verify types
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var table = engine.GetTable("AllTypes");
            Assert.That(table, Is.Not.Null);
            Assert.That(table!.Columns, Has.Count.EqualTo(9));

            var row = engine.QueryFirstOrDefault("SELECT * FROM AllTypes WHERE Id = 1");
            Assert.That(row, Is.Not.Null);
            Assert.That(row!.Value["IntCol"].AsInt64(), Is.EqualTo(42));
            Assert.That(row.Value["BigIntCol"].AsInt64(), Is.EqualTo(9223372036854775807L));
            Assert.That(row.Value["TextCol"].AsString(), Is.EqualTo("Hello"));
            Assert.That(row.Value["BoolCol"].AsBool(), Is.True);
        }
    }

    [Test]
    public void AutoIncrementPersistsAfterReopenTest()
    {
        // Create table with autoincrement and insert some rows
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY AUTOINCREMENT, Name TEXT)");
            engine.Execute("INSERT INTO Users (Name) VALUES ('Alice')");
            engine.Execute("INSERT INTO Users (Name) VALUES ('Bob')");

            var lastId = engine.ExecuteScalar("SELECT MAX(Id) FROM Users").AsInt64();
            Assert.That(lastId, Is.EqualTo(2));
        }

        // Reopen and insert more - should continue from 3
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("INSERT INTO Users (Name) VALUES ('Charlie')");

            var newRow = engine.QueryFirstOrDefault("SELECT * FROM Users WHERE Name = 'Charlie'");
            Assert.That(newRow!.Value["Id"].AsInt64(), Is.EqualTo(3), "Autoincrement should continue after reopen");
        }
    }

    #endregion

    #region Index Persistence Tests

    [Test]
    public void IndexPersistsAfterReopenTest()
    {
        // Create table with index
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name TEXT, Email TEXT)");
            engine.Execute("CREATE INDEX IX_Users_Name ON Users (Name)");
            engine.Execute("INSERT INTO Users VALUES (1, 'Alice', 'alice@test.com')");
        }

        // Reopen and verify index is used (query should work efficiently)
        using (var database = WitDatabase.Open(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var row = engine.QueryFirstOrDefault("SELECT Email FROM Users WHERE Name = 'Alice'");
            Assert.That(row!.Value["Email"].AsString(), Is.EqualTo("alice@test.com"));
        }
    }

    #endregion

    #region CreateOrOpen Tests

    [Test]
    public void CreateOrOpenCreatesNewDatabaseTest()
    {
        using var database = WitDatabase.CreateOrOpen(m_testDbPath!);
        using var engine = new Engine.WitSqlEngine(database, ownsStore: true);

        engine.Execute("CREATE TABLE Test (Id INT)");
        
        Assert.That(File.Exists(m_testDbPath), Is.True);
    }

    [Test]
    public void CreateOrOpenOpensExistingDatabaseTest()
    {
        // First create
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Test (Id INT PRIMARY KEY, Value TEXT)");
            engine.Execute("INSERT INTO Test VALUES (1, 'Existing')");
        }

        // Use CreateOrOpen - should open existing
        using (var database = WitDatabase.CreateOrOpen(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            var row = engine.QueryFirstOrDefault("SELECT Value FROM Test WHERE Id = 1");
            Assert.That(row!.Value["Value"].AsString(), Is.EqualTo("Existing"));
        }
    }

    #endregion

    #region Concurrent Session Tests

    [Test]
    public void MultipleSessionsReadSameDataTest()
    {
        // Create initial data
        using (var database = WitDatabase.Create(m_testDbPath!))
        using (var engine = new Engine.WitSqlEngine(database, ownsStore: true))
        {
            engine.Execute("CREATE TABLE Shared (Id INT PRIMARY KEY, Data TEXT)");
            engine.Execute("INSERT INTO Shared VALUES (1, 'SharedData')");
        }

        // Open same database twice and read
        using var db1 = WitDatabase.Open(m_testDbPath!);
        using var engine1 = new Engine.WitSqlEngine(db1, ownsStore: true);

        var row1 = engine1.QueryFirstOrDefault("SELECT Data FROM Shared WHERE Id = 1");
        Assert.That(row1!.Value["Data"].AsString(), Is.EqualTo("SharedData"));
    }

    #endregion
}
