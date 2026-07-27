using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>ef-translation</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// These are all end-to-end LINQ queries rather than assertions over generated SQL: the claim in
/// every case is that a consumer writing ordinary EF Core gets a wrong answer, and the generated
/// SQL is only how it happens.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class EfTranslationFindingsTests
{
    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEfTrans_{Guid.NewGuid():N}.witdb");
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

    #region StartsWith / EndsWith do not escape wildcards in the search term

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the search term is spliced into the LIKE pattern unescaped and with no "
            + "ESCAPE clause. StartsWith(\"a_\") returned ALL FOUR seeded rows - a%b, a_c, axb, azc - "
            + "instead of the one that literally starts with \"a_\". "
            + "ef-translation, EntityFramework/Query/Translators/WitStringMethodTranslator.cs:128")]
    public void StartsWithTreatsAPercentInTheTermAsALiteralTest()
    {
        // Finding: WitStringMethodTranslator.cs:128 - StartsWith becomes `LIKE argument || '%'`
        // with the argument spliced in unescaped and no ESCAPE clause, so a % or _ that the caller
        // meant literally becomes a wildcard. `StartsWith("a%")` then matches everything beginning
        // with "a".
        using var context = CreateSeededContext();

        var matches = context.Rows
            .Where(r => r.Value!.StartsWith("a%"))
            .Select(r => r.Value)
            .OrderBy(v => v)
            .ToList();

        Assert.That(matches, Is.EqualTo(new[] { "a%b" }),
            "the % is part of the search term, not a wildcard");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the search term is spliced into the LIKE pattern unescaped and with no "
            + "ESCAPE clause. StartsWith(\"a_\") returned ALL FOUR seeded rows - a%b, a_c, axb, azc - "
            + "instead of the one that literally starts with \"a_\". "
            + "ef-translation, EntityFramework/Query/Translators/WitStringMethodTranslator.cs:128")]
    public void EndsWithTreatsAPercentInTheTermAsALiteralTest()
    {
        using var context = CreateSeededContext();

        var matches = context.Rows
            .Where(r => r.Value!.EndsWith("%b"))
            .Select(r => r.Value)
            .OrderBy(v => v)
            .ToList();

        Assert.That(matches, Is.EqualTo(new[] { "a%b" }),
            "the % is part of the search term, not a wildcard");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: the search term is spliced into the LIKE pattern unescaped and with no "
            + "ESCAPE clause. StartsWith(\"a_\") returned ALL FOUR seeded rows - a%b, a_c, axb, azc - "
            + "instead of the one that literally starts with \"a_\". "
            + "ef-translation, EntityFramework/Query/Translators/WitStringMethodTranslator.cs:128")]
    public void StartsWithTreatsAnUnderscoreInTheTermAsALiteralTest()
    {
        using var context = CreateSeededContext();

        var matches = context.Rows
            .Where(r => r.Value!.StartsWith("a_"))
            .Select(r => r.Value)
            .OrderBy(v => v)
            .ToList();

        Assert.That(matches, Is.EqualTo(new[] { "a_c" }),
            "the _ is part of the search term, not a single-character wildcard");
    }

    #endregion

    #region StartsWith and Contains disagree with each other

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and measured on the same row: StartsWith(\"upper\") matched 1 row "
            + "(\"UPPERcase\") while Contains(\"upper\") matched 0. StartsWith is translated to LIKE, "
            + "which the engine evaluates case-insensitively; Contains is translated to INSTR, which is "
            + "ordinal. Two string predicates over the same data give opposite answers. "
            + "ef-translation, Engine/Expressions/ExpressionEvaluator.Conditional.cs:158")]
    public void StartsWithAndContainsAgreeOnCaseTest()
    {
        // Finding: ExpressionEvaluator.Conditional.cs:158 - StartsWith is translated to LIKE, which
        // the engine evaluates case-insensitively, while Contains is translated to INSTR, which is
        // ordinal. Two string predicates in the same query therefore disagree about the same data.
        // Whatever the provider's case policy is, these two must not differ.
        using var context = CreateSeededContext();

        var byStartsWith = context.Rows.Count(r => r.Value!.StartsWith("upper"));
        var byContains = context.Rows.Count(r => r.Value!.Contains("upper"));

        Assert.That(byStartsWith, Is.EqualTo(byContains),
            $"StartsWith matched {byStartsWith} row(s) and Contains matched {byContains} - the two " +
            "must apply the same case rule");
    }

    #endregion

    #region Translators emit functions the engine does not implement

    [Test]
    [Ignore("CONFIRMED 2026-07-27: NotSupportedException \"Function not supported: MILLISECOND\". "
            + "ef-translation, EntityFramework/Query/Translators/WitMemberTranslator.cs:110")]
    public void MillisecondTranslatesToSomethingExecutableTest()
    {
        // Finding: WitMemberTranslator.cs:110 - the translators emit MILLISECOND, TOTAL_SECONDS,
        // a two-argument LOG, unsigned/short CASTs and fractional DATEADD, none of which the engine
        // implements. Each fails at runtime rather than falling back to client evaluation.
        using var context = CreateSeededContext();

        Assert.That(() => context.Rows.Count(r => r.When.Millisecond == 0), Throws.Nothing);
    }

    [Test]
    public void LogWithAnExplicitBaseTranslatesToSomethingExecutableTest()
    {
        // Passes - NOT REPRODUCED. Math.Log(x, base) is one of the five functions the finding names,
        // and it executes. Kept as the pin that separates the working case from MILLISECOND,
        // TOTAL_SECONDS, the short CAST and fractional DATEADD, which do not.
        using var context = CreateSeededContext();

        Assert.That(() => context.Rows.Count(r => Math.Log(r.Number, 2) > 0), Throws.Nothing);
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and it does not even reach the engine: the generated SQL fails to parse - "
            + "WitSqlParsingException \"no viable alternative at input '>TIMESTAMP'\".")]
    public void FractionalDateAddTranslatesToSomethingExecutableTest()
    {
        using var context = CreateSeededContext();

        Assert.That(() => context.Rows.Count(r => r.When.AddDays(1.5) > DateTime.UnixEpoch),
            Throws.Nothing);
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: NotSupportedException \"Function not supported: TOTAL_SECONDS\".")]
    public void TotalSecondsTranslatesToSomethingExecutableTest()
    {
        using var context = CreateSeededContext();

        Assert.That(() => context.Rows.Count(r => r.Duration.TotalSeconds > 0), Throws.Nothing);
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: NotSupportedException \"CAST to SMALLINT not supported\".")]
    public void ShortCastTranslatesToSomethingExecutableTest()
    {
        using var context = CreateSeededContext();

        Assert.That(() => context.Rows.Count(r => (short)r.Id == 1), Throws.Nothing);
    }

    #endregion

    #region JSON columns and primitive collections

    [Test]
    public void PrimitiveCollectionRoundTripsTest()
    {
        // Passes - NOT REPRODUCED, so the finding is half right. `ToJson` owned entities really are
        // unsupported (see JsonOwnedEntityRoundTripsTest), but a primitive collection round-trips
        // correctly. Kept as the pin for the half that works.
        var options = new DbContextOptionsBuilder<CollectionContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

        using (var seed = new CollectionContext(options))
        {
            seed.Database.EnsureCreated();
            seed.TaggedRows.Add(new CollectionContext.Tagged { Id = 100, Tags = [1, 2, 3] });
            seed.SaveChanges();
        }

        using var context = new CollectionContext(options);
        var row = context.TaggedRows.Single(t => t.Id == 100);

        Assert.That(row.Tags, Is.EqualTo(new[] { 1, 2, 3 }),
            "a primitive collection must survive a round trip");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: InvalidOperationException \"The store type 'null' specified for JSON "
            + "column 'Detail' ... is not supported by the current provider. JSON columns require a "
            + "provider-specific JSON store type.\" Note it fails at MODEL BUILD, not at query time - "
            + "which is why the JSON entity had to be moved into its own DbContext before any other "
            + "test in this fixture could be trusted. "
            + "ef-translation, EntityFramework/Query/WitQuerySqlGenerator.cs:11")]
    public void JsonOwnedEntityRoundTripsTest()
    {
        var options = new DbContextOptionsBuilder<JsonContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

        using (var seed = new JsonContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Owners.Add(new JsonContext.Owner
            {
                Id = 200,
                Detail = new JsonContext.Detail { Note = "hello", Weight = 7 }
            });
            seed.SaveChanges();
        }

        using var context = new JsonContext(options);
        var row = context.Owners.Single(o => o.Id == 200);

        Assert.That(row.Detail?.Note, Is.EqualTo("hello"),
            "an owned entity mapped with ToJson must survive a round trip");
    }

    #endregion

    #region Helpers

    private EfTransContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EfTransContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;
        return new EfTransContext(options);
    }

    private EfTransContext CreateSeededContext()
    {
        using (var seed = CreateContext())
        {
            seed.Database.EnsureCreated();
            seed.Rows.AddRange(
                new EfTransContext.Row { Id = 1, Value = "a%b" },
                new EfTransContext.Row { Id = 2, Value = "axb" },
                new EfTransContext.Row { Id = 3, Value = "a_c" },
                new EfTransContext.Row { Id = 4, Value = "azc" },
                new EfTransContext.Row { Id = 5, Value = "UPPERcase" });
            seed.SaveChanges();
        }

        return CreateContext();
    }

    #endregion

    private sealed class EfTransContext : DbContext
    {
        public EfTransContext(DbContextOptions<EfTransContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();

        public sealed class Row
        {
            public int Id { get; set; }
            public string? Value { get; set; }
            public DateTime When { get; set; }
            public double Number { get; set; } = 8;
            public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);
        }
    }

    /// <summary>
    /// A primitive collection lives in its own context: EF Core 8+ maps List&lt;int&gt; to a JSON
    /// column, and if that mapping fails it fails at *model build*, which would take every other
    /// test in this fixture down with it. Keeping the models apart is what makes the string and
    /// function verdicts trustworthy.
    /// </summary>
    private sealed class CollectionContext : DbContext
    {
        public CollectionContext(DbContextOptions<CollectionContext> options) : base(options) { }

        public DbSet<Tagged> TaggedRows => Set<Tagged>();

        public sealed class Tagged
        {
            public int Id { get; set; }
            public List<int> Tags { get; set; } = [];
        }
    }

    private sealed class JsonContext : DbContext
    {
        public JsonContext(DbContextOptions<JsonContext> options) : base(options) { }

        public DbSet<Owner> Owners => Set<Owner>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Owner>().OwnsOne(o => o.Detail, b => b.ToJson());
        }

        public sealed class Owner
        {
            public int Id { get; set; }
            public Detail? Detail { get; set; }
        }

        public sealed class Detail
        {
            public string? Note { get; set; }
            public int Weight { get; set; }
        }
    }
}
