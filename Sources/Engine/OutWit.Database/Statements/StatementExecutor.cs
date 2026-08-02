using System.Data;
using System.Linq.Expressions;
using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Query;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// Executes WitSql statements against the database.
/// </summary>
public sealed partial class StatementExecutor
{
    #region Constants

    /// <summary>
    /// How many statements deep execution may go before it is refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nested execution had no bound at all until 2026-08-01. A trigger inserting into its own table
    /// recursed until the stack ran out: <b>200 levels passed, 400 passed, and 600 killed the host
    /// process</b>. <c>StackOverflowException</c> cannot be caught in .NET, so an application
    /// embedding this database died with it - no exception, no rollback, no message.
    /// </para>
    /// <para>
    /// 32 is SQL Server's nesting limit and roughly what PostgreSQL's <c>max_stack_depth</c> allows.
    /// The value is far less important than the class of failure it replaces: an error a caller can
    /// catch, naming the limit, instead of a dead process. It is deliberately not configurable -
    /// raising it trades a catchable error for the crash it exists to prevent, and no consumer
    /// should be able to make that trade by accident.
    /// </para>
    /// </remarks>
    private const int MAX_EXECUTION_DEPTH = 32;

    #endregion

    #region Fields

    private readonly ContextExecution m_context;
    private readonly QueryPlanner m_planner;
    
    /// <summary>
    /// Cache for parsed SQL expressions (CHECK constraints, computed columns, etc.).
    /// Key is the SQL expression string, value is the parsed expression.
    /// </summary>

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new statement executor.
    /// </summary>
    /// <param name="context">The execution context.</param>
    public StatementExecutor(ContextExecution context)
    {
        m_context = context;
        m_planner = new QueryPlanner(context);
    }

    #endregion

    #region Execute

    /// <summary>
    /// Executes a WitSql statement and returns the result.
    /// </summary>
    /// <param name="statement">The statement to execute.</param>
    /// <returns>The execution result.</returns>
    public WitSqlResult Execute(WitSqlStatement statement)
    {
        // Counted here rather than at the places that nest, because this is the one door every
        // nested statement comes through - a trigger body today, a procedure body tomorrow - and a
        // count kept at the call sites is a count that a new call site can forget to keep.
        m_context.ExecutionDepth++;

        try
        {
            if (m_context.ExecutionDepth > MAX_EXECUTION_DEPTH)
            {
                throw new NestingLimitException(
                    $"Statements are nested more than {MAX_EXECUTION_DEPTH} deep, which is the limit. "
                    + "A trigger whose body writes to its own table recurses without end; give it a "
                    + "WHEN condition that stops, or break the cycle between the triggers involved.");
            }

            return ExecuteCore(statement);
        }
        finally
        {
            m_context.ExecutionDepth--;
        }
    }

    private WitSqlResult ExecuteCore(WitSqlStatement statement)
    {
        return statement switch
        {
            // DML
            WitSqlStatementSelect select => ExecuteSelect(select),
            WitSqlStatementInsert insert => ExecuteInsert(insert),
            WitSqlStatementUpdate update => ExecuteUpdate(update),
            WitSqlStatementDelete delete => ExecuteDelete(delete),
            WitSqlStatementTruncate truncate => ExecuteTruncate(truncate),
            WitSqlStatementMerge merge => ExecuteMerge(merge),
            
            // DDL - Tables
            WitSqlStatementCreateTable createTable => ExecuteCreateTable(createTable),
            WitSqlStatementDropTable dropTable => ExecuteDropTable(dropTable),
            WitSqlStatementAlterTable alterTable => ExecuteAlterTable(alterTable),
            
            // DDL - Indexes
            WitSqlStatementCreateIndex createIndex => ExecuteCreateIndex(createIndex),
            WitSqlStatementDropIndex dropIndex => ExecuteDropIndex(dropIndex),
            
            // DDL - Views
            WitSqlStatementCreateView createView => ExecuteCreateView(createView),
            WitSqlStatementDropView dropView => ExecuteDropView(dropView),
            
            // DDL - Triggers
            WitSqlStatementCreateTrigger createTrigger => ExecuteCreateTrigger(createTrigger),
            WitSqlStatementDropTrigger dropTrigger => ExecuteDropTrigger(dropTrigger),
            
            // DDL - Routines
            WitSqlStatementCreateFunction createFunction => ExecuteCreateFunction(createFunction),
            WitSqlStatementDropFunction dropFunction => ExecuteDropFunction(dropFunction),
            WitSqlStatementCreateProcedure createProcedure => ExecuteCreateProcedure(createProcedure),
            WitSqlStatementDropProcedure dropProcedure => ExecuteDropProcedure(dropProcedure),
            WitSqlStatementCall call => ExecuteCall(call),

            // DDL - Sequences
            WitSqlStatementCreateSequence createSequence => ExecuteCreateSequence(createSequence),
            WitSqlStatementDropSequence dropSequence => ExecuteDropSequence(dropSequence),
            WitSqlStatementAlterSequence alterSequence => ExecuteAlterSequence(alterSequence),
            
            // Transaction Control
            WitSqlStatementBeginTransaction beginTx => ExecuteBeginTransaction(beginTx),
            WitSqlStatementCommit commit => ExecuteCommit(commit),
            WitSqlStatementRollback rollback => ExecuteRollback(rollback),
            WitSqlStatementSavepoint savepoint => ExecuteSavepoint(savepoint),
            WitSqlStatementReleaseSavepoint release => ExecuteReleaseSavepoint(release),
            WitSqlStatementSetTransaction setTx => ExecuteSetTransaction(setTx),
            
            // Query Analysis
            WitSqlStatementExplain explain => ExecuteExplain(explain),
            
            _ => throw new NotSupportedException($"Statement type not supported: {statement.GetType().Name}")
        };
    }

    #endregion

    #region Expression Cache

    // Removed in 9.0.0 together with its ten callers. It existed to amortise re-parsing schema
    // expressions that were stored as text; the catalog now stores them as trees, so there is
    // nothing to parse and nothing to cache. ClearExpressionCache had no callers at all.

    #endregion

    #region Helpers

    /// <summary>
    /// The nesting limit, raised as a type so the levels it passes through do not each re-wrap it.
    /// </summary>
    /// <remarks>
    /// A trigger body wraps whatever its statements throw, to say which trigger failed. That is right
    /// for an ordinary failure and wrong for this one: the limit is crossed at the deepest level and
    /// unwinds through every level above it, so the caller would read
    /// <i>"Error executing trigger body:"</i> thirty-two times before the sentence that matters.
    /// Publicly this is still an <see cref="InvalidOperationException"/>; the type exists only to be
    /// recognised on the way out.
    /// </remarks>
    private sealed class NestingLimitException(string message) : InvalidOperationException(message);

    private IEnumerable<WitSqlRow> EnumerateRows(IResultIterator iterator)
    {
        try
        {
            while (iterator.MoveNext())
            {
                yield return iterator.Current;
            }
        }
        finally
        {
            iterator.Dispose();
        }
    }

    #endregion
}

