using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One row of the matrix section 5.2 is built around: an edit, how it has to be carried out, and why.
/// </summary>
/// <param name="Change">What a person would call it.</param>
/// <param name="Category">In place, a rebuild, or a drop and a create.</param>
/// <param name="Reason">Why - the sentence the designer shows next to the marker.</param>
public sealed record SchemaCapability(string Change, SchemaEditCategory Category, string Reason);

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
/// <item><b><c>ALTER COLUMN ... TYPE</c> DOES rewrite the rows</b> - the plan says it does not. It is
/// still not offered in place, for a worse reason: a value that cannot convert is silently replaced
/// (a word becomes 0, an integer becomes 01/01/0001) and changing the type back does not bring it
/// back.</item>
/// <item><b>An index on a dropped column is left behind</b>, so a drop has to take it explicitly.</item>
/// <item><b>ADD COLUMN NOT NULL with no DEFAULT is accepted on a table that has rows</b>, leaves NULLs
/// in the column and then refuses every later write to the table - including an UPDATE of an
/// unrelated column. The designer refuses that combination itself.</item>
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
        new("Add a column", SchemaEditCategory.InPlace,
            "ADD COLUMN, including UNIQUE, CHECK, REFERENCES, DEFAULT and computed columns"),

        new("Drop a column", SchemaEditCategory.InPlace,
            "DROP COLUMN. The foreign key on it goes with it - an index on it does NOT, so Studio drops that first"),

        new("Rename a column or a table", SchemaEditCategory.InPlace,
            "RENAME"),

        new("Change DEFAULT or NOT NULL", SchemaEditCategory.InPlace,
            "ALTER COLUMN - the only two properties of a column this engine changes"),

        new("Add or drop UNIQUE, CHECK, FOREIGN KEY", SchemaEditCategory.InPlace,
            "ADD CONSTRAINT / DROP CONSTRAINT, and the constraint must be named"),

        new("Change a column's type", SchemaEditCategory.Rebuild,
            "ALTER COLUMN ... TYPE is accepted and it rewrites the rows, but a value that cannot " +
            "convert is silently replaced - 'not a number' becomes 0 - and the old value is gone. " +
            "A rebuild converts with a CAST the user can see, after counting what will not survive"),

        new("Add a primary key", SchemaEditCategory.Rebuild,
            "The engine refuses: 'Adding PRIMARY KEY constraint to existing table is not supported'"),

        new("Remove or change a primary key", SchemaEditCategory.Rebuild,
            "Dropping a key column is refused too: 'it is part of the primary key'"),

        new("Change the order of the columns", SchemaEditCategory.Rebuild,
            "There is no syntax for it: FIRST, AFTER, MODIFY and CHANGE are all parse errors"),

        new("Change a view's body", SchemaEditCategory.DropCreate,
            "There is no ALTER VIEW and no CREATE OR REPLACE"),

        new("Change a trigger's body", SchemaEditCategory.DropCreate,
            "There is no ALTER TRIGGER - and none to enable or disable one either")
    ];

    /// <summary>
    /// Things the design asks for that this engine has no syntax for at all. Shown in the designer as
    /// absent rather than as a button that fails - the same rule as WS-55 for the Database tab.
    /// </summary>
    public static IReadOnlyList<string> NotInTheEngine { get; } =
    [
        "Rebuilding an index in place - there is no REINDEX and no ALTER INDEX; Studio drops and creates it",
        "Enabling or disabling a trigger - there is no ALTER TRIGGER",
        "Moving a column - there is no FIRST, AFTER, MODIFY or CHANGE",
        "A sequence's step, minimum, maximum or cycle - CREATE SEQUENCE takes a name and START WITH, nothing else"
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
    /// The one-word marker that goes in the row (WS-39).
    /// </summary>
    public static string MarkerOf(SchemaEditCategory category) => category switch
    {
        SchemaEditCategory.InPlace => "in place",
        SchemaEditCategory.Rebuild => "rebuild",
        _ => "drop + create"
    };

    /// <summary>
    /// Why a change is in the category it is in - the sentence shown beside the marker, so the rule is
    /// never a bare icon.
    /// </summary>
    public static string ReasonOf(SchemaEditKind kind) => kind switch
    {
        SchemaEditKind.ChangeColumnType =>
            "A type change rewrites every row, and a value that will not convert is replaced without a word. " +
            "Studio rebuilds the table instead, so the conversion is a CAST you can see and the casualties are counted first.",

        SchemaEditKind.AddPrimaryKey =>
            "The engine refuses to add a primary key to a table that already exists.",

        SchemaEditKind.DropPrimaryKey =>
            "The engine refuses to drop a column that is part of the primary key.",

        SchemaEditKind.ReorderColumns =>
            "The language has no way to move a column.",

        SchemaEditKind.ReplaceViewBody =>
            "There is no ALTER VIEW in this language, so the view is dropped and created again.",

        SchemaEditKind.ReplaceTriggerBody =>
            "There is no ALTER TRIGGER in this language, so the trigger is dropped and created again.",

        _ => "One ALTER TABLE. The rows are not touched."
    };

    #endregion
}
