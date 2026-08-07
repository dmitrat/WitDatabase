using System.Data;
using System.Text;
using OutWit.Database.Studio.Converters;

namespace OutWit.Database.Studio.Services;

/// <summary>What goes into the dump (6.5).</summary>
/// <param name="Schema">The objects.</param>
/// <param name="Data">The rows.</param>
/// <param name="ObjectsBesidesTables">Views, indexes and triggers.</param>
/// <param name="Sequences">Sequences, with the value they are at.</param>
public sealed record DumpOptions(
    bool Schema = true,
    bool Data = true,
    bool ObjectsBesidesTables = true,
    bool Sequences = false);

/// <summary>
/// The whole database as a WitSQL script (WS-51).
///
/// <para>
/// <b>A dump is not a copy, and the two live in different places for that reason.</b> This is text
/// that has to be executed again; a byte copy keeps the encryption, the pages and the statistics.
/// Offering them in one window would be asking someone to choose between a backup and a transfer
/// without explaining the difference.
/// </para>
/// <para>
/// The order follows the dependencies: a table that references another comes after it, so the script
/// can be run top to bottom into an empty database. That is measured by running it rather than by
/// reading it.
/// </para>
/// </summary>
public static class DatabaseDump
{
    #region Functions

    /// <summary>
    /// Sorts tables so that a table comes after everything it references.
    ///
    /// <para>
    /// <b>A cycle does not hang and does not lose a table.</b> Two tables that reference each other are
    /// legal here and cannot be ordered, so the remaining ones are emitted in their original order and
    /// the caller is told - a dump that silently dropped a table would be a backup with a hole in it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> InDependencyOrder(
        IReadOnlyList<string> tables,
        IReadOnlyDictionary<string, IReadOnlyList<string>> references,
        out IReadOnlyList<string> unordered)
    {
        var remaining = new List<string>(tables);
        var ordered = new List<string>();
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(table => !references.TryGetValue(table, out var needs)
                    || needs.All(need => placed.Contains(need)
                        || !tables.Contains(need, StringComparer.OrdinalIgnoreCase)
                        || string.Equals(need, table, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (ready.Count == 0)
                break;

            foreach (var table in ready)
            {
                ordered.Add(table);
                placed.Add(table);
                remaining.Remove(table);
            }
        }

        // Whatever is left is in a cycle. It is emitted anyway, in the order it came, and named.
        unordered = remaining;
        ordered.AddRange(remaining);

        return ordered;
    }

    /// <summary>
    /// Writes the script. Never throws for an object it cannot render: it writes a comment naming what
    /// was left out, because a dump that quietly omits a view is a dump nobody can trust.
    /// </summary>
    public static async Task<string> WriteAsync(IDatabaseSession session, DumpOptions options,
        CancellationToken ct = default)
    {
        var script = new StringBuilder();

        var tables = (await session.GetTablesAsync(ct)).Select(table => table.Name).ToList();

        var references = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var keys = await session.GetForeignKeysAsync(table, ct);

            references[table] = keys.Select(key => key.ToTable).Distinct().ToList();
        }

        var ordered = InDependencyOrder(tables, references, out var cycle);

        script.AppendLine("-- WitDatabase dump. This is a WitSQL script: run it to rebuild.");
        script.AppendLine("-- A byte copy of the file is a different thing and lives in the Database tab.");

        if (cycle.Count > 0)
        {
            script.AppendLine("-- These tables reference each other, so no order satisfies them all: "
                + string.Join(", ", cycle));
            script.AppendLine("-- They are written in catalogue order; the foreign keys may need a second pass.");
        }

        script.AppendLine();

        if (options.Schema)
        {
            foreach (var table in ordered)
            {
                ct.ThrowIfCancellationRequested();

                var definition = await session.GetTableDefinitionAsync(table, ct);

                if (string.IsNullOrEmpty(definition))
                {
                    script.AppendLine($"-- SKIPPED: the catalogue cannot describe table {table}.");
                    continue;
                }

                script.AppendLine(definition.TrimEnd().TrimEnd(';') + ";");
                script.AppendLine();
            }
        }

        if (options.Data)
        {
            foreach (var table in ordered)
            {
                ct.ThrowIfCancellationRequested();

                await WriteRowsAsync(session, table, script, ct);
            }
        }

        if (options.Schema && options.ObjectsBesidesTables)
        {
            await WriteDefinitionsAsync(session, script, ct);
        }

        if (options.Schema && options.Sequences)
        {
            foreach (var sequence in await session.GetSequencesAsync(ct))
                script.AppendLine($"-- SEQUENCE {sequence}: the catalogue publishes its value, and the "
                    + "language has no way to set one. Recreate it and advance it by hand.");
        }

        return script.ToString();
    }

    private static async Task WriteRowsAsync(IDatabaseSession session, string table, StringBuilder script,
        CancellationToken ct)
    {
        var result = await session.ExecuteQueryAsync($"SELECT * FROM [{table}]", ct);

        if (result.Data == null || result.Data.Rows.Count == 0)
            return;

        var columns = string.Join(", ",
            result.Data.Columns.Cast<DataColumn>().Select(column => DdlWriter.Identifier(column.ColumnName)));

        foreach (DataRow row in result.Data.Rows)
        {
            ct.ThrowIfCancellationRequested();

            var values = string.Join(", ", row.ItemArray.Select(SqlValueFormatter.FormatForSql));

            script.AppendLine($"INSERT INTO {DdlWriter.Identifier(table)} ({columns}) VALUES ({values});");
        }

        script.AppendLine();
    }

    /// <summary>
    /// Views, indexes and triggers. <b>A view whose body the catalogue cannot render is NAMED, not
    /// skipped</b> - the phase-8 rule working as designed: a UNION or a subquery comes back with a null
    /// definition rather than a rendering that lost something, and a dump that dropped it in silence
    /// would be a dump that quietly loses an object.
    /// </summary>
    private static async Task WriteDefinitionsAsync(IDatabaseSession session, StringBuilder script,
        CancellationToken ct)
    {
        foreach (var view in await session.GetViewsAsync(ct))
        {
            var definition = await session.GetViewDefinitionAsync(view, ct);

            script.AppendLine(string.IsNullOrEmpty(definition)
                ? $"-- SKIPPED: the catalogue cannot render view {view}. Its body has to be written by hand."
                : definition.TrimEnd().TrimEnd(';') + ";");
        }

        foreach (var index in await session.GetIndexesAsync(ct))
        {
            var definition = await session.GetIndexDefinitionAsync(index, ct);

            script.AppendLine(string.IsNullOrEmpty(definition)
                ? $"-- SKIPPED: the catalogue cannot describe index {index}."
                : definition.TrimEnd().TrimEnd(';') + ";");
        }

        foreach (var trigger in await session.GetTriggersAsync(ct))
        {
            var definition = await session.GetTriggerDefinitionAsync(trigger, ct);

            script.AppendLine(string.IsNullOrEmpty(definition)
                ? $"-- SKIPPED: the catalogue cannot render trigger {trigger}."
                : definition.TrimEnd().TrimEnd(';') + ";");
        }
    }

    #endregion
}
