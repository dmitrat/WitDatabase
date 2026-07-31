using Microsoft.EntityFrameworkCore;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Integration;

/// <summary>
/// Phase 7 — the declared size, followed from the EF model all the way to a refused save.
/// </summary>
/// <remarks>
/// <para>
/// This path was covered nowhere, and the reason is worth keeping: it ran through <b>two independent
/// defects that covered for each other</b>. The migrations generator dropped <c>maxLength</c> on the way
/// to the DDL, so the column was created without a size; the engine ignored declared sizes anyway, so a
/// column that did carry one behaved no differently. Fixing either alone changed nothing observable,
/// which is exactly how a pair like that survives an audit.
/// </para>
/// <para>
/// Both halves are fixed now — the generator emits <c>VARCHAR(n)</c>, and the engine records, reports
/// and enforces it — so the combination is testable for the first time. It is tested end to end rather
/// than at either seam, because each seam already has its own test and neither would have caught this.
/// </para>
/// </remarks>
[TestFixture]
public class DeclaredSizeEndToEndTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbSize_{Guid.NewGuid():N}.witdb");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (File.Exists(m_testDbPath))
                File.Delete(m_testDbPath);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region Tests

    /// <summary>
    /// A model that declares <c>HasMaxLength(5)</c> must refuse a six-character value on save.
    /// </summary>
    [Test]
    public void ModelMaxLengthIsEnforcedOnSaveTest()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();

        // The control first: a value that fits must go in, or "it was refused" would mean nothing.
        context.Codes.Add(new Code { Id = 1, Value = "12345" });
        Assert.That(() => context.SaveChanges(), Throws.Nothing,
            "a value of exactly the declared length must be accepted");

        using var second = NewContext();
        second.Codes.Add(new Code { Id = 2, Value = "123456" });

        var refused = Assert.Throws<DbUpdateException>(() => second.SaveChanges());

        TestContext.Out.WriteLine($"PROBE  six characters into HasMaxLength(5)  ->  {refused!.InnerException?.Message}");

        Assert.That(refused.InnerException?.Message, Does.Contain("too long"),
            "the refusal must say what was wrong with the value");
    }

    /// <summary>
    /// And the size the model declared has to be in the schema the database describes, not only in the
    /// model - which is the half the migrations generator used to drop.
    /// </summary>
    [Test]
    public void ModelMaxLengthReachesTheDatabaseSchemaTest()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();

        var connection = context.Database.GetDbConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS "
            + "WHERE TABLE_NAME = 'Codes' AND COLUMN_NAME = 'Value'";

        var reported = command.ExecuteScalar();

        TestContext.Out.WriteLine($"PROBE  INFORMATION_SCHEMA says the column's length is  ->  {reported}");

        Assert.That(Convert.ToInt32(reported), Is.EqualTo(5),
            "the length the model declared must be what the database describes");
    }

    #endregion

    #region Tools

    private SizeContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SizeContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;

        return new SizeContext(options);
    }

    private sealed class SizeContext : DbContext
    {
        public SizeContext(DbContextOptions<SizeContext> options) : base(options)
        {
        }

        public DbSet<Code> Codes => Set<Code>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Code>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Value).HasMaxLength(5);
            });
        }
    }

    private sealed class Code
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }

    #endregion
}
