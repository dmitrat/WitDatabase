using Microsoft.EntityFrameworkCore;
using OutWit.Database.AdoNet;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// An owned collection gets a composite primary key of (owner key, generated ordinal). EF treats
/// the ordinal as store-generated and inserts without it, asking for it back with RETURNING - but
/// the emitted DDL declares it as a plain NOT NULL column, because value generation is only emitted
/// for a single-column integer key. The insert then fails on the NOT NULL constraint.
///
/// Found by EF Core's specification suite; it is what blocks WitFindTest today.
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
    [Ignore("CONFIRMED. The generated DDL declares the owned collection's key column as "
            + "\"Id\" INT NOT NULL inside PRIMARY KEY (\"OwnerId\", \"Id\") with no value "
            + "generation, while EF emits INSERT INTO ... (\"OwnerId\", \"Prop\") VALUES (...) "
            + "RETURNING \"Id\". Observed: NOT NULL constraint failed: "
            + "Owner_Items.Id. Value generation is only emitted for a single-column integer key.")]
    public void OwnedCollectionRowIsInsertedWithAGeneratedKeyTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Add(new Owner
        {
            Id = 1,
            Items = { new OwnedItem { Prop = "first" }, new OwnedItem { Prop = "second" } },
        });

        Assert.DoesNotThrow(
            () => context.SaveChanges(),
            "EF generates the owned collection's ordinal key in the store, so the schema must "
            + "declare it as generated");

        Assert.That(context.Set<Owner>().Single().Items, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// States the mechanism separately from the symptom: the column carries EF's
    /// ValueGenerated.OnAdd, so whatever the schema says about it must reflect that.
    /// </summary>
    [Test]
    [Ignore("CONFIRMED. The column is marked ValueGenerated.OnAdd in the model, but the DDL for it "
            + "is \"Id\" INT NOT NULL - the AUTOINCREMENT clause is only emitted when the integer "
            + "key is the sole primary key column.")]
    public void OwnedCollectionKeyColumnIsDeclaredAsGeneratedTest()
    {
        using var context = CreateContext();

        // The owner's own key is AUTOINCREMENT, so asserting against the whole script would pass
        // while the defect stands. Only the owned collection's own statement is evidence.
        var statement = CreateStatementFor(context.Database.GenerateCreateScript(), "Owner_Items");

        Assert.That(statement, Does.Contain("AUTOINCREMENT"),
            $"the owned collection's key column is store-generated, so its own CREATE TABLE must "
            + $"say so. Generated:{Environment.NewLine}{statement}");
    }

    /// <summary>
    /// Extracts the CREATE TABLE statement for one table out of a create script.
    /// </summary>
    private static string CreateStatementFor(string script, string tableName)
    {
        var start = script.IndexOf($"\"{tableName}\"", StringComparison.Ordinal);

        Assert.That(start, Is.GreaterThanOrEqualTo(0),
            $"the create script must contain a statement for '{tableName}'");

        var end = script.IndexOf(");", start, StringComparison.Ordinal);

        return end < 0 ? script[start..] : script[start..(end + 2)];
    }

    #endregion

    #region Helpers

    private OwnedContext CreateContext() => new(m_databasePath);

    #endregion

    #region Model

    public class Owner
    {
        public int Id { get; set; }

        public List<OwnedItem> Items { get; } = [];
    }

    public class OwnedItem
    {
        public string Prop { get; set; } = null!;
    }

    private sealed class OwnedContext(string path) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.UseWitDb(new WitDbConnection($"Data Source={path}"));

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.Entity<Owner>().OwnsMany(e => e.Items, b => b.ToTable("Owner_Items"));
    }

    #endregion
}
