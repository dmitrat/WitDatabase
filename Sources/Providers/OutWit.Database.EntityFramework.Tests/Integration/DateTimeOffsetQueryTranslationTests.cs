using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// LINQ translation for <see cref="DateTimeOffset"/> comparisons (OAuth state expiry, invites).
/// </summary>
[TestFixture]
public class DateTimeOffsetQueryTranslationTests
{
    private string? m_testDbPath;

    [SetUp]
    public void Setup() =>
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbEf_Dto_{Guid.NewGuid():N}.witdb");

    [TearDown]
    public void TearDown()
    {
        if (m_testDbPath is not null && File.Exists(m_testDbPath))
        {
            try { File.Delete(m_testDbPath); } catch { /* best effort */ }
        }
    }

    [Test]
    public async Task WhereExpiresAtUtcGreaterThanUtcNow_TranslatesAndFiltersTest()
    {
        var options = new DbContextOptionsBuilder<ExpiryDbContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

        await using var context = new ExpiryDbContext(options);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        var expired = DateTimeOffset.UtcNow.AddMinutes(-5);
        var valid = DateTimeOffset.UtcNow.AddMinutes(30);

        context.States.Add(new ExpiryRow { State = "expired", ExpiresAtUtc = expired });
        context.States.Add(new ExpiryRow { State = "valid", ExpiresAtUtc = valid });
        await context.SaveChangesAsync().ConfigureAwait(false);

        var row = await context.States
            .FirstOrDefaultAsync(x => x.State == "valid" && x.ExpiresAtUtc >= DateTimeOffset.UtcNow)
            .ConfigureAwait(false);

        Assert.That(row, Is.Not.Null);
        Assert.That(row!.State, Is.EqualTo("valid"));
    }

    private sealed class ExpiryRow
    {
        public int Id { get; set; }
        public string State { get; set; } = "";
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    private sealed class ExpiryDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<ExpiryRow> States => Set<ExpiryRow>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExpiryRow>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.State).IsRequired();
                e.Property(x => x.ExpiresAtUtc);
            });
        }
    }
}
