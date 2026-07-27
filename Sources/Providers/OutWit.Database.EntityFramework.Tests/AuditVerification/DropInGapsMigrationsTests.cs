using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using OutWit.Database.EntityFramework.Extensions;

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

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and worse than written: a column-type change emits NOTHING AT ALL - not "
            + "even the explanatory comment the sibling operations produce. The migration is recorded "
            + "as applied and the column keeps its old type. "
            + "dropin-gaps, EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:182")]
    public void AlterColumnEmitsAStatementForATypeChangeTest()
    {
        // Finding: WitMigrationsSqlGenerator.cs:182 - a column-type change produces nothing, so the
        // model says the column is one type and the database keeps another, silently.
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

        AssertEmitsRealSql(operation, "ALTER");
    }

    #endregion

    #region AddPrimaryKey / DropPrimaryKey / RenameIndex

    [Test]
    [Ignore("CONFIRMED 2026-07-27: emits only "
            + "\"-- WitDatabase limitation: Cannot add PRIMARY KEY to existing table. Columns: Id\". "
            + "dropin-gaps, EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:320")]
    public void AddPrimaryKeyEmitsAStatementTest()
    {
        // Finding: WitMigrationsSqlGenerator.cs:320 - emitted as a SQL comment.
        AssertEmitsRealSql(
            new AddPrimaryKeyOperation { Name = "PK_T", Table = "T", Columns = ["Id"] },
            "PRIMARY KEY");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: emits only "
            + "\"-- WitDatabase limitation: Cannot drop PRIMARY KEY from existing table. Table: T\".")]
    public void DropPrimaryKeyEmitsAStatementTest()
    {
        AssertEmitsRealSql(
            new DropPrimaryKeyOperation { Name = "PK_T", Table = "T" },
            "PRIMARY KEY");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: emits only \"-- Rename index: IX_Old -> IX_New\".")]
    public void RenameIndexEmitsAStatementTest()
    {
        AssertEmitsRealSql(
            new RenameIndexOperation { Name = "IX_Old", NewName = "IX_New", Table = "T" },
            "INDEX");
    }

    #endregion

    #region Index options silently dropped

    [Test]
    [Ignore("CONFIRMED 2026-07-27, and the consequence is stronger than \"silently dropped\". A filtered "
            + "UNIQUE index became CREATE UNIQUE INDEX ... ON \"T\" (\"Value\") with no WHERE, which "
            + "enforces a STRICTER constraint than the model declares - rows the application is "
            + "entitled to insert are rejected. "
            + "dropin-gaps, EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:239")]
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
    [Ignore("CONFIRMED 2026-07-27: emitted as CREATE INDEX ... (\"Value\") with the DESC direction dropped.")]
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

    [Test]
    [Ignore("CONFIRMED 2026-07-27: throws NotSupportedException - EnsureSchemaOperation is not handled at "
            + "all. dropin-gaps, EntityFramework/Metadata/WitModelValidator.cs:56")]
    public void EnsureSchemaEmitsAStatementTest()
    {
        // Finding: WitModelValidator.cs:56 - schemas are unsupported at every layer, yet `public`
        // is the one schema name the validator accepts.
        AssertEmitsRealSql(new EnsureSchemaOperation { Name = "public" }, "SCHEMA");
    }

    [Test]
    [Ignore("CONFIRMED 2026-07-27: CREATE TABLE emits \"T\" with the schema dropped entirely, while EF's "
            + "query and update generators keep it - so the one schema name WitModelValidator accepts "
            + "(\"public\") produces DDL that does not match the DML.")]
    public void SchemaQualifiedTableIsAddressableTest()
    {
        var sql = GenerateSql(new CreateTableOperation
        {
            Name = "T",
            Schema = "public",
            Columns =
            {
                new AddColumnOperation { Name = "Id", Table = "T", Schema = "public", ClrType = typeof(int) }
            }
        });

        Assert.That(sql, Does.Contain("public"),
            $"the schema the validator accepts must reach the emitted SQL. Generated: {sql}");
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
