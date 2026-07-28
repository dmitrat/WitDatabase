using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Storage;

namespace OutWit.Database.EntityFramework.Tests.AuditVerification;

/// <summary>
/// Verification of the migrations half of the <c>dropin-gaps</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// These findings all share a shape worth naming: the generator <b>emits a SQL comment</b> where it
/// cannot emit a statement. A comment is a valid script that changes nothing, so the migration is
/// reported as applied while the database keeps its old schema - the model and the database diverge
/// with no error anywhere. Each test therefore asserts not merely that SQL was produced, but that it
/// is not comment-only.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class DropInGapsMigrationsTests
{
    private string m_testDbPath = null!;

    [SetUp]
    public void Setup()
    {
        m_testDbPath = Path.Combine(Path.GetTempPath(), $"WitDbDropIn_{Guid.NewGuid():N}.witdb");
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

    #region AlterColumn

    /// <summary>
    /// The finding asked for a statement. The oracle says otherwise: EF Core's SQLite provider
    /// refuses an AlterColumnOperation outright, because its ALTER TABLE cannot change a type
    /// either. What was wrong here was never the missing statement - it was that nothing was
    /// emitted and nothing was said, so the migration was recorded as applied and the column kept
    /// its old type.
    /// </summary>
    [Test]
    public void AlterColumnRefusesATypeChangeRatherThanEmittingNothingTest()
    {
        var operation = new AlterColumnOperation
        {
            Name = "Amount",
            Table = "Orders",
            ClrType = typeof(decimal),
            ColumnType = "DECIMAL(18,2)",
            IsNullable = false,
            OldColumn = new AddColumnOperation
            {
                Name = "Amount",
                Table = "Orders",
                ClrType = typeof(int),
                ColumnType = "INT",
                IsNullable = false
            }
        };

        AssertRefusedRatherThanCommentedOut(operation, "a column type change");
    }

    #endregion

    #region AddPrimaryKey / DropPrimaryKey / RenameIndex

    [Test]
    public void AddPrimaryKeyIsRefusedRatherThanCommentedOutTest()
    {
        // A table left without the key its model declares takes duplicates in silence, so a
        // migration that appears to succeed is the worse outcome. SQLite refuses this too.
        AssertRefusedRatherThanCommentedOut(
            new AddPrimaryKeyOperation { Name = "PK_T", Table = "T", Columns = ["Id"] },
            "adding a primary key to an existing table");
    }

    [Test]
    public void DropPrimaryKeyIsRefusedRatherThanCommentedOutTest()
    {
        AssertRefusedRatherThanCommentedOut(
            new DropPrimaryKeyOperation { Name = "PK_T", Table = "T" },
            "dropping the primary key of an existing table");
    }

    [Test]
    public void RenameIndexIsRefusedRatherThanCommentedOutTest()
    {
        // The comment left the index under its old name, so the migration that referred to the new
        // one failed later for a reason with no connection to the cause.
        AssertRefusedRatherThanCommentedOut(
            new RenameIndexOperation { Name = "IX_Old", NewName = "IX_New", Table = "T" },
            "renaming an index");
    }

    #endregion

    #region Index options silently dropped

    [Test]
    public void FilteredIndexKeepsItsFilterTest()
    {
        // Finding: WitMigrationsSqlGenerator.cs:239 - HasFilter, IncludeProperties and descending
        // indexes are dropped. A filtered index silently becoming a full index is not just a
        // performance question: a filtered UNIQUE index enforces a *different* constraint.
        var sql = GenerateSql(new CreateIndexOperation
        {
            Name = "IX_T_Value",
            Table = "T",
            Columns = ["Value"],
            Filter = "[Value] IS NOT NULL",
            IsUnique = true
        });

        Assert.That(sql, Does.Contain("WHERE").IgnoreCase,
            $"the index filter must survive into the SQL. Generated: {sql}");
    }

    [Test]
    public void DescendingIndexKeepsItsDirectionTest()
    {
        var sql = GenerateSql(new CreateIndexOperation
        {
            Name = "IX_T_Value",
            Table = "T",
            Columns = ["Value"],
            IsDescending = [true]
        });

        Assert.That(sql, Does.Contain("DESC").IgnoreCase,
            $"the descending direction must survive into the SQL. Generated: {sql}");
    }

    #endregion

    #region Schemas

    /// <summary>
    /// The finding asked for real SQL here. That was more than the oracle does: EF Core's SQLite
    /// provider, which likewise has no schemas, returns no commands at all for this operation. So
    /// the defect was never the missing statement - it was the NotSupportedException, which failed
    /// migrations EF Core emits as a matter of course.
    /// </summary>
    [Test]
    public void EnsureSchemaIsIgnoredRatherThanRefusedTest()
    {
        var operation = new EnsureSchemaOperation { Name = "public" };

        Assert.DoesNotThrow(
            () => GenerateSql(operation),
            "WitDatabase has one schema, so there is nothing to create - but refusing the operation "
            + "fails migrations that are perfectly valid");

        Assert.That(GenerateSql(operation).Trim(), Is.Empty,
            "and nothing should be emitted for it either, as SQLite does");
    }

    /// <summary>
    /// The finding asked for the schema to reach the SQL. The oracle says the opposite: EF Core's
    /// SQLite provider, which likewise has no schemas, drops the name from DDL *and* DML and the
    /// table round-trips perfectly well. What was wrong was that WitDatabase dropped it in one
    /// place and kept it in the other, so the DDL and the DML disagreed about the table's name.
    ///
    /// The consistency is the requirement, and dropping it everywhere is how SQLite gets there.
    /// </summary>
    [Test]
    public void SchemaIsDroppedFromDdlAndDmlAlikeTest()
    {
        var ddl = GenerateSql(new CreateTableOperation
        {
            Name = "T",
            Schema = "public",
            Columns =
            {
                new AddColumnOperation { Name = "Id", Table = "T", Schema = "public", ClrType = typeof(int) }
            }
        });

        Assert.That(ddl, Does.Not.Contain("public"),
            $"the DDL names the table without its schema. Generated: {ddl}");

        var helper = new WitSqlGenerationHelper(new RelationalSqlGenerationHelperDependencies());

        Assert.That(helper.DelimitIdentifier("T", "public"), Is.EqualTo(helper.DelimitIdentifier("T")),
            "and so must everything else, or a query looks for a table the DDL never created");
    }

    #endregion

    #region ExecuteUpdate / ExecuteDelete across a navigation

    [Test]
    public void ExecuteDeleteAcrossANavigationWorksTest()
    {
        // Finding: WitDbServiceCollectionExtensions.cs:37 - ExecuteUpdate/ExecuteDelete support only
        // single-table statements. A predicate that reaches through a navigation makes EF emit a
        // DELETE with a correlated subquery, which is the shape OpenIddict's pruning uses.
        using var context = CreateContext();
        context.Database.EnsureCreated();

        var parent = new DropInTestContext.Parent { Id = 1, Name = "keep" };
        context.Parents.Add(parent);
        context.Children.Add(new DropInTestContext.Child { Id = 1, ParentId = 1 });
        context.SaveChanges();

        Assert.That(
            () => context.Children.Where(c => c.Parent!.Name == "keep").ExecuteDelete(),
            Throws.Nothing,
            "a predicate over a navigation is ordinary EF Core usage");
    }

    [Test]
    public void ExecuteUpdateAcrossANavigationWorksTest()
    {
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Parents.Add(new DropInTestContext.Parent { Id = 1, Name = "keep" });
        context.Children.Add(new DropInTestContext.Child { Id = 1, ParentId = 1, Note = "before" });
        context.SaveChanges();

        Assert.That(
            () => context.Children
                .Where(c => c.Parent!.Name == "keep")
                .ExecuteUpdate(s => s.SetProperty(c => c.Note, "after")),
            Throws.Nothing,
            "a predicate over a navigation is ordinary EF Core usage");
    }

    [Test]
    public void ExecuteDeleteOverAnExplicitJoinWorksTest()
    {
        // A harder shape than the navigation predicate: an explicit join, which is what forces a
        // genuinely multi-table statement rather than a correlated subquery over one table.
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Parents.Add(new DropInTestContext.Parent { Id = 1, Name = "keep" });
        context.Children.Add(new DropInTestContext.Child { Id = 1, ParentId = 1 });
        context.SaveChanges();

        Assert.That(
            () => context.Children
                .Join(context.Parents, c => c.ParentId, p => p.Id, (c, p) => new { c, p })
                .Where(x => x.p.Name == "keep")
                .Select(x => x.c)
                .ExecuteDelete(),
            Throws.Nothing,
            "EF Core allows ExecuteDelete over a join as long as the final selector names one table");
    }

    [Test]
    public void ExecuteDeleteOverAGroupedSubqueryWorksTest()
    {
        // The OpenIddict pruning shape: delete rows whose key is not present in a projection of
        // another table.
        using var context = CreateContext();
        context.Database.EnsureCreated();

        context.Parents.Add(new DropInTestContext.Parent { Id = 1, Name = "keep" });
        context.Parents.Add(new DropInTestContext.Parent { Id = 2, Name = "prune" });
        context.Children.Add(new DropInTestContext.Child { Id = 1, ParentId = 1 });
        context.Children.Add(new DropInTestContext.Child { Id = 2, ParentId = 2 });
        context.SaveChanges();

        Assert.That(
            () => context.Children
                .Where(c => !context.Parents
                    .Where(p => p.Name == "keep")
                    .Select(p => p.Id)
                    .Contains(c.ParentId))
                .ExecuteDelete(),
            Throws.Nothing,
            "pruning by a NOT IN over a filtered projection of another table is the shape " +
            "OpenIddict's pruning uses");
    }

    #endregion

    #region Helpers

    private DropInTestContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DropInTestContext>()
            .UseWitDb($"Data Source={m_testDbPath}")
            .Options;
        return new DropInTestContext(options);
    }

    private string GenerateSql(MigrationOperation operation)
    {
        var services = new ServiceCollection();
        services.AddDbContext<DropInTestContext>(options =>
            options.UseWitDb($"Data Source={m_testDbPath}"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DropInTestContext>();

        var generator = context.GetService<IMigrationsSqlGenerator>();
        var commands = generator.Generate([operation]);

        return string.Join("\n", commands.Select(c => c.CommandText));
    }

    /// <summary>
    /// Asserts that the operation produced an executable statement rather than nothing at all or a
    /// comment. Comment-only output is the failure mode these findings describe.
    /// </summary>
    /// <summary>
    /// An operation WitDatabase cannot carry out must say so. Emitting a comment instead let the
    /// migration be recorded as applied while nothing had happened, so the model and the database
    /// parted company with nothing to show for it. EF Core's SQLite provider, whose ALTER TABLE is
    /// just as limited, throws NotSupportedException for every one of these.
    /// </summary>
    private void AssertRefusedRatherThanCommentedOut(MigrationOperation operation, string subject)
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => GenerateSql(operation),
            $"{subject} cannot be applied, so the migration must stop rather than appear to succeed");

        Assert.That(exception!.Message, Does.Contain("WitDatabase"),
            "and the message must say who is refusing and why");
    }

    private void AssertEmitsRealSql(MigrationOperation operation, string expectedFragment)
    {
        var sql = GenerateSql(operation);

        var executable = string.Join("\n", sql
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("--") && !line.StartsWith("/*")));

        Assert.Multiple(() =>
        {
            Assert.That(executable, Is.Not.Empty,
                $"the operation must emit an executable statement, not nothing or a comment. " +
                $"Generated: <{sql}>");
            Assert.That(executable, Does.Contain(expectedFragment).IgnoreCase,
                $"Generated: <{sql}>");
        });
    }

    #endregion

    private sealed class DropInTestContext : DbContext
    {
        public DropInTestContext(DbContextOptions<DropInTestContext> options) : base(options) { }

        public DbSet<Row> Rows => Set<Row>();
        public DbSet<Parent> Parents => Set<Parent>();
        public DbSet<Child> Children => Set<Child>();

        public sealed class Row
        {
            public int Id { get; set; }
            public string? Value { get; set; }
        }

        public sealed class Parent
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        public sealed class Child
        {
            public int Id { get; set; }
            public int ParentId { get; set; }
            public string? Note { get; set; }
            public Parent? Parent { get; set; }
        }
    }
}
