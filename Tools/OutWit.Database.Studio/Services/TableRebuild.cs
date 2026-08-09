using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One step of a rebuild: what it is called, and the statements it runs.
/// </summary>
public sealed class RebuildStep
{
    public required string Title { get; init; }

    public required IReadOnlyList<string> Statements { get; init; }

    /// <summary>
    /// What is true of the database if the rebuild stops after this step - the sentence the
    /// interruption report shows (WS-41). Written per step rather than derived, because "your data is
    /// in two tables" and "your table does not exist and its rows are in the carrier" need different
    /// words and different next actions.
    /// </summary>
    public required string StateIfStoppedHere { get; init; }

    /// <summary>
    /// What to run to get back to where the rebuild started, from the state after this step.
    /// </summary>
    public IReadOnlyList<string> Recovery { get; init; } = [];

    public DdlOutcome Outcome { get; internal set; } = DdlOutcome.NotReached;

    public string? ErrorMessage { get; internal set; }
}

/// <summary>
/// A whole rebuild, worked out before anything runs so the user can read it (WS-41).
/// </summary>
public sealed class TableRebuildPlan
{
    public required string Table { get; init; }

    public required string Carrier { get; init; }

    public required IReadOnlyList<RebuildStep> Steps { get; init; }

    /// <summary>
    /// How many rows will be carried, or null when the count did not come back in time.
    /// </summary>
    public long? RowCount { get; init; }

    /// <summary>
    /// Objects that will be recreated by the rebuild itself: the table's own indexes and triggers.
    /// </summary>
    public IReadOnlyList<string> Recreated { get; init; } = [];

    /// <summary>
    /// Objects that point at this table and that the rebuild will NOT repair - the sentence the design
    /// calls "их придётся пересоздать вручную". Measured: dropping a table leaves a referencing foreign
    /// key and a view over it in the catalogue, and both then fail when used.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>
    /// One line per column whose conversion will lose something, with the number of rows.
    /// </summary>
    public IReadOnlyList<string> Casualties { get; init; } = [];

    /// <summary>
    /// What the rebuild cannot carry across because the catalogue does not publish it: an index's sort
    /// direction, and the columns of a covering index. Both are accepted by CREATE INDEX and neither
    /// comes back from INFORMATION_SCHEMA.INDEXES, so an index recreated here is the index the
    /// catalogue could describe - not necessarily the one that was there.
    /// </summary>
    public IReadOnlyList<string> Losses { get; init; } = [];

    public bool HasCasualties => Casualties.Count > 0;

    /// <summary>
    /// The whole thing as a script, for "Show script" and for anyone who would rather run it
    /// themselves.
    /// </summary>
    public string Script => string.Join("\n\n", Steps.Select(step =>
        $"-- {step.Title}\n{string.Join("\n", step.Statements)}"));
}

/// <summary>
/// What a rebuild left behind. Complete, or a precise account of where it stopped.
/// </summary>
public sealed class TableRebuildReport
{
    public required TableRebuildPlan Plan { get; init; }

    public required bool IsComplete { get; init; }

    public RebuildStep? StoppedAt { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The state of the database in words, and what to do about it.
    /// </summary>
    public string Summary => IsComplete
        ? $"{Plan.Table} was rebuilt."
        : $"The rebuild stopped at \"{StoppedAt?.Title}\". {StoppedAt?.StateIfStoppedHere}";

    public IReadOnlyList<string> Recovery => StoppedAt?.Recovery ?? [];
}

/// <summary>
/// Builds a table again with a new shape, and carries its rows across (5.3).
///
/// <b>The plan is not the design's plan, and the reason is a measurement.</b> Section 5.3 ends with
/// "rename Orders__new to Orders". On this engine that step loses data: measured 2026-08-06 on both
/// stores and across a close and reopen, <c>ALTER TABLE ... RENAME TO</c> leaves the renamed table's
/// key generator at zero, and the next generated INSERT lands on key 1 and <b>overwrites the row that
/// is there</b> - silently, reporting one row affected. The controls: a rename of a COLUMN does not do
/// it, an ADD COLUMN does not do it, an explicit duplicate key is refused correctly with a UNIQUE
/// violation, and a UNIQUE index on the key column turns the overwrite into a refusal. So the
/// generated-key path skips the check the explicit path makes, and the rename is what leaves it
/// pointing at an occupied key.
///
/// <b>Fixed in the engine since (KnownIssues 5): the rename carries the counter, and a generated key
/// that lands on an existing row is refused.</b> The rebuild still does not rename - that is now a
/// choice rather than a necessity, and it is kept because copying out leaves the carrier as something
/// to recover from when a step fails.
///
/// This rebuild therefore never renames a table. It copies the rows OUT to a carrier, drops the
/// original, creates it again under its own name and copies them back - measured to leave the
/// generator correct, including after a reopen. The cost is one more copy of the data and a window in
/// which the table does not exist, which is why the plan says so and asks for a backup.
/// </summary>
public sealed class TableRebuild
{
    #region Planning

    /// <summary>
    /// Works out the whole rebuild without running any of it.
    /// </summary>
    /// <param name="drafts">The shape the table is to have.</param>
    public static async Task<TableRebuildPlan> PlanAsync(
        IDatabaseSession session,
        string table,
        IReadOnlyList<ColumnDraft> drafts,
        CancellationToken ct = default)
    {
        var target = drafts.Where(d => !d.IsDeleted).ToList();

        // A computed column is never carried: naming one in the column list of an INSERT is refused,
        // and the rebuilt table computes it again anyway.
        var carried = target.Where(d => !d.IsComputed && !d.IsNew).ToList();

        var carrier = table + SchemaCapabilities.REBUILD_SUFFIX;

        var indexes = await session.GetTableIndexesAsync(table, ct);
        var triggers = await session.GetTableTriggersAsync(table, ct);
        var referencing = await session.GetReferencingKeysAsync(table, ct);
        var views = await session.GetViewsMentioningAsync(table, ct);
        var rowCount = await session.TryCountRowsAsync(table, TimeSpan.FromSeconds(2), ct);

        var casualties = await CountCasualtiesAsync(session, table, target, ct);

        // The carrier holds the CURRENT shape with no key generator and no constraints: it exists for
        // one INSERT ... SELECT and one read back, and a constraint on it could refuse rows that are
        // already in the database.
        var carrierColumns = carried
            .Select(d => new ColumnDraft
            {
                Name = d.Original!.Name,
                DataType = d.Original.DataType,
                MaxLength = d.Original.MaxLength,
                NumericPrecision = d.Original.NumericPrecision,
                NumericScale = d.Original.NumericScale,
                IsNullable = true
            })
            .ToList();

        var oldNames = carried.Select(d => DdlWriter.Identifier(d.Original!.Name)).ToList();
        var newNames = carried.Select(d => DdlWriter.Identifier(d.Name)).ToList();

        var readBack = carried.Select(d => d.TypeChanged
            ? $"CAST({DdlWriter.Identifier(d.Original!.Name)} AS {d.TypeText})"
            : DdlWriter.Identifier(d.Original!.Name));

        var steps = new List<RebuildStep>
        {
            new()
            {
                Title = $"Copy the rows out to {carrier}",
                Statements =
                [
                    DdlWriter.CreateTable(carrier, carrierColumns),
                    carried.Count == 0
                        ? $"-- {table} has no columns to carry"
                        : $"INSERT INTO {DdlWriter.Identifier(carrier)} ({string.Join(", ", oldNames)}) " +
                          $"SELECT {string.Join(", ", oldNames)} FROM {DdlWriter.Identifier(table)};"
                ],
                StateIfStoppedHere =
                    $"{table} is untouched. {carrier} may exist and holds a copy; it can be dropped.",
                Recovery = [DdlWriter.DropTable(carrier)]
            },
            new()
            {
                Title = $"Build {table} again with the new shape",
                Statements =
                [
                    DdlWriter.DropTable(table),
                    DdlWriter.CreateTable(table, target)
                ],
                StateIfStoppedHere =
                    $"{table} has been dropped and every row is in {carrier}. Nothing is lost, and " +
                    $"nothing will read {table} until it is created again.",
                Recovery =
                [
                    DdlWriter.CreateTable(table, target),
                    $"INSERT INTO {DdlWriter.Identifier(table)} ({string.Join(", ", newNames)}) " +
                    $"SELECT {string.Join(", ", oldNames)} FROM {DdlWriter.Identifier(carrier)};"
                ]
            },
            new()
            {
                Title = "Carry the rows back",
                Statements =
                [
                    carried.Count == 0
                        ? $"-- nothing to carry back into {table}"
                        : $"INSERT INTO {DdlWriter.Identifier(table)} ({string.Join(", ", newNames)}) " +
                          $"SELECT {string.Join(", ", readBack)} FROM {DdlWriter.Identifier(carrier)};"
                ],
                StateIfStoppedHere =
                    $"{table} exists with the new shape and may hold none or some of the rows; every " +
                    $"row is still in {carrier}.",
                Recovery =
                [
                    $"DELETE FROM {DdlWriter.Identifier(table)};",
                    $"INSERT INTO {DdlWriter.Identifier(table)} ({string.Join(", ", newNames)}) " +
                    $"SELECT {string.Join(", ", readBack)} FROM {DdlWriter.Identifier(carrier)};"
                ]
            },
            new()
            {
                Title = "Put back what was on the table, and drop the carrier",
                Statements =
                [
                    ..indexes.Select(index => DdlWriter.CreateIndex(FromCatalogue(index, table))),
                    ..triggers.Select(WriteTrigger),
                    DdlWriter.DropTable(carrier)
                ],
                StateIfStoppedHere =
                    $"{table} holds every row. Some of its indexes or triggers may not have been put " +
                    $"back, and {carrier} may still be there.",
                Recovery = [DdlWriter.DropTable(carrier)]
            }
        };

        var losses = new List<string>();

        if (indexes.Count > 0)
        {
            losses.Add(
                $"{indexes.Count} index(es) will be created again from what the catalogue publishes - " +
                "which does not include a column's sort direction or the columns of a covering index.");
        }

        return new TableRebuildPlan
        {
            Table = table,
            Carrier = carrier,
            Steps = steps,
            RowCount = rowCount,
            Recreated =
            [
                ..indexes.Select(i => $"index {i.Name}"),
                ..triggers.Select(t => $"trigger {t.Name}")
            ],
            Dependencies =
            [
                ..referencing.Select(fk => $"{fk.FromTable}.{fk.FromColumn} points at this table"),
                ..views.Select(v => $"view {v} reads this table")
            ],
            Casualties = casualties,
            Losses = losses
        };
    }

    private static async Task<List<string>> CountCasualtiesAsync(
        IDatabaseSession session, string table, IReadOnlyList<ColumnDraft> drafts, CancellationToken ct)
    {
        var casualties = new List<string>();

        foreach (var draft in drafts.Where(d => !d.IsNew && d.TypeChanged && !d.IsComputed))
        {
            var count = await session.CountValuesThatWillNotConvertAsync(
                table, draft.Original!.Name, draft.OriginalTypeText, draft.TypeText, ct);

            if (count == null)
            {
                casualties.Add(
                    $"{draft.Original.Name}: the engine will not compare {draft.OriginalTypeText} with " +
                    $"{draft.TypeText}, so how many values survive the conversion is not known here.");
            }
            else if (count > 0)
            {
                casualties.Add(
                    $"{draft.Original.Name}: {count} value(s) will not survive the conversion to " +
                    $"{draft.TypeText} and will be replaced.");
            }
        }

        return casualties;
    }

    /// <summary>
    /// An index as the catalogue can describe it. Everything the catalogue does not publish - the
    /// direction, the included columns - is lost here, which is why the plan says so out loud.
    /// </summary>
    private static IndexDraft FromCatalogue(IndexInfo index, string table) => new()
    {
        Name = index.Name,
        Table = table,
        Columns = index.Columns.Select(c => new IndexColumn(c)).ToList(),
        IsUnique = index.IsUnique,
        FilterCondition = index.FilterCondition
    };

    private static string WriteTrigger(TriggerInfo trigger) => DdlWriter.CreateTrigger(new TriggerDraft
    {
        Name = trigger.Name,
        Table = trigger.Table,
        Timing = trigger.Timing,
        Event = trigger.Event,
        UpdateColumns = trigger.UpdateColumns,
        ForEachRow = trigger.IsRowTrigger,
        Condition = trigger.Condition,
        Body = trigger.Body ?? string.Empty
    });

    #endregion

    #region Running

    /// <summary>
    /// Runs the plan, step by step, telling <paramref name="onStep"/> where it has got to. Stops at
    /// the first refusal and reports; nothing is undone, because nothing can be.
    /// </summary>
    public static async Task<TableRebuildReport> RunAsync(
        IDatabaseSession session,
        TableRebuildPlan plan,
        Action<RebuildStep>? onStep = null,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        foreach (var step in plan.Steps)
        {
            onStep?.Invoke(step);

            foreach (var statement in step.Statements)
            {
                if (statement.StartsWith("--", StringComparison.Ordinal))
                    continue;

                try
                {
                    await session.ExecuteNonQueryAsync(statement, ct);
                }
                catch (Exception ex)
                {
                    step.Outcome = DdlOutcome.Failed;
                    step.ErrorMessage = ex.Message.Split('\n')[0].Trim();

                    logger?.LogError(ex, "Rebuild of {Table} stopped at \"{Step}\"", plan.Table, step.Title);

                    return new TableRebuildReport
                    {
                        Plan = plan,
                        IsComplete = false,
                        StoppedAt = step,
                        ErrorMessage = step.ErrorMessage
                    };
                }
            }

            step.Outcome = DdlOutcome.Applied;
        }

        return new TableRebuildReport { Plan = plan, IsComplete = true };
    }

    #endregion
}
