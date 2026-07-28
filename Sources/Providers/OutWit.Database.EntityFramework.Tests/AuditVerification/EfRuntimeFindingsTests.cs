using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>ef-runtime</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// Three of this dimension's four entries are settled elsewhere: the scaffolding claim under
/// <c>engine-schema-ddl</c>, <c>SetOutputIdentity</c> under <c>cross-cutting</c>, and
/// <c>WitModelRuntimeInitializer</c> by deletion - that file was removed in commit b686dd3, the
/// convention-set-builder fix, which is in the 2.0.0 merge. Only the bulk-extensions claim is new
/// here.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class EfRuntimeFindingsTests
{
    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEfRuntime_{Guid.NewGuid():N}.witdb");
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

    #region Bulk extensions skip shadow properties

    [Test]
    public void BulkInsertPersistsAShadowPropertyTest()
    {
        // Finding: WitDbBulkExtensions.cs:463 - GetInsertColumns filters with
        // `.Where(p => !p.IsShadowProperty())`, so every shadow property is dropped from the insert.
        // Shadow properties are not exotic: EF Core creates one for any relationship whose foreign
        // key has no CLR property, which is the default when a navigation is declared without one.
        //
        // NB the entity here deliberately has NO converted property. A first version used the same
        // entity as the converter test and failed with the converter's error, which would have been
        // recorded as a shadow-property defect it says nothing about.
        var options = Options();

        using (var seed = new EfRuntimeContext(options))
        {
            seed.Database.EnsureCreated();

            var row = new EfRuntimeContext.Plain { Id = 1, Name = "a" };
            seed.Entry(row).Property("Tenant").CurrentValue = "acme";
            seed.BulkInsert([row]);
        }

        using var context = new EfRuntimeContext(options);
        var stored = context.Plains
            .Select(r => EF.Property<string>(r, "Tenant"))
            .Single();

        Assert.That(stored, Is.EqualTo("acme"),
            "a shadow property carries real data and must be written like any other column");
    }

    #endregion

    #region Bulk extensions bypass value converters

    [Test]
    public void BulkInsertAppliesTheValueConverterTest()
    {
        // The other half: the value is taken straight off the CLR property, so a configured
        // conversion never runs and the column holds the unconverted representation. The row then
        // reads back wrongly - or not at all - through the same converter.
        var options = Options();

        using (var seed = new EfRuntimeContext(options))
        {
            seed.Database.EnsureCreated();
            seed.BulkInsert([
                new EfRuntimeContext.Row { Id = 2, Name = "b", Status = EfRuntimeContext.State.Active }
            ]);
        }

        using var context = new EfRuntimeContext(options);
        var row = context.Rows.Single(r => r.Id == 2);

        Assert.That(row.Status, Is.EqualTo(EfRuntimeContext.State.Active),
            "the converted value must round-trip through the converter that wrote it");
    }

    [Test]
    public void SaveChangesAppliesTheValueConverterTest()
    {
        // Control: the ordinary SaveChanges path must honour the converter. If this failed too, the
        // defect would be in the mapping rather than in the bulk extensions.
        var options = Options();

        using (var seed = new EfRuntimeContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Rows.Add(new EfRuntimeContext.Row
            {
                Id = 3,
                Name = "c",
                Status = EfRuntimeContext.State.Active
            });
            seed.SaveChanges();
        }

        using var context = new EfRuntimeContext(options);
        var row = context.Rows.Single(r => r.Id == 3);

        Assert.That(row.Status, Is.EqualTo(EfRuntimeContext.State.Active));
    }

    #endregion

    #region Helpers

    private DbContextOptions<EfRuntimeContext> Options() =>
        new DbContextOptionsBuilder<EfRuntimeContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

    #endregion

    private sealed class EfRuntimeContext : DbContext
    {
        public EfRuntimeContext(DbContextOptions<EfRuntimeContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();
        public DbSet<Plain> Plains => Set<Plain>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plain>().Property<string>("Tenant");
            modelBuilder.Entity<Row>()
                .Property(r => r.Status)
                .HasConversion(v => v == State.Active ? "Y" : "N",
                               v => v == "Y" ? State.Active : State.Inactive);
        }

        public enum State { Inactive, Active }

        public sealed class Row
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public State Status { get; set; }
        }

        /// <summary>An entity with a shadow property and no value converter anywhere near it.</summary>
        public sealed class Plain
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }
    }
}
