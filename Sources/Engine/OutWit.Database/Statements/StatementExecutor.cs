using System.Data;
using System.Linq.Expressions;
using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Query;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Statements;

/// <summary>
/// Executes WitSql statements against the database.
/// </summary>
public sealed partial class StatementExecutor
{
    #region Fields

    private readonly ContextExecution m_context;
    private readonly QueryPlanner m_planner;

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
        return statement switch
        {
            // DML
            WitSqlStatementSelect select => ExecuteSelect(select),
            WitSqlStatementInsert insert => ExecuteInsert(insert),
            WitSqlStatementUpdate update => ExecuteUpdate(update),
            WitSqlStatementDelete delete => ExecuteDelete(delete),
            
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
            
            // DDL - Sequences
            WitSqlStatementCreateSequence createSequence => ExecuteCreateSequence(createSequence),
            WitSqlStatementDropSequence dropSequence => ExecuteDropSequence(dropSequence),
            WitSqlStatementAlterSequence alterSequence => ExecuteAlterSequence(alterSequence),
            
            _ => throw new NotSupportedException($"Statement type not supported: {statement.GetType().Name}")
        };
    }

    #endregion

    #region Helpers

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

