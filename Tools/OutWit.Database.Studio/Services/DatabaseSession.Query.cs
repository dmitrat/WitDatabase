using Microsoft.Extensions.Logging;
using OutWit.Database.AdoNet;
using OutWit.Database.Studio.Models;
using System.Diagnostics;

namespace OutWit.Database.Studio.Services;


/// <summary>
/// Query execution methods for DatabaseSession.
/// </summary>
public sealed partial class DatabaseSession
{
    #region Query Execution

    public Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default)
    {
        return ExecuteQueryAsync(SqlStatement.Of(sql), ct);
    }

    public async Task<QueryResult> ExecuteQueryAsync(SqlStatement statement, CancellationToken ct = default)
    {
        EnsureConnected();

        var result = new QueryResult();
        var sw = Stopwatch.StartNew();

        try
        {
            result = await ExecuteQueryInternalAsync(statement, ct);
            result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;

            CountInTransaction();

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

    private async Task<QueryResult> ExecuteQueryInternalAsync(SqlStatement statement, CancellationToken ct)
    {
        using var command = CreateCommand(statement, transaction: null);

        using var reader = await command.ExecuteReaderAsync(ct);

        // A statement that returns no columns returned no rows either - it changed some. Counting the
        // rows of an empty table for an INSERT is how "312 rows inserted" used to be reported as 0.
        if (reader.FieldCount == 0)
        {
            return new QueryResult
            {
                Data = null,
                RowsAffected = Math.Max(reader.RecordsAffected, 0),
                ReturnedRows = false
            };
        }

        var dataTable = CreateDataTableFromReader(reader);
        await PopulateDataTableAsync(dataTable, reader, ct);

        return new QueryResult
        {
            Data = dataTable,
            RowsAffected = dataTable.Rows.Count,
            ReturnedRows = true
        };
    }

    /// <summary>
    /// Builds the command for a statement and binds its values. The binding is the whole point: the
    /// value goes to the engine as a value, so nothing a person typed can become syntax.
    ///
    /// The session's own manual transaction (WS-26) is attached when the caller names none. Measured
    /// 2026-08-05: the provider applies the connection's open transaction to every command on it
    /// anyway, so this changes no behaviour - it makes the code say what is happening.
    /// </summary>
    private WitDbCommand CreateCommand(SqlStatement statement, System.Data.Common.DbTransaction? transaction)
    {
        var command = m_connection!.CreateCommand();
        command.CommandText = statement.Text;

        var effective = transaction ?? m_transaction;

        if (effective != null)
            command.Transaction = (WitDbTransaction)effective;

        foreach (var parameter in statement.Parameters)
            command.Parameters.Add(new WitDbParameter(parameter.Name, parameter.Value ?? DBNull.Value));

        return command;
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

    public Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default)
    {
        return ExecuteNonQueryAsync(SqlStatement.Of(sql), ct);
    }

    public async Task<int> ExecuteNonQueryAsync(SqlStatement statement, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = CreateCommand(statement, transaction: null);

        var affected = await command.ExecuteNonQueryAsync(ct);

        CountInTransaction();

        return affected;
    }

    public async Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default)
    {
        EnsureConnected();

        using var command = CreateCommand(SqlStatement.Of(sql), transaction: null);

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
    public async Task<BatchResult> ExecuteBatchAsync(IReadOnlyList<SqlStatement> statements, CancellationToken ct = default)
    {
        EnsureConnected();

        if (statements.Count == 0)
            return BatchResult.Empty;

        // A manual transaction may already be open on this connection (WS-26), and a connection holds
        // exactly one - beginning a second throws. The buffer still has to be all-or-nothing, so it
        // gets a savepoint of its own inside the user's transaction: the edits either all land in it,
        // or all leave it, and what the user does with the transaction afterwards stays theirs.
        if (m_transaction != null)
            return await ExecuteBatchInSavepointAsync(statements, ct);

        var affected = 0;

        System.Data.Common.DbTransaction transaction = await m_connection!.BeginTransactionAsync(ct);

        try
        {
            for (var i = 0; i < statements.Count; i++)
            {
                // Measured 2026-08-05: leaving the transaction unset changes nothing, because the
                // provider applies the connection's open transaction to every command on it. Set
                // anyway - that is the ADO.NET contract, and a consumer reading this code should not
                // have to know the provider's habits.
                using System.Data.Common.DbCommand command = CreateCommand(statements[i], transaction);

                try
                {
                    var rows = await command.ExecuteNonQueryAsync(ct);

                    // A statement that says how many rows it must touch is asserting that the row it
                    // was built from is still the row in the database (WS-37). Zero means it is not.
                    if (statements[i].ExpectedRows is { } expected && rows != expected)
                    {
                        await transaction.RollbackAsync(ct);

                        m_logger.LogWarning(
                            "Batch of {Count} rolled back at statement {Index}: {Rows} rows affected, {Expected} expected",
                            statements.Count, i + 1, rows, expected);

                        return new BatchResult
                        {
                            Committed = false,
                            IsConflict = true,
                            FailedIndex = i,
                            MatchedRows = rows,
                            ExpectedRows = expected
                        };
                    }

                    affected += rows;
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

    /// <summary>
    /// The same set of statements, applied inside a transaction somebody else opened. The savepoint is
    /// what keeps the promise: released on success, rolled back to on failure, and in neither case is
    /// the user's own transaction ended by the table editor.
    /// </summary>
    private async Task<BatchResult> ExecuteBatchInSavepointAsync(
        IReadOnlyList<SqlStatement> statements, CancellationToken ct)
    {
        const string SAVEPOINT = "studio_batch";

        var affected = 0;

        await ExecuteNonQueryAsync($"SAVEPOINT {SAVEPOINT}", ct);

        for (var i = 0; i < statements.Count; i++)
        {
            try
            {
                var rows = await ExecuteNonQueryAsync(statements[i], ct);

                if (statements[i].ExpectedRows is { } expected && rows != expected)
                {
                    await ExecuteNonQueryAsync($"ROLLBACK TO SAVEPOINT {SAVEPOINT}", ct);

                    return new BatchResult
                    {
                        Committed = false,
                        IsConflict = true,
                        FailedIndex = i,
                        MatchedRows = rows,
                        ExpectedRows = expected
                    };
                }

                affected += rows;
            }
            catch (Exception ex)
            {
                await ExecuteNonQueryAsync($"ROLLBACK TO SAVEPOINT {SAVEPOINT}", ct);

                m_logger.LogWarning(ex,
                    "Batch of {Count} statements rolled back to a savepoint at statement {Index}, " +
                    "inside the connection's open transaction", statements.Count, i + 1);

                return new BatchResult
                {
                    Committed = false,
                    FailedIndex = i,
                    ErrorMessage = FormatErrorMessage(ex)
                };
            }
        }

        await ExecuteNonQueryAsync($"RELEASE SAVEPOINT {SAVEPOINT}", ct);

        m_logger.LogInformation(
            "Batch of {Count} statements applied inside the open transaction, {Rows} rows affected",
            statements.Count, affected);

        // Not "committed" in the sense the caller usually means: the rows are in the transaction, and
        // whoever opened it decides. The table editor reads this as success, which is right - the
        // edits are applied as far as this connection is concerned.
        return new BatchResult
        {
            Committed = true,
            RowsAffected = affected
        };
    }

    #endregion
}
