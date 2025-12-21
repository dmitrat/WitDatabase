using OutWit.Database.Parser.Generated;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Visitor;

/// <summary>
/// Visitor that builds AST from ANTLR parse tree.
/// </summary>
internal sealed partial class WitSqlVisitor : WitSqlBaseVisitor<object?>
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
        return null;
    }

    private new WitSqlStatement? VisitTransactionStatement(WitSqlParser.TransactionStatementContext context)
    {
        // Stubs for transaction statements
        if (context.beginTransaction() != null)
            throw new NotImplementedException("BEGIN TRANSACTION not yet implemented");
        if (context.commitStatement() != null)
            throw new NotImplementedException("COMMIT not yet implemented");
        if (context.rollbackStatement() != null)
            throw new NotImplementedException("ROLLBACK not yet implemented");
        if (context.savepointStatement() != null)
            throw new NotImplementedException("SAVEPOINT not yet implemented");
        if (context.releaseStatement() != null)
            throw new NotImplementedException("RELEASE SAVEPOINT not yet implemented");
        return null;
    }

    #endregion
}
