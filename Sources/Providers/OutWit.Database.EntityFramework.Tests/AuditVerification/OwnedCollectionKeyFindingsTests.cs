using Microsoft.EntityFrameworkCore;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// An owned collection gets a composite key of (owner key, generated ordinal) unless configured
/// otherwise, and no file-backed provider can satisfy that - the row counter can only stand behind a
/// single-column key. **EF Core's SQLite provider has the same limit**, which is why the first
/// version of this fixture was wrong: it asserted that WitDatabase should generate the value, and
/// the differential oracle showed SQLite failing on the identical model.
///
/// What SQLite does and WitDatabase did not is *say so*. It rejects the model at validation, naming
/// the entity and the key. WitDatabase accepted the model, emitted DDL that could not work, and
/// failed on the first insert with `NOT NULL constraint failed: Item.Id` - a data error for what is
/// a modelling mistake, pointing at a column the caller never mentioned.
///
/// So the defect is the missing diagnosis, and that is what these tests hold.
/// </summary>
[TestFixture]
public sealed class OwnedCollectionKeyFindingsTests
{
    #region Fields

    private string m_directory = null!;
    private string m_databasePath = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb_owned_{Guid.NewGuid():N}");
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
    public void GeneratedValueInACompositeKeyIsRejectedWhenTheModelIsBuiltTest()
    {
        using var context = new OwnedContext(m_databasePath);

        var exception = Assert.Throws<InvalidOperationException>(
            () => context.Database.EnsureCreated(),
            "the model cannot work, so it must be refused before any schema is written");

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("composite key"),
                "the message must name the limit that was hit");
            Assert.That(exception.Message, Does.Contain("Item"),
                "and the entity type that hit it");
            Assert.That(exception.Message, Does.Contain("HasKey"),
                "and what to do about it - an owned collection is the usual way to arrive here");
        });
    }

    /// <summary>
    /// The failure must arrive before any data work. Reporting it on the first insert is what made
    /// the original diagnosis so hard: the error named a column the caller had never written to.
    /// </summary>
    [Test]
    public void TheModelIsRefusedBeforeAnySchemaIsWrittenTest()
    {
        using (var context = new OwnedContext(m_databasePath))
        {
            Assert.Throws<InvalidOperationException>(() => context.Database.EnsureCreated());
        }

        Assert.That(File.Exists(m_databasePath), Is.False,
            "a model that is refused must leave no database behind");
    }

    /// <summary>
    /// The check must not catch composite keys that are perfectly serviceable - only those with a
    /// generated member. Without this the validation would refuse most join tables.
    /// </summary>
    [Test]
    public void CompositeKeyWithoutGeneratedValuesIsAcceptedTest()
    {
        using var context = new ExplicitKeyContext(m_databasePath);

        Assert.DoesNotThrow(() => context.Database.EnsureCreated());

        context.Add(new Owner { Id = 1, Items = { new Item { Ordinal = 1, Prop = "a" } } });

        Assert.DoesNotThrow(() => context.SaveChanges());
    }

    #endregion

    #region Model

    public class Owner
    {
        public int Id { get; set; }

        public List<Item> Items { get; } = [];
    }

    public class Item
    {
        public int Ordinal { get; set; }

        public string Prop { get; set; } = null!;
    }

    /// <summary>
    /// An owned collection with EF's default key: (OwnerId, generated ordinal).
    /// </summary>
    private sealed class OwnedContext(string path) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseWitDb(new WitDbConnection($"Data Source={path}"));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Owner>(e =>
            {
                e.HasKey(x => x.Id);
                e.OwnsMany(x => x.Items);
            });
    }

    /// <summary>
    /// The same shape with the ordinal supplied by the caller - a composite key that works.
    /// </summary>
    private sealed class ExplicitKeyContext(string path) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseWitDb(new WitDbConnection($"Data Source={path}"));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Owner>(e =>
            {
                e.HasKey(x => x.Id);
                e.OwnsMany(x => x.Items, b =>
                {
                    b.Property(x => x.Ordinal).ValueGeneratedNever();
                    b.HasKey("OwnerId", nameof(Item.Ordinal));
                });
            });
    }

    #endregion
}
