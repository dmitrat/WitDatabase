using Microsoft.EntityFrameworkCore;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// EnsureDeleted removed the data file and reported success while leaving the index directory on
/// disk, so a database recreated at the same path inherited the deleted database's indexes and
/// rejected rows it did not contain.
///
/// Found by EF Core's specification suite: its shared-store fixtures delete and recreate the store
/// between fixtures, and every seeded row collided with an index belonging to a database that no
/// longer existed.
/// </summary>
[TestFixture]
public sealed class DatabaseDeletionFindingsTests
{
    #region Fields

    private string m_directory = null!;
    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb_delete_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);

        m_databasePath = Path.Combine(m_directory, "app.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        if (!Directory.Exists(m_directory))
            return;

        try
        {
            Directory.Delete(m_directory, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup only.
        }
    }

    #endregion

    #region Tests

    [Test]
    public void EnsureDeletedLeavesNothingOfTheDatabaseBehindTest()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new DeletionProbe { Id1 = 88, Id2 = "Cat", Foo = "Olive" });
            context.SaveChanges();
        }

        Assert.That(Directory.GetFileSystemEntries(m_directory), Is.Not.Empty,
            "the database must exist before the deletion is meaningful");

        using (var context = CreateContext())
        {
            Assert.That(context.Database.EnsureDeleted(), Is.True);
        }

        Assert.That(Directory.GetFileSystemEntries(m_directory, "*", SearchOption.AllDirectories),
            Is.Empty,
            "EnsureDeleted reported success, so nothing belonging to the database may remain - the "
            + "index directory used to survive it");
    }

    [Test]
    public void DatabaseRecreatedAtTheSamePathDoesNotInheritTheDeletedIndexesTest()
    {
        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();
            context.Add(new DeletionProbe { Id1 = 88, Id2 = "Cat", Foo = "Olive" });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            context.Database.EnsureDeleted();
        }

        using (var context = CreateContext())
        {
            context.Database.EnsureCreated();

            Assert.That(context.Set<DeletionProbe>().Count(), Is.EqualTo(0),
                "the new database starts empty");

            Assert.DoesNotThrow(
                () =>
                {
                    context.Add(new DeletionProbe { Id1 = 88, Id2 = "Cat", Foo = "Olive" });
                    context.SaveChanges();
                },
                "the key is free in the new database - it was rejected by the deleted database's "
                + "primary key index, which had outlived it on disk");
        }
    }

    #endregion

    #region Helpers

    private DeletionProbeContext CreateContext() => new(m_databasePath);

    #endregion

    #region Model

    public class DeletionProbe
    {
        public int Id1 { get; set; }

        public string Id2 { get; set; } = null!;

        public string? Foo { get; set; }
    }

    private sealed class DeletionProbeContext(string path) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseWitDb(new WitDbConnection($"Data Source={path}"));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<DeletionProbe>().HasKey(e => new { e.Id1, e.Id2 });
    }

    #endregion
}
