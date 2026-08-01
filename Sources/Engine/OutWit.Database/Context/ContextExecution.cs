using OutWit.Database.Core.Interfaces;
using OutWit.Database.Interfaces;
using OutWit.Database.Model;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Sql;
using OutWit.Database.Values;

namespace OutWit.Database.Context;

/// <summary>
/// Execution context passed down through iterator tree.
/// </summary>
public sealed class ContextExecution
{
    /// <summary>
    /// Gets the database interface for data access operations.
    /// </summary>
    public required IDatabase Database { get; init; }

    /// <summary>
    /// Gets the parameter values for the current query.
    /// </summary>
    public Dictionary<string, WitSqlValue> Parameters { get; } = new();

    /// <summary>
    /// Gets the cancellation token for the current operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Gets or sets the current row for correlated subqueries.
    /// </summary>
    public WitSqlRow? OuterRow { get; set; }

    /// <summary>
    /// Gets the per-row state dictionary for expression evaluation.
    /// </summary>
    public Dictionary<string, object> State { get; } = new();

    /// <summary>
    /// Gets or sets the number of rows affected by last INSERT/UPDATE/DELETE.
    /// </summary>
    public long LastChangesCount { get; set; }

    /// <summary>
    /// Gets or sets the row ID of last inserted row.
    /// </summary>
    public long LastInsertRowId { get; set; }

    /// <summary>
    /// Gets or sets the active trigger context containing OLD/NEW rows.
    /// Null when not executing inside a trigger.
    /// </summary>
    public ContextTrigger? TriggerContext { get; set; }

    /// <summary>
    /// Gets or sets the pending isolation level for the next transaction.
    /// Set by SET TRANSACTION ISOLATION LEVEL and consumed by BEGIN TRANSACTION.
    /// </summary>
    public WitIsolationLevel? PendingIsolationLevel { get; set; }

    /// <summary>
    /// Gets the CTE (Common Table Expression) definitions for the current query.
    /// Maps CTE name to its definition (including column names and query).
    /// </summary>
    public Dictionary<string, ClauseCteDefinition> CteDefinitions { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the cache for CTE results.
    /// When a CTE is executed, its results are cached here for reuse.
    /// Maps CTE name to cached rows and schema.
    /// </summary>
    public Dictionary<string, CteCacheEntry> CteCache { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the EXCLUDED pseudo-row for ON CONFLICT DO UPDATE.
    /// Contains the values that would have been inserted if there was no conflict.
    /// </summary>
    public WitSqlRow? ExcludedRow { get; set; }

    /// <summary>
    /// How many statements deep execution currently is. The statement a caller submitted is 1;
    /// a statement in a trigger body it fires is 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It lives on the context rather than on <c>StatementExecutor</c> because a nested body runs on
    /// the <b>same</b> executor - <c>ExecuteTriggerBody</c> calls <c>Execute</c> on itself - so the
    /// executor cannot tell the levels apart. A fresh context is built for every statement a caller
    /// submits, which is what resets the count.
    /// </para>
    /// <para>
    /// See <see cref="StatementExecutor"/>'s nesting limit for why this is counted at all: measured
    /// 2026-08-01, 600 levels of trigger self-recursion killed the host process outright.
    /// </para>
    /// </remarks>
    public int ExecutionDepth { get; set; }
}