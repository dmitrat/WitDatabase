using OutWit.Database.Context;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Engine;

/// <summary>
/// A prepared SQL statement that can be executed multiple times with different parameters.
/// Provides significant performance improvement for repeated executions by caching the parsed AST.
/// </summary>
/// <remarks>
/// <para>
/// Performance benefits:
/// - Parsing is done once at preparation time
/// - Same executor context can be reused for batch operations
/// - Parameters are bound without re-parsing the SQL text
/// </para>
/// <para>
/// Thread safety: This class is NOT thread-safe. Each thread should have its own prepared statement.
/// </para>
/// </remarks>
public sealed class WitSqlEngineStatement : IDisposable
{
    #region Fields

    private readonly IDatabase m_database;
    private readonly IReadOnlyList<WitSqlStatement> m_statements;
    private readonly string m_sql;
    private readonly Dictionary<string, object?> m_parameters = new(StringComparer.OrdinalIgnoreCase);
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new prepared statement.
    /// </summary>
    /// <param name="database">The database to execute against.</param>
    /// <param name="statements">The parsed SQL statements.</param>
    /// <param name="sql">The original SQL text (for debugging).</param>
    internal WitSqlEngineStatement(IDatabase database, IReadOnlyList<WitSqlStatement> statements, string sql = "")
    {
        m_database = database;
        m_statements = statements;
        m_sql = sql;
    }

    #endregion

    #region Parameters

    /// <summary>
    /// Set a parameter value.
    /// </summary>
    /// <param name="name">The parameter name (with or without @ prefix).</param>
    /// <param name="value">The parameter value.</param>
    /// <returns>This prepared statement for fluent chaining.</returns>
    public WitSqlEngineStatement SetParameter(string name, object? value)
    {
        ThrowIfDisposed();
        var paramName = WitSqlParameterKeys.ToContextKey(name);
        m_parameters[paramName] = value;
        return this;
    }

    /// <summary>
    /// Set multiple parameters at once.
    /// </summary>
    /// <param name="parameters">Dictionary of parameter names and values.</param>
    /// <returns>This prepared statement for fluent chaining.</returns>
    public WitSqlEngineStatement SetParameters(IDictionary<string, object?> parameters)
    {
        ThrowIfDisposed();
        foreach (var (key, value) in parameters)
        {
            SetParameter(key, value);
        }
        return this;
    }

    /// <summary>
    /// Clear all parameter values.
    /// </summary>
    /// <returns>This prepared statement for fluent chaining.</returns>
    public WitSqlEngineStatement ClearParameters()
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        
        var context = CreateExecutionContext(cancellationToken);
        ApplyParameters(context);

        return ExecuteInternal(context, cancellationToken);
    }

    /// <summary>
    /// Execute the prepared statement with a set of parameters.
    /// Parameters are applied and then cleared after execution.
    /// </summary>
    /// <param name="parameters">Parameters for this execution.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>The query result.</returns>
    public WitSqlResult Execute(IDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        ClearParameters();
        SetParameters(parameters);
        return Execute(cancellationToken);
    }

    #endregion

    #region Batch Execution

    /// <summary>
    /// Execute the prepared statement multiple times with different parameter sets.
    /// This is significantly faster than calling Execute() in a loop because:
    /// - Single executor context is reused
    /// - No re-parsing of SQL
    /// - Optimized for INSERT/UPDATE/DELETE operations
    /// </summary>
    /// <param name="parameterSets">Collection of parameter dictionaries, one per execution.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Total number of rows affected across all executions.</returns>
    /// <example>
    /// <code>
    /// using var stmt = engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
    /// 
    /// var users = new[]
    /// {
    ///     new Dictionary&lt;string, object?&gt; { ["name"] = "Alice", ["email"] = "alice@test.com" },
    ///     new Dictionary&lt;string, object?&gt; { ["name"] = "Bob", ["email"] = "bob@test.com" },
    ///     new Dictionary&lt;string, object?&gt; { ["name"] = "Charlie", ["email"] = "charlie@test.com" }
    /// };
    /// 
    /// int rowsAffected = stmt.ExecuteBatch(users);
    /// </code>
    /// </example>
    public int ExecuteBatch(IEnumerable<IDictionary<string, object?>> parameterSets, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        int totalRowsAffected = 0;
        var context = CreateExecutionContext(cancellationToken);

        foreach (var paramSet in parameterSets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Clear and apply new parameters
            context.Parameters.Clear();
            foreach (var (key, value) in paramSet)
            {
                var paramName = WitSqlParameterKeys.ToContextKey(key);
                context.Parameters[paramName] = WitSqlValue.FromObject(value);
            }

            // Execute with the same context (reuses StatementExecutor)
            using var result = ExecuteInternal(context, cancellationToken);
            totalRowsAffected += result.RowsAffected;
        }

        return totalRowsAffected;
    }

    /// <summary>
    /// Execute the prepared statement multiple times with parameter sets from anonymous objects or POCOs.
    /// Uses reflection to extract parameter values from object properties.
    /// </summary>
    /// <typeparam name="T">Type of parameter objects. Must not be a dictionary type.</typeparam>
    /// <param name="parameterObjects">Collection of objects with properties matching parameter names.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>Total number of rows affected across all executions.</returns>
    /// <example>
    /// <code>
    /// using var stmt = engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@Name, @Email)");
    /// 
    /// var users = new[]
    /// {
    ///     new { Name = "Alice", Email = "alice@test.com" },
    ///     new { Name = "Bob", Email = "bob@test.com" }
    /// };
    /// 
    /// int rowsAffected = stmt.ExecuteBatch(users);
    /// </code>
    /// </example>
    public int ExecuteBatch<T>(IEnumerable<T> parameterObjects, CancellationToken cancellationToken = default) where T : class
    {
        ThrowIfDisposed();
        
        // Check if T is a dictionary type - if so, redirect to the dictionary overload
        if (typeof(IDictionary<string, object?>).IsAssignableFrom(typeof(T)))
        {
            return ExecuteBatch(parameterObjects.Cast<IDictionary<string, object?>>(), cancellationToken);
        }
        
        // Get properties once for the type
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanRead && !IsIndexerProperty(p))
            .ToArray();

        int totalRowsAffected = 0;
        var context = CreateExecutionContext(cancellationToken);

        foreach (var obj in parameterObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Clear and apply parameters from object properties
            context.Parameters.Clear();
            foreach (var prop in properties)
            {
                var paramName = $"@{prop.Name}";
                var value = prop.GetValue(obj);
                context.Parameters[paramName] = WitSqlValue.FromObject(value);
            }

            // Execute
            using var result = ExecuteInternal(context, cancellationToken);
            totalRowsAffected += result.RowsAffected;
        }

        return totalRowsAffected;
    }

    /// <summary>
    /// Checks if a property is an indexer (like Item in dictionaries).
    /// </summary>
    private static bool IsIndexerProperty(System.Reflection.PropertyInfo prop)
    {
        return prop.GetIndexParameters().Length > 0;
    }

    #endregion

    #region Internal

    private ContextExecution CreateExecutionContext(CancellationToken cancellationToken)
    {
        return new ContextExecution
        {
            Database = m_database,
            CancellationToken = cancellationToken
        };
    }

    private void ApplyParameters(ContextExecution context)
    {
        foreach (var (key, value) in m_parameters)
        {
            context.Parameters[key] = WitSqlValue.FromObject(value);
        }
    }

    private WitSqlResult ExecuteInternal(ContextExecution context, CancellationToken cancellationToken)
    {
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the prepared statement and clears parameters.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        m_parameters.Clear();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the original SQL text.
    /// </summary>
    public string Sql => m_sql;

    /// <summary>
    /// Gets the number of parsed statements.
    /// </summary>
    public int StatementCount => m_statements.Count;

    #endregion
}
