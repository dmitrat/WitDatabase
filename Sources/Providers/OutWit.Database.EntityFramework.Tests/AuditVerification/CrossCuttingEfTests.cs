using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Verification of the EF-provider half of the <c>cross-cutting</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// The credential-leak claim is checked first here, as the plan asks - a password reaching a log is
/// the one finding in this dimension whose cost does not scale with how often the defect is hit.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class CrossCuttingEfTests
{
    private const string Password = "hunter2-should-never-be-logged";

    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbCrossCut_{Guid.NewGuid():N}.witdb");
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

    #region Connection-string password reaching the log

    [Test]
    public void LogFragmentDoesNotContainTheConnectionStringPasswordTest()
    {
        // Finding: WitDbContextOptionsExtension.cs:246 - LogFragment appends the connection string
        // verbatim. EF Core writes LogFragment at Information level when the context is first used,
        // so an encryption password ends up in ordinary application logs.
        var options = new DbContextOptionsBuilder<CrossCutContext>()
            .UseWitDb($"Data Source={m_testDbPath};Password={Password}")
            .Options;

        var extension = options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);

        var fragment = extension!.Info.LogFragment;

        Assert.That(fragment, Does.Not.Contain(Password),
            $"the password must be redacted before it reaches a log. LogFragment was: {fragment}");
    }

    [Test]
    public void DebugInfoDoesNotContainTheConnectionStringPasswordTest()
    {
        // The second surface named by the finding. PopulateDebugInfo feeds the service-provider
        // cache key, which EF Core also includes in diagnostics.
        var options = new DbContextOptionsBuilder<CrossCutContext>()
            .UseWitDb($"Data Source={m_testDbPath};Password={Password}")
            .Options;

        var extension = options.FindExtension<WitDbContextOptionsExtension>()!;
        var debugInfo = new Dictionary<string, string>();
        extension.Info.PopulateDebugInfo(debugInfo);

        var rendered = string.Join(";", debugInfo.Select(kv => $"{kv.Key}={kv.Value}"));

        Assert.That(rendered, Does.Not.Contain(Password),
            $"the password must not reach debug info. Was: {rendered}");
    }

    #endregion

    #region Migration literals and the current culture

    [Test]
    [Ignore("CONFIRMED 2026-07-27: under de-DE the generator emitted "
            + "ALTER TABLE \"T\" ADD COLUMN \"Price\" DECIMAL(18,2) NOT NULL DEFAULT 1,5; - a comma "
            + "decimal separator. A migration generated on a comma-locale developer machine is "
            + "corrupt SQL. "
            + "cross-cutting, EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:809")]
    public void MigrationLiteralsAreCultureInvariantTest()
    {
        // Finding: WitMigrationsSqlGenerator.cs:809 - literals are formatted with the current
        // culture, so under a comma-decimal locale a DECIMAL default becomes "1,5" and the emitted
        // SQL either fails to parse or silently changes meaning by splitting into two arguments.
        var previous = CultureInfo.CurrentCulture;
        string sql;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            sql = GenerateSql(new AddColumnOperation
            {
                Name = "Price",
                Table = "T",
                ClrType = typeof(decimal),
                ColumnType = "DECIMAL(18,2)",
                IsNullable = false,
                DefaultValue = 1.5m
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.That(sql, Does.Contain("1.5"),
            $"a decimal literal must be emitted with an invariant separator. Generated: {sql}");
    }

    #endregion

    #region DateTime.Now translated to a UTC function

    [Test]
    [Ignore("CONFIRMED 2026-07-27 and measured: the server returned 05:09:58 while local time was "
            + "08:09:58+03:00 - off by exactly the machine's UTC offset, 180 minutes. DateTime.Now, "
            + "DateTime.Today and DateTimeOffset.Now all translate to NOW(), which the engine defines "
            + "as UTC. "
            + "cross-cutting, EntityFramework/Query/Translators/WitMemberTranslator.cs:133")]
    public void DateTimeNowDoesNotTranslateToAUtcFunctionTest()
    {
        // Finding: WitMemberTranslator.cs:133 - DateTime.Now, DateTime.Today and
        // DateTimeOffset.Now are all translated to NOW(), which the engine defines as UTC. For any
        // consumer east or west of Greenwich that silently shifts every "local now" comparison.
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var localNow = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        if (Math.Abs((localNow - utcNow).TotalMinutes) < 1)
            Assert.Ignore("The machine runs at UTC, so local and UTC cannot be told apart here");

        context.Rows.Add(new CrossCutContext.Row { Id = 1, Value = "x" });
        context.SaveChanges();

        // NOW() evaluated server-side must agree with the CLR's notion of local time, since that is
        // what DateTime.Now means to the caller writing the query.
        var serverNow = context.Rows
            .Select(_ => DateTime.Now)
            .First();

        Assert.That(Math.Abs((serverNow - localNow).TotalMinutes), Is.LessThan(5),
            $"DateTime.Now must translate to local time. Server returned {serverNow:O}, " +
            $"local is {localNow:O}, UTC is {utcNow:O}");
    }

    #endregion

    // NOT VERIFIED HERE - "idempotent scripts are generated without guards"
    // (WitMigrationsSqlGenerator.cs:312). A DbContext declared inline in a test assembly has no
    // migration classes, so IMigrator.GenerateScript(..., Idempotent) has nothing to guard and any
    // assertion over its output is vacuous - a first attempt passed only because the DDL happened to
    // contain "IF NOT EXISTS", which is not a migration guard. Testing this honestly needs a context
    // with real `Migration` subclasses and a history table, which belongs with the
    // `dotnet ef migrations add` round-trip the audit lists separately under tests-and-gaps.
    //
    // The other half of that same finding - "three migration operations are emitted as SQL
    // comments" - is confirmed in DropInGapsMigrationsTests.

    #region BulkOptions.SetOutputIdentity

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and worse than \"does the opposite of its documentation\": enabling the "
            + "option makes the bulk insert FAIL - "
            + "InvalidOperationException: UNIQUE constraint failed: GeneratedRows.Id (duplicate value: 0). "
            + "It adds the identity property to the insert column list, so every row is sent an "
            + "explicit zero key and the second one collides with the first. Any bulk insert of more "
            + "than one row with a generated key is broken by it. "
            + "cross-cutting, EntityFramework/Extensions/WitDbBulkExtensions.cs:555")]
    public void SetOutputIdentityReadsGeneratedKeysBackTest()
    {
        // Finding: WitDbBulkExtensions.cs:555 - the option is documented as "requires reading
        // LAST_INSERT_ROWID after each insert. Only enable when you need the generated IDs", but
        // the code uses it to ADD the identity property to the insert column list, i.e. to *send*
        // the CLR default rather than to read the generated value back.
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var rows = new List<CrossCutContext.Generated>
        {
            new() { Name = "a" },
            new() { Name = "b" }
        };

        context.BulkInsert(rows, new BulkOptions { SetOutputIdentity = true });

        Assert.That(rows.Select(r => r.Id), Has.None.EqualTo(0),
            "SetOutputIdentity promises the generated keys are read back into the entities");
    }

    #endregion

    #region Helpers

    private CrossCutContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CrossCutContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;
        return new CrossCutContext(options);
    }

    private string GenerateSql(MigrationOperation operation)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrossCutContext>(o => o.UseWitDb($"Data Source={m_testDbPath}"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrossCutContext>();

        var generator = context.GetService<IMigrationsSqlGenerator>();
        return string.Join("\n", generator.Generate([operation]).Select(c => c.CommandText));
    }

    #endregion

    private sealed class CrossCutContext : DbContext
    {
        public CrossCutContext(DbContextOptions<CrossCutContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();

        public DbSet<Generated> GeneratedRows => Set<Generated>();

        public sealed class Row
        {
            public int Id { get; set; }
            public string? Value { get; set; }
        }

        public sealed class Generated
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }
    }
}
