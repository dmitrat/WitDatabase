using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Visitor;

/// <summary>
/// Visitor that builds AST from ANTLR parse tree.
/// </summary>
internal sealed partial class WitSqlVisitor : WitSqlParserBaseVisitor<object?>
{
    #region Script and Statement Entry Points

    public override IReadOnlyList<WitSqlStatement> VisitScript(WitSqlParser.ScriptContext context)
    {
        var statements = new List<WitSqlStatement>();
        foreach (var stmtCtx in context.statement())
        {
            var stmt = VisitStatement(stmtCtx);
            if (stmt != null)
            {
                statements.Add(stmt);
            }
        }
        return statements;
    }

    public override WitSqlStatement? VisitStatement(WitSqlParser.StatementContext context)
    {
        // Navigate through new grammar hierarchy
        if (context.dmlStatement() is { } dml)
            return VisitDmlStatement(dml);
        if (context.ddlStatement() is { } ddl)
            return VisitDdlStatement(ddl);
        if (context.transactionStatement() is { } txn)
            return VisitTransactionStatement(txn);
        if (context.signalStatement() is { } signal)
            return VisitSignalStatement(signal);
        if (context.explainStatement() is { } explain)
            return VisitExplainStatement(explain);
        return null;
    }

    #endregion

    #region DML, DDL, Transaction Statement Routing

    private new WitSqlStatement? VisitDmlStatement(WitSqlParser.DmlStatementContext context)
    {
        if (context.queryExpression() is { } query)
            return VisitQueryExpression(query);
        if (context.insertStatement() is { } insert)
            return VisitInsertStatement(insert);
        if (context.updateStatement() is { } update)
            return VisitUpdateStatement(update);
        if (context.deleteStatement() is { } delete)
            return VisitDeleteStatement(delete);
        if (context.mergeStatement() is { } merge)
            return VisitMergeStatement(merge);
        return null;
    }

    private new WitSqlStatement? VisitDdlStatement(WitSqlParser.DdlStatementContext context)
    {
        if (context.createTableStatement() is { } createTable)
            return VisitCreateTableStatement(createTable);
        if (context.dropTableStatement() is { } dropTable)
            return VisitDropTableStatement(dropTable);
        if (context.alterTableStatement() is { } alterTable)
            return VisitAlterTableStatement(alterTable);
        if (context.createIndexStatement() is { } createIndex)
            return VisitCreateIndexStatement(createIndex);
        if (context.dropIndexStatement() is { } dropIndex)
            return VisitDropIndexStatement(dropIndex);
        if (context.createViewStatement() is { } createView)
            return VisitCreateViewStatement(createView);
        if (context.dropViewStatement() is { } dropView)
            return VisitDropViewStatement(dropView);
        if (context.createTriggerStatement() is { } createTrigger)
            return VisitCreateTriggerStatement(createTrigger);
        if (context.dropTriggerStatement() is { } dropTrigger)
            return VisitDropTriggerStatement(dropTrigger);
        if (context.createSequenceStatement() is { } createSequence)
            return VisitCreateSequenceStatement(createSequence);
        if (context.dropSequenceStatement() is { } dropSequence)
            return VisitDropSequenceStatement(dropSequence);
        if (context.alterSequenceStatement() is { } alterSequence)
            return VisitAlterSequenceStatement(alterSequence);
        if (context.truncateTableStatement() is { } truncate)
            return VisitTruncateTableStatement(truncate);
        return null;
    }

    private new WitSqlStatement? VisitTransactionStatement(WitSqlParser.TransactionStatementContext context)
    {
        if (context.beginTransaction() is { } begin)
        {
            return new WitSqlStatementBeginTransaction
            {
                Line = begin.Start.Line,
                Column = begin.Start.Column
            };
        }

        if (context.commitStatement() is { } commit)
        {
            return new WitSqlStatementCommit
            {
                Line = commit.Start.Line,
                Column = commit.Start.Column
            };
        }

        if (context.rollbackStatement() is { } rollback)
        {
            return new WitSqlStatementRollback
            {
                Line = rollback.Start.Line,
                Column = rollback.Start.Column,
                SavepointName = rollback.IDENTIFIER()?.GetText()
            };
        }

        if (context.savepointStatement() is { } savepoint)
        {
            return new WitSqlStatementSavepoint
            {
                Line = savepoint.Start.Line,
                Column = savepoint.Start.Column,
                Name = savepoint.IDENTIFIER().GetText()
            };
        }

        if (context.releaseStatement() is { } release)
        {
            return new WitSqlStatementReleaseSavepoint
            {
                Line = release.Start.Line,
                Column = release.Start.Column,
                Name = release.IDENTIFIER().GetText()
            };
        }

        if (context.setTransactionStatement() is { } setTransaction)
        {
            return VisitSetTransactionStatement(setTransaction);
        }

        return null;
    }

    private WitSqlStatementSetTransaction VisitSetTransactionStatement(WitSqlParser.SetTransactionStatementContext context)
    {
        var isolationLevelCtx = context.isolationLevel();
        var isolationLevel = ParseIsolationLevel(isolationLevelCtx);

        return new WitSqlStatementSetTransaction
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            IsolationLevel = isolationLevel
        };
    }

    private static IsolationLevelType ParseIsolationLevel(WitSqlParser.IsolationLevelContext context)
    {
        if (context.SERIALIZABLE() != null)
            return IsolationLevelType.Serializable;
        if (context.SNAPSHOT() != null)
            return IsolationLevelType.Snapshot;
        if (context.REPEATABLE() != null)
            return IsolationLevelType.RepeatableRead;
        if (context.UNCOMMITTED() != null)
            return IsolationLevelType.ReadUncommitted;
        return IsolationLevelType.ReadCommitted;
    }

    #endregion

    #region EXPLAIN Statement

    private WitSqlStatementExplain VisitExplainStatement(WitSqlParser.ExplainStatementContext context)
    {
        var isQueryPlan = context.QUERY() != null && context.PLAN() != null;

        return new WitSqlStatementExplain
        {
            Line = context.Start.Line,
            Column = context.Start.Column,
            QueryPlan = isQueryPlan,
            Statement = VisitQueryExpression(context.queryExpression())
        };
    }

    #endregion
}
