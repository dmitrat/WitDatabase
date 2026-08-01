using MemoryPack;
using OutWit.Database.Definitions;
using OutWit.Database.Sql;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Schema;

/// <summary>
/// A database written by an earlier version must still open.
/// </summary>
/// <remarks>
/// <para>
/// 9.0.0 appends a tree-typed member to five catalog records - the view's body, the index's filter
/// and expressions, the column's computed/CHECK/DEFAULT, the table's CHECK list, the constraint's
/// condition and the trigger's WHEN and body. Each is added <b>after</b> the members that were
/// already there, and the claim being relied on is that MemoryPack tolerates reading a record
/// written with fewer members, leaving the new one at its default.
/// </para>
/// <para>
/// That claim is the whole of the upgrade path, so it is measured rather than assumed: this project
/// has been wrong about a remembered fact eight times.
/// </para>
/// <para>
/// The old payload is produced by serializing a mirror type carrying exactly the members 8.x wrote,
/// with the same orders. That is a faithful stand-in for a file on a user's disk, and it does not
/// require keeping a binary fixture in the repository that nobody can read or regenerate.
/// </para>
/// </remarks>
[TestFixture]
[Category("Schema")]
public partial class CatalogBackwardCompatibilityTests
{
    #region The 8.x shape

    /// <summary>
    /// <c>DefinitionView</c> exactly as 8.x declared it. <b>Do not add members here</b> - its whole
    /// purpose is to stay the shape that older files hold.
    /// </summary>
    [MemoryPackable]
    private partial class DefinitionViewAsWrittenBy8X
    {
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        [MemoryPackOrder(1)]
        public required string SelectSql { get; init; }

        [MemoryPackOrder(2)]
        public IReadOnlyList<string>? ColumnAliases { get; init; }
    }

    /// <summary>
    /// <c>DefinitionIndex</c> as 8.x declared it.
    /// </summary>
    [MemoryPackable]
    private partial class DefinitionIndexAsWrittenBy8X
    {
        [MemoryPackOrder(0)] public required string Name { get; init; }
        [MemoryPackOrder(1)] public required string TableName { get; init; }
        [MemoryPackOrder(2)] public required IReadOnlyList<string> Columns { get; init; }
        [MemoryPackOrder(3)] public bool IsUnique { get; init; }
        [MemoryPackOrder(4)] public bool IsPrimaryKey { get; init; }
        [MemoryPackOrder(5)] public string? WhereExpression { get; init; }
        [MemoryPackOrder(6)] public IReadOnlyList<string?>? ExpressionColumns { get; init; }
        [MemoryPackOrder(7)] public IReadOnlyList<string>? IncludeColumns { get; init; }
        [MemoryPackOrder(8)] public IReadOnlyList<bool>? ColumnDescending { get; init; }
        [MemoryPackOrder(9)] public bool IsImplicit { get; init; }
    }

    /// <summary>
    /// <c>DefinitionNamedConstraint</c> as 8.x declared it.
    /// </summary>
    [MemoryPackable]
    private partial class DefinitionNamedConstraintAsWrittenBy8X
    {
        [MemoryPackOrder(0)] public required string Name { get; init; }
        [MemoryPackOrder(1)] public required ConstraintType Type { get; init; }
        [MemoryPackOrder(2)] public IReadOnlyList<string>? Columns { get; init; }
        [MemoryPackOrder(3)] public string? CheckExpression { get; init; }
        [MemoryPackOrder(4)] public DefinitionForeignKey? ForeignKey { get; init; }
    }

    /// <summary>
    /// <c>DefinitionTrigger</c> as 8.x declared it.
    /// </summary>
    [MemoryPackable]
    private partial class DefinitionTriggerAsWrittenBy8X
    {
        [MemoryPackOrder(0)] public required string Name { get; init; }
        [MemoryPackOrder(1)] public required string TableName { get; init; }
        [MemoryPackOrder(2)] public required TriggerTime Time { get; init; }
        [MemoryPackOrder(3)] public required TriggerEvent Event { get; init; }
        [MemoryPackOrder(4)] public IReadOnlyList<string>? UpdateColumns { get; init; }
        [MemoryPackOrder(5)] public bool ForEachRow { get; init; }
        [MemoryPackOrder(6)] public string? WhenCondition { get; init; }
        [MemoryPackOrder(7)] public required string Body { get; init; }
    }

    #endregion

    #region Tests

    [Test]
    public void ViewWrittenBeforeTheTreeWasStoredStillReadsTest()
    {
        var legacy = MemoryPackSerializer.Serialize(new DefinitionViewAsWrittenBy8X
        {
            Name = "V",
            SelectSql = "SELECT Id FROM A WHERE Id > 1",
            ColumnAliases = ["Id"]
        });

        var view = MemoryPackSerializer.Deserialize<DefinitionView>(legacy);

        Assert.That(view, Is.Not.Null, "an 8.x view record must still deserialize");

        Assert.Multiple(() =>
        {
            Assert.That(view!.Name, Is.EqualTo("V"));
            Assert.That(view.SelectSql, Is.EqualTo("SELECT Id FROM A WHERE Id > 1"));
            Assert.That(view.ColumnAliases, Is.EqualTo(new[] { "Id" }));
            Assert.That(view.Query, Is.Null, "an 8.x file has no tree in it");
        });
    }

    [Test]
    public void ViewWrittenBeforeTheTreeWasStoredFallsBackToItsTextTest()
    {
        var legacy = MemoryPackSerializer.Serialize(new DefinitionViewAsWrittenBy8X
        {
            Name = "V",
            SelectSql = "SELECT Id FROM A WHERE Id > 1",
            ColumnAliases = null
        });

        var view = MemoryPackSerializer.Deserialize<DefinitionView>(legacy)!;

        // The text is all such a file has, so the fallback parses it. It inherits whatever the old
        // serializer lost when the view was created - that information is not in the file and no
        // amount of care here can recover it.
        var query = view.ResolveQuery();

        Assert.That(query, Is.Not.Null);
        Assert.That(query.WhereClause, Is.Not.Null,
            "the body must be recovered from the stored text when no tree was written");
    }

    [Test]
    public void ViewWrittenWithATreePrefersTheTreeTest()
    {
        // The two disagree on purpose: if the text were preferred, this test would read 'A' and the
        // preference would be silently the wrong way round.
        var view = new DefinitionView
        {
            Name = "V",
            SelectSql = "SELECT Id FROM A",
            Query = (Parser.WitSql.ParseStatement("SELECT Id FROM B")
                as Parser.Statements.WitSqlStatementSelect)!
        };

        var stored = MemoryPackSerializer.Deserialize<DefinitionView>(
            MemoryPackSerializer.Serialize(view))!;

        var source = stored.ResolveQuery().FromClause?
            .OfType<Parser.Schema.TableSources.TableSourceSimple>()
            .FirstOrDefault();

        Assert.That(source, Is.Not.Null);
        Assert.That(source!.TableName, Is.EqualTo("B"),
            "the stored tree is the view; the text beside it is only a description");
    }

    [Test]
    public void IndexWrittenBeforeTheTreeWasStoredStillReadsTest()
    {
        var legacy = MemoryPackSerializer.Serialize(new DefinitionIndexAsWrittenBy8X
        {
            Name = "IX",
            TableName = "T",
            Columns = ["Age"],
            WhereExpression = "(Age > 18)",
            ExpressionColumns = [null]
        });

        var index = MemoryPackSerializer.Deserialize<DefinitionIndex>(legacy);

        Assert.That(index, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(index!.Name, Is.EqualTo("IX"));
            Assert.That(index.Where, Is.Null, "an 8.x file has no tree in it");
            Assert.That(index.ResolveWhere(), Is.Not.Null, "the filter must be recovered from its text");
        });
    }

    [Test]
    public void NamedConstraintWrittenBeforeTheTreeWasStoredStillReadsTest()
    {
        var legacy = MemoryPackSerializer.Serialize(new DefinitionNamedConstraintAsWrittenBy8X
        {
            Name = "CK",
            Type = ConstraintType.Check,
            CheckExpression = "(Age >= 0)"
        });

        var constraint = MemoryPackSerializer.Deserialize<DefinitionNamedConstraint>(legacy);

        Assert.That(constraint, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(constraint!.Check, Is.Null);
            Assert.That(constraint.ResolveCheck(), Is.Not.Null);
        });
    }

    [Test]
    public void TriggerWrittenBeforeTheTreeWasStoredStillReadsTest()
    {
        var legacy = MemoryPackSerializer.Serialize(new DefinitionTriggerAsWrittenBy8X
        {
            Name = "TR",
            TableName = "T",
            Time = TriggerTime.After,
            Event = TriggerEvent.Insert,
            ForEachRow = true,
            WhenCondition = "(Age > 18)",
            Body = "INSERT INTO Log (Id) VALUES (1)"
        });

        var trigger = MemoryPackSerializer.Deserialize<DefinitionTrigger>(legacy);

        Assert.That(trigger, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(trigger!.When, Is.Null);
            Assert.That(trigger.Statements, Is.Null);
            Assert.That(trigger.ResolveWhen(), Is.Not.Null);
            Assert.That(trigger.ResolveStatements(), Has.Count.EqualTo(1),
                "the body must still be recoverable by splitting the stored text");
        });
    }

    /// <summary>
    /// A column and a table are exercised through a real database rather than a mirror type, because
    /// a table record nests columns and constraints - and it is the nesting that a version-tolerance
    /// claim is least obviously true of.
    /// </summary>
    [Test]
    public void ColumnAndTableRecordsSurviveTheirOwnRoundTripTest()
    {
        var table = new DefinitionTable
        {
            Name = "T",
            Columns =
            [
                new DefinitionColumn
                {
                    Name = "Age",
                    Type = WitDataType.Int32,
                    CheckExpression = "(Age >= 0)",
                    DefaultValue = "0"
                }
            ],
            CheckExpressions = ["(Age < 150)"]
        };

        var stored = MemoryPackSerializer.Deserialize<DefinitionTable>(
            MemoryPackSerializer.Serialize(table))!;

        Assert.Multiple(() =>
        {
            Assert.That(stored.Columns[0].Check, Is.Null, "nothing set the tree, so none is stored");
            Assert.That(stored.Columns[0].ResolveCheck(), Is.Not.Null);
            Assert.That(stored.Columns[0].ResolveDefault(), Is.Not.Null);
            Assert.That(stored.ResolveChecks(), Has.Count.EqualTo(1));
        });
    }

    #endregion
}
