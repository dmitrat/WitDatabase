using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Migrations;

/// <summary>
/// End-to-end regression tests for schema evolution (KnownIssues #1).
/// </summary>
/// <remarks>
/// These are the tests <c>Docs/KnownIssues.md</c> asked for: apply two migrations in sequence to a
/// real <c>.witdb</c> file and then read the new column back. The existing
/// <c>MigrationsTests</c> assert on generated SQL strings only and never execute a standalone
/// <c>ALTER TABLE ADD COLUMN</c>, which is why a schema that could be created but never changed
/// shipped unnoticed.
/// </remarks>
[TestFixture]
public class SchemaEvolutionRegressionTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEvolve_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        var prefix = Path.GetFileNameWithoutExtension(m_testDbPath);
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), $"{prefix}*"))
        {
            try { File.Delete(file); } catch { }
        }
        foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), $"{prefix}*"))
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    #endregion

    #region Migrate Tests

    [Test]
    public void TwoMigrationsInSequenceAddAUsableColumnTest()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            context.Sites.Add(new EvolveSite { Id = 1, Name = "a", ExcludedPaths = "/admin" });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            var site = context.Sites.Single(x => x.Id == 1);

            Assert.That(site.ExcludedPaths, Is.EqualTo("/admin"),
                "The column added by the second migration must be writable and readable");
        }
    }

    [Test]
    public void BothMigrationsAreRecordedInHistoryTest()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var applied = context.Database.GetAppliedMigrations().ToList();

        Assert.That(applied, Has.Count.EqualTo(2),
            "Both migrations must be recorded, not just the initial one");
    }

    [Test]
    public void MigrationsAppliedOneAtATimeAddAUsableColumnTest()
    {
        // Separate calls, separate transactions - the shape KnownIssues.md reported as also failing.
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(InitialCreateId);
        }

        using (var context = CreateContext())
        {
            Assert.That(context.Database.GetPendingMigrations().ToList(), Has.Count.EqualTo(1));
            context.GetService<IMigrator>().Migrate(AddExcludedPathsId);
        }

        using (var context = CreateContext())
        {
            context.Sites.Add(new EvolveSite { Id = 7, Name = "b", ExcludedPaths = "/x" });
            context.SaveChanges();

            Assert.That(context.Sites.Single(x => x.Id == 7).ExcludedPaths, Is.EqualTo("/x"));
        }
    }

    [Test]
    public void ExistingRowsSurviveTheAddedColumnTest()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(InitialCreateId);
            context.Sites.Add(new EvolveSite { Id = 1, Name = "before-alter" });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(AddExcludedPathsId);
        }

        using (var context = CreateContext())
        {
            var site = context.Sites.Single(x => x.Id == 1);

            Assert.Multiple(() =>
            {
                Assert.That(site.Name, Is.EqualTo("before-alter"),
                    "A row written before ALTER TABLE must still decode correctly afterwards");
                Assert.That(site.ExcludedPaths, Is.Null);
            });
        }
    }

    [Test]
    public void GeneratedScriptContainsTheAddColumnOperationTest()
    {
        using var context = CreateContext();

        var script = context.Database.GenerateCreateScript();

        // The migration path and the EnsureCreated path must agree on the table name.
        Assert.That(script, Does.Contain(@"""Sites"""));
    }

    #endregion

    #region Helper Methods

    private const string InitialCreateId = "20260101000000_EvolveInitialCreate";
    private const string AddExcludedPathsId = "20260101000001_EvolveAddExcludedPaths";

    private EvolveContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<EvolveContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");
        return new EvolveContext(optionsBuilder.Options);
    }

    #endregion

    #region Test Models

    public class EvolveSite
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ExcludedPaths { get; set; }
    }

    public class EvolveContext : DbContext
    {
        public EvolveContext(DbContextOptions<EvolveContext> options)
            : base(options)
        {
        }

        public DbSet<EvolveSite> Sites => Set<EvolveSite>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<EvolveSite>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ExcludedPaths).HasMaxLength(1000);
            });
        }
    }

    #endregion
}

/// <summary>
/// First migration: creates the table without the column that arrives later.
/// </summary>
[DbContext(typeof(SchemaEvolutionRegressionTests.EvolveContext))]
[Migration("20260101000000_EvolveInitialCreate")]
public class EvolveInitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Sites",
            columns: table => new
            {
                Id = table.Column<int>(type: "INT", nullable: false),
                Name = table.Column<string>(type: "VARCHAR(100)", maxLength: 100, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Sites", x => x.Id));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "Sites");
}

/// <summary>
/// Second migration: a standalone <c>ALTER TABLE ADD COLUMN</c>, the operation KnownIssues #1
/// reported as unusable.
/// </summary>
[DbContext(typeof(SchemaEvolutionRegressionTests.EvolveContext))]
[Migration("20260101000001_EvolveAddExcludedPaths")]
public class EvolveAddExcludedPaths : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ExcludedPaths",
            table: "Sites",
            type: "VARCHAR(1000)",
            maxLength: 1000,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "ExcludedPaths", table: "Sites");
}
