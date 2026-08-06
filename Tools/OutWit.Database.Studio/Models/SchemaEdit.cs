namespace OutWit.Database.Studio.Models;

/// <summary>
/// How an edit has to be carried out - the whole point of section 5.2, and the thing the designer
/// shows in the row before the user presses Apply (WS-39).
/// </summary>
public enum SchemaEditCategory
{
    /// <summary>
    /// One ALTER TABLE, and the rows are not touched.
    /// </summary>
    InPlace,

    /// <summary>
    /// The table has to be built again with the new shape and its rows carried across (5.3).
    /// </summary>
    Rebuild,

    /// <summary>
    /// The object has no ALTER at all, so it is dropped and created again.
    /// </summary>
    DropCreate
}

/// <summary>
/// Every edit the designer can make. The kind decides the category, and the category decides what the
/// row shows and what Apply does.
/// </summary>
public enum SchemaEditKind
{
    AddColumn,
    DropColumn,
    RenameColumn,
    RenameTable,
    SetDefault,
    DropDefault,
    SetNotNull,
    DropNotNull,
    AddUnique,
    AddCheck,
    AddForeignKey,
    DropConstraint,

    ChangeColumnType,
    AddPrimaryKey,
    DropPrimaryKey,
    ReorderColumns,

    ReplaceViewBody,
    ReplaceTriggerBody
}

/// <summary>
/// One pending edit: what it is, what it will run, and which of the three categories it falls into.
///
/// The statements are worked out when the edit is made rather than when it is applied, because the DDL
/// panel shows them the whole time (WS-38) and an edit whose text appears only at the moment of
/// execution cannot be read before it happens.
/// </summary>
public sealed class SchemaEdit
{
    public required SchemaEditKind Kind { get; init; }

    public required string Table { get; init; }

    /// <summary>
    /// The column this edit is about, when it is about one. Used to put the category marker on the
    /// right row of the grid.
    /// </summary>
    public string? Column { get; init; }

    /// <summary>
    /// One line a person can read: "add column ShippedAt", "change Total to DECIMAL(20,4)".
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// What this edit runs, in order. An in-place edit is one statement; a DROP+CREATE is two.
    /// A rebuild is empty here - it is not a statement, it is <see cref="Services.TableRebuild"/>.
    /// </summary>
    public required IReadOnlyList<string> Statements { get; init; }

    public SchemaEditCategory Category => Services.SchemaCapabilities.CategoryOf(Kind);

    public override string ToString() => Description;
}

/// <summary>
/// What happened to one statement of a set. Applied is not the same as "no error": a statement that
/// never ran because an earlier one stopped the set is neither applied nor failed.
/// </summary>
public sealed record DdlStatementOutcome(int Index, string Sql, DdlOutcome Outcome, string? ErrorMessage = null)
{
    public bool IsApplied => Outcome == DdlOutcome.Applied;
}

public enum DdlOutcome
{
    Applied,
    Failed,

    /// <summary>
    /// The set stopped before this statement was reached. Named rather than left out, because "what
    /// did NOT happen" is half of the answer after an interrupted sequence (WS-42).
    /// </summary>
    NotReached
}

/// <summary>
/// The report a set of DDL leaves behind.
///
/// It exists because this engine does not roll DDL back. Measured 2026-08-06: ADD COLUMN and CREATE
/// TABLE inside a transaction both survive ROLLBACK. So a set of schema edits is not all-or-nothing
/// and cannot be made so - the only honest thing to do is run it a statement at a time, stop at the
/// first refusal and say exactly which statements are now in the database (WS-42).
/// </summary>
public sealed class DdlApplyReport
{
    public required IReadOnlyList<DdlStatementOutcome> Outcomes { get; init; }

    public int AppliedCount => Outcomes.Count(o => o.IsApplied);

    public int Total => Outcomes.Count;

    public DdlStatementOutcome? Failure => Outcomes.FirstOrDefault(o => o.Outcome == DdlOutcome.Failed);

    public bool IsComplete => Failure == null && Outcomes.All(o => o.IsApplied);

    /// <summary>
    /// True when something was applied AND something was not - the state a user has to be told about
    /// in words, because no rollback took it back.
    /// </summary>
    public bool IsPartial => !IsComplete && AppliedCount > 0;

    public string? ErrorMessage => Failure?.ErrorMessage;

    public string Summary => IsComplete
        ? Total == 1 ? "1 change applied" : $"{Total} changes applied"
        : $"Applied {AppliedCount} of {Total}";

    public static DdlApplyReport Empty { get; } = new() { Outcomes = [] };
}
