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
public sealed partial class WitSqlEngine : IDatabase, IDisposable, IAsyncDisposable, ITransactionManager
{
    #region Fields

    private readonly WitDatabase m_database;
    private readonly SchemaCatalog m_schema;
    private readonly QueryPlanCache m_planCache;
    private readonly bool m_ownsStore;

    /// <summary>
    /// When true this session refuses any statement that could write. Set at construction and never
    /// changed, so a caller cannot promote a read-only connection to a writing one.
    /// </summary>
    private readonly bool m_readOnly;
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
        : this(database, new SchemaCatalog(database.Store), ownsStore)
    {
    }

    /// <summary>
    /// Creates a new WitSqlEngine instance over a schema catalog it shares with other engines.
    /// </summary>
    /// <param name="database">The underlying WitDatabase instance.</param>
    /// <param name="schema">
    /// The catalog to use. Pass the <b>same instance</b> to every engine over a given database.
    /// </param>
    /// <param name="ownsStore">If true, the engine will dispose the database when disposed.</param>
    /// <remarks>
    /// An engine is a <b>session</b> - it holds the current transaction - while the schema is a property
    /// of the <b>database</b>. Until 5.0.0 the two were fused: every engine built its own
    /// <see cref="SchemaCatalog"/>, which loads the schema once in its constructor into plain
    /// dictionaries of tables, indexes, views, triggers, sequences, row ids and row counts. Two sessions
    /// over one database therefore diverged, and measurably so - a table created by one was
    /// <c>Table not found</c> to the other, and a row inserted by one was visible to the other's scan
    /// while that other's <c>COUNT(*)</c> still said zero.
    ///
    /// Several connections addressing one database is the supported deployment shape - an ASP.NET Core
    /// host with scoped <c>DbContext</c>s - so this constructor exists for the caller that owns the
    /// database to hand one catalog to every session on it. The engine does not dispose a catalog it was
    /// given.
    /// </remarks>
    public WitSqlEngine(WitDatabase database, SchemaCatalog schema, bool ownsStore = false)
        : this(database, schema, ownsStore, readOnly: false)
    {
    }

    /// <summary>
    /// Creates a new WitSqlEngine instance, optionally as a read-only session.
    /// </summary>
    /// <param name="database">The underlying WitDatabase instance.</param>
    /// <param name="schema">The catalog to use; pass the same instance to every engine over a database.</param>
    /// <param name="ownsStore">If true, the engine will dispose the database when disposed.</param>
    /// <param name="readOnly">
    /// If true, this session refuses every statement that could change data or schema.
    /// </param>
    /// <remarks>
    /// Read-only is a property of the <b>session</b>, not of the storage, because several connections
    /// share one database and one of them being read-only must not stop the others writing. So a
    /// read-only session is a restriction on what may be executed through it, which is also the
    /// semantics a consumer expects from <c>Read Only=true</c> on a connection string.
    ///
    /// Opening the <i>storage</i> read-only - for genuinely read-only media - is a different feature and
    /// is not this flag.
    /// </remarks>
    public WitSqlEngine(WitDatabase database, SchemaCatalog schema, bool ownsStore, bool readOnly)
    {
        m_database = database;
        m_schema = schema ?? throw new ArgumentNullException(nameof(schema));
        m_planCache = new QueryPlanCache();
        m_ownsStore = ownsStore;
        m_readOnly = readOnly;

        // Ensure physical indexes are created/synced for all schema indexes
        // This handles the case where schema indexes were persisted but physical indexes were not
        EnsurePhysicalIndexesExist();
    }

    #endregion
    
    #region Index Synchronization

    /// <summary>
    /// Makes sure every index the catalogue names can actually answer for the rows in its table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This used to check whether an index EXISTED, and that is a question nothing can fail.</b>
    /// <c>WitDatabase.RestoreIndexesFromMetadata</c> runs first and calls <c>CreateIndex</c> for
    /// every index the metadata names, which MAKES the physical index - so by the time this method
    /// looked, one was always there, whatever had happened to its content. The
    /// <c>physicalIndex == null</c> branch was unreachable for any index in the metadata, which is
    /// all of them, and the summary's promise that "rebuilding happens lazily when the index is
    /// first accessed" was describing code that does not exist.
    /// </para>
    /// <para>
    /// What that cost: copy a <c>.witdb</c> without its <c>_indexes</c> directory - which is what
    /// copying a database means to everyone outside Studio - and the catalogue still names the
    /// index, the planner still uses it, the index holds nothing, and the query answers zero rows
    /// with no error. Measured 2026-08-14, encrypted and plain alike. Deleting the index file with
    /// the directory left in place does the same.
    /// </para>
    /// <para>
    /// <b>The rule is not "rebuild anything empty".</b> <c>FillIndexFromExistingData</c> skips rows
    /// whose indexed columns are NULL and rows outside a partial index's condition, so an index over
    /// an all-NULL column is legitimately empty; rebuilding on emptiness would rescan the whole
    /// table on every open, for ever. The rule is <see cref="ISecondaryIndex.ContentWasFound"/> -
    /// was there anything to load - which the store below knows for exactly one moment and now
    /// keeps.
    /// </para>
    /// <para>
    /// <c>BuildIndexFromExistingData</c> still returns early when the index holds entries, so an
    /// index that was created empty and then filled by something else is not rebuilt twice. The
    /// case it does NOT cover is an index that was created empty, partly filled, and left - that is
    /// <c>KnownIssues</c> 14's open remainder and needs the index to record what it was built
    /// against.
    /// </para>
    /// </remarks>
    private void EnsurePhysicalIndexesExist()
    {
        if (!m_database.SupportsIndexes)
            return;

        foreach (var indexDef in m_schema.GetIndexes())
        {
            var physicalIndex = m_database.GetIndex(indexDef.Name);

            if (physicalIndex == null)
            {
                m_database.CreateIndex(indexDef.Name, indexDef.IsUnique);
                BuildIndexFromExistingData(indexDef);
                continue;
            }

            // There is an index object either way; the question is whether anything was behind it.
            if (!physicalIndex.ContentWasFound)
                BuildIndexFromExistingData(indexDef);
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

    /// <summary>
    /// Refuses any statement on a read-only session that is not known to be incapable of writing.
    /// </summary>
    /// <remarks>
    /// <b>Fail closed.</b> The list below is what a read-only session <i>allows</i>, and everything else
    /// is refused - so a statement kind added to the grammar tomorrow is refused until somebody decides
    /// it is safe, rather than allowed until somebody notices it is not. A read-only guarantee that
    /// enumerated the writing statements instead would quietly weaken every time the language grew.
    ///
    /// Transaction control is allowed: it changes no data by itself, and refusing it would break
    /// ordinary consumer code - EF Core opens a transaction around <c>SaveChanges</c> and ADO.NET
    /// callers wrap reads in one routinely. A transaction that then tries to write is refused on the
    /// writing statement, which is where the error belongs.
    /// </remarks>
    /// <summary>
    /// Refuses a write that does not arrive as a SQL statement, so it cannot slip past
    /// <see cref="EnsureStatementsAreReadOnly"/>.
    /// </summary>
    /// <remarks>
    /// The bulk API writes directly and never parses anything, so guarding <c>Execute</c> alone would
    /// have left <c>BulkInsert</c>, <c>BulkUpdate</c> and <c>BulkDelete</c> as five ways through a
    /// read-only connection. Any future write path that bypasses statement execution needs this call
    /// too - which is the argument for it being a named method rather than an inline check.
    /// </remarks>
    internal void EnsureNotReadOnly(string operation)
    {
        if (!m_readOnly)
            return;

        throw new InvalidOperationException(
            $"This connection is read-only, so {operation} is not allowed on it. Open a connection "
            + "without 'Read Only=true' (or without 'Mode=ReadOnly') to modify data or schema.");
    }

    private static void EnsureStatementsAreReadOnly(IReadOnlyList<Parser.Statements.WitSqlStatement> statements)
    {
        foreach (var statement in statements)
        {
            var allowed = statement is
                Parser.Statements.WitSqlStatementSelect or
                Parser.Statements.WitSqlStatementExplain or
                Parser.Statements.WitSqlStatementBeginTransaction or
                Parser.Statements.WitSqlStatementCommit or
                Parser.Statements.WitSqlStatementRollback or
                Parser.Statements.WitSqlStatementSavepoint or
                Parser.Statements.WitSqlStatementReleaseSavepoint or
                Parser.Statements.WitSqlStatementSetTransaction;

            if (allowed)
                continue;

            var kind = statement.GetType().Name;

            if (kind.StartsWith("WitSqlStatement", StringComparison.Ordinal))
                kind = kind["WitSqlStatement".Length..].ToUpperInvariant();

            throw new InvalidOperationException(
                $"This connection is read-only, so {kind} is not allowed on it. Open a connection "
                + "without 'Read Only=true' (or without 'Mode=ReadOnly') to modify data or schema.");
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

        if (m_readOnly)
            EnsureStatementsAreReadOnly(statements);

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

    /// <remarks>
    /// <c>CALL</c> is here because a procedure body is a list of statements and any of them may
    /// write. Left out, each body statement opened and committed its own transaction, so a body that
    /// failed on its third statement kept the first two - measured, and exactly the class
    /// <see cref="ExecuteAtomically"/> exists to close. A call is one statement to the caller, so it
    /// has to be one unit of work.
    /// </remarks>
    private static bool ModifiesData(Parser.Statements.WitSqlStatement statement) =>
        statement is Parser.Statements.WitSqlStatementInsert
            or Parser.Statements.WitSqlStatementUpdate
            or Parser.Statements.WitSqlStatementDelete
            or Parser.Statements.WitSqlStatementMerge
            or Parser.Statements.WitSqlStatementCall;

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

    /// <summary>
    /// Closes the engine, and the database under it, without a synchronous storage call.
    /// </summary>
    /// <remarks>
    /// The last link of the asynchronous close chain and the only one a consumer holds: everything
    /// below - the transactional store, the MVCC store, the B+Tree store, its page manager and its page
    /// cache - now closes asynchronously, and an engine that offered nothing but <see cref="Dispose"/>
    /// meant none of it could be reached. It matters for a storage that has no synchronous operations
    /// at all, which is what <c>OutWit.Database.Core.IndexedDb</c> is.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        m_currentTransaction?.Dispose();

        if (!m_ownsStore)
            return;

        try
        {
            await m_database.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort - don't fail dispose on flush errors, exactly as the synchronous close.
        }

        await m_database.DisposeAsync().ConfigureAwait(false);
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

    /// <inheritdoc/>
    public Core.Interfaces.WitIsolationLevel? PendingIsolationLevel { get; set; }

    #endregion
}
