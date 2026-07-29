using OutWit.Database.Context;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser;
using OutWit.Database.Query;
using OutWit.Database.Schema;
using OutWit.Database.Sql;
using OutWit.Database.Statements;
using OutWit.Database.Values;

namespace OutWit.Database.Engine;

/// <summary>
/// The main SQL execution engine for WitDatabase.
/// Provides query execution, DDL/DML operations, and transaction management.
/// </summary>
public sealed partial class WitSqlEngine : IDatabase, IDisposable, ITransactionManager
{
    #region Fields

    private readonly WitDatabase m_database;
    private readonly SchemaCatalog m_schema;
    private readonly QueryPlanCache m_planCache;
    private readonly bool m_ownsStore;
    private ITransaction? m_currentTransaction;

    /// <summary>
    /// Default query timeout. Null means no timeout.
    /// </summary>
    private TimeSpan? m_defaultQueryTimeout;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new WitSqlEngine instance.
    /// </summary>
    /// <param name="database">The underlying WitDatabase instance.</param>
    /// <param name="ownsStore">If true, the engine will dispose the database when disposed.</param>
    public WitSqlEngine(WitDatabase database, bool ownsStore = false)
    {
        m_database = database;
        m_schema = new SchemaCatalog(database.Store);
        m_planCache = new QueryPlanCache();
        m_ownsStore = ownsStore;
        
        // Ensure physical indexes are created/synced for all schema indexes
        // This handles the case where schema indexes were persisted but physical indexes were not
        EnsurePhysicalIndexesExist();
    }

    #endregion
    
    #region Index Synchronization

    /// <summary>
    /// Ensures physical indexes exist for all schema indexes.
    /// Creates missing physical indexes but does NOT rebuild existing ones.
    /// Rebuilding happens lazily when the index is first accessed.
    /// </summary>
    private void EnsurePhysicalIndexesExist()
    {
        if (!m_database.SupportsIndexes)
            return;

        foreach (var indexDef in m_schema.GetIndexes())
        {
            // Check if physical index exists
            var physicalIndex = m_database.GetIndex(indexDef.Name);
            if (physicalIndex == null)
            {
                // Physical index doesn't exist - create it and build from data
                m_database.CreateIndex(indexDef.Name, indexDef.IsUnique);
                BuildIndexFromExistingData(indexDef);
            }
            // If physical index exists (even if empty), don't rebuild
            // The data should be persisted in the index files
        }
    }

    #endregion

    #region Execute

    /// <summary>
    /// Execute a SQL query and return the result.
    /// </summary>
    /// <param name="sql">SQL query text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>The query result.</returns>
    public WitSqlResult Execute(string sql,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        return Execute(sql, parameters, timeout: null, cancellationToken);
    }

    /// <summary>
    /// Execute a SQL query with a timeout and return the result.
    /// </summary>
    /// <param name="sql">SQL query text.</param>
    /// <param name="parameters">Query parameters (optional).</param>
    /// <param name="timeout">Query timeout. Null uses default timeout.</param>
    /// <param name="cancellationToken">Cancellation token (optional).</param>
    /// <returns>The query result.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the query times out.</exception>
    public WitSqlResult Execute(string sql,
        IDictionary<string, object?>? parameters,
        TimeSpan? timeout,
        CancellationToken cancellationToken = default)
    {
        // Determine effective timeout
        var effectiveTimeout = timeout ?? m_defaultQueryTimeout;

        // Create a combined cancellation token if timeout is specified
        CancellationToken effectiveToken;
        CancellationTokenSource? timeoutCts = null;

        if (effectiveTimeout.HasValue && effectiveTimeout.Value > TimeSpan.Zero)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(effectiveTimeout.Value);
            effectiveToken = timeoutCts.Token;
        }
        else
        {
            effectiveToken = cancellationToken;
        }

        try
        {
            return ExecuteInternal(sql, parameters, effectiveToken);
        }
        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
        {
            // Convert timeout cancellation to a more specific exception message
            throw new TimeoutException($"Query execution exceeded the timeout of {effectiveTimeout}");
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    private WitSqlResult ExecuteInternal(string sql,
        IDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        // Try to get cached plan
        IReadOnlyList<Parser.Statements.WitSqlStatement> statements;
        if (m_planCache.TryGet(sql, out var cachedEntry) && cachedEntry != null)
        {
            // Use cached parsed statement
            statements = [cachedEntry.Statement];
        }
        else
        {
            // Parse and cache
            statements = WitSql.Parse(sql);
            if (statements.Count == 0)
                throw new InvalidOperationException("No SQL statement found");

            // Cache single statements (multi-statement SQL is rare and not worth caching)
            if (statements.Count == 1)
            {
                m_planCache.Add(sql, statements[0]);
            }
        }

        var context = new ContextExecution
        {
            Database = this,
            CancellationToken = cancellationToken,
            LastInsertRowId = LastInsertRowId,
            LastChangesCount = LastChangesCount
        };

        // Add parameters
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
            {
                var paramName = WitSqlParameterKeys.ToContextKey(key);
                context.Parameters[paramName] = WitSqlValue.FromObject(value);
            }
        }

        var executor = new StatementExecutor(context);

        // Execute all statements, return result of last one
        WitSqlResult? result = null;
        foreach (var statement in statements)
        {
            result?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            result = ExecuteAtomically(executor, statement);
        }

        // Persist state for next call
        LastInsertRowId = context.LastInsertRowId;
        LastChangesCount = context.LastChangesCount;

        return result!;
    }

    /// <summary>
    /// Runs one statement so that it either happens completely or not at all.
    /// </summary>
    /// <remarks>
    /// A multi-row DML that failed part-way used to leave the rows it had already written: an INSERT
    /// that threw on the third row kept the first two, and an UPDATE that threw on a later row left
    /// an earlier one changed. Both were confirmed by the 2026-07 audit and are closed here.
    ///
    /// <b>Why not validate every row first.</b> That was the obvious fix and it is wrong:
    /// intra-statement uniqueness depends on the earlier rows already being present, so a
    /// pre-validating INSERT would happily accept two rows with the same key. A statement is a unit
    /// of work, and the mechanism for a unit of work already exists.
    ///
    /// <b>What else this closes.</b> Autocommit opened no transaction at all, which meant nothing on
    /// that path was ever committed or flushed - the crash runner's C3 control showed a killed
    /// process losing every autocommit row <i>and the table it created</i>, because none of it had
    /// reached the file. A statement-scoped transaction commits, and committing flushes.
    ///
    /// <b>The cost, stated rather than buried.</b> Every autocommit write now pays a commit, and a
    /// commit flushes. That is the price of the D in ACID on this path and it is what PostgreSQL,
    /// SQL Server and SQLite all charge; phase 5 measures it rather than guessing.
    ///
    /// Only data-modifying statements are wrapped. A SELECT needs no transaction, and the
    /// transaction-control statements are how a caller opens one explicitly - wrapping those would
    /// mean BEGIN opening a transaction inside a transaction.
    /// </remarks>
    private WitSqlResult ExecuteAtomically(StatementExecutor executor, Parser.Statements.WitSqlStatement statement)
    {
        // A database built without transactions cannot be given one, and asking would throw. That
        // configuration has traded atomicity away deliberately - `Transactions=false` in the
        // connection string, or WithoutTransactions() - so the statement runs as it always did.
        if (m_currentTransaction != null || !m_database.SupportsTransactions || !ModifiesData(statement))
            return executor.Execute(statement);

        // The handle rolls back if the commit below never runs, which is what makes a statement that
        // throws part-way leave nothing behind.
        using var transaction = BeginTransaction();

        var result = executor.Execute(statement);

        Commit();

        return result;
    }

    private static bool ModifiesData(Parser.Statements.WitSqlStatement statement) =>
        statement is Parser.Statements.WitSqlStatementInsert
            or Parser.Statements.WitSqlStatementUpdate
            or Parser.Statements.WitSqlStatementDelete
            or Parser.Statements.WitSqlStatementMerge;

    #endregion

    #region Query Timeout

    /// <summary>
    /// Gets or sets the default query timeout.
    /// Null means no timeout (queries can run indefinitely).
    /// </summary>
    public TimeSpan? DefaultQueryTimeout
    {
        get => m_defaultQueryTimeout;
        set => m_defaultQueryTimeout = value;
    }

    #endregion

    #region Schema Information

    /// <summary>
    /// Gets the schema catalog for accessing database metadata.
    /// </summary>
    public SchemaCatalog Catalog => m_schema;

    /// <summary>
    /// Gets the query plan cache for statistics and management.
    /// </summary>
    public QueryPlanCache PlanCache => m_planCache;

    /// <summary>
    /// Gets all table names in the database.
    /// </summary>
    public IEnumerable<string> GetAllTableNames()
    {
        return m_schema.TableNames;
    }

    #endregion

    #region Cache Invalidation

    /// <summary>
    /// Invalidates the query plan cache.
    /// Called automatically after DDL operations.
    /// </summary>
    internal void InvalidatePlanCache()
    {
        m_planCache.Invalidate();
    }

    /// <summary>
    /// Invalidates query plans for a specific table.
    /// Called after DDL operations on that table.
    /// </summary>
    internal void InvalidatePlanCacheForTable(string tableName)
    {
        m_planCache.InvalidateTable(tableName);
    }

    #endregion

    #region Flush

    /// <summary>
    /// Flushes any pending writes to durable storage.
    /// Call this to ensure all data is persisted.
    /// </summary>
    public void Flush()
    {
        m_database.Flush();
    }

    /// <summary>
    /// Flushes any pending writes asynchronously.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        await m_database.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the engine and optionally the underlying database.
    /// </summary>
    public void Dispose()
    {
        m_currentTransaction?.Dispose();

        if (m_ownsStore)
        {
            // Flush before dispose to ensure all data is persisted
            try
            {
                m_database.Flush();
            }
            catch
            {
                // Best effort - don't fail dispose on flush errors
            }
            
            m_database.Dispose();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Row ID of last inserted row (for LAST_INSERT_ROWID function).
    /// </summary>
    public long LastInsertRowId { get; private set; }

    /// <summary>
    /// Number of rows affected by last INSERT/UPDATE/DELETE (for CHANGES function).
    /// </summary>
    public long LastChangesCount { get; private set; }

    /// <summary>
    /// Gets the current active transaction, if any.
    /// </summary>
    public ITransaction? CurrentTransaction => m_currentTransaction;

    #endregion
}
