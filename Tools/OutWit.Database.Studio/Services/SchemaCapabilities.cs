using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One row of the matrix section 5.2 is built around: an edit, how it has to be carried out, and why.
/// </summary>
/// <remarks>
/// <b>Keys, not sentences.</b> This used to carry English prose as positional record arguments, which
/// is the one shape the localisation lint could not see at all - there is no destination name in front
/// of a positional argument for a rule to key on. Rule 4 exists because of this type; see
/// <c>LocalizationCoverageTests</c>.
/// </remarks>
/// <param name="ChangeKey">Catalogue key for what a person would call the edit.</param>
/// <param name="Category">In place, a rebuild, or a drop and a create.</param>
/// <param name="ReasonKey">Catalogue key for why it is in that category.</param>
public sealed record SchemaCapability(string ChangeKey, SchemaEditCategory Category, string ReasonKey);

/// <summary>
/// What this engine's DDL will and will not do, as data.
///
/// The point of holding it here rather than in a document is that
/// <c>SchemaMatrixTests</c> runs every row of it against a real database: an in-place row must
/// actually be accepted, a rebuild row must actually be refused or measurably wrong. A matrix that
/// nobody re-measures drifts away from the engine and starts promising things - which is the exact
/// failure section 5 exists to prevent.
///
/// Everything below was measured on 2026-08-06 against the shipping engine, and three of the plan's
/// assumptions did not survive:
/// <list type="bullet">
/// <item><b><c>ALTER COLUMN ... TYPE</c> DOES rewrite the rows</b> - the plan says it does not. It
/// used to replace a value it could not convert with a default, silently; the engine refuses such a
/// value now (see KnownIssues 6). It is still not offered in place, because that refusal stops at the
/// FIRST bad value and says nothing about the rest, while a rebuild counts them all first.</item>
/// <item><b>An index on a dropped column used to be left behind</b> (KnownIssues 8); the engine takes
/// it now, and the designer still drops it explicitly first so that the statement is one the user
/// reads in the DDL panel.</item>
/// <item><b>ADD COLUMN NOT NULL with no DEFAULT</b> used to be accepted on a table that has rows,
/// leaving NULLs in the column and closing the table for writing; the engine refuses it now
/// (KnownIssues 7). The designer still refuses it first, so the row says why while the user is
/// deciding rather than Apply coming back with an error.</item>
/// </list>
/// </summary>
public static class SchemaCapabilities
{
    #region Constants

    /// <summary>
    /// The suffix the rebuild's carrying table gets. Not "__new": the rebuild copies the rows OUT,
    /// rebuilds the table under its own name and copies them back, because renaming a table on this
    /// engine loses its key generator (see <see cref="TableRebuild"/>).
    /// </summary>
    public const string REBUILD_SUFFIX = "__old";

    #endregion

    #region Matrix

    /// <summary>
    /// The table of 5.2, in the order the design shows it.
    /// </summary>
    public static IReadOnlyList<SchemaCapability> Matrix { get; } =
    [
        new("Schema.Cap.AddColumn", SchemaEditCategory.InPlace, "Schema.Cap.AddColumn.Why"),
        new("Schema.Cap.DropColumn", SchemaEditCategory.InPlace, "Schema.Cap.DropColumn.Why"),
        new("Schema.Cap.Rename", SchemaEditCategory.InPlace, "Schema.Cap.Rename.Why"),
        new("Schema.Cap.DefaultOrNotNull", SchemaEditCategory.InPlace, "Schema.Cap.DefaultOrNotNull.Why"),
        new("Schema.Cap.Constraints", SchemaEditCategory.InPlace, "Schema.Cap.Constraints.Why"),
        new("Schema.Cap.ColumnType", SchemaEditCategory.Rebuild, "Schema.Cap.ColumnType.Why"),
        new("Schema.Cap.AddPrimaryKey", SchemaEditCategory.Rebuild, "Schema.Cap.AddPrimaryKey.Why"),
        new("Schema.Cap.ChangePrimaryKey", SchemaEditCategory.Rebuild, "Schema.Cap.ChangePrimaryKey.Why"),
        new("Schema.Cap.ColumnOrder", SchemaEditCategory.Rebuild, "Schema.Cap.ColumnOrder.Why"),
        new("Schema.Cap.ViewBody", SchemaEditCategory.DropCreate, "Schema.Cap.ViewBody.Why"),
        new("Schema.Cap.TriggerBody", SchemaEditCategory.DropCreate, "Schema.Cap.TriggerBody.Why")
    ];

    /// <summary>
    /// Things the design asks for that this engine has no syntax for at all. Shown in the designer as
    /// absent rather than as a button that fails - the same rule as WS-55 for the Database tab.
    /// </summary>
    public static IReadOnlyList<string> NotInTheEngine { get; } =
    [
        "Schema.Absent.Reindex",
        "Schema.Absent.EnableTrigger",
        "Schema.Absent.MoveColumn",
        "Schema.Absent.Sequence"
    ];

    #endregion

    #region Functions

    public static SchemaEditCategory CategoryOf(SchemaEditKind kind) => kind switch
    {
        SchemaEditKind.ChangeColumnType => SchemaEditCategory.Rebuild,
        SchemaEditKind.AddPrimaryKey => SchemaEditCategory.Rebuild,
        SchemaEditKind.DropPrimaryKey => SchemaEditCategory.Rebuild,
        SchemaEditKind.ReorderColumns => SchemaEditCategory.Rebuild,

        SchemaEditKind.ReplaceViewBody => SchemaEditCategory.DropCreate,
        SchemaEditKind.ReplaceTriggerBody => SchemaEditCategory.DropCreate,

        _ => SchemaEditCategory.InPlace
    };

    /// <summary>
    /// The catalogue key of the one-word marker that goes in the row (WS-39).
    /// </summary>
    /// <remarks>
    /// A key rather than the word, and the caller localises. The word used to be the marker itself and
    /// the designer read it BACK to work out the category - which would have started answering "in
    /// place" to a Russian marker the day this was translated. The category travels as the enum now.
    /// </remarks>
    public static string MarkerOf(SchemaEditCategory category) => category switch
    {
        SchemaEditCategory.InPlace => "Schema.Marker.InPlace",
        SchemaEditCategory.Rebuild => "Schema.Marker.Rebuild",
        _ => "Schema.Marker.DropCreate"
    };

    /// <summary>
    /// The catalogue key of why a change is in the category it is in - the sentence shown beside the
    /// marker, so the rule is never a bare icon.
    /// </summary>
    public static string ReasonOf(SchemaEditKind kind) => kind switch
    {
        SchemaEditKind.ChangeColumnType => "Schema.Reason.ChangeColumnType",
        SchemaEditKind.AddPrimaryKey => "Schema.Reason.AddPrimaryKey",
        SchemaEditKind.DropPrimaryKey => "Schema.Reason.DropPrimaryKey",
        SchemaEditKind.ReorderColumns => "Schema.Reason.ReorderColumns",
        SchemaEditKind.ReplaceViewBody => "Schema.Reason.ReplaceViewBody",
        SchemaEditKind.ReplaceTriggerBody => "Schema.Reason.ReplaceTriggerBody",

        _ => "Schema.Reason.InPlace"
    };

    #endregion
}
