using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>blocker-migrations</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// One entry of this dimension names a class that no longer exists: <c>WitMigrationsModelDiffer</c>
/// was deleted in commit b686dd3, the convention-set-builder fix, which is in the 2.0.0 merge. The
/// behaviour it was blamed for is therefore EF Core's own now - which still has to be checked rather
/// than assumed, and is what the first two tests here do.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class BlockerMigrationsFindingsTests
{
    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbBlocker_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        var prefix = Path.GetFileNameWithoutExtension(m_testDbPath);
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), $"{prefix}*"))
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
        foreach (var dir in Directory.GetDirectories(Path.GetTempPath(), $"{prefix}*"))
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    #region BuildCreateOperations drops HasData and skips Sort()

    [Test]
    public void SeedDataReachesTheGeneratedScriptTest()
    {
        // ALREADY FIXED. Finding: WitMigrationsModelDiffer.cs:71 - HasData seed rows were dropped
        // and Sort() was skipped. That class was DELETED in commit b686dd3 (the convention-set
        // builder fix, in the 2.0.0 merge), so EF Core's own MigrationsModelDiffer does the work
        // now. This test and the next one verify that rather than assume it, and both pass - they
        // stay active as the regression pins for the removal.
        using var context = CreateContext();

        var script = context.Database.GenerateCreateScript();

        Assert.That(script, Does.Contain("seeded"),
            $"HasData rows must appear in the create script. Generated:\n{script}");
    }

    [Test]
    public void CreateScriptOrdersParentBeforeChildTest()
    {
        // The Sort() half of the same finding: a child table that references a parent must not be
        // created first, or the foreign key cannot resolve.
        using var context = CreateContext();

        var script = context.Database.GenerateCreateScript();

        var parentAt = script.IndexOf("\"Parents\"", StringComparison.OrdinalIgnoreCase);
        var childAt = script.IndexOf("\"Children\"", StringComparison.OrdinalIgnoreCase);

        Assert.That(parentAt, Is.GreaterThanOrEqualTo(0), $"Parents missing from:\n{script}");
        Assert.That(childAt, Is.GreaterThanOrEqualTo(0), $"Children missing from:\n{script}");
        Assert.That(parentAt, Is.LessThan(childAt),
            $"the referenced table must be created first. Generated:\n{script}");
    }

    #endregion

    #region AddColumn / ColumnDefinition drop maxLength, precision and scale

    [Test]
    public void AddColumnKeepsItsMaxLengthTest()
    {
        // Finding: WitMigrationsSqlGenerator.cs:102 - AddColumn and ColumnDefinition ignore the
        // model and the type mapping source, so a declared length or precision never reaches the
        // emitted DDL and the column is created wider than the model says.
        var sql = GenerateSql(new AddColumnOperation
        {
            Name = "Code",
            Table = "T",
            ClrType = typeof(string),
            MaxLength = 16,
            IsNullable = true
        });

        Assert.That(sql, Does.Contain("16"),
            $"the declared maximum length must reach the DDL. Generated: {sql}");
    }

    [Test]
    public void AddColumnKeepsItsPrecisionAndScaleTest()
    {
        var sql = GenerateSql(new AddColumnOperation
        {
            Name = "Amount",
            Table = "T",
            ClrType = typeof(decimal),
            Precision = 18,
            Scale = 4,
            IsNullable = false
        });

        Assert.That(sql, Does.Contain("18").And.Contain("4"),
            $"the declared precision and scale must reach the DDL. Generated: {sql}");
    }

    #endregion

    #region Helpers

    private BlockerContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BlockerContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;
        return new BlockerContext(options);
    }

    private string GenerateSql(MigrationOperation operation)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BlockerContext>(o => o.UseWitDb($"Data Source={m_testDbPath}"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BlockerContext>();

        var generator = context.GetService<IMigrationsSqlGenerator>();
        return string.Join("\n", generator.Generate([operation]).Select(c => c.CommandText));
    }

    #endregion

    private sealed class BlockerContext : DbContext
    {
        public BlockerContext(DbContextOptions<BlockerContext> options) : base(options) { }

        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>().HasData(new Parent { Id = 1, Name = "seeded" });
        }

        public sealed class Parent
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        public sealed class Child
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public Parent? Parent { get; set; }
        }
    }
}
