using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// <c>ToString()</c> and <c>Convert.To*</c> inside a query, which is <c>Docs/KnownIssues.md</c> 3.
/// </summary>
/// <remarks>
/// <para>
/// The engine was never at fault: <c>CAST(x AS VARCHAR)</c>, <c>CAST(x AS TEXT)</c> and
/// <c>CONVERT(VARCHAR, x)</c> all answer correctly when executed directly. Nothing translated the
/// CLR call into one of them, so EF had nothing to emit and the query failed to translate at all -
/// a translation error rather than a client-side fallback, which is why it is worth fixing rather
/// than documenting.
/// </para>
/// <para>
/// The shape in the issue is a stored code presented as a name: <c>GroupBy(x =&gt; x.DeviceType)</c>
/// followed by <c>group.Key.ToString()</c>. It is common wherever an int stands for something.
/// </para>
/// </remarks>
[TestFixture]
public class ConvertToStringTranslationTests
{
    #region Fields

    private string m_path = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUpAsync()
    {
        m_path = Path.Combine(Path.GetTempPath(), $"WitDbEf_ToString_{Guid.NewGuid():N}.witdb");

        await using var context = Open();
        await context.Database.EnsureCreatedAsync();

        context.Events.AddRange(
            new DeviceEvent { DeviceType = 42, Weight = 1.5, Flag = true },
            new DeviceEvent { DeviceType = 42, Weight = 2.5, Flag = false },
            new DeviceEvent { DeviceType = 7, Weight = 3.5, Flag = true });

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

    #region Tests

    /// <summary>
    /// The query from the issue, verbatim in shape: group by an int column and present the key as a
    /// string. It threw at translation time.
    /// </summary>
    [Test]
    public async Task AGroupKeyCanBePresentedAsAStringAsync()
    {
        await using var context = Open();

        var rows = await context.Events
            .GroupBy(item => item.DeviceType)
            .Select(group => new { Key = group.Key.ToString(), Count = group.LongCount() })
            .OrderBy(row => row.Key)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows.Select(row => row.Key), Is.EquivalentTo(new[] { "42", "7" }));
            Assert.That(rows.Single(row => row.Key == "42").Count, Is.EqualTo(2));
        });
    }

    /// <summary>
    /// The same call on a plain column rather than on a group key, and on the other primitive types
    /// that reach it - each one is a separate CLR method, so one registration does not cover them.
    /// </summary>
    [Test]
    public async Task AColumnCanBePresentedAsAStringAsync()
    {
        await using var context = Open();

        var texts = await context.Events
            .Where(item => item.DeviceType == 7)
            .Select(item => new
            {
                Number = item.DeviceType.ToString(),
                Weight = item.Weight.ToString(),
                Flag = item.Flag.ToString()
            })
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(texts.Single().Number, Is.EqualTo("7"));
            Assert.That(texts.Single().Weight, Is.Not.Empty);
            Assert.That(texts.Single().Flag, Is.Not.Empty);
        });
    }

    /// <summary>
    /// <c>Convert.ToString</c> is a different CLR method from <c>ToString()</c> and gets there by the
    /// same registration - so it is asked separately rather than assumed.
    /// </summary>
    [Test]
    public async Task ConvertToStringTranslatesTooAsync()
    {
        await using var context = Open();

        var texts = await context.Events
            .Where(item => item.DeviceType == 7)
            .Select(item => Convert.ToString(item.DeviceType))
            .ToListAsync();

        Assert.That(texts.Single(), Is.EqualTo("7"));
    }

    /// <summary>
    /// <b>The translation has to be a SERVER-side one, and this is what says so.</b> A query that
    /// filters on the converted text can only work if the conversion reached the database: if EF had
    /// quietly evaluated it on the client, this would either throw or read every row first.
    /// </summary>
    [Test]
    public async Task TheConversionHappensInTheDatabaseAsync()
    {
        await using var context = Open();

        var matching = await context.Events
            .Where(item => item.DeviceType.ToString() == "42")
            .CountAsync();

        Assert.That(matching, Is.EqualTo(2));
    }

    /// <summary>
    /// CONTROL: the same query asking for a value that is not there answers zero. Without it, "the
    /// conversion works" would be satisfied by a filter that matched everything.
    /// </summary>
    [Test]
    public async Task AConversionThatMatchesNothingAnswersZeroAsync()
    {
        await using var context = Open();

        var matching = await context.Events
            .Where(item => item.DeviceType.ToString() == "999")
            .CountAsync();

        Assert.That(matching, Is.Zero);
    }

    #endregion

    #region Tools

    private EventsDbContext Open()
    {
        var options = new DbContextOptionsBuilder<EventsDbContext>()
            .UseWitDb($"Data Source={m_path}")
            .Options;

        return new EventsDbContext(options);
    }

    private sealed class DeviceEvent
    {
        public int Id { get; set; }

        public int DeviceType { get; set; }

        public double Weight { get; set; }

        public bool Flag { get; set; }
    }

    private sealed class EventsDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<DeviceEvent> Events => Set<DeviceEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DeviceEvent>(entity => entity.HasKey(row => row.Id));
        }
    }

    #endregion
}
