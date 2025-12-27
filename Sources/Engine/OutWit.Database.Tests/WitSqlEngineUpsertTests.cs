namespace OutWit.Database.Tests;

/// <summary>
/// Tests for INSERT OR REPLACE, INSERT OR IGNORE, and ON CONFLICT clauses.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineUpsertTests : WitSqlEngineTestsBase
{
    #region Setup

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        CreateTestTables();
    }

    private void CreateTestTables()
    {
        // Create Users table with primary key
        m_engine.Execute(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY,
                Name TEXT NOT NULL,
                Email TEXT,
                Score INT DEFAULT 0
            )");

        // Create Products table with unique constraint via index
        m_engine.Execute(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Sku TEXT NOT NULL,
                Name TEXT NOT NULL,
                Price DECIMAL NOT NULL,
                Stock INT DEFAULT 0
            )");
        m_engine.Execute("CREATE UNIQUE INDEX IX_Products_Sku ON Products (Sku)");

        // Create Settings table (key-value store pattern)
        // Use SettingKey instead of Key to avoid reserved word issues
        m_engine.Execute(@"
            CREATE TABLE Settings (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                SettingKey TEXT NOT NULL,
                SettingValue TEXT
            )");
        m_engine.Execute("CREATE UNIQUE INDEX IX_Settings_Key ON Settings (SettingKey)");
    }

    private void InsertTestData()
    {
        m_engine.Execute("INSERT INTO Users (Id, Name, Email, Score) VALUES (1, 'Alice', 'alice@test.com', 100)");
        m_engine.Execute("INSERT INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob', 'bob@test.com', 200)");
        m_engine.Execute("INSERT INTO Users (Id, Name, Email, Score) VALUES (3, 'Charlie', 'charlie@test.com', 150)");
    }

    #endregion

    #region INSERT OR REPLACE Tests

    [Test]
    public void InsertOrReplaceNewRowTest()
    {
        InsertTestData();

        m_engine.Execute("INSERT OR REPLACE INTO Users (Id, Name, Email, Score) VALUES (4, 'Diana', 'diana@test.com', 250)");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[3]["Name"].AsString(), Is.EqualTo("Diana"));
    }

    [Test]
    public void InsertOrReplaceExistingRowTest()
    {
        InsertTestData();

        m_engine.Execute("INSERT OR REPLACE INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob Updated', 'bob.new@test.com', 300)");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(3)); // Still 3 rows
        
        var bob = rows.First(r => r["Id"].AsInt64() == 2);
        Assert.That(bob["Name"].AsString(), Is.EqualTo("Bob Updated"));
        Assert.That(bob["Email"].AsString(), Is.EqualTo("bob.new@test.com"));
        Assert.That(bob["Score"].AsInt64(), Is.EqualTo(300));
    }

    [Test]
    public void InsertOrReplaceMultipleRowsTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT OR REPLACE INTO Users (Id, Name, Email, Score) VALUES 
                (1, 'Alice Updated', 'alice.new@test.com', 500),
                (5, 'Eve', 'eve@test.com', 50)");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(4)); // 3 original - 1 replaced + 1 new = 4
        
        var alice = rows.First(r => r["Id"].AsInt64() == 1);
        Assert.That(alice["Name"].AsString(), Is.EqualTo("Alice Updated"));
        
        var eve = rows.First(r => r["Id"].AsInt64() == 5);
        Assert.That(eve["Name"].AsString(), Is.EqualTo("Eve"));
    }

    #endregion

    #region INSERT OR IGNORE Tests

    [Test]
    public void InsertOrIgnoreNewRowTest()
    {
        InsertTestData();

        m_engine.Execute("INSERT OR IGNORE INTO Users (Id, Name, Email, Score) VALUES (4, 'Diana', 'diana@test.com', 250)");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[3]["Name"].AsString(), Is.EqualTo("Diana"));
    }

    [Test]
    public void InsertOrIgnoreExistingRowTest()
    {
        InsertTestData();

        m_engine.Execute("INSERT OR IGNORE INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob Ignored', 'bob.ignored@test.com', 999)");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(3)); // Still 3 rows
        
        var bob = rows.First(r => r["Id"].AsInt64() == 2);
        Assert.That(bob["Name"].AsString(), Is.EqualTo("Bob")); // Original value
        Assert.That(bob["Score"].AsInt64(), Is.EqualTo(200)); // Original value
    }

    [Test]
    public void InsertOrIgnoreMultipleRowsTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT OR IGNORE INTO Users (Id, Name, Email, Score) VALUES 
                (1, 'Alice Ignored', 'ignored@test.com', 999),
                (5, 'Eve', 'eve@test.com', 50)");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(4)); // 3 original + 1 new, 1 ignored
        
        var alice = rows.First(r => r["Id"].AsInt64() == 1);
        Assert.That(alice["Name"].AsString(), Is.EqualTo("Alice")); // Original value
        
        var eve = rows.First(r => r["Id"].AsInt64() == 5);
        Assert.That(eve["Name"].AsString(), Is.EqualTo("Eve"));
    }

    #endregion

    #region ON CONFLICT DO NOTHING Tests

    [Test]
    public void OnConflictDoNothingNewRowTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (4, 'Diana', 'diana@test.com', 250)
            ON CONFLICT (Id) DO NOTHING");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(4));
    }

    [Test]
    public void OnConflictDoNothingExistingRowTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob Ignored', 'bob.ignored@test.com', 999)
            ON CONFLICT (Id) DO NOTHING");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(3));
        
        var bob = rows.First(r => r["Id"].AsInt64() == 2);
        Assert.That(bob["Name"].AsString(), Is.EqualTo("Bob")); // Original value
    }

    [Test]
    public void OnConflictDoNothingWithUniqueConstraintTest()
    {
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget', 29.99, 100)");

        m_engine.Execute(@"
            INSERT INTO Products (Sku, Name, Price, Stock) VALUES ('SKU001', 'Different Widget', 49.99, 50)
            ON CONFLICT (Sku) DO NOTHING");

        var rows = m_engine.Query("SELECT * FROM Products");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Widget")); // Original value
    }

    #endregion

    #region ON CONFLICT DO UPDATE Tests

    [Test]
    public void OnConflictDoUpdateNewRowTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (4, 'Diana', 'diana@test.com', 250)
            ON CONFLICT (Id) DO UPDATE SET Name = 'Updated Name'");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[3]["Name"].AsString(), Is.EqualTo("Diana")); // New row, no conflict
    }

    [Test]
    public void OnConflictDoUpdateExistingRowTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob New', 'bob.new@test.com', 999)
            ON CONFLICT (Id) DO UPDATE SET Name = 'Bob Updated', Score = 500");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(3));
        
        var bob = rows.First(r => r["Id"].AsInt64() == 2);
        Assert.That(bob["Name"].AsString(), Is.EqualTo("Bob Updated"));
        Assert.That(bob["Score"].AsInt64(), Is.EqualTo(500));
        Assert.That(bob["Email"].AsString(), Is.EqualTo("bob@test.com")); // Not updated
    }

    [Test]
    public void OnConflictDoUpdateWithExcludedTest()
    {
        InsertTestData();

        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob New', 'bob.new@test.com', 999)
            ON CONFLICT (Id) DO UPDATE SET 
                Name = EXCLUDED.Name,
                Email = EXCLUDED.Email,
                Score = EXCLUDED.Score");

        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Id");
        Assert.That(rows, Has.Count.EqualTo(3));
        
        var bob = rows.First(r => r["Id"].AsInt64() == 2);
        Assert.That(bob["Name"].AsString(), Is.EqualTo("Bob New"));
        Assert.That(bob["Email"].AsString(), Is.EqualTo("bob.new@test.com"));
        Assert.That(bob["Score"].AsInt64(), Is.EqualTo(999));
    }

    [Test]
    public void OnConflictDoUpdateIncrementScoreTest()
    {
        InsertTestData();

        // Increment score by adding EXCLUDED value
        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (1, 'Alice', 'alice@test.com', 50)
            ON CONFLICT (Id) DO UPDATE SET Score = Score + EXCLUDED.Score");

        var rows = m_engine.Query("SELECT * FROM Users WHERE Id = 1");
        Assert.That(rows[0]["Score"].AsInt64(), Is.EqualTo(150)); // 100 + 50
    }

    [Test]
    public void OnConflictDoUpdateWithWhereTest()
    {
        InsertTestData();

        // Only update if current score is less than new score
        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob', 'bob@test.com', 150)
            ON CONFLICT (Id) DO UPDATE SET Score = EXCLUDED.Score
            WHERE Score < EXCLUDED.Score");

        var bob = m_engine.Query("SELECT * FROM Users WHERE Id = 2")[0];
        Assert.That(bob["Score"].AsInt64(), Is.EqualTo(200)); // Not updated (200 > 150)

        // Now try with higher score
        m_engine.Execute(@"
            INSERT INTO Users (Id, Name, Email, Score) VALUES (2, 'Bob', 'bob@test.com', 300)
            ON CONFLICT (Id) DO UPDATE SET Score = EXCLUDED.Score
            WHERE Score < EXCLUDED.Score");

        bob = m_engine.Query("SELECT * FROM Users WHERE Id = 2")[0];
        Assert.That(bob["Score"].AsInt64(), Is.EqualTo(300)); // Updated (200 < 300)
    }

    [Test]
    public void OnConflictDoUpdateSettingsPatternTest()
    {
        // Common pattern: upsert key-value settings
        m_engine.Execute(@"
            INSERT INTO Settings (SettingKey, SettingValue) VALUES ('theme', 'dark')
            ON CONFLICT (SettingKey) DO UPDATE SET SettingValue = EXCLUDED.SettingValue");

        var rows = m_engine.Query("SELECT * FROM Settings WHERE SettingKey = 'theme'");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["SettingValue"].AsString(), Is.EqualTo("dark"));

        // Update existing setting
        m_engine.Execute(@"
            INSERT INTO Settings (SettingKey, SettingValue) VALUES ('theme', 'light')
            ON CONFLICT (SettingKey) DO UPDATE SET SettingValue = EXCLUDED.SettingValue");

        rows = m_engine.Query("SELECT * FROM Settings WHERE SettingKey = 'theme'");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["SettingValue"].AsString(), Is.EqualTo("light"));
    }

    #endregion

    #region UPSERT with RETURNING Tests

    [Test]
    public void OnConflictDoUpdateReturningInsertedTest()
    {
        var result = m_engine.Execute(@"
            INSERT INTO Settings (SettingKey, SettingValue) VALUES ('newkey', 'newvalue')
            ON CONFLICT (SettingKey) DO UPDATE SET SettingValue = EXCLUDED.SettingValue
            RETURNING SettingKey, SettingValue");

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["SettingKey"].AsString(), Is.EqualTo("newkey"));
        Assert.That(rows[0]["SettingValue"].AsString(), Is.EqualTo("newvalue"));
    }

    [Test]
    public void OnConflictDoUpdateReturningUpdatedTest()
    {
        m_engine.Execute("INSERT INTO Settings (SettingKey, SettingValue) VALUES ('existingkey', 'oldvalue')");

        var result = m_engine.Execute(@"
            INSERT INTO Settings (SettingKey, SettingValue) VALUES ('existingkey', 'newvalue')
            ON CONFLICT (SettingKey) DO UPDATE SET SettingValue = EXCLUDED.SettingValue
            RETURNING SettingKey, SettingValue");

        Assert.That(result.RowsAffected, Is.EqualTo(1));
        var rows = result.ReadAll();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["SettingKey"].AsString(), Is.EqualTo("existingkey"));
        Assert.That(rows[0]["SettingValue"].AsString(), Is.EqualTo("newvalue"));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void InsertOrReplaceWithAutoIncrementTest()
    {
        m_engine.Execute("INSERT INTO Products (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget', 29.99, 100)");
        var originalId = m_engine.Query("SELECT Id FROM Products WHERE Sku = 'SKU001'")[0]["Id"].AsInt64();

        // Replace should delete and insert new row with potentially new ID
        m_engine.Execute("INSERT OR REPLACE INTO Products (Sku, Name, Price, Stock) VALUES ('SKU001', 'Widget Updated', 39.99, 50)");

        var rows = m_engine.Query("SELECT * FROM Products WHERE Sku = 'SKU001'");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Widget Updated"));
        // Note: ID might change after REPLACE
    }

    [Test]
    public void OnConflictWithMultipleUniqueConstraintsTest()
    {
        // Create table with both PK and unique constraint via index
        m_engine.Execute(@"
            CREATE TABLE MultiUnique (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Code VARCHAR,
                Name VARCHAR
            )");
        m_engine.Execute("CREATE UNIQUE INDEX IX_MultiUnique_Code ON MultiUnique (Code)");

        m_engine.Execute("INSERT INTO MultiUnique (Code, Name) VALUES ('A001', 'First')");

        // Conflict on Code
        m_engine.Execute(@"
            INSERT INTO MultiUnique (Code, Name) VALUES ('A001', 'Updated')
            ON CONFLICT (Code) DO UPDATE SET Name = EXCLUDED.Name");

        var rows = m_engine.Query("SELECT * FROM MultiUnique");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Updated"));
    }

    #endregion
}
