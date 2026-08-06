using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// What the schema designer has to ask the database that nothing else did (stage 8).
///
/// Three of the four questions here exist because of something measured rather than something
/// designed: whether a table has rows at all (a NOT NULL column may only be added when it does not),
/// how many values would not survive a type conversion (the rebuild counts them before it starts),
/// and which views name a table (dropping it leaves them in the catalogue, failing at read time).
/// </summary>
public sealed partial class DatabaseSession
{
    #region Rows

    /// <summary>
    /// Whether the table holds anything - asked by scanning for one row, never with COUNT(*), which on
    /// this engine is separate state that can disagree with the rows.
    /// </summary>
    public async Task<bool> HasAnyRowsAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = m_connection!.CreateCommand();
        command.CommandText = $"SELECT * FROM {DdlWriter.Identifier(tableName)} LIMIT 1";

        using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct);
    }

    /// <summary>
    /// How many values in a column would not come back unchanged from a round trip through another
    /// type - the number the rebuild shows before it converts anything (WS-41).
    ///
    /// It is a round trip rather than a conversion because this engine's CAST never fails: measured
    /// 2026-08-06, <c>CAST('not a number' AS INTEGER)</c> is 0 and <c>CAST('3.9' AS INTEGER)</c> is
    /// also 0. Both are casualties, and neither raises anything. Comparing the value with itself after
    /// the trip is the only question the engine will answer honestly.
    /// </summary>
    public async Task<int?> CountValuesThatWillNotConvertAsync(
        string tableName, string columnName, string fromType, string toType, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var column = DdlWriter.Identifier(columnName);

            var sql =
                $"SELECT {column} FROM {DdlWriter.Identifier(tableName)} " +
                $"WHERE {column} IS NOT NULL AND CAST(CAST({column} AS {toType}) AS {fromType}) <> {column}";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;

            var count = 0;
            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
                count++;

            return count;
        }
        catch (Exception ex)
        {
            // A type pair the engine will not compare is not a reason to refuse the rebuild - it is a
            // reason to say the count is unknown, which the caller renders differently from zero.
            m_logger.LogDebug(ex, "Could not count unconvertible values in {Table}.{Column}", tableName, columnName);

            return null;
        }
    }

    #endregion

    #region Triggers

    /// <summary>
    /// The triggers on one table, complete enough to write them out again. A trigger is dropped with
    /// its table, so a rebuild that does not carry these loses them silently.
    /// </summary>
    public async Task<IReadOnlyList<TriggerInfo>> GetTableTriggersAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        var triggers = new List<TriggerInfo>();

        try
        {
            const string sql =
                "SELECT TRIGGER_NAME, EVENT_OBJECT_TABLE, ACTION_TIMING, EVENT_MANIPULATION, " +
                "ACTION_ORIENTATION, ACTION_CONDITION, ACTION_STATEMENT " +
                "FROM INFORMATION_SCHEMA.TRIGGERS WHERE EVENT_OBJECT_TABLE = @tableName";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@tableName", tableName);

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                triggers.Add(new TriggerInfo
                {
                    Name = reader.GetString(0),
                    Table = reader.IsDBNull(1) ? tableName : reader.GetString(1),
                    Timing = reader.IsDBNull(2) ? "AFTER" : reader.GetString(2),
                    Event = reader.IsDBNull(3) ? "INSERT" : reader.GetString(3),
                    Orientation = reader.IsDBNull(4) ? "ROW" : reader.GetString(4),
                    Condition = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Body = reader.IsDBNull(6) ? null : reader.GetString(6)
                });
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read the triggers of {Table}", tableName);
        }

        return triggers;
    }

    #endregion

    #region Views

    /// <summary>
    /// The views whose definition names a table.
    ///
    /// Textual, and it says so wherever it is shown: the catalogue keeps a view's body as a rendering
    /// of the stored tree, and for two shapes - a UNION and a subquery - that rendering comes back
    /// NULL. A view Studio cannot read is a view it cannot check and must not offer to edit.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetViewsMentioningAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        var views = new List<string>();

        try
        {
            const string sql = "SELECT TABLE_NAME, VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var definition = reader.IsDBNull(1) ? null : reader.GetString(1);

                if (definition != null && definition.Contains(tableName, StringComparison.OrdinalIgnoreCase))
                    views.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read the views over {Table}", tableName);
        }

        return views;
    }

    #endregion
}
