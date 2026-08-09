using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The edits waiting to be applied to one table, the DDL they come to, and what applying them did.
///
/// Three things make this its own class rather than a list in the ViewModel:
///
/// 1. <b>The order is not the order they were typed in.</b> An index on a column has to be dropped
///    before the column - measured 2026-08-06: DROP COLUMN leaves the index in the catalogue, still
///    naming a column that no longer exists, and it survives a reopen.
///
/// 2. <b>Two of the edits are refused by Studio, not by the engine.</b> ADD COLUMN NOT NULL with no
///    DEFAULT is accepted by the engine on a table that already has rows, leaves NULL in every one of
///    them, and then refuses every later write to that table - including an UPDATE of an unrelated
///    column. There is no undo for it either: giving the column a default afterwards repairs new rows
///    and leaves the NULLs. So the designer will not write it.
///
/// 3. <b>Applying is not a transaction and must not pretend to be.</b> Measured: ADD COLUMN and
///    CREATE TABLE both survive a ROLLBACK. <see cref="DatabaseSession.ExecuteBatchAsync"/> is
///    all-or-nothing by way of a transaction, which for DDL would be a promise the engine does not
///    keep - so this runs statement by statement and reports (WS-42).
/// </summary>
public sealed class SchemaChangeSet
{
    #region Constructors

    public SchemaChangeSet(string table)
    {
        Table = table;
    }

    #endregion

    #region Building

    /// <summary>
    /// Works out what has to happen to turn the columns the catalogue reported into the drafts on
    /// screen. Returns the edits; the refusals come back in <paramref name="refusals"/> rather than
    /// as exceptions, because they belong on the screen next to the row.
    /// </summary>
    /// <param name="drafts">The rows of the designer, in their current state.</param>
    /// <param name="indexes">The table's indexes, so a dropped column takes its index with it.</param>
    /// <param name="hasRows">
    /// Whether the table has any rows. The NOT NULL refusal only applies when it does - on an empty
    /// table the same statement is harmless, and refusing it there would be a rule invented by Studio.
    /// </param>
    public static SchemaChangeSet Build(
        string table,
        IReadOnlyList<ColumnDraft> drafts,
        IReadOnlyList<IndexInfo> indexes,
        bool hasRows,
        out IReadOnlyList<string> refusals)
    {
        var set = new SchemaChangeSet(table);
        var refused = new List<string>();

        // 1. Renames first: everything below names columns, and a rename changes the name.
        foreach (var draft in drafts.Where(d => !d.IsNew && !d.IsDeleted && d.NameChanged))
        {
            set.Add(new SchemaEdit
            {
                Kind = SchemaEditKind.RenameColumn,
                Table = table,
                Column = draft.Original!.Name,
                Description = $"rename {draft.Original.Name} to {draft.Name}",
                Statements = [DdlWriter.RenameColumn(table, draft.Original.Name, draft.Name)]
            });
        }

        // 2. Drops, each taking its indexes with it.
        foreach (var draft in drafts.Where(d => d.IsDeleted && !d.IsNew))
        {
            var name = draft.Original!.Name;

            // A key column cannot be dropped - the engine refuses with "it is part of the primary
            // key". Studio knows that from the catalogue, so the row says "rebuild" while the user is
            // still deciding rather than after Apply has come back with a refusal (WS-39). The design's
            // 5.7 shows the refusal becoming an offer; knowing beforehand is the same offer, earlier.
            if (draft.IsPrimaryKey)
            {
                set.Add(new SchemaEdit
                {
                    Kind = SchemaEditKind.DropPrimaryKey,
                    Table = table,
                    Column = name,
                    Description = $"drop column {name}, which is part of the primary key",
                    Statements = []
                });

                continue;
            }

            var orphaned = indexes
                .Where(index => index.Columns.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
                .Select(index => index.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var statements = orphaned.Select(DdlWriter.DropIndex).Append(DdlWriter.DropColumn(table, name)).ToList();

            set.Add(new SchemaEdit
            {
                Kind = SchemaEditKind.DropColumn,
                Table = table,
                Column = name,
                Description = orphaned.Count == 0
                    ? $"drop column {name}"
                    : $"drop column {name} and {Plural(orphaned.Count, "index", "indexes")} on it",
                Statements = statements
            });
        }

        // 3. Additions.
        foreach (var draft in drafts.Where(d => d.IsNew && !d.IsDeleted))
        {
            if (!draft.IsNullable && string.IsNullOrWhiteSpace(draft.DefaultValue) && hasRows && !draft.IsComputed)
            {
                refused.Add(
                    $"{draft.Name}: a NOT NULL column added to a table that already has rows needs a DEFAULT. " +
                    "Without one the engine accepts the change, leaves NULL in every existing row and then " +
                    "refuses every write to the table.");

                continue;
            }

            set.Add(new SchemaEdit
            {
                Kind = SchemaEditKind.AddColumn,
                Table = table,
                Column = draft.Name,
                Description = $"add column {draft.Name} {draft.TypeText}",
                Statements = [DdlWriter.AddColumn(table, draft)]
            });
        }

        // 4. The two properties this engine will change on an existing column.
        foreach (var draft in drafts.Where(d => !d.IsNew && !d.IsDeleted))
        {
            var name = draft.Name;

            if (draft.DefaultChanged)
            {
                set.Add(string.IsNullOrWhiteSpace(draft.DefaultValue)
                    ? new SchemaEdit
                    {
                        Kind = SchemaEditKind.DropDefault,
                        Table = table,
                        Column = name,
                        Description = $"drop the default on {name}",
                        Statements = [DdlWriter.DropDefault(table, name)]
                    }
                    : new SchemaEdit
                    {
                        Kind = SchemaEditKind.SetDefault,
                        Table = table,
                        Column = name,
                        Description = $"set the default on {name} to {draft.DefaultValue}",
                        Statements = [DdlWriter.SetDefault(table, name, draft.DefaultValue!)]
                    });
            }

            if (draft.NullabilityChanged)
            {
                set.Add(draft.IsNullable
                    ? new SchemaEdit
                    {
                        Kind = SchemaEditKind.DropNotNull,
                        Table = table,
                        Column = name,
                        Description = $"allow NULL in {name}",
                        Statements = [DdlWriter.DropNotNull(table, name)]
                    }
                    : new SchemaEdit
                    {
                        Kind = SchemaEditKind.SetNotNull,
                        Table = table,
                        Column = name,
                        Description = $"require a value in {name}",
                        Statements = [DdlWriter.SetNotNull(table, name)]
                    });
            }

            // 5. The ones the engine will not do at all. They are recorded as edits so the row can
            // show the category and Apply can offer the rebuild (5.7) - they carry no statements.
            if (draft.TypeChanged)
            {
                set.Add(new SchemaEdit
                {
                    Kind = SchemaEditKind.ChangeColumnType,
                    Table = table,
                    Column = name,
                    Description = $"change {name} from {draft.OriginalTypeText} to {draft.TypeText}",
                    Statements = []
                });
            }

            if (draft.KeyChanged)
            {
                set.Add(new SchemaEdit
                {
                    Kind = draft.IsPrimaryKey ? SchemaEditKind.AddPrimaryKey : SchemaEditKind.DropPrimaryKey,
                    Table = table,
                    Column = name,
                    Description = draft.IsPrimaryKey
                        ? $"make {name} part of the primary key"
                        : $"take {name} out of the primary key",
                    Statements = []
                });
            }
        }

        refusals = refused;

        return set;
    }

    public void Add(SchemaEdit edit) => m_edits.Add(edit);

    public void Clear() => m_edits.Clear();

    #endregion

    #region Applying

    /// <summary>
    /// Runs the edits that carry statements, one statement at a time, stopping at the first refusal.
    ///
    /// No transaction, deliberately - see the class comment. The report says which statements are in
    /// the database and which never ran, and the caller shows it (WS-42).
    /// </summary>
    /// <remarks>
    /// <b>This ran <c>InPlaceStatements</c> until 2026-08-09, and that was a silent no-op for a whole
    /// category.</b> A trigger replacement is <c>DropCreate</c>, not <c>InPlace</c>, so its DROP and
    /// CREATE were left out, the report came back empty - and an empty report is COMPLETE - so the
    /// trigger editor said the trigger had been replaced, closed, and had changed nothing. Found while
    /// measuring whether a new case could fail; see
    /// <c>SchemaDialogTests.ReplacingATriggerActuallyReplacesItAsync</c>.
    /// </remarks>
    public async Task<DdlApplyReport> ApplyAsync(IDatabaseSession session, ILogger? logger = null,
        CancellationToken ct = default)
    {
        if (!HasSomethingToRun)
            return DdlApplyReport.Empty;

        var statements = ApplicableStatements;

        var outcomes = new List<DdlStatementOutcome>();
        var stopped = false;

        for (var i = 0; i < statements.Count; i++)
        {
            if (stopped)
            {
                outcomes.Add(new DdlStatementOutcome(i, statements[i], DdlOutcome.NotReached));
                continue;
            }

            try
            {
                await session.ExecuteNonQueryAsync(statements[i], ct);
                outcomes.Add(new DdlStatementOutcome(i, statements[i], DdlOutcome.Applied));
            }
            catch (Exception ex)
            {
                var message = ex.Message.Split('\n')[0].Trim();

                logger?.LogWarning(ex, "Schema change stopped at statement {Index} of {Count} on {Table}",
                    i + 1, statements.Count, Table);

                outcomes.Add(new DdlStatementOutcome(i, statements[i], DdlOutcome.Failed, message));
                stopped = true;
            }
        }

        return new DdlApplyReport { Outcomes = outcomes };
    }

    #endregion

    #region Properties

    public string Table { get; }

    public IReadOnlyList<SchemaEdit> Edits => m_edits;

    public bool IsEmpty => m_edits.Count == 0;

    public int Count => m_edits.Count;

    /// <summary>
    /// The edits that one ALTER TABLE each can carry out.
    /// </summary>
    public IReadOnlyList<SchemaEdit> InPlace =>
        m_edits.Where(e => e.Category == SchemaEditCategory.InPlace).ToList();

    /// <summary>
    /// The edits that need the table built again. Their presence is what turns Apply into the
    /// rebuild conversation of 5.3.
    /// </summary>
    public IReadOnlyList<SchemaEdit> NeedingRebuild =>
        m_edits.Where(e => e.Category == SchemaEditCategory.Rebuild).ToList();

    public bool NeedsRebuild => NeedingRebuild.Count > 0;

    public IReadOnlyList<string> InPlaceStatements =>
        InPlace.SelectMany(e => e.Statements).ToList();

    /// <summary>
    /// Every edit that can be RUN - which is everything except the ones needing a rebuilt table, and
    /// those carry no statements at all. The distinction that matters to <see cref="ApplyAsync"/> is
    /// "has statements", not "is one ALTER TABLE": a DROP and a CREATE is still a thing to execute.
    /// </summary>
    public IReadOnlyList<SchemaEdit> Applicable =>
        m_edits.Where(e => e.Category != SchemaEditCategory.Rebuild).ToList();

    public IReadOnlyList<string> ApplicableStatements =>
        Applicable.SelectMany(e => e.Statements).ToList();

    /// <summary>
    /// Whether there is anything here for <see cref="ApplyAsync"/> to run - the one question the Apply
    /// button and the executor both ask, so that they cannot disagree.
    /// </summary>
    /// <remarks>
    /// They did disagree, in the same direction and one layer apart. <c>ApplyAsync</c> ran
    /// <c>InPlaceStatements</c> and silently skipped a whole category, and the structure tab's gate
    /// asked for <c>InPlace.Count > 0</c> - so a change set made only of <c>DropCreate</c> edits would
    /// have left the button grey in front of statements that were ready to run. The executor was fixed
    /// on 2026-08-09; this is the gate's half, and putting the question in one place is what stops the
    /// next category from having to be remembered twice.
    /// </remarks>
    public bool HasSomethingToRun => ApplicableStatements.Count > 0;

    /// <summary>
    /// The whole set as text, which is what the DDL panel shows (WS-38). A rebuild appears as a
    /// comment naming itself: it is not a statement, and writing something that looks like one would
    /// be the exact promise section 5 refuses to make.
    /// </summary>
    public string Sql
    {
        get
        {
            var lines = new List<string>();

            foreach (var edit in m_edits)
            {
                if (edit.Category == SchemaEditCategory.Rebuild)
                {
                    lines.Add($"-- {edit.Description}: needs the table to be rebuilt (see Apply)");
                    continue;
                }

                lines.AddRange(edit.Statements);
            }

            return string.Join("\n", lines);
        }
    }

    #endregion

    #region Tools

    private static string Plural(int count, string one, string many) =>
        count == 1 ? $"1 {one}" : $"{count} {many}";

    #endregion

    #region Fields

    private readonly List<SchemaEdit> m_edits = [];

    #endregion
}
