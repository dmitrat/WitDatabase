using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Reads everything in the database and reports what came back (WS-61).
/// </summary>
/// <remarks>
/// <para>
/// <b>It is called verification by READING, and the name is precision rather than modesty.</b> This
/// engine has no <c>PRAGMA integrity_check</c>; what Studio can do with the surface it has is walk
/// every table and every index with queries and say what answered. That finds unreadable pages, a
/// failed decryption, a damaged SSTable, a catalogue the engine cannot parse and a row count that
/// disagrees with the rows - and it does NOT find an inconsistent B-tree, a lost page or a broken free
/// list. A real integrity check reads the structure; this reads the data through it, and a full
/// <c>SELECT</c> coming back green says nothing about the parts of the file nobody is using.
/// </para>
/// <para>
/// <b>An index that the planner did not use is not a checked index.</b> The query is built so that the
/// index is the obvious way to answer it, and the plan is then read to see whether it actually was;
/// when it was not, the row says INCONCLUSIVE rather than ok. A green tick for a structure nobody
/// touched is worse than no tick - and it is easy to earn here, because this planner refuses to
/// consider an index below ten rows.
/// </para>
/// </remarks>
public static class ReadChecker
{
    #region Functions

    /// <summary>
    /// Walks the whole database. Reports progress per object, and stops when asked.
    /// </summary>
    public static async Task<ReadCheckReport> RunAsync(
        IDatabaseSession session,
        IProgress<ReadCheckItem>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var items = new List<ReadCheckItem>();
        var cancelled = false;

        try
        {
            var tables = await session.GetTablesAsync(ct);

            // The catalogue is read FIRST and reported as its own line: if it cannot be read there is
            // nothing to walk, and "no tables" and "the catalogue is damaged" must not look alike.
            Record(items, progress, new ReadCheckItem(ReadCheckSubject.Catalog, string.Empty,
                ReadCheckOutcome.Ok, tables.Count));

            foreach (var table in tables)
            {
                ct.ThrowIfCancellationRequested();

                Record(items, progress, await ReadTableAsync(session, table.Name, ct));

                foreach (var index in await IndexesOfAsync(session, table.Name, ct))
                {
                    ct.ThrowIfCancellationRequested();

                    Record(items, progress, await ReadIndexAsync(session, index, ct));
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            // The catalogue itself would not answer. One line, and the walk is over.
            Record(items, progress, new ReadCheckItem(ReadCheckSubject.Catalog, string.Empty,
                ReadCheckOutcome.Failed, 0, EngineMessage: ex.Message));
        }

        return new ReadCheckReport(items, cancelled);
    }

    #endregion

    #region Tools

    private static async Task<ReadCheckItem> ReadTableAsync(IDatabaseSession session, string name,
        CancellationToken ct)
    {
        try
        {
            var rows = await session.ScanAsync($"SELECT * FROM [{name}]", ct);

            // Asked AFTER the scan and kept beside it rather than compared here: the disagreement is
            // the finding, and the report is what says so.
            var counter = await CounterAsync(session, name, ct);

            return new ReadCheckItem(ReadCheckSubject.Table, name, ReadCheckOutcome.Ok, rows, counter);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReadCheckItem(ReadCheckSubject.Table, name, ReadCheckOutcome.Failed, 0,
                EngineMessage: ex.Message);
        }
    }

    private static async Task<long?> CounterAsync(IDatabaseSession session, string name,
        CancellationToken ct)
    {
        try
        {
            var value = await session.ExecuteScalarAsync($"SELECT COUNT(*) FROM [{name}]", ct);

            return value == null ? null : Convert.ToInt64(value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A counter that will not answer is not a reason to call the table unreadable - the rows
            // came back. It simply leaves nothing to compare.
            return null;
        }
    }

    private static async Task<IReadOnlyList<IndexInfo>> IndexesOfAsync(IDatabaseSession session,
        string table, CancellationToken ct)
    {
        try
        {
            return await session.GetTableIndexesAsync(table, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<ReadCheckItem> ReadIndexAsync(IDatabaseSession session, IndexInfo index,
        CancellationToken ct)
    {
        var column = index.Columns.FirstOrDefault();

        if (string.IsNullOrEmpty(column))
        {
            return new ReadCheckItem(ReadCheckSubject.Index, index.Name, ReadCheckOutcome.Inconclusive,
                0, NoteKey: "ReadCheck.Note.NoColumns");
        }

        try
        {
            // A value out of the table itself, because an index is only reachable through a predicate
            // that matches one. Measured 2026-08-08: this planner answers ORDER BY with a SORT over a
            // full scan and a range with a FILTER over one, so an EQUALITY is the only shape that gets
            // into an index at all - which is why the report says "a seek" rather than "a traversal".
            var value = await session.ExecuteScalarAsync(
                $"SELECT [{column}] FROM [{index.TableName}] WHERE [{column}] IS NOT NULL LIMIT 1", ct);

            if (value == null || value == DBNull.Value)
            {
                return new ReadCheckItem(ReadCheckSubject.Index, index.Name,
                    ReadCheckOutcome.Inconclusive, 0, NoteKey: "ReadCheck.Note.NothingToSeekWith");
            }

            var statement = new SqlStatement(
                $"SELECT [{column}] FROM [{index.TableName}] WHERE [{column}] = @value",
                [new SqlParameter("@value", value)]);

            // EXPLAIN takes the written-out form, which is what the panel would show a person; the
            // READING is done with the parameter, because a value is never concatenated into SQL that
            // runs (B4).
            var used = await PlanUsesIndexAsync(session, $"EXPLAIN {statement.ToDisplaySql()}",
                index.Name, ct);

            var rows = await session.ScanAsync(statement, ct);

            return new ReadCheckItem(ReadCheckSubject.Index, index.Name,
                used ? ReadCheckOutcome.Ok : ReadCheckOutcome.Inconclusive, rows,
                NoteKey: used ? "ReadCheck.Note.SeekNotTraversal" : "ReadCheck.Note.PlannerDidNotUseIt");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ReadCheckItem(ReadCheckSubject.Index, index.Name, ReadCheckOutcome.Failed, 0,
                EngineMessage: ex.Message);
        }
    }

    /// <summary>
    /// Whether the plan for this query names the index.
    /// </summary>
    /// <remarks>
    /// <c>EXPLAIN</c> answers id, parent and detail and no numbers at all on this engine, so the name
    /// appearing in a detail is the whole of what can be asked. It is enough for the question here,
    /// which is "was the index touched" rather than "how much did it cost".
    /// </remarks>
    private static async Task<bool> PlanUsesIndexAsync(IDatabaseSession session, string sql, string index,
        CancellationToken ct)
    {
        try
        {
            var plan = await session.ExecuteQueryAsync(sql, ct);

            if (plan.Data == null)
                return false;

            foreach (System.Data.DataRow row in plan.Data.Rows)
            {
                foreach (var value in row.ItemArray)
                {
                    if (value?.ToString() is { } text
                        && text.Contains(index, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static void Record(List<ReadCheckItem> items, IProgress<ReadCheckItem>? progress,
        ReadCheckItem item)
    {
        items.Add(item);
        progress?.Report(item);
    }

    #endregion
}
