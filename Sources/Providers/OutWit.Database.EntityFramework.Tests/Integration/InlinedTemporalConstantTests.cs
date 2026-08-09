using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// A temporal constant written INSIDE a LINQ expression, which EF Core inlines into the SQL as a
/// literal rather than passing as a parameter. This is <c>Docs/KnownIssues.md</c> 2, and the second
/// half of it is worse than the first.
/// </summary>
/// <remarks>
/// <para>
/// <b>It failed twice, in two different ways.</b> EF Core's own mappings emit ANSI typed literals -
/// <c>DATE '…'</c>, <c>TIMESTAMP '…'</c> - and the grammar had no such form, so the query threw
/// <c>WitSqlParsingException</c> before reaching the engine. The provider was then changed to emit a
/// plain quoted string, which parses; measured 2026-08-09, <b>that answers with no rows at all</b> -
/// a row written by this provider cannot be found by the very text the provider writes, because text
/// compared with a temporal column is not converted. A loud failure had become a silent one.
/// </para>
/// <para>
/// Both are fixed by the same thing: the grammar has typed literals now and the mappings emit them.
/// Every case here writes through the provider and reads back through it, so what is being measured
/// is the round trip a user gets rather than the SQL text.
/// </para>
/// </remarks>
[TestFixture]
public class InlinedTemporalConstantTests
{
    #region Fields

    private static readonly DateOnly THE_DATE = new(2026, 7, 1);
    private static readonly TimeOnly THE_TIME = new(13, 45, 30, 123);
    private static readonly DateTime THE_STAMP = new(2026, 7, 1, 13, 45, 30, 123);
    private static readonly DateTimeOffset THE_MOMENT =
        new(2026, 7, 1, 13, 45, 30, TimeSpan.FromHours(3));

    private string m_path = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUpAsync()
    {
        m_path = Path.Combine(Path.GetTempPath(), $"WitDbEf_Temporal_{Guid.NewGuid():N}.witdb");

        await using var context = Open();
        await context.Database.EnsureCreatedAsync();

        context.Rows.Add(new TemporalRow
        {
            Label = "the one",
            Date = THE_DATE,
            Time = THE_TIME,
            Stamp = THE_STAMP,
            Moment = THE_MOMENT
        });

        await context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(m_path))
                File.Delete(m_path);
        }
        catch
        {
            // The fixture's own leftovers are not the subject.
        }
    }

    #endregion

    #region The four types

    /// <summary>
    /// The shape the issue names: <c>new DateOnly(2026, 7, 1)</c> written into the expression, which
    /// EF inlines. The same value through a local was always fine, because EF parameterises that.
    /// </summary>
    [Test]
    public async Task ADateWrittenIntoTheExpressionFindsItsRowAsync()
    {
        await using var context = Open();

        var found = await context.Rows.SingleOrDefaultAsync(row => row.Date == new DateOnly(2026, 7, 1));

        Assert.That(found, Is.Not.Null, "the row is there and the constant names it");
    }

    [Test]
    public async Task ATimeWrittenIntoTheExpressionFindsItsRowAsync()
    {
        await using var context = Open();

        var found = await context.Rows
            .SingleOrDefaultAsync(row => row.Time == new TimeOnly(13, 45, 30, 123));

        Assert.That(found, Is.Not.Null);
    }

    /// <summary>
    /// A <see cref="DateTime"/> carrying a FRACTION of a second, which is where the quoted-string form
    /// was measured to answer with nothing.
    /// </summary>
    [Test]
    public async Task AStampWithAFractionWrittenIntoTheExpressionFindsItsRowAsync()
    {
        await using var context = Open();

        var found = await context.Rows
            .SingleOrDefaultAsync(row => row.Stamp == new DateTime(2026, 7, 1, 13, 45, 30, 123));

        Assert.That(found, Is.Not.Null);
    }

    [Test]
    public async Task AMomentWithAnOffsetWrittenIntoTheExpressionFindsItsRowAsync()
    {
        await using var context = Open();

        var found = await context.Rows.SingleOrDefaultAsync(
            row => row.Moment == new DateTimeOffset(2026, 7, 1, 13, 45, 30, TimeSpan.FromHours(3)));

        Assert.That(found, Is.Not.Null);
    }

    #endregion

    #region What the answer has to mean

    /// <summary>
    /// CONTROL, and it is the one that makes the four above worth running: a constant that names a
    /// DIFFERENT value finds nothing. Without it, every case here would pass against a provider whose
    /// filter reached nothing at all.
    /// </summary>
    [Test]
    public async Task AConstantThatNamesAnotherValueFindsNothingAsync()
    {
        await using var context = Open();

        var found = await context.Rows.SingleOrDefaultAsync(row => row.Date == new DateOnly(2020, 1, 1));

        Assert.That(found, Is.Null);
    }

    /// <summary>
    /// The second control: the same instant written with a DIFFERENT offset is the same moment, and a
    /// typed literal compares instants. The quoted-string form compared text, so this was false for
    /// two spellings of one moment.
    /// </summary>
    [Test]
    public async Task TheSameMomentUnderAnotherOffsetIsTheSameMomentAsync()
    {
        await using var context = Open();

        var found = await context.Rows.SingleOrDefaultAsync(
            row => row.Moment == new DateTimeOffset(2026, 7, 1, 10, 45, 30, TimeSpan.Zero));

        Assert.That(found, Is.Not.Null, "13:45+03:00 and 10:45Z are one instant");
    }

    /// <summary>
    /// And the shape that ALREADY worked has to go on working: a captured local is parameterised by
    /// EF, never inlined, so it never met the grammar at all.
    /// </summary>
    [Test]
    public async Task ACapturedLocalStillWorksAsync()
    {
        await using var context = Open();

        var when = THE_DATE;
        var found = await context.Rows.SingleOrDefaultAsync(row => row.Date == when);

        Assert.That(found, Is.Not.Null);
    }

    /// <summary>
    /// The values come back as they went in, fraction and offset included - which is what says the
    /// literal carried the value rather than a rounded rendering of it.
    /// </summary>
    [Test]
    public async Task TheValuesComeBackAsTheyWentInAsync()
    {
        await using var context = Open();

        var row = await context.Rows.SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(row.Date, Is.EqualTo(THE_DATE));
            Assert.That(row.Time, Is.EqualTo(THE_TIME));
            Assert.That(row.Stamp, Is.EqualTo(THE_STAMP));
            Assert.That(row.Moment.ToUniversalTime(), Is.EqualTo(THE_MOMENT.ToUniversalTime()));
        });
    }

    #endregion

    #region Tools

    private TemporalDbContext Open()
    {
        var options = new DbContextOptionsBuilder<TemporalDbContext>()
            .UseWitDb($"Data Source={m_path}")
            .Options;

        return new TemporalDbContext(options);
    }

    private sealed class TemporalRow
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        public DateOnly Date { get; set; }

        public TimeOnly Time { get; set; }

        public DateTime Stamp { get; set; }

        public DateTimeOffset Moment { get; set; }
    }

    private sealed class TemporalDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<TemporalRow> Rows => Set<TemporalRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TemporalRow>(entity =>
            {
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Label).IsRequired();
            });
        }
    }

    #endregion
}
