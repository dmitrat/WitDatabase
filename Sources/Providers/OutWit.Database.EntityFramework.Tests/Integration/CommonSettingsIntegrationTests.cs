using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OutWit.Common.Settings.Configuration;
using OutWit.Common.Settings.Database;
using OutWit.Common.Settings.Providers;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for OutWit.Common.Settings.Database running on this provider.
/// The settings library is a real, independent consumer of the EF Core stack: it
/// builds its own schema through EnsureCreated, writes through a DbContext it owns,
/// and reads back through a second context. That exercises model creation, the
/// migrations differ, INFORMATION_SCHEMA and the update pipeline together, which no
/// single test in this suite does on its own.
/// </summary>
[TestFixture]
public class CommonSettingsIntegrationTests
{
    #region Fields

    private string m_testDir = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDir = Path.Combine(Path.GetTempPath(), $"WitDbSettings_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_testDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_testDir))
        {
            try { Directory.Delete(m_testDir, recursive: true); } catch { }
        }
    }

    #endregion

    #region Schema Creation Tests

    [Test]
    public void EnsureCreatedCreatesSettingsTablesTest()
    {
        var dbPath = GetDbPath("ensure-created");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using var context = new SettingsDbContext(optionsBuilder.Options);
        var result = context.Database.EnsureCreated();

        Assert.That(result, Is.True, "EnsureCreated should return true for new database");
        Assert.That(File.Exists(dbPath), Is.True, "Database file should exist");
    }

    [Test]
    public void EnsureCreatedCreatesSettingsAndGroupsTablesTest()
    {
        var dbPath = GetDbPath("list-tables");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using var context = new SettingsDbContext(optionsBuilder.Options);
        context.Database.EnsureCreated();

        context.Database.OpenConnection();
        try
        {
            var tables = new List<string>();

            using var cmd = context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.That(tables, Does.Contain("Settings"),
                $"Tables found: [{string.Join(", ", tables)}]. Expected 'Settings' table.");
            Assert.That(tables, Does.Contain("SettingsGroups"),
                $"Tables found: [{string.Join(", ", tables)}]. Expected 'SettingsGroups' table.");
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    [Test]
    public void InformationSchemaReportsTablesAfterEnsureCreatedTest()
    {
        var dbPath = GetDbPath("has-tables");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using var context = new SettingsDbContext(optionsBuilder.Options);
        context.Database.EnsureCreated();

        context.Database.OpenConnection();
        try
        {
            using var cmd = context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            var count = cmd.ExecuteScalar();

            Assert.That(count, Is.Not.Null, "INFORMATION_SCHEMA.TABLES query should return a result");
            Assert.That(Convert.ToInt64(count), Is.GreaterThan(0),
                "Should have tables after EnsureCreated");
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    [Test]
    public void EnsureCreatedThenWriteSettingsTest()
    {
        var dbPath = GetDbPath("ensure-write");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using (var context = new SettingsDbContext(optionsBuilder.Options))
        {
            context.Database.EnsureCreated();
        }

        // A second context must find the schema the first one created.
        using (var context = new SettingsDbContext(optionsBuilder.Options))
        {
            context.Set<SettingsEntryEntity>().Add(new SettingsEntryEntity
            {
                Group = "TestGroup",
                Key = "TestKey",
                Value = "TestValue",
                ValueKind = "String",
                Tag = "",
                Hidden = false
            });

            var saved = context.SaveChanges();
            Assert.That(saved, Is.EqualTo(1), "Should insert one row into Settings table");
        }
    }

    #endregion

    #region Create Script Tests

    [Test]
    public void GenerateCreateScriptProducesSettingsSchemaTest()
    {
        var dbPath = GetDbPath("gen-sql");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using var context = new SettingsDbContext(optionsBuilder.Options);

        var script = context.Database.GenerateCreateScript();

        TestContext.Out.WriteLine($"--- Generated Script ---\n{script}");

        // An empty script here means the migrations differ produced no operations,
        // which is how a missing-table failure surfaces to a consumer.
        Assert.That(script, Is.Not.Null.And.Not.Empty,
            "GenerateCreateScript should produce SQL");

        Assert.That(script, Does.Contain("Settings").IgnoreCase,
            "Script should reference Settings table");
    }

    [Test]
    public void GeneratedCreateScriptCreatesUsableSchemaTest()
    {
        var dbPath = GetDbPath("exec-sql");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using var context = new SettingsDbContext(optionsBuilder.Options);

        var script = context.Database.GenerateCreateScript();
        TestContext.Out.WriteLine($"Script:\n{script}");

        context.Database.OpenConnection();
        try
        {
            var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var sql in statements)
            {
                if (string.IsNullOrWhiteSpace(sql))
                    continue;

                TestContext.Out.WriteLine($"Executing: {sql}");
                using var cmd = context.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }

            using var checkCmd = context.Database.GetDbConnection().CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'";
            var count = Convert.ToInt64(checkCmd.ExecuteScalar());

            Assert.That(count, Is.GreaterThan(0), "Tables should exist after executing generated SQL");
        }
        finally
        {
            context.Database.CloseConnection();
        }

        var provider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={dbPath}"),
            isReadOnly: false);

        provider.Write("General", GetSampleEntries());

        var result = provider.Read("General");
        Assert.That(result, Has.Count.EqualTo(3));
    }

    #endregion

    #region Provider Tests

    [Test]
    public void ProviderWritesAndReadsBackTest()
    {
        var dbPath = GetDbPath("provider-write");

        var provider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={dbPath}"),
            isReadOnly: false);

        var entries = new List<SettingsEntry>
        {
            new SettingsEntry
            {
                Group = "General",
                Key = "UserName",
                Value = "admin",
                ValueKind = "String",
                Tag = "",
                Hidden = false
            }
        };

        provider.Write("General", entries);

        var result = provider.Read("General");
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Key, Is.EqualTo("UserName"));
        Assert.That(result[0].Value, Is.EqualTo("admin"));
    }

    [Test]
    public void ProviderWritesAndReadsGroupInfoTest()
    {
        var dbPath = GetDbPath("provider-groupinfo");

        var provider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={dbPath}"),
            isReadOnly: false);

        var entries = new List<SettingsEntry>
        {
            new SettingsEntry
            {
                Group = "Advanced",
                Key = "LogLevel",
                Value = "Monday",
                ValueKind = "Enum",
                Tag = "System.DayOfWeek, System.Runtime",
                Hidden = false
            }
        };

        provider.Write("Advanced", entries);
        provider.WriteGroupInfo(new[]
        {
            new SettingsGroupInfo { Group = "Advanced", DisplayName = "Advanced", Priority = 10 }
        });

        var groups = provider.ReadGroupInfo();
        Assert.That(groups, Has.Count.EqualTo(1));
        Assert.That(groups[0].Group, Is.EqualTo("Advanced"));
        Assert.That(groups[0].DisplayName, Is.EqualTo("Advanced"));
        Assert.That(groups[0].Priority, Is.EqualTo(10));
    }

    [Test]
    public void WrittenValuesAreVisibleToANewProviderTest()
    {
        var dbPath = GetDbPath("second-provider");

        var writeProvider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={dbPath}"),
            isReadOnly: false);

        writeProvider.Write("AdvancedSettings", GetSampleEntries());
        writeProvider.WriteGroupInfo(new[]
        {
            new SettingsGroupInfo { Group = "AdvancedSettings", DisplayName = "Advanced", Priority = 10 }
        });

        // A second provider opens the same file with no knowledge of the first.
        var readProvider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={dbPath}"),
            isReadOnly: true);

        var entries = readProvider.Read("AdvancedSettings");
        Assert.That(entries, Has.Count.EqualTo(3));

        var groups = readProvider.ReadGroupInfo();
        Assert.That(groups, Has.Count.EqualTo(1));
    }

    [Test]
    public void ProviderWritesToPreCreatedSchemaTest()
    {
        var dbPath = GetDbPath("manual-tables");

        var optionsBuilder = new DbContextOptionsBuilder<SettingsDbContext>();
        optionsBuilder.UseWitDb($"Data Source={dbPath}");

        using (var context = new SettingsDbContext(optionsBuilder.Options))
        {
            context.Database.OpenConnection();
            using var cmd = context.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Settings"" (
                    ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                    ""Group"" TEXT NOT NULL,
                    ""Key"" TEXT NOT NULL,
                    ""Value"" TEXT NOT NULL,
                    ""ValueKind"" TEXT NOT NULL,
                    ""Tag"" TEXT NOT NULL,
                    ""Hidden"" BOOLEAN NOT NULL
                )";
            cmd.ExecuteNonQuery();

            using var cmd2 = context.Database.GetDbConnection().CreateCommand();
            cmd2.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""SettingsGroups"" (
                    ""Id"" INT PRIMARY KEY AUTOINCREMENT,
                    ""Group"" TEXT NOT NULL,
                    ""DisplayName"" TEXT NOT NULL,
                    ""Priority"" INT NOT NULL
                )";
            cmd2.ExecuteNonQuery();

            context.Database.CloseConnection();
        }

        var provider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={dbPath}"),
            isReadOnly: false);

        provider.Write("General", GetSampleEntries());

        var result = provider.Read("General");
        Assert.That(result, Has.Count.EqualTo(3));
    }

    #endregion

    #region Scoped Settings Tests

    [Test]
    public void DefaultAndUserScopesMergeAndSaveTest()
    {
        var defaultsPath = GetDbPath("defaults");
        var userPath = GetDbPath("user");

        var seedProvider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={defaultsPath}"),
            isReadOnly: false);

        seedProvider.Write("AdvancedSettings", GetSampleEntries());
        seedProvider.WriteGroupInfo(new[]
        {
            new SettingsGroupInfo { Group = "AdvancedSettings", DisplayName = "Advanced", Priority = 10 }
        });

        // Two databases behind one manager: defaults are read-only, the user scope
        // is created on demand by Save.
        var manager = new SettingsBuilder()
            .UseDatabase(o => o.UseWitDb($"Data Source={defaultsPath}"), SettingsScope.Default)
            .UseDatabase(o => o.UseWitDb($"Data Source={userPath}"), SettingsScope.User)
            .ConfigureGroup("AdvancedSettings", priority: 10, displayName: "Advanced")
            .Build();

        manager.Load();
        manager.Merge();

        Assert.That(manager.Collections, Has.Count.EqualTo(1));

        var collection = manager.Collections[0];
        Assert.That(collection.Group, Is.EqualTo("AdvancedSettings"));
        Assert.That(collection.Count(), Is.EqualTo(3));

        manager.Save();

        var userProvider = new DatabaseSettingsProvider(
            o => o.UseWitDb($"Data Source={userPath}"),
            isReadOnly: true);

        var userEntries = userProvider.Read("AdvancedSettings");
        Assert.That(userEntries, Has.Count.EqualTo(3));
    }

    #endregion

    #region Helpers

    private string GetDbPath(string name)
    {
        return Path.Combine(m_testDir, $"{name}.witdb");
    }

    private static IReadOnlyList<SettingsEntry> GetSampleEntries()
    {
        return new List<SettingsEntry>
        {
            new SettingsEntry
            {
                Group = "AdvancedSettings",
                Key = "LogLevel",
                Value = "Monday",
                ValueKind = "Enum",
                Tag = "System.DayOfWeek, System.Runtime",
                Hidden = false
            },
            new SettingsEntry
            {
                Group = "AdvancedSettings",
                Key = "DataPath",
                Value = "./data",
                ValueKind = "Folder",
                Tag = "",
                Hidden = false
            },
            new SettingsEntry
            {
                Group = "AdvancedSettings",
                Key = "EnableDiagnostics",
                Value = "False",
                ValueKind = "Boolean",
                Tag = "",
                Hidden = false
            }
        };
    }

    #endregion
}
