namespace OutWit.Database.Studio.Models;

/// <summary>
/// One column of an index, and which way it is sorted.
/// </summary>
/// <param name="Expression">
/// A column name, or an expression - this engine takes <c>CREATE INDEX ... (LOWER(Name))</c>. The
/// catalogue then reports the column as <c>$expr0</c>, so an expression index can be created and
/// cannot be read back; see the phase document.
/// </param>
public sealed record IndexColumn(string Expression, bool IsDescending = false)
{
    public override string ToString() => IsDescending ? $"{Expression} DESC" : Expression;
}

/// <summary>
/// An index as the dialog holds it (WS-43).
///
/// Everything here was measured against the engine on 2026-08-06 before it was offered:
/// <list type="bullet">
/// <item>UNIQUE, DESC, several columns, a partial WHERE and INCLUDE are all accepted;</item>
/// <item>the planner USES a plain index and a covering one, and does NOT use a partial index or the
/// direction - so the dialog says what an option will and will not buy;</item>
/// <item><c>DROP INDEX x ON t</c> is a syntax error - the name alone is the whole statement;</item>
/// <item>there is no REINDEX and no ALTER INDEX, so "rebuild" is DROP and CREATE.</item>
/// </list>
/// </summary>
public sealed class IndexDraft
{
    public string Name { get; set; } = string.Empty;

    public string Table { get; set; } = string.Empty;

    public List<IndexColumn> Columns { get; set; } = [];

    public bool IsUnique { get; set; }

    /// <summary>
    /// The partial index's condition, without the WHERE.
    /// </summary>
    public string? FilterCondition { get; set; }

    /// <summary>
    /// Columns carried in the index without being part of its key.
    /// </summary>
    public List<string> IncludedColumns { get; set; } = [];

    public bool HasFilter => !string.IsNullOrWhiteSpace(FilterCondition);

    public bool HasIncluded => IncludedColumns.Count > 0;
}

/// <summary>
/// A trigger as the editor holds it (WS-45).
///
/// The shapes the grammar takes, measured 2026-08-06:
/// <list type="bullet">
/// <item>the WHEN condition must be PARENTHESISED - <c>WHEN NEW.Total &gt; 100</c> is a parse error
/// and <c>WHEN (NEW.Total &gt; 100)</c> is accepted, which is why the editor writes the brackets;</item>
/// <item><c>FOR EACH STATEMENT</c> is refused, and omitting <c>FOR EACH ROW</c> is how a statement
/// trigger is written - the catalogue then reports ACTION_ORIENTATION = STATEMENT;</item>
/// <item>the body takes only SELECT, INSERT, UPDATE, DELETE and MERGE - the engine says so itself;</item>
/// <item><c>SET NEW.column = ...</c> does not parse at all, so no template offers it.</item>
/// </list>
/// </summary>
public sealed class TriggerDraft
{
    public string Name { get; set; } = string.Empty;

    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// BEFORE, AFTER or INSTEAD OF.
    /// </summary>
    public string Timing { get; set; } = "AFTER";

    /// <summary>
    /// INSERT, UPDATE or DELETE.
    /// </summary>
    public string Event { get; set; } = "INSERT";

    /// <summary>
    /// FOR EACH ROW when true. False writes no FOR EACH clause at all, which is the only way this
    /// grammar expresses a statement trigger.
    /// </summary>
    public bool ForEachRow { get; set; } = true;

    /// <summary>
    /// The condition, without WHEN and without the brackets the writer adds.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// The statements between BEGIN and END, as the user typed them.
    /// </summary>
    public string Body { get; set; } = string.Empty;
}
