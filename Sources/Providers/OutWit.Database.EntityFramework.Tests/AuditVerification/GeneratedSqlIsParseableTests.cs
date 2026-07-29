using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.Parser;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Asks the question the acceptance oracle cannot: does WitDatabase's own EF Core provider generate
/// SQL that WitDatabase's own parser can read back?
/// </summary>
/// <remarks>
/// <para>
/// <c>WitQuerySqlGenerator</c> derives from EF Core's <c>QuerySqlGenerator</c> and overrides only
/// <c>VisitSqlBinary</c>, <c>VisitSqlUnary</c>, <c>VisitOrdering</c>, <c>VisitCase</c> and
/// <c>VisitCollate</c>. Everything else is inherited — including <c>VisitValues</c>, which emits a
/// <c>VALUES</c> table source, and <c>VisitCrossApply</c>/<c>VisitOuterApply</c>, which emit
/// <c>CROSS APPLY</c> / <c>OUTER APPLY</c>.
/// </para>
/// <para>
/// A provider emitting SQL its own engine cannot parse is worse than a missing dialect feature: the
/// query fails at runtime, after the model built cleanly. This fixture pins the round trip
/// <b>generate → parse</b> for the shapes phase 3 identified, so the answer is measured rather than
/// inferred from which methods happen to be overridden.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class GeneratedSqlIsParseableTests
{
    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbGenSql_{Guid.NewGuid():N}.witdb");
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

    #region Inlined collections

    [Test]
    public void SqlForAnInlinedCollectionIsParseableTest()
    {
        // EF Core 8+ translates a Contains over an inlined collection to a VALUES table source on
        // providers that do not override it. This is the shape the audit predicted and the reason
        // VALUES is in phase 3's scope at all.
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var ids = new[] { 1, 2, 3 };

        var sql = context.Rows
            .Where(row => EF.Constant(ids).Contains(row.Id))
            .Select(row => row.Id)
            .ToQueryString();

        TestContext.Out.WriteLine(sql);

        Assert.That(() => WitSql.Parse(sql), Throws.Nothing,
            $"the provider generated SQL its own parser rejects:{Environment.NewLine}{sql}");
    }

    [Test]
    public void SqlForAParameterisedCollectionIsParseableTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var ids = new List<int> { 1, 2, 3 };

        var sql = context.Rows
            .Where(row => ids.Contains(row.Id))
            .Select(row => row.Id)
            .ToQueryString();

        TestContext.Out.WriteLine(sql);

        Assert.That(() => WitSql.Parse(sql), Throws.Nothing,
            $"the provider generated SQL its own parser rejects:{Environment.NewLine}{sql}");
    }

    #endregion

    #region Correlated shapes, which is where APPLY would come from

    /// <summary>
    /// A correlated <c>Take</c> must be refused at translation time, not emitted as unparseable SQL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before the fix this produced <c>OUTER APPLY ( … ) AS "r1"</c> — SQL the provider's own parser
    /// rejects. The model built cleanly and the query failed at execution with a syntax error naming
    /// a construct the caller never wrote, which is the worst place for this to surface.
    /// </para>
    /// <para>
    /// The replacement behaviour was taken from the oracle rather than invented:
    /// <c>OracleCorrelatedTakeOnSqliteCharacterisationTest</c> shows EF Core's SQLite provider
    /// raising <i>"Translating this query requires the SQL APPLY operation, which is not supported on
    /// SQLite"</i> for the identical query. Refusing is the correct answer, not a concession —
    /// <c>APPLY</c> is a lateral join and no general rewrite into this engine's joins preserves its
    /// semantics.
    /// </para>
    /// </remarks>
    [Test]
    public void CorrelatedTakeIsRefusedRatherThanEmittedAsApplyTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        Assert.That(
            () => context.Rows
                .Select(row => new
                {
                    row.Id,
                    Recent = context.Rows.Where(other => other.Id > row.Id).Take(1).ToList()
                })
                .ToQueryString(),
            Throws.InvalidOperationException.With.Message.Contains("APPLY"),
            "the provider must refuse the query rather than emit APPLY its own parser cannot read");
    }

    /// <summary>
    /// The refusal has to be actionable, not merely loud.
    /// </summary>
    [Test]
    public void ApplyRefusalNamesTheWayOutTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var message = Assert.Throws<InvalidOperationException>(() => context.Rows
            .Select(row => new
            {
                row.Id,
                Recent = context.Rows.Where(other => other.Id > row.Id).Take(1).ToList()
            })
            .ToQueryString())!.Message;

        Assert.Multiple(() =>
        {
            Assert.That(message, Does.Contain("WitDatabase"), "say which provider refused");
            Assert.That(message, Does.Contain("Take"), "name the LINQ shape that caused it");
            Assert.That(message, Does.Contain("join").IgnoreCase, "name a way out");
        });
    }

    /// <summary>
    /// What EF Core's <b>SQLite</b> provider does with the identical query — the oracle, asked before
    /// choosing how to fix the WitDatabase side.
    /// </summary>
    /// <remarks>
    /// SQLite has no <c>APPLY</c> either, so whatever its provider does with a correlated <c>Take</c>
    /// is the shape a provider without lateral support is supposed to produce. Reading its answer is
    /// cheaper and more reliable than inventing one: this is the same discipline that corrected nine
    /// of 29 findings in phase 2.
    /// </remarks>
    [Test]
    public void OracleCorrelatedTakeOnSqliteCharacterisationTest()
    {
        using var context = CreateSqliteContext();
        context.Database.EnsureCreated();

        string outcome;
        try
        {
            outcome = context.Rows
                .Select(row => new
                {
                    row.Id,
                    Recent = context.Rows.Where(other => other.Id > row.Id).Take(1).ToList()
                })
                .ToQueryString();
        }
        catch (Exception exception)
        {
            outcome = $"{exception.GetType().Name}: {exception.Message}";
        }

        TestContext.Out.WriteLine(outcome);

        Assert.Pass("characterisation only - see the printed result");
    }

    #endregion

    #region Helpers

    private SqliteGenSqlContext CreateSqliteContext()
    {
        var options = new DbContextOptionsBuilder<SqliteGenSqlContext>()
            .UseSqlite($"Data Source={m_testDbPath}.sqlite")
            .Options;

        return new SqliteGenSqlContext(options);
    }

    private sealed class SqliteGenSqlContext : DbContext
    {
        public SqliteGenSqlContext(DbContextOptions<SqliteGenSqlContext> options) : base(options) { }

        public DbSet<GenSqlContext.Row> Rows => Set<GenSqlContext.Row>();
    }

    private GenSqlContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GenSqlContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

        return new GenSqlContext(options);
    }

    private sealed class GenSqlContext : DbContext
    {
        public GenSqlContext(DbContextOptions<GenSqlContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();

        public sealed class Row
        {
            public int Id { get; set; }
            public string? Value { get; set; }
        }
    }

    #endregion
}
