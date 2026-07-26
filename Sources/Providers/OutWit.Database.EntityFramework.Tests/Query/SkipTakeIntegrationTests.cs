using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Query;

/// <summary>
/// Executing tests for <c>Skip</c>/<c>Take</c> pagination.
/// </summary>
/// <remarks>
/// <c>Skip(n)</c> without <c>Take(n)</c> returned an empty list: the generator emitted SQLite's
/// <c>LIMIT -1 OFFSET n</c> placeholder and the engine took the -1 literally. The existing query
/// tests in this project assert on LINQ expression trees and BCL reflection facts, never on a
/// result set, so none of them could have caught it.
/// </remarks>
[TestFixture]
public class SkipTakeIntegrationTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbPaging_{Guid.NewGuid():N}.witdb");

        using var context = CreateContext();
        context.Database.EnsureCreated();

        for (var i = 1; i <= 5; i++)
            context.Items.Add(new PagedItem { Id = i, Name = $"item-{i}" });

        context.SaveChanges();
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

    #region Tests

    [Test]
    public void SkipWithoutTakeReturnsTheRemainingRowsTest()
    {
        using var context = CreateContext();

        var ids = context.Items.OrderBy(x => x.Id).Skip(2).Select(x => x.Id).ToList();

        Assert.That(ids, Is.EqualTo(new[] { 3, 4, 5 }));
    }

    [Test]
    public void SkipAndTakePaginateTest()
    {
        using var context = CreateContext();

        var ids = context.Items.OrderBy(x => x.Id).Skip(1).Take(2).Select(x => x.Id).ToList();

        Assert.That(ids, Is.EqualTo(new[] { 2, 3 }));
    }

    [Test]
    public void TakeWithoutSkipBoundsTheResultTest()
    {
        using var context = CreateContext();

        var ids = context.Items.OrderBy(x => x.Id).Take(2).Select(x => x.Id).ToList();

        Assert.That(ids, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void SkipPastTheEndReturnsNothingTest()
    {
        using var context = CreateContext();

        var ids = context.Items.OrderBy(x => x.Id).Skip(99).Select(x => x.Id).ToList();

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public void SkipZeroReturnsEveryRowTest()
    {
        using var context = CreateContext();

        var ids = context.Items.OrderBy(x => x.Id).Skip(0).Select(x => x.Id).ToList();

        Assert.That(ids, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public async Task SkipWithoutTakeReturnsTheRemainingRowsAsyncTest()
    {
        await using var context = CreateContext();

        var ids = await context.Items.OrderBy(x => x.Id).Skip(3).Select(x => x.Id).ToListAsync();

        Assert.That(ids, Is.EqualTo(new[] { 4, 5 }));
    }

    #endregion

    #region Helper Methods

    private PagedContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<PagedContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");
        return new PagedContext(optionsBuilder.Options);
    }

    #endregion

    #region Test Models

    public class PagedItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class PagedContext : DbContext
    {
        public PagedContext(DbContextOptions<PagedContext> options)
            : base(options)
        {
        }

        public DbSet<PagedItem> Items => Set<PagedItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<PagedItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            });
        }
    }

    #endregion
}
