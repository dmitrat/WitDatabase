using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using OutWit.Database.EntityFramework.Extensions;

namespace OutWit.Database.EntityFramework.Tests.Metadata;

/// <summary>
/// Regression tests for the relational convention set (KnownIssues #1b).
/// </summary>
/// <remarks>
/// The provider used to register no <see cref="IProviderConventionSetBuilder"/> at all, so EF Core
/// fell back to the core builder and the whole relational convention set was missing. The visible
/// symptom was that default table names came from the entity CLR type instead of the
/// <c>DbSet</c> property, so the same model produced <c>Website</c> here and <c>Websites</c> on
/// every other provider — which is why a migration written against another provider's names failed
/// with "Table 'Websites' not found".
/// </remarks>
[TestFixture]
public class WitConventionSetBuilderTests
{
    #region Fields

    private string m_testDbPath = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbConventions_{Guid.NewGuid():N}.witdb");
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

    #region Convention Set Tests

    [Test]
    public void ProviderConventionSetBuilderIsRelationalTest()
    {
        using var context = CreateContext();

        var builder = context.GetService<IProviderConventionSetBuilder>();

        Assert.That(builder, Is.InstanceOf<RelationalConventionSetBuilder>(),
            "A relational provider must supply a RelationalConventionSetBuilder. With the core " +
            "ProviderConventionSetBuilder the entire relational convention set is silently absent.");
    }

    [Test]
    public void TableNameFromDbSetConventionIsRegisteredTest()
    {
        using var context = CreateContext();

        var conventionSet = context.GetService<IProviderConventionSetBuilder>().CreateConventionSet();

        var names = conventionSet.ModelFinalizingConventions.Select(c => c.GetType().Name)
            .Concat(conventionSet.EntityTypeAddedConventions.Select(c => c.GetType().Name))
            .ToList();

        Assert.That(names, Does.Contain("TableNameFromDbSetConvention"));
    }

    #endregion

    #region Table Naming Tests

    [Test]
    public void TableNameComesFromDbSetPropertyNotClrTypeTest()
    {
        using var context = CreateContext();

        var website = context.Model.FindEntityType(typeof(ConventionWebsite))!;
        var visit = context.Model.FindEntityType(typeof(ConventionVisit))!;

        Assert.Multiple(() =>
        {
            Assert.That(website.GetTableName(), Is.EqualTo("Websites"),
                "DbSet<ConventionWebsite> Websites => table \"Websites\"");
            Assert.That(visit.GetTableName(), Is.EqualTo("Visits"),
                "DbSet<ConventionVisit> Visits => table \"Visits\"");
        });
    }

    [Test]
    public void PrimaryKeyAndIndexNamesFollowTheTableNameTest()
    {
        using var context = CreateContext();

        var visit = context.Model.FindEntityType(typeof(ConventionVisit))!;

        Assert.Multiple(() =>
        {
            Assert.That(visit.FindPrimaryKey()!.GetName(), Is.EqualTo("PK_Visits"));
            Assert.That(visit.GetIndexes().Select(i => i.GetDatabaseName()),
                Does.Contain("IX_Visits_WebsiteId"));
        });
    }

    [Test]
    public void CreatedTableIsQueryableUnderTheDbSetNameTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Websites.Add(new ConventionWebsite { Id = 1, Name = "n" });
        context.SaveChanges();

        // Raw SQL against the name every other provider would produce.
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"SELECT COUNT(*) FROM ""Websites""";
            Assert.That(Convert.ToInt64(command.ExecuteScalar()), Is.EqualTo(1));
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    #endregion

    #region Migration Operation Fidelity Tests

    [Test]
    public void CreateTableOperationsCarryMaxLengthTest()
    {
        using var context = CreateContext();

        var operations = Diff(context, null, DesignTimeRelationalModel(context));

        var websites = operations.OfType<CreateTableOperation>().Single(o => o.Name == "Websites");
        var name = websites.Columns.Single(c => c.Name == "Name");

        Assert.That(name.MaxLength, Is.EqualTo(100),
            "HasMaxLength(100) must survive into the migration operation. The previous custom " +
            "differ rebuilt operations by hand and dropped every facet.");
    }

    [Test]
    public void StockDifferAcceptsTheDesignTimeModelTest()
    {
        using var context = CreateContext();

        // The custom WitModelRuntimeInitializer used to hand the differ a read-optimized model,
        // which threw "The requested configuration is not stored in the read-optimized model".
        Assert.DoesNotThrow(() => Diff(context, null, DesignTimeRelationalModel(context)));
    }

    [Test]
    public void DiffOfTwoModelsProducesAddColumnOperationTest()
    {
        // This is KnownIssues #1a in its smallest form: `dotnet ef migrations add` after adding one
        // property. The old differ swallowed the exception and returned an empty operation list, so
        // the migration was empty while the snapshot recorded the new property.
        using var before = CreateContext();
        using var after = CreateContextWithExtraColumn();

        var operations = Diff(
            after,
            DesignTimeRelationalModel(before),
            DesignTimeRelationalModel(after));

        var addColumn = operations.OfType<AddColumnOperation>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(operations, Is.Not.Empty,
                "Adding a property must produce at least one migration operation");
            Assert.That(addColumn.Select(o => o.Name), Does.Contain("ExcludedPaths"));
            Assert.That(addColumn.Single(o => o.Name == "ExcludedPaths").Table, Is.EqualTo("Websites"));
            Assert.That(addColumn.Single(o => o.Name == "ExcludedPaths").MaxLength, Is.EqualTo(1000));
        });
    }

    #endregion

    #region Helper Methods

    private ConventionContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConventionContext>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");
        return new ConventionContext(optionsBuilder.Options);
    }

    private ConventionContextWithExtraColumn CreateContextWithExtraColumn()
    {
        var optionsBuilder = new DbContextOptionsBuilder<ConventionContextWithExtraColumn>();
        optionsBuilder.UseWitDb($"Data Source={m_testDbPath}");
        return new ConventionContextWithExtraColumn(optionsBuilder.Options);
    }

    private static IRelationalModel DesignTimeRelationalModel(DbContext context)
        => context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

    private static IReadOnlyList<MigrationOperation> Diff(
        DbContext context, IRelationalModel? source, IRelationalModel? target)
        => context.GetService<IMigrationsModelDiffer>().GetDifferences(source, target);

    #endregion

    #region Test Models

    public class ConventionWebsite
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ConventionVisit
    {
        public int Id { get; set; }
        public int WebsiteId { get; set; }
        public string UrlPath { get; set; } = string.Empty;
    }

    public class ConventionWebsiteWithPaths
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ExcludedPaths { get; set; }
    }

    public class ConventionContext : DbContext
    {
        public ConventionContext(DbContextOptions<ConventionContext> options)
            : base(options)
        {
        }

        public DbSet<ConventionWebsite> Websites => Set<ConventionWebsite>();
        public DbSet<ConventionVisit> Visits => Set<ConventionVisit>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ConventionWebsite>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            });

            modelBuilder.Entity<ConventionVisit>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.UrlPath).IsRequired().HasMaxLength(500);
                entity.HasIndex(e => e.WebsiteId);
            });
        }
    }

    /// <summary>
    /// The same model as <see cref="ConventionContext"/> plus one nullable column, so the two can be
    /// diffed exactly as <c>dotnet ef migrations add</c> diffs a snapshot against a changed model.
    /// </summary>
    public class ConventionContextWithExtraColumn : DbContext
    {
        public ConventionContextWithExtraColumn(DbContextOptions<ConventionContextWithExtraColumn> options)
            : base(options)
        {
        }

        public DbSet<ConventionWebsiteWithPaths> Websites => Set<ConventionWebsiteWithPaths>();
        public DbSet<ConventionVisit> Visits => Set<ConventionVisit>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ConventionWebsiteWithPaths>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.ExcludedPaths).HasMaxLength(1000);
            });

            modelBuilder.Entity<ConventionVisit>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedNever();
                entity.Property(e => e.UrlPath).IsRequired().HasMaxLength(500);
                entity.HasIndex(e => e.WebsiteId);
            });
        }
    }

    #endregion
}
