using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Migrations;

/// <summary>
/// Integration tests covering runtime EF Core migrations through <see cref="DatabaseFacade.MigrateAsync"/>.
/// </summary>
[TestFixture]
public class MigrateAsyncIntegrationTests
{
    #region Constants

    private const string INITIAL_MIGRATION_ID = "20250101000000_InitialCreate";
    private const string ADD_DESCRIPTION_MIGRATION_ID = "20250102000000_AddDescription";

    #endregion

    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbMigrateAsync_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(m_testDbPath)}*"))
        {
            try { File.Delete(file); } catch { }
        }

        foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(m_testDbPath)}*"))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    #endregion

    #region Tests

    [Test]
    public async Task MigrateAsyncAppliesAllPendingMigrationsTest()
    {
        await using var context = CreateContext(m_testDbPath);

        var pendingBefore = context.Database.GetPendingMigrations().ToList();
        Assert.That(pendingBefore, Is.EqualTo(new[] { INITIAL_MIGRATION_ID, ADD_DESCRIPTION_MIGRATION_ID }));

        await context.Database.MigrateAsync();

        Assert.That(File.Exists(m_testDbPath), Is.True);

        var applied = context.Database.GetAppliedMigrations().ToList();
        Assert.That(applied, Is.EqualTo(new[] { INITIAL_MIGRATION_ID, ADD_DESCRIPTION_MIGRATION_ID }));

        context.Products.Add(new MigrationProduct
        {
            Name = "Migrated",
            Description = "Created through MigrateAsync"
        });

        var saved = await context.SaveChangesAsync();
        Assert.That(saved, Is.EqualTo(1));

        var historyCount = await GetHistoryRowCountAsync(context);
        Assert.That(historyCount, Is.EqualTo(2));
    }

    [Test]
    public async Task MigrateAsyncUpgradesExistingDatabaseFromOlderMigrationTest()
    {
        await using (var initialContext = CreateContext(m_testDbPath))
        {
            var migrator = initialContext.GetService<IMigrator>();
            await migrator.MigrateAsync(INITIAL_MIGRATION_ID);

            var applied = initialContext.Database.GetAppliedMigrations().ToList();
            Assert.That(applied, Is.EqualTo(new[] { INITIAL_MIGRATION_ID }));
        }

        await using (var upgradedContext = CreateContext(m_testDbPath))
        {
            await upgradedContext.Database.MigrateAsync();

            var applied = upgradedContext.Database.GetAppliedMigrations().ToList();
            Assert.That(applied, Is.EqualTo(new[] { INITIAL_MIGRATION_ID, ADD_DESCRIPTION_MIGRATION_ID }));

            upgradedContext.Products.Add(new MigrationProduct
            {
                Name = "Upgraded",
                Description = "Column added by later migration"
            });

            var saved = await upgradedContext.SaveChangesAsync();
            Assert.That(saved, Is.EqualTo(1));

            var product = await upgradedContext.Products.SingleAsync(p => p.Name == "Upgraded");
            Assert.That(product.Description, Is.EqualTo("Column added by later migration"));
        }
    }

    #endregion

    #region Helpers

    private static MigrationDbContext CreateContext(string path)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MigrationDbContext>();
        optionsBuilder.UseWitDb($"Data Source={path}", options =>
        {
            options.MigrationsAssembly(typeof(MigrateAsyncIntegrationTests).Assembly.FullName);
        });

        return new MigrationDbContext(optionsBuilder.Options);
    }

    private static async Task<long> GetHistoryRowCountAsync(DbContext context)
    {
        await context.Database.OpenConnectionAsync();

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    #endregion
}

public class MigrationProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MigrationDbContext : DbContext
{
    public MigrationDbContext(DbContextOptions<MigrationDbContext> options)
        : base(options)
    {
    }

    public DbSet<MigrationProduct> Products => Set<MigrationProduct>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MigrationProduct>(entity =>
        {
            entity.ToTable("MigrationProducts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(4000);
        });
    }
}

[DbContext(typeof(MigrationDbContext))]
[Migration("20250101000000_InitialCreate")]
public sealed class InitialCreateMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MigrationProducts",
            columns: table => new
            {
                Id = table.Column<int>(type: "INT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MigrationProducts", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MigrationProducts");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("OutWit.Database.EntityFramework.Tests.Migrations.MigrationProduct", entity =>
        {
            entity.ToTable("MigrationProducts");
            entity.Property<int>("Id");
            entity.Property<string>("Name").IsRequired();
            entity.HasKey("Id");
        });
    }
}

[DbContext(typeof(MigrationDbContext))]
[Migration("20250102000000_AddDescription")]
public sealed class AddDescriptionMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Description",
            table: "MigrationProducts",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Description",
            table: "MigrationProducts");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("OutWit.Database.EntityFramework.Tests.Migrations.MigrationProduct", entity =>
        {
            entity.ToTable("MigrationProducts");
            entity.Property<int>("Id");
            entity.Property<string?>("Description");
            entity.Property<string>("Name").IsRequired();
            entity.HasKey("Id");
        });
    }
}
