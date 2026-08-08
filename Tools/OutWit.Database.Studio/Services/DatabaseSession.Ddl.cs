using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;
using System.Text;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// DDL generation methods for DatabaseSession (table/view/index/trigger definitions).
///
/// <para>
/// <b>A definition here is a statement that can be executed back.</b> Two of the four used to be the
/// catalogue's BODY column handed straight on - <c>VIEW_DEFINITION</c> is a view's query and
/// <c>ACTION_STATEMENT</c> is a trigger's body, both correct by the standard and neither of them a
/// <c>CREATE</c> - so the dump they feed produced a script that could not rebuild either object.
/// Measured 2026-08-08; see <c>DatabaseDumpTests</c>. The parts come from the catalogue and
/// <see cref="DdlWriter"/> assembles them, which is the same writer the designer runs and the one
/// whose every shape has been executed against the engine.
/// </para>
/// </summary>
public sealed partial class DatabaseSession
{
    #region View Definition

    /// <summary>
    /// The view's QUERY, as the catalogue publishes it - what the structure tab lets a person edit.
    /// Null when the catalogue cannot render it, which is a real answer: measured for a UNION and for
    /// a subquery, and the editor refuses to rewrite a view whose body it does not have.
    /// </summary>
    public async Task<string?> GetViewBodyAsync(string viewName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            const string sql = "SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = @viewName";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@viewName", viewName);

            var result = await command.ExecuteScalarAsync(ct);
            return result as string;
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get view body for {ViewName}", viewName);
            return null;
        }
    }

    /// <summary>
    /// The <c>CREATE VIEW</c> that would rebuild it.
    /// </summary>
    /// <remarks>
    /// This used to return the bare query, and the dump wrote that - so the script carried a loose
    /// <c>SELECT …;</c> which <b>runs</b>, reports success, and leaves no view behind. A trigger at
    /// least failed loudly; a view was lost in silence.
    /// </remarks>
    public async Task<string?> GetViewDefinitionAsync(string viewName, CancellationToken ct = default)
    {
        var body = await GetViewBodyAsync(viewName, ct);

        return string.IsNullOrWhiteSpace(body) ? null : DdlWriter.CreateView(viewName, body);
    }

    #endregion

    #region Trigger Definition

    /// <summary>
    /// The <c>CREATE TRIGGER</c> that would rebuild it, assembled from the catalogue's parts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ACTION_STATEMENT</c> alone is the body, and the dump wrote it verbatim - so a dumped database
    /// with a trigger ended with <c>INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id);</c> as a
    /// statement of its own and the engine refused it with "Column 'Id' not found". The splitter was
    /// not implicated: measured the same day, a hand-written <c>BEGIN … END</c> body comes back from
    /// <see cref="SqlScript.Split"/> as one statement.
    /// </para>
    /// <para>
    /// <b>One part is not published and so cannot be rebuilt:</b> a trigger declared
    /// <c>UPDATE OF V</c> appears in <c>INFORMATION_SCHEMA.TRIGGERS</c> as a plain <c>UPDATE</c> - there
    /// is no column for the column list - so this widens it to every column. It changes nothing today
    /// because the engine does not honour <c>UPDATE OF</c> when firing either (measured: a trigger on
    /// one column fires on an update of another), and that is pinned in the engine's own suite.
    /// </para>
    /// </remarks>
    public async Task<string?> GetTriggerDefinitionAsync(string triggerName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            const string sql = @"
                SELECT ACTION_TIMING, EVENT_MANIPULATION, EVENT_OBJECT_TABLE,
                       ACTION_ORIENTATION, ACTION_CONDITION, ACTION_STATEMENT
                FROM INFORMATION_SCHEMA.TRIGGERS
                WHERE TRIGGER_NAME = @triggerName";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@triggerName", triggerName);

            using var reader = await command.ExecuteReaderAsync(ct);

            if (!await reader.ReadAsync(ct))
                return null;

            var body = reader.IsDBNull(5) ? null : reader.GetString(5);

            // A body the catalogue cannot render is null, and a trigger with no body is not a trigger.
            // The caller names it rather than writing half of one - the phase-8 rule.
            if (string.IsNullOrWhiteSpace(body))
                return null;

            return DdlWriter.CreateTrigger(new TriggerDraft
            {
                Name = triggerName,
                Timing = reader.GetString(0),
                Event = reader.GetString(1),
                Table = reader.GetString(2),

                // FOR EACH ROW is written only for a row trigger: FOR EACH STATEMENT is a parse error
                // on this grammar, and omitting the clause is how a statement trigger is expressed.
                ForEachRow = reader.GetString(3).Equals("ROW", StringComparison.OrdinalIgnoreCase),
                Condition = reader.IsDBNull(4) ? null : reader.GetString(4),
                Body = body
            });
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get trigger definition for {TriggerName}", triggerName);
            return null;
        }
    }

    #endregion

    #region Index Definition

    public async Task<string?> GetIndexDefinitionAsync(string indexName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var indexInfo = await ReadIndexInfoAsync(indexName, ct);
            if (indexInfo == null)
                return null;

            return BuildIndexDefinition(indexName, indexInfo.Value);
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get index definition for {IndexName}", indexName);
            return null;
        }
    }

    private async Task<(string TableName, List<string> Columns, bool IsUnique, string? Filter)?> ReadIndexInfoAsync(
        string indexName, CancellationToken ct)
    {
        const string sql = @"
            SELECT TABLE_NAME, COLUMN_NAME, IS_UNIQUE, FILTER_CONDITION
            FROM INFORMATION_SCHEMA.INDEXES 
            WHERE INDEX_NAME = @indexName
            ORDER BY ORDINAL_POSITION";

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@indexName", indexName);

        string? tableName = null;
        var columns = new List<string>();
        var isUnique = false;
        string? filter = null;

        using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tableName ??= reader.GetString(0);
            columns.Add(reader.GetString(1));
            isUnique = IsYes(reader, 2);
            filter ??= reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        if (tableName == null || columns.Count == 0)
            return null;

        return (tableName, columns, isUnique, filter);
    }

    /// <summary>
    /// Reads a yes-or-no column the way <c>INFORMATION_SCHEMA</c> actually publishes it.
    /// </summary>
    /// <remarks>
    /// <b>It is the STRING "YES" or "NO", not a boolean</b>, and reading it with <c>GetBoolean</c>
    /// answered TRUE for both - so every index came back from the catalogue as <c>CREATE UNIQUE
    /// INDEX</c>. That is not a display problem: it is what the DUMP writes, and a dumped database
    /// whose non-unique index has the duplicate values a non-unique index is for cannot be restored at
    /// all. Found on 2026-08-08 by executing a dump back into an empty database for the first time -
    /// the transfer WS-58 is built on - which failed with "UNIQUE constraint failed: Cannot create
    /// unique index 'IX_Orders_CustomerId' ... duplicate values exist".
    /// </remarks>
    private static bool IsYes(System.Data.Common.DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return false;

        var value = reader.GetValue(ordinal);

        return value switch
        {
            bool flag => flag,
            string text => text.Equals("YES", StringComparison.OrdinalIgnoreCase)
                           || text.Equals("Y", StringComparison.OrdinalIgnoreCase)
                           || text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                           || text == "1",
            _ => Convert.ToInt64(value) != 0
        };
    }

    private static string BuildIndexDefinition(
        string indexName, 
        (string TableName, List<string> Columns, bool IsUnique, string? Filter) info)
    {
        var uniqueStr = info.IsUnique ? "UNIQUE " : "";
        var filterStr = string.IsNullOrEmpty(info.Filter) ? "" : $" WHERE {info.Filter}";
        return $"CREATE {uniqueStr}INDEX {indexName} ON {info.TableName} ({string.Join(", ", info.Columns)}){filterStr}";
    }

    #endregion

    #region Table Definition

    public async Task<string?> GetTableDefinitionAsync(string tableName, CancellationToken ct = default)
    {
        EnsureConnected();

        try
        {
            var columns = await GetColumnsAsync(tableName, ct);
            if (columns.Count == 0)
                return null;

            var foreignKeys = await GetForeignKeysAsync(tableName, ct);

            return BuildTableDefinition(tableName, columns, foreignKeys);
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Failed to get table definition for {TableName}", tableName);
            return null;
        }
    }

    private static string BuildTableDefinition(
        string tableName,
        IReadOnlyList<Models.ColumnInfo> columns,
        IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CREATE TABLE \"{tableName}\" (");

        var columnDefs = new List<string>();
        var pkColumns = new List<string>();
        var hasAutoIncrementPk = false;

        foreach (var col in columns)
        {
            var colDef = BuildColumnDefinition(col, foreignKeys, ref hasAutoIncrementPk, pkColumns);
            columnDefs.Add(colDef);
        }

        sb.AppendLine(string.Join(",\n", columnDefs));

        // Only add separate PRIMARY KEY constraint if not already defined inline
        if (pkColumns.Count > 0 && !hasAutoIncrementPk)
        {
            sb.AppendLine($"    ,PRIMARY KEY ({string.Join(", ", pkColumns)})");
        }

        sb.Append(");");

        return sb.ToString();
    }

    private static string BuildColumnDefinition(
        Models.ColumnInfo col,
        IReadOnlyList<ForeignKeyInfo> foreignKeys,
        ref bool hasAutoIncrementPk,
        List<string> pkColumns)
    {
        // Handle computed columns separately
        if (col.IsComputed && !string.IsNullOrEmpty(col.GenerationExpression))
        {
            return BuildComputedColumnDefinition(col);
        }

        var colDef = $"    \"{col.Name}\" {FormatDataType(col)}";

        // For single-column auto-increment primary key, use inline PRIMARY KEY AUTOINCREMENT
        if (col.IsPrimaryKey && col.IsAutoIncrement)
        {
            colDef += " PRIMARY KEY AUTOINCREMENT";
            hasAutoIncrementPk = true;
        }
        else
        {
            if (!col.IsNullable)
                colDef += " NOT NULL";

            if (col.IsPrimaryKey)
                pkColumns.Add($"\"{col.Name}\"");
        }

        // UNIQUE constraint (inline)
        if (col.IsUnique && !col.IsPrimaryKey)
            colDef += " UNIQUE";

        // DEFAULT value
        if (!string.IsNullOrEmpty(col.DefaultValue))
            colDef += $" DEFAULT {col.DefaultValue}";

        // CHECK constraint (inline)
        if (!string.IsNullOrEmpty(col.CheckExpression))
            colDef += $" CHECK ({col.CheckExpression})";

        // COLLATE
        if (!string.IsNullOrEmpty(col.Collation))
            colDef += $" COLLATE {col.Collation}";

        // Foreign key reference (inline for single column)
        var fk = foreignKeys.FirstOrDefault(f => f.FromColumn == col.Name);
        if (fk != null)
        {
            colDef += BuildForeignKeyClause(fk);
        }

        return colDef;
    }

    private static string BuildComputedColumnDefinition(Models.ColumnInfo col)
    {
        var colDef = $"    \"{col.Name}\" AS ({col.GenerationExpression})";
        if (col.IsGenerated == "STORED")
            colDef += " STORED";
        return colDef;
    }

    private static string BuildForeignKeyClause(ForeignKeyInfo fk)
    {
        var clause = $" REFERENCES \"{fk.ToTable}\"(\"{fk.ToColumn}\")";
        
        if (!string.IsNullOrEmpty(fk.OnDelete) && fk.OnDelete != "NO ACTION")
            clause += $" ON DELETE {fk.OnDelete}";
        
        if (!string.IsNullOrEmpty(fk.OnUpdate) && fk.OnUpdate != "NO ACTION")
            clause += $" ON UPDATE {fk.OnUpdate}";
        
        return clause;
    }

    private static string FormatDataType(Models.ColumnInfo col)
    {
        var dataType = col.DataType;

        // Add length for string/binary types
        if (col.MaxLength.HasValue && IsLengthBasedType(dataType))
        {
            dataType += $"({col.MaxLength.Value})";
        }
        // Add precision and scale for decimal types
        else if (col.NumericPrecision.HasValue && IsPrecisionBasedType(dataType))
        {
            dataType += col.NumericScale.HasValue
                ? $"({col.NumericPrecision.Value},{col.NumericScale.Value})"
                : $"({col.NumericPrecision.Value})";
        }

        return dataType;
    }

    private static bool IsLengthBasedType(string dataType) =>
        dataType.Equals("VARCHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("CHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("NVARCHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("NCHAR", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("VARBINARY", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("BINARY", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrecisionBasedType(string dataType) =>
        dataType.Equals("DECIMAL", StringComparison.OrdinalIgnoreCase) ||
        dataType.Equals("NUMERIC", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Foreign Keys

    /// <summary>
    /// The foreign keys of a table, with their rules, for writing the table's DDL out.
    ///
    /// Public because the object inspector asks the same question (2.5), and two readers of the same
    /// catalogue view is how a client starts disagreeing with itself.
    /// </summary>
    public async Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(string tableName, CancellationToken ct = default)
    {
        var foreignKeys = new List<ForeignKeyInfo>();

        try
        {
            const string sql = @"
                SELECT 
                    kcu.COLUMN_NAME,
                    kcu.REFERENCED_TABLE_NAME,
                    kcu.REFERENCED_COLUMN_NAME,
                    rc.DELETE_RULE,
                    rc.UPDATE_RULE
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                INNER JOIN INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                    ON rc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME
                WHERE kcu.TABLE_NAME = @tableName
                  AND kcu.REFERENCED_TABLE_NAME IS NOT NULL";

            using var command = m_connection!.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@tableName", tableName);

            using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                foreignKeys.Add(new ForeignKeyInfo
                {
                    ConstraintName = string.Empty,
                    FromTable = tableName,
                    FromColumn = reader.GetString(0),
                    ToTable = reader.GetString(1),
                    ToColumn = reader.GetString(2),
                    OnDelete = reader.IsDBNull(3) ? null : reader.GetString(3),
                    OnUpdate = reader.IsDBNull(4) ? null : reader.GetString(4)
                });
            }
        }
        catch (Exception ex)
        {
            m_logger.LogDebug(ex, "Unable to read foreign key metadata for table {TableName}", tableName);
        }

        return foreignKeys;
    }

    #endregion
}
