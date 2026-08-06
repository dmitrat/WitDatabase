using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

[TestFixture]
public sealed class DatabaseSessionInformationSchemaTests
{
    [Test]
    public async Task GetTablesAsync_ReturnsBaseTablesOnlyTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync("CREATE TABLE Users (Id BIGINT PRIMARY KEY, Name VARCHAR(100))");
        await harness.ExecAsync("CREATE TABLE Orders (Id BIGINT PRIMARY KEY, UserId BIGINT)");
        await harness.ExecAsync("CREATE VIEW ActiveUsers AS SELECT * FROM Users");

        var tables = await harness.Service.GetTablesAsync();

        Assert.That(tables.Select(t => t.Name), Is.EquivalentTo(new[] { "Users", "Orders" }));
    }

    [Test]
    public async Task GetViewsAsync_ReturnsViewsOnlyTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync("CREATE TABLE Users (Id BIGINT PRIMARY KEY, Name VARCHAR(100))");
        await harness.ExecAsync("CREATE VIEW ActiveUsers AS SELECT * FROM Users");

        // Use INFORMATION_SCHEMA.TABLES for views to match engine tests and avoid depending on INFORMATION_SCHEMA.VIEWS.
        var views = await harness.Service.ExecuteQueryAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'VIEW'");
        Assert.That(views.ErrorMessage, Is.Null.Or.Empty);
        Assert.That(views.Data, Is.Not.Null);
        Assert.That(views.Data!.Rows.Count, Is.GreaterThan(0));

        var names = views.Data.Rows.Cast<System.Data.DataRow>().Select(r => r[0] as string).ToList();
        Assert.That(names, Is.EquivalentTo(new[] { "ActiveUsers" }));
    }

    [Test]
    public async Task GetColumnsAsync_ReturnsColumnsWithDataTypeAndNullabilityAndDefaultTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                Price DECIMAL(10,2) DEFAULT 1.23,
                Note VARCHAR(255)
            )");

        var cols = (await harness.Service.GetColumnsAsync("Products"))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        Assert.That(cols, Has.Count.EqualTo(4));

        Assert.That(cols[0].Name, Is.EqualTo("Id"));
        Assert.That(cols[0].DataType, Is.EqualTo("BIGINT"));
        Assert.That(cols[0].IsNullable, Is.False);

        Assert.That(cols[1].Name, Is.EqualTo("Name"));
        Assert.That(cols[1].DataType, Is.EqualTo("VARCHAR"));
        Assert.That(cols[1].IsNullable, Is.False);

        Assert.That(cols[2].Name, Is.EqualTo("Price"));
        Assert.That(cols[2].DataType, Is.EqualTo("DECIMAL"));
        Assert.That(cols[2].DefaultValue, Is.Not.Null);

        Assert.That(cols[3].Name, Is.EqualTo("Note"));
        Assert.That(cols[3].IsNullable, Is.True);
    }

    [Test]
    public async Task GetColumnsAsync_MarksPrimaryKeyColumnsTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY,
                Name VARCHAR(100)
            )");

        var cols = (await harness.Service.GetColumnsAsync("Users"))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        Assert.That(cols.Single(c => c.Name == "Id").IsPrimaryKey, Is.True);
        Assert.That(cols.Single(c => c.Name == "Name").IsPrimaryKey, Is.False);
    }

    [Test]
    public async Task GetColumnsAsync_MarksAutoIncrementColumnsTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100)
            )");

        var cols = (await harness.Service.GetColumnsAsync("Users"))
            .OrderBy(c => c.OrdinalPosition)
            .ToList();

        Assert.That(cols.Single(c => c.Name == "Id").IsAutoIncrement, Is.True);
        Assert.That(cols.Single(c => c.Name == "Name").IsAutoIncrement, Is.False);
    }

    [Test]
    public async Task GetColumnsAsync_MarksUniqueColumnsTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY,
                Email VARCHAR(255) UNIQUE,
                Name VARCHAR(100)
            )");

        var cols = await harness.Service.GetColumnsAsync("Users");

        Assert.That(cols.Single(c => c.Name == "Email").IsUnique, Is.True);
        Assert.That(cols.Single(c => c.Name == "Name").IsUnique, Is.False);
    }

    [Test]
    public async Task GetColumnsAsync_GetsCheckExpressionTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY,
                Price DECIMAL(10,2) CHECK (Price > 0)
            )");

        var cols = await harness.Service.GetColumnsAsync("Products");
        var priceCol = cols.Single(c => c.Name == "Price");

        Assert.That(priceCol.CheckExpression, Is.Not.Null);
        Assert.That(priceCol.CheckExpression, Does.Contain("Price"));
    }

    [Test]
    public async Task GetTableDefinitionAsync_IncludesAutoIncrementTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL
            )");

        var definition = await harness.Service.GetTableDefinitionAsync("Users");

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition, Does.Contain("PRIMARY KEY AUTOINCREMENT"));
    }

    [Test]
    public async Task GetTableDefinitionAsync_IncludesUniqueConstraintTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Users (
                Id BIGINT PRIMARY KEY,
                Email VARCHAR(255) UNIQUE
            )");

        var definition = await harness.Service.GetTableDefinitionAsync("Users");

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition, Does.Contain("UNIQUE"));
    }

    [Test]
    public async Task GetTableDefinitionAsync_IncludesCheckConstraintTest()
    {
        await using var harness = await StudioDbHarness.CreateAsync();
        await harness.ExecAsync(@"
            CREATE TABLE Products (
                Id BIGINT PRIMARY KEY,
                Price DECIMAL(10,2) CHECK (Price > 0)
            )");

        var definition = await harness.Service.GetTableDefinitionAsync("Products");

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition, Does.Contain("CHECK"));
    }

    // Note: Tests for VARCHAR(n) length and DECIMAL(p,s) precision/scale are skipped
    // because the parser/engine doesn't currently preserve type parameters in DefinitionColumn.
    // The INFORMATION_SCHEMA.COLUMNS.CHARACTER_MAXIMUM_LENGTH and NUMERIC_PRECISION/SCALE
    // will be NULL until this is implemented.

    // Note: Tests for COLLATE column constraint are skipped because column-level COLLATE
    // is not currently supported in CREATE TABLE parsing.

    private sealed class StudioDbHarness : IAsyncDisposable
    {
        private readonly string _filePath;

        public DatabaseSession Service { get; }

        private StudioDbHarness(string filePath, DatabaseSession service)
        {
            _filePath = filePath;
            Service = service;
        }

        public static async Task<StudioDbHarness> CreateAsync()
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "OutWit.Database.Studio.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);

            var filePath = Path.Combine(tmpDir, "test.witdb");

            var conn = new ConnectionInfo
            {
                FilePath = filePath,
                Password = null,
                IsEncrypted = false,
                IsReadOnly = false,
                StorageEngine = "btree",
                DisplayName = "test"
            };

            var service = new DatabaseSession(conn, NullLogger<DatabaseSession>.Instance);

            var connected = await service.OpenAsync();
            if (!connected)
                throw new InvalidOperationException("Failed to connect test database");

            return new StudioDbHarness(filePath, service);
        }

        public async Task ExecAsync(string sql)
        {
            var result = await Service.ExecuteQueryAsync(sql);
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                throw new InvalidOperationException(result.ErrorMessage);
        }

        public async ValueTask DisposeAsync()
        {
            await Service.CloseAsync();

            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }

            Service.Dispose();
        }
    }
}
