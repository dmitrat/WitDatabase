using OutWit.Database.Sql;

namespace OutWit.Database.Engine;

/// <summary>
/// Bulk operations for WitSqlEngine.
/// Provides high-performance batch data manipulation.
/// </summary>
public sealed partial class WitSqlEngine
{
    #region Bulk INSERT

    /// <summary>
    /// Insert multiple rows into a table in a single optimized operation.
    /// This is significantly faster than executing individual INSERT statements
    /// because it:
    /// - Parses the INSERT statement once
    /// - Reuses the execution context
    /// - Batches constraint validation where possible
    /// </summary>
    /// <param name="tableName">The target table name.</param>
    /// <param name="columns">Column names for the insert.</param>
    /// <param name="rows">Rows to insert, each row is an array of values matching columns.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Number of rows inserted.</returns>
    /// <example>
    /// <code>
    /// var columns = new[] { "Name", "Email", "Age" };
    /// var rows = new List&lt;object?[]&gt;
    /// {
    ///     new object?[] { "Alice", "alice@test.com", 25 },
    ///     new object?[] { "Bob", "bob@test.com", 30 },
    ///     new object?[] { "Charlie", "charlie@test.com", 35 }
    /// };
    /// 
    /// int inserted = engine.BulkInsert("Users", columns, rows);
    /// </code>
    /// </example>
    public int BulkInsert(string tableName, IReadOnlyList<string> columns, 
        IEnumerable<object?[]> rows, CancellationToken cancellationToken = default)
    {
        EnsureNotReadOnly(nameof(BulkInsert));

        if (columns.Count == 0)
            throw new ArgumentException("At least one column must be specified.", nameof(columns));

        // Build parameterized INSERT statement
        var paramNames = columns.Select((_, i) => $"@p{i}").ToArray();
        var columnList = string.Join(", ", columns);
        var paramList = string.Join(", ", paramNames);
        var sql = $"INSERT INTO {tableName} ({columnList}) VALUES ({paramList})";

        // Prepare once
        using var stmt = Prepare(sql);

        // Execute for each row
        int totalInserted = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (row.Length != columns.Count)
                throw new ArgumentException(
                    $"Row has {row.Length} values but {columns.Count} columns specified.");

            // Bind parameters
            stmt.ClearParameters();
            for (int i = 0; i < row.Length; i++)
            {
                stmt.SetParameter($"p{i}", row[i]);
            }

            using var result = stmt.Execute(cancellationToken);
            totalInserted += result.RowsAffected;
        }

        return totalInserted;
    }

    /// <summary>
    /// Insert multiple rows from objects using property names as column names.
    /// </summary>
    /// <typeparam name="T">Type of objects to insert.</typeparam>
    /// <param name="tableName">The target table name.</param>
    /// <param name="objects">Objects to insert.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Number of rows inserted.</returns>
    /// <example>
    /// <code>
    /// var users = new[]
    /// {
    ///     new { Name = "Alice", Email = "alice@test.com", Age = 25 },
    ///     new { Name = "Bob", Email = "bob@test.com", Age = 30 }
    /// };
    /// 
    /// int inserted = engine.BulkInsert("Users", users);
    /// </code>
    /// </example>
    public int BulkInsert<T>(string tableName, IEnumerable<T> objects, 
        CancellationToken cancellationToken = default) where T : class
    {
        EnsureNotReadOnly(nameof(BulkInsert));

        // Check if T is a dictionary type - if so, redirect to the dictionary overload
        if (typeof(IDictionary<string, object?>).IsAssignableFrom(typeof(T)))
        {
            return BulkInsert(tableName, objects.Cast<IDictionary<string, object?>>(), cancellationToken);
        }
        
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        if (properties.Length == 0)
            throw new ArgumentException("Type has no readable properties.", nameof(objects));

        // Build parameterized INSERT statement using property names
        var columns = properties.Select(p => p.Name).ToArray();
        var paramNames = columns.Select((_, i) => $"@p{i}").ToArray();
        var columnList = string.Join(", ", columns);
        var paramList = string.Join(", ", paramNames);
        var sql = $"INSERT INTO {tableName} ({columnList}) VALUES ({paramList})";

        // Prepare once
        using var stmt = Prepare(sql);

        // Execute for each object
        int totalInserted = 0;
        foreach (var obj in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stmt.ClearParameters();
            for (int i = 0; i < properties.Length; i++)
            {
                var value = properties[i].GetValue(obj);
                stmt.SetParameter($"p{i}", value);
            }

            using var result = stmt.Execute(cancellationToken);
            totalInserted += result.RowsAffected;
        }

        return totalInserted;
    }

    /// <summary>
    /// Insert multiple rows from dictionaries.
    /// Each dictionary maps column name to value.
    /// </summary>
    /// <param name="tableName">The target table name.</param>
    /// <param name="rows">Rows to insert as dictionaries.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Number of rows inserted.</returns>
    /// <example>
    /// <code>
    /// var rows = new[]
    /// {
    ///     new Dictionary&lt;string, object?&gt; { ["Name"] = "Alice", ["Email"] = "alice@test.com" },
    ///     new Dictionary&lt;string, object?&gt; { ["Name"] = "Bob", ["Email"] = "bob@test.com" }
    /// };
    /// 
    /// int inserted = engine.BulkInsert("Users", rows);
    /// </code>
    /// </example>
    public int BulkInsert(string tableName, IEnumerable<IDictionary<string, object?>> rows, 
        CancellationToken cancellationToken = default)
    {
        EnsureNotReadOnly(nameof(BulkInsert));

        // Get first row to determine columns
        var rowsList = rows.ToList();
        if (rowsList.Count == 0)
            return 0;

        var firstRow = rowsList[0];
        var columns = firstRow.Keys.ToArray();

        // Build parameterized INSERT statement
        var paramNames = columns.Select((_, i) => $"@p{i}").ToArray();
        var columnList = string.Join(", ", columns);
        var paramList = string.Join(", ", paramNames);
        var sql = $"INSERT INTO {tableName} ({columnList}) VALUES ({paramList})";

        // Prepare once
        using var stmt = Prepare(sql);

        // Execute for each row
        int totalInserted = 0;
        foreach (var row in rowsList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stmt.ClearParameters();
            for (int i = 0; i < columns.Length; i++)
            {
                var value = row.TryGetValue(columns[i], out var v) ? v : null;
                stmt.SetParameter($"p{i}", value);
            }

            using var result = stmt.Execute(cancellationToken);
            totalInserted += result.RowsAffected;
        }

        return totalInserted;
    }

    #endregion

    #region Bulk UPDATE

    /// <summary>
    /// Update multiple rows with the same SET values based on a WHERE condition.
    /// </summary>
    /// <param name="tableName">The target table name.</param>
    /// <param name="setValues">Dictionary of column name to new value.</param>
    /// <param name="whereCondition">WHERE condition (without the WHERE keyword), or null to update all rows.</param>
    /// <param name="whereParameters">Parameters for the WHERE condition.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Number of rows updated.</returns>
    /// <example>
    /// <code>
    /// // Update all users' status to "active"
    /// engine.BulkUpdate("Users", 
    ///     new Dictionary&lt;string, object?&gt; { ["Status"] = "active" }, 
    ///     null);
    /// 
    /// // Update users with specific condition
    /// engine.BulkUpdate("Users",
    ///     new Dictionary&lt;string, object?&gt; { ["LastLogin"] = DateTime.Now },
    ///     "Status = @status",
    ///     new Dictionary&lt;string, object?&gt; { ["status"] = "active" });
    /// </code>
    /// </example>
    public int BulkUpdate(string tableName, IDictionary<string, object?> setValues,
        string? whereCondition = null, IDictionary<string, object?>? whereParameters = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotReadOnly(nameof(BulkUpdate));

        if (setValues.Count == 0)
            throw new ArgumentException("At least one SET value must be specified.", nameof(setValues));

        // Build UPDATE statement
        var setClauses = setValues.Keys.Select((col, i) => $"{col} = @set{i}").ToArray();
        var setClause = string.Join(", ", setClauses);
        var sql = $"UPDATE {tableName} SET {setClause}";

        if (!string.IsNullOrEmpty(whereCondition))
        {
            sql += $" WHERE {whereCondition}";
        }

        // Prepare parameters
        var parameters = new Dictionary<string, object?>();
        var setValuesList = setValues.Values.ToArray();
        for (int i = 0; i < setValuesList.Length; i++)
        {
            parameters[$"set{i}"] = setValuesList[i];
        }

        if (whereParameters != null)
        {
            foreach (var (key, value) in whereParameters)
            {
                parameters[key] = value;
            }
        }

        return ExecuteNonQuery(sql, parameters);
    }

    #endregion

    #region Bulk DELETE

    /// <summary>
    /// Delete multiple rows based on a WHERE condition.
    /// </summary>
    /// <param name="tableName">The target table name.</param>
    /// <param name="whereCondition">WHERE condition (without the WHERE keyword), or null to delete all rows.</param>
    /// <param name="whereParameters">Parameters for the WHERE condition.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Number of rows deleted.</returns>
    /// <example>
    /// <code>
    /// // Delete all inactive users
    /// engine.BulkDelete("Users", "Status = @status", 
    ///     new Dictionary&lt;string, object?&gt; { ["status"] = "inactive" });
    /// 
    /// // Delete all rows (TRUNCATE equivalent)
    /// engine.BulkDelete("Users", null);
    /// </code>
    /// </example>
    public int BulkDelete(string tableName, string? whereCondition = null,
        IDictionary<string, object?>? whereParameters = null,
        CancellationToken cancellationToken = default)
    {
        EnsureNotReadOnly(nameof(BulkDelete));

        var sql = $"DELETE FROM {tableName}";

        if (!string.IsNullOrEmpty(whereCondition))
        {
            sql += $" WHERE {whereCondition}";
        }

        return ExecuteNonQuery(sql, whereParameters);
    }

    #endregion
}
