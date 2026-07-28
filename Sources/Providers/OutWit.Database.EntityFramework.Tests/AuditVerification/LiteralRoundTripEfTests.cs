using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Verification of the EF-side <c>literal-roundtrip</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// The decimal-literal entry of this dimension is in
/// <c>OutWit.Database.Tests/AuditVerification/LiteralRoundTripFindingsTests</c>.
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class LiteralRoundTripEfTests
{
    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbLiteral_{Guid.NewGuid():N}.witdb");
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

    #region char CLR property mapped to StringTypeMapping

    [Test]
    public void CharPropertyRoundTripsTest()
    {
        // Finding: WitTypeMappingSource.cs:150 - a `char` property is given a StringTypeMapping, so
        // the value is handed to a mapping that expects a string.
        var options = Options<CharContext>();

        using (var seed = new CharContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Rows.Add(new CharContext.Row { Id = 1, Grade = 'A' });
            seed.SaveChanges();
        }

        using var context = new CharContext(options);
        var row = context.Rows.Single(r => r.Id == 1);

        Assert.That(row.Grade, Is.EqualTo('A'), "a char property must survive a round trip");
    }

    [Test]
    public void InlinedCharConstantIsQueryableTest()
    {
        // The specific shape the finding names: an *inlined* char constant, which the type mapping
        // has to render into SQL rather than send as a parameter.
        var options = Options<CharContext>();

        using (var seed = new CharContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Rows.Add(new CharContext.Row { Id = 1, Grade = 'A' });
            seed.Rows.Add(new CharContext.Row { Id = 2, Grade = 'B' });
            seed.SaveChanges();
        }

        using var context = new CharContext(options);

        Assert.That(() => context.Rows.Count(r => r.Grade == 'A'), Throws.Nothing,
            "comparing a char property to a char constant is ordinary EF Core");
    }

    #endregion

    #region Schema-qualified identifiers do not round-trip

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and it fails earlier than the finding suggests: EnsureCreated itself "
            + "throws NotSupportedException for EnsureSchemaOperation, so the table is never created "
            + "at all - there is no DDL/DML mismatch to reach. "
            + "literal-roundtrip, EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:39")]
    public void DefaultSchemaTableIsReachableTest()
    {
        // Finding: WitMigrationsSqlGenerator.cs:39 - the DDL drops the schema while EF's query and
        // update generators keep it, so the one schema value WitModelValidator permits makes every
        // table unreachable. `dropin-gaps` confirmed the DDL half in isolation; this is the
        // end-to-end consequence, which is the part that decides whether a user is affected.
        var options = Options<SchemaContext>();

        using var context = new SchemaContext(options);
        context.Database.EnsureCreated();

        Assert.That(() => context.Rows.ToList(), Throws.Nothing,
            "a model the validator accepts must produce a database its own queries can read");
    }

    #endregion

    #region Helpers

    private DbContextOptions<TContext> Options<TContext>() where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

    #endregion

    private sealed class CharContext : DbContext
    {
        public CharContext(DbContextOptions<CharContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();

        public sealed class Row
        {
            public int Id { get; set; }
            public char Grade { get; set; }
        }
    }

    private sealed class SchemaContext : DbContext
    {
        public SchemaContext(DbContextOptions<SchemaContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // "public" is the one schema name WitModelValidator accepts.
            modelBuilder.HasDefaultSchema("public");
        }

        public sealed class Row
        {
            public int Id { get; set; }
            public string? Value { get; set; }
        }
    }
}
