using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;
using System.Diagnostics;

namespace OutWit.Database.Studio.Services;


/// <summary>
/// Query execution methods for DatabaseSession.
/// </summary>
public sealed partial class DatabaseSession
{
    #region Query Execution

    public async Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        var result = new QueryResult();
        var sw = Stopwatch.StartNew();

        try
        {
            result = await ExecuteQueryInternalAsync(sql, ct);
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

            m_logger.LogInformation(
                "Query executed successfully in {Time}ms, {Rows} rows",
                result.ExecutionTimeMs, result.RowsAffected);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = FormatErrorMessage(ex);
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
            m_logger.LogError(ex, "Query execution failed");
        }
        finally
        {
            sw.Stop();
        }

        return result;
    }

    private async Task<QueryResult> ExecuteQueryInternalAsync(string sql, CancellationToken ct)
    {
        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync(ct);

        var dataTable = CreateDataTableFromReader(reader);
        await PopulateDataTableAsync(dataTable, reader, ct);

        return new QueryResult
        {
            Data = dataTable,
            RowsAffected = dataTable.Rows.Count
        };
    }

    private static System.Data.DataTable CreateDataTableFromReader(System.Data.Common.DbDataReader reader)
    {
        var dataTable = new System.Data.DataTable("QueryResult");

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var columnType = reader.GetFieldType(i);
            dataTable.Columns.Add(columnName, columnType);
        }

        return dataTable;
    }

    private static async Task PopulateDataTableAsync(
        System.Data.DataTable dataTable,
        System.Data.Common.DbDataReader reader,
        CancellationToken ct)
    {
        while (await reader.ReadAsync(ct))
        {
            var row = dataTable.NewRow();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }
            dataTable.Rows.Add(row);
        }

        // Rows.Add leaves every row in state Added, which says "this row is not in the database yet" -
        // the opposite of the truth about rows just read out of it. The editor believed it: DataRow
        // .Delete() on an Added row DETACHES it instead of marking it deleted, so deleting a row threw
        // RowNotInTableException out of the commit and nothing was ever deleted.
        dataTable.AcceptChanges();
    }

    public async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = m_connection!.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// Runs a set of statements as one transaction: all of them reach the database, or none do.
    ///
    /// The table editor used to send its buffer one statement at a time, each in its own try/catch,
    /// collecting the failures into a list and showing the first three. A set that failed halfway
    /// through left the rows it had already written, and the user was told "Update failed: ..." with
    /// no way of knowing what had gone in.
    ///
    /// Everything here is typed as the ADO.NET base classes - DbTransaction, DbCommand - so this
    /// exercises the drop-in surface a consumer has, not the provider's own type.
    /// </summary>
    public async Task<BatchResult> ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default)
    {
        EnsureConnected();

        if (statements.Count == 0)
            return BatchResult.Empty;

        var affected = 0;

        System.Data.Common.DbTransaction transaction = await m_connection!.BeginTransactionAsync(ct);

        try
        {
            for (var i = 0; i < statements.Count; i++)
            {
                using System.Data.Common.DbCommand command = m_connection.CreateCommand();

                // Measured 2026-08-05: leaving this unset changes nothing, because the provider
                // applies the connection's open transaction to every command on it. Set anyway - that
                // is the ADO.NET contract, and a consumer reading this code should not have to know
                // the provider's habits.
                command.Transaction = transaction;
                command.CommandText = statements[i];

                try
                {
                    affected += await command.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(ct);

                    m_logger.LogWarning(ex,
                        "Batch of {Count} statements rolled back at statement {Index}", statements.Count, i + 1);

                    return new BatchResult
                    {
                        Committed = false,
                        FailedIndex = i,
                        ErrorMessage = FormatErrorMessage(ex)
                    };
                }
            }

            await transaction.CommitAsync(ct);

            m_logger.LogInformation("Batch of {Count} statements committed, {Rows} rows affected",
                statements.Count, affected);

            return new BatchResult
            {
                Committed = true,
                RowsAffected = affected
            };
        }
        catch (Exception ex)
        {
            // The commit itself failed, or the rollback did. Either way nothing here can be assumed
            // applied, and saying so is the whole point of the method.
            m_logger.LogError(ex, "Batch of {Count} statements could not be committed", statements.Count);

            return new BatchResult
            {
                Committed = false,
                FailedIndex = statements.Count - 1,
                ErrorMessage = FormatErrorMessage(ex)
            };
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    #endregion
}
