using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// What the Explorer and the object inspector ask a session, beyond the lists of names: routines,
/// foreign keys, indexes with their columns, and a row count that is allowed to give up.
/// </summary>
public sealed partial class DatabaseSession
{
    #region Routines

    /// <summary>
    /// Functions and procedures. The tree has had no folder for these while the engine has had the
    /// subsystem since phase 9d - a client that does not show them is telling the user they are not
    /// there (WS-21).
    /// </summary>
    public async Task<IReadOnlyList<RoutineInfo>> GetRoutinesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        const string SQL = "SELECT ROUTINE_NAME, ROUTINE_TYPE, DATA_TYPE, ROUTINE_DEFINITION "
            + "FROM INFORMATION_SCHEMA.ROUTINES";

        var routines = new List<RoutineInfo>();

        try
        {
            using var command = m_connection!.CreateCommand();
            command.CommandText = SQL;

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                routines.Add(new RoutineInfo
                {
                    Name = reader.GetString(0),
                    RoutineType = reader.IsDBNull(1) ? "FUNCTION" : reader.GetString(1),
                    DataType = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Definition = reader.IsDBNull(3) ? null : reader.GetString(3)
                });
            }
        }
        catch (Exception ex)
        {
            // A database written by an engine without the routine catalogue answers nothing here, and
            // that is not an error the user needs to see - the folder simply comes back empty.
            m_logger.LogDebug(ex, "Unable to read INFORMATION_SCHEMA.ROUTINES");
        }

        return routines;
    }

    #endregion

    #region Keys and indexes

    /// <summary>
    /// The foreign keys that POINT AT this table - the "referenced by" half of the inspector. Both
    /// directions matter: what a table depends on, and what would break if it went.
    ///
    /// The other direction is <see cref="GetForeignKeysAsync"/>, which lives with the DDL writer
    /// because that is what needed it first.
    /// </summary>
    public async Task<IReadOnlyList<ForeignKeyInfo>> GetReferencingKeysAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        const string SQL = "SELECT CONSTRAINT_NAME, TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME "
            + "FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE "
            + "WHERE REFERENCED_TABLE_NAME = @tableName "
            + "ORDER BY CONSTRAINT_NAME, ORDINAL_POSITION";

        var keys = new List<ForeignKeyInfo>();

        try
        {
            using var command = m_connection!.CreateCommand();
            command.CommandText = SQL;
            command.Parameters.Add(new OutWit.Database.AdoNet.WitDbParameter("@tableName", tableName));

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(3) || reader.IsDBNull(4))
                    continue;

                keys.Add(new ForeignKeyInfo
                {
                    ConstraintName = reader.GetString(0),
                    FromTable = reader.GetString(1),
                    FromColumn = reader.GetString(2),
                    ToTable = reader.GetString(3),
                    ToColumn = reader.GetString(4)
                });
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read INFORMATION_SCHEMA.KEY_COLUMN_USAGE");
        }

        return keys;
    }

    /// <summary>
    /// The indexes of one table, each with the columns it covers, in order.
    /// </summary>
    public async Task<IReadOnlyList<IndexInfo>> GetTableIndexesAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        const string SQL = "SELECT INDEX_NAME, COLUMN_NAME, IS_UNIQUE, FILTER_CONDITION "
            + "FROM INFORMATION_SCHEMA.INDEXES WHERE TABLE_NAME = @tableName "
            + "ORDER BY INDEX_NAME, ORDINAL_POSITION";

        var byName = new Dictionary<string, (List<string> Columns, bool IsUnique, string? Filter)>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            using var command = m_connection!.CreateCommand();
            command.CommandText = SQL;
            command.Parameters.Add(new OutWit.Database.AdoNet.WitDbParameter("@tableName", tableName));

            using var reader = await command.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                var columnName = reader.IsDBNull(1) ? null : reader.GetString(1);
                var unique = !reader.IsDBNull(2)
                    && reader.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase);
                var filter = reader.IsDBNull(3) ? null : reader.GetString(3);

                if (!byName.TryGetValue(name, out var entry))
                    byName[name] = entry = ([], unique, filter);

                if (columnName != null)
                    entry.Columns.Add(columnName);
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read INFORMATION_SCHEMA.INDEXES");
        }

        return byName
            .Select(pair => new IndexInfo
            {
                Name = pair.Key,
                TableName = tableName,
                Columns = pair.Value.Columns,
                IsUnique = pair.Value.IsUnique,
                FilterCondition = pair.Value.Filter
            })
            .ToList();
    }

    #endregion

    #region Counting

    /// <summary>
    /// How many rows a table has, or null if the count did not finish in time.
    ///
    /// A count is <c>SELECT COUNT(*)</c>, and the tree asks it for every table at once. On this engine
    /// the answer is a counter kept beside the data, so it is usually instant - but "usually" is not
    /// something a tree can be built on, and a client that freezes on a table it has never opened is
    /// unusable in exactly the case that matters (WS-16). So the caller sets a deadline, and a count
    /// that misses it is reported as unknown rather than waited for.
    /// </summary>
    public async Task<long?> TryCountRowsAsync(string tableName, TimeSpan timeout, CancellationToken ct = default)
    {
        EnsureConnected();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            using var command = m_connection!.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM [{tableName.Replace("]", "]]")}]";

            var value = await command.ExecuteScalarAsync(deadline.Token);

            return value == null || value == DBNull.Value ? null : Convert.ToInt64(value);
        }
        catch (OperationCanceledException)
        {
            m_logger.LogDebug("Counting {Table} did not finish within {Timeout}", tableName, timeout);
            return null;
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Counting {Table} failed", tableName);
            return null;
        }
    }

    #endregion
}
