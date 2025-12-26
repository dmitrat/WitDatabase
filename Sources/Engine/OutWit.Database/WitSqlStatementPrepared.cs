using OutWit.Database.Context;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Statements;
using OutWit.Database.Values;

namespace OutWit.Database;

/// <summary>
/// A prepared SQL statement that can be executed multiple times with different parameters.
/// </summary>
public sealed class WitSqlStatementPrepared : IDisposable
{
    #region Fields

    private readonly IDatabase m_database;
    private readonly IReadOnlyList<WitSqlStatement> m_statements;
    private readonly Dictionary<string, object?> m_parameters = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new prepared statement.
    /// </summary>
    /// <param name="database">The database to execute against.</param>
    /// <param name="statements">The parsed SQL statements.</param>
    internal WitSqlStatementPrepared(IDatabase database, IReadOnlyList<WitSqlStatement> statements)
    {
        m_database = database;
        m_statements = statements;
    }

    #endregion

    #region Parameters

    /// <summary>
    /// Set a parameter value.
    /// </summary>
    /// <param name="name">The parameter name (with or without @ prefix).</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>This prepared statement for fluent chaining.</returns>
    public WitSqlStatementPrepared SetParameter(string name, object? value)
    {
        var paramName = name.StartsWith("@") ? name : $"@{name}";
        m_parameters[paramName] = value;
        return this;
    }

    /// <summary>
    /// Clear all parameter values.
    /// </summary>
    /// <returns>This prepared statement for fluent chaining.</returns>
    public WitSqlStatementPrepared ClearParameters()
    {
        m_parameters.Clear();
        return this;
    }

    #endregion

    #region Execute

    /// <summary>
    /// Execute the prepared statement.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>The query result.</returns>
    public WitSqlResult Execute(CancellationToken cancellationToken = default)
    {
        var context = new ContextExecution
        {
            Database = m_database,
            CancellationToken = cancellationToken
        };

        foreach (var (key, value) in m_parameters)
        {
            context.Parameters[key] = WitSqlValue.FromObject(value);
        }

        var executor = new StatementExecutor(context);

        WitSqlResult? result = null;
        foreach (var statement in m_statements)
        {
            result?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            result = executor.Execute(statement);
        }

        return result!;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the prepared statement and clears parameters.
    /// </summary>
    public void Dispose()
    {
        m_parameters.Clear();
    }

    #endregion
}
