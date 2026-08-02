using System.Data;
using System.Data.Common;
using OutWit.Database.Engine;
using OutWit.Database.Sql;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Represents a SQL command to be executed against a WitDatabase database.
/// </summary>
public sealed class WitDbCommand : DbCommand
{
    #region Constants

    private const int DEFAULT_COMMAND_TIMEOUT = 30;

    #endregion

    #region Fields

    private string m_commandText = string.Empty;
    private int m_commandTimeout = DEFAULT_COMMAND_TIMEOUT;
    private CommandType m_commandType = CommandType.Text;
    private WitDbConnection? m_connection;
    private WitDbTransaction? m_transaction;
    private WitDbParameterCollection m_parameters;
    private bool m_designTimeVisible = true;
    private UpdateRowSource m_updatedRowSource = UpdateRowSource.None;
    
    // Prepared statement caching
    private WitSqlEngineStatement? m_preparedStatement;
    private string? m_preparedCommandText;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbCommand"/> class.
    /// </summary>
    public WitDbCommand()
    {
        m_parameters = new WitDbParameterCollection();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbCommand"/> class
    /// with the specified command text.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    public WitDbCommand(string commandText)
        : this()
    {
        m_commandText = commandText;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbCommand"/> class
    /// with the specified command text and connection.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    /// <param name="connection">The connection to use.</param>
    public WitDbCommand(string commandText, WitDbConnection connection)
        : this(commandText)
    {
        m_connection = connection;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WitDbCommand"/> class
    /// with the specified command text, connection, and transaction.
    /// </summary>
    /// <param name="commandText">The SQL command text.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="transaction">The transaction to use.</param>
    public WitDbCommand(string commandText, WitDbConnection connection, WitDbTransaction? transaction)
        : this(commandText, connection)
    {
        m_transaction = transaction;
    }

    #endregion

    #region Execute

    /// <inheritdoc/>
    public override int ExecuteNonQuery()
    {
        EnsureConnectionOpen();

        using var result = ExecuteInternal();
        return result.RowsAffected;
    }

    /// <inheritdoc/>
    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        EnsureConnectionOpen();

        return await Task.Run(() =>
        {
            using var result = ExecuteInternal(cancellationToken);
            return result.RowsAffected;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override object? ExecuteScalar()
    {
        EnsureConnectionOpen();

        using var result = ExecuteInternal();
        if (!result.Read() || result.Columns.Count == 0)
            return null;

        var value = result.CurrentRow[0];
        return value.IsNull ? null : value.ToObject();
    }

    /// <inheritdoc/>
    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(ExecuteScalar, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        EnsureConnectionOpen();

        var result = ExecuteInternal();

        var reader = new WitDbDataReader(result, m_connection!, behavior);
        m_connection!.RegisterReader(reader);

        return reader;
    }

    /// <inheritdoc/>
    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        return await Task.Run(() => ExecuteDbDataReader(behavior), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command and returns a data reader.
    /// </summary>
    /// <returns>A <see cref="WitDbDataReader"/> for reading the results.</returns>
    public new WitDbDataReader ExecuteReader()
    {
        return (WitDbDataReader)ExecuteDbDataReader(CommandBehavior.Default);
    }

    /// <summary>
    /// Executes the command and returns a data reader with the specified behavior.
    /// </summary>
    /// <param name="behavior">The command behavior.</param>
    /// <returns>A <see cref="WitDbDataReader"/> for reading the results.</returns>
    public new WitDbDataReader ExecuteReader(CommandBehavior behavior)
    {
        return (WitDbDataReader)ExecuteDbDataReader(behavior);
    }

    /// <summary>
    /// Executes the command asynchronously and returns a data reader.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that returns a <see cref="WitDbDataReader"/>.</returns>
    public new async Task<WitDbDataReader> ExecuteReaderAsync(CancellationToken cancellationToken = default)
    {
        return (WitDbDataReader)await ExecuteDbDataReaderAsync(CommandBehavior.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the command asynchronously and returns a data reader with the specified behavior.
    /// </summary>
    /// <param name="behavior">The command behavior.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that returns a <see cref="WitDbDataReader"/>.</returns>
    public new async Task<WitDbDataReader> ExecuteReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        return (WitDbDataReader)await ExecuteDbDataReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The single point every execution path goes through, and therefore the single place where an
    /// engine failure becomes a <see cref="DbException"/>.
    /// </summary>
    /// <remarks>
    /// <b>Only what comes out of the engine is wrapped.</b> The guards this provider raises for API
    /// misuse - no connection, no command text, a transaction already in progress - stay
    /// <see cref="InvalidOperationException"/>, which is what ADO.NET means by them and what SqlClient
    /// raises too. What must be a <c>DbException</c> is a DATABASE failure: a missing table, a
    /// constraint violation, a syntax error. Every framework that handles database failures generically
    /// - EF Core execution strategies, Polly, ASP.NET diagnostics - keys off <c>DbException</c> and saw
    /// none of them.
    ///
    /// <see cref="OperationCanceledException"/> is left alone: a cancelled command is the caller's
    /// doing, not the database's, and callers catch it by its own type.
    /// </remarks>
    private WitSqlResult ExecuteInternal(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(m_commandText))
            throw new InvalidOperationException("CommandText is not set.");

        var engine = m_connection!.Engine!;
        var parameters = BuildParametersDictionary();
        var sql = m_commandType == CommandType.StoredProcedure ? BuildCallStatement() : m_commandText;

        try
        {
            // If we have a valid prepared statement for this exact command text, use it.
            // A stored-procedure command is never prepared: what is prepared is SQL, and the SQL
            // here is built from the parameter collection, which the caller may change between
            // executions.
            if (m_commandType == CommandType.Text
                && m_preparedStatement != null
                && m_preparedCommandText == m_commandText)
            {
                return m_preparedStatement.Execute(parameters, cancellationToken);
            }

            // Execute without prepared statement (uses engine's internal query plan cache)
            var timeout = m_commandTimeout > 0 ? TimeSpan.FromSeconds(m_commandTimeout) : (TimeSpan?)null;
            return engine.Execute(sql, parameters, timeout, cancellationToken);
        }
        catch (Exception e) when (e is not DbException && e is not OperationCanceledException)
        {
            throw WitDbException.FromException(e);
        }
    }

    /// <summary>
    /// Turns a <c>StoredProcedure</c> command into the <c>CALL</c> the engine runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The command text is the routine name and the parameter collection is the argument list, which
    /// is what every ADO.NET caller expects. The arguments are written as the parameters' own names
    /// rather than as values, so nothing is interpolated into SQL - the engine binds them from the
    /// same dictionary a text command uses, and a string argument cannot become syntax.
    /// </para>
    /// <para>
    /// <b>Order is the collection's order.</b> ADO parameters are named, a <c>CALL</c>'s are
    /// positional, and there is no third thing to consult: the routine's own parameter order lives
    /// in the catalog and matching by name against it would silently reorder a caller's arguments
    /// when the names differ. The collection order is what the caller wrote, and a mismatch is
    /// refused by the engine with the routine's arity rather than papered over here.
    /// </para>
    /// </remarks>
    private string BuildCallStatement()
    {
        var arguments = new string[m_parameters.Count];

        for (var i = 0; i < m_parameters.Count; i++)
        {
            var name = ((WitDbParameter)m_parameters[i]!).ParameterName;

            arguments[i] = name.StartsWith('@') || name.StartsWith(':') || name.StartsWith('$')
                ? name
                : "@" + name;
        }

        return $"CALL {m_commandText.Trim()}({string.Join(", ", arguments)})";
    }

    private Dictionary<string, object?> BuildParametersDictionary()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (WitDbParameter param in m_parameters)
        {
            var name = param.ParameterName;
            
            // Normalize parameter name - add @ prefix if not present
            if (!name.StartsWith("@") && !name.StartsWith(":") && !name.StartsWith("$"))
            {
                name = "@" + name;
            }

            dict[name] = param.Value;
        }

        return dict;
    }

    #endregion

    #region Prepare

    /// <summary>
    /// Prepares the SQL statement for execution.
    /// After calling Prepare(), subsequent Execute calls will use the cached prepared statement,
    /// avoiding SQL parsing overhead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prepared statements provide significant performance benefits for repeated executions:
    /// - SQL is parsed only once during Prepare()
    /// - Subsequent executions reuse the parsed statement
    /// - Parameters are bound without re-parsing the SQL text
    /// </para>
    /// <para>
    /// The prepared statement is invalidated if CommandText changes.
    /// </para>
    /// </remarks>
    public override void Prepare()
    {
        EnsureConnectionOpen();

        if (string.IsNullOrWhiteSpace(m_commandText))
            throw new InvalidOperationException("CommandText is not set.");

        // If already prepared with same command text, no need to re-prepare
        if (m_preparedStatement != null && m_preparedCommandText == m_commandText)
            return;

        // Dispose old prepared statement if exists
        m_preparedStatement?.Dispose();
        
        // Create new prepared statement
        m_preparedStatement = m_connection!.Engine!.Prepare(m_commandText);
        m_preparedCommandText = m_commandText;
    }

    /// <summary>
    /// Prepares the command asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public override async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(Prepare, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets whether the command has been prepared.
    /// </summary>
    public bool IsPrepared => m_preparedStatement != null && m_preparedCommandText == m_commandText;

    /// <summary>
    /// Unprepares the command, disposing the cached prepared statement.
    /// </summary>
    public void Unprepare()
    {
        m_preparedStatement?.Dispose();
        m_preparedStatement = null;
        m_preparedCommandText = null;
    }

    #endregion

    #region Cancel

    /// <inheritdoc/>
    public override void Cancel()
    {
        // WitDatabase doesn't support cancellation of in-progress commands
        // (other than via CancellationToken passed to async methods)
    }

    #endregion

    #region Parameters

    /// <inheritdoc/>
    protected override DbParameter CreateDbParameter()
    {
        return new WitDbParameter();
    }

    /// <summary>
    /// Creates a new parameter.
    /// </summary>
    /// <returns>A new <see cref="WitDbParameter"/>.</returns>
    public new WitDbParameter CreateParameter()
    {
        return new WitDbParameter();
    }

    /// <inheritdoc/>
    protected override DbParameterCollection DbParameterCollection => m_parameters;

    /// <summary>
    /// Gets the parameter collection.
    /// </summary>
    public new WitDbParameterCollection Parameters => m_parameters;

    #endregion

    #region Helpers

    private void EnsureConnectionOpen()
    {
        if (m_connection == null)
            throw new InvalidOperationException("Connection is not set.");

        if (m_connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");
    }

    /// <summary>
    /// Invalidates the prepared statement when command text changes.
    /// </summary>
    private void InvalidatePreparedStatement()
    {
        if (m_preparedStatement != null && m_preparedCommandText != m_commandText)
        {
            m_preparedStatement.Dispose();
            m_preparedStatement = null;
            m_preparedCommandText = null;
        }
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            m_preparedStatement?.Dispose();
            m_preparedStatement = null;
            m_preparedCommandText = null;
            m_parameters.Clear();
        }
        base.Dispose(disposing);
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override string CommandText
    {
        get => m_commandText;
        set
        {
            var newText = value ?? string.Empty;
            if (m_commandText != newText)
            {
                m_commandText = newText;
                // Invalidate prepared statement when command text changes
                InvalidatePreparedStatement();
            }
        }
    }

    /// <inheritdoc/>
    public override int CommandTimeout
    {
        get => m_commandTimeout;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "CommandTimeout must be non-negative.");
            m_commandTimeout = value;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// <c>Text</c> and <c>StoredProcedure</c>. The second is how every ADO.NET caller invokes a
    /// procedure - set <see cref="DbCommand.CommandText"/> to the routine name, add the parameters,
    /// and read the result - and until phase 9d it threw, which meant a procedure could exist in
    /// this database and not be reachable from ordinary consumer code at all.
    /// </para>
    /// <para>
    /// <c>TableDirect</c> stays refused: it means "the CommandText is a table name, return all of
    /// it", which is a shape this provider has no translation for and which almost nothing uses.
    /// Refusing it is better than answering something approximate.
    /// </para>
    /// </remarks>
    public override CommandType CommandType
    {
        get => m_commandType;
        set
        {
            if (value is not (CommandType.Text or CommandType.StoredProcedure))
            {
                throw new NotSupportedException(
                    $"CommandType.{value} is not supported. Use Text, or StoredProcedure with the "
                    + "routine name as the CommandText.");
            }

            m_commandType = value;
        }
    }

    /// <inheritdoc/>
    protected override DbConnection? DbConnection
    {
        get => m_connection;
        set => m_connection = (WitDbConnection?)value;
    }

    /// <summary>
    /// Gets or sets the connection for this command.
    /// </summary>
    public new WitDbConnection? Connection
    {
        get => m_connection;
        set => m_connection = value;
    }

    /// <inheritdoc/>
    protected override DbTransaction? DbTransaction
    {
        get => m_transaction;
        set => m_transaction = (WitDbTransaction?)value;
    }

    /// <summary>
    /// Gets or sets the transaction for this command.
    /// </summary>
    public new WitDbTransaction? Transaction
    {
        get => m_transaction;
        set => m_transaction = value;
    }

    /// <inheritdoc/>
    public override bool DesignTimeVisible
    {
        get => m_designTimeVisible;
        set => m_designTimeVisible = value;
    }

    /// <inheritdoc/>
    public override UpdateRowSource UpdatedRowSource
    {
        get => m_updatedRowSource;
        set => m_updatedRowSource = value;
    }

    #endregion
}
