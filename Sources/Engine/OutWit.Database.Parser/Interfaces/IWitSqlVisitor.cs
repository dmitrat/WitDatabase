using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Interfaces
{
    public interface IWitSqlVisitor<out T>
    {
        #region Statements - DML

        T VisitStatementSelect(WitSqlStatementSelect node);
        T VisitStatementInsert(WitSqlStatementInsert node);
        T VisitStatementUpdate(WitSqlStatementUpdate node);
        T VisitStatementDelete(WitSqlStatementDelete node);
        T VisitStatementMerge(WitSqlStatementMerge node);

        #endregion

        #region Statements - DDL

        T VisitStatementCreateTable(WitSqlStatementCreateTable node);
        T VisitStatementDropTable(WitSqlStatementDropTable node);
        T VisitStatementAlterTable(WitSqlStatementAlterTable node);
        T VisitStatementCreateIndex(WitSqlStatementCreateIndex node);
        T VisitStatementDropIndex(WitSqlStatementDropIndex node);
        T VisitStatementCreateView(WitSqlStatementCreateView node);
        T VisitStatementDropView(WitSqlStatementDropView node);
        T VisitStatementCreateTrigger(WitSqlStatementCreateTrigger node);
        T VisitStatementDropTrigger(WitSqlStatementDropTrigger node);
        T VisitStatementCreateSequence(WitSqlStatementCreateSequence node);
        T VisitStatementDropSequence(WitSqlStatementDropSequence node);
        T VisitStatementAlterSequence(WitSqlStatementAlterSequence node);
        T VisitStatementTruncate(WitSqlStatementTruncate node);
        T VisitStatementSignal(WitSqlStatementSignal node);

        #endregion

        #region Statements - Transactions

        T VisitStatementBeginTransaction(WitSqlStatementBeginTransaction node);
        T VisitStatementCommit(WitSqlStatementCommit node);
        T VisitStatementRollback(WitSqlStatementRollback node);
        T VisitStatementSavepoint(WitSqlStatementSavepoint node);
        T VisitStatementReleaseSavepoint(WitSqlStatementReleaseSavepoint node);
        T VisitStatementSetTransaction(WitSqlStatementSetTransaction node);

        #endregion

        #region Statements - Query Analysis

        T VisitStatementExplain(WitSqlStatementExplain node);

        #endregion

        #region Expressions

        T VisitExpressionLiteral(WitSqlExpressionLiteral node);
        T VisitExpressionColumnRef(WitSqlExpressionColumnRef node);
        T VisitExpressionBinary(WitSqlExpressionBinary node);
        T VisitExpressionUnary(WitSqlExpressionUnary node);
        T VisitExpressionFunctionCall(WitSqlExpressionFunctionCall node);
        T VisitExpressionCase(WitSqlExpressionCase node);
        T VisitExpressionCast(WitSqlExpressionCast node);
        T VisitExpressionBetween(WitSqlExpressionBetween node);
        T VisitExpressionIn(WitSqlExpressionIn node);
        T VisitExpressionLike(WitSqlExpressionLike node);
        T VisitExpressionIsNull(WitSqlExpressionIsNull node);
        T VisitExpressionSubquery(WitSqlExpressionSubquery node);
        T VisitExpressionGlob(WitSqlExpressionGlob node);
        T VisitExpressionIif(WitSqlExpressionIif node);
        T VisitExpressionExists(WitSqlExpressionExists node);
        T VisitExpressionParameter(WitSqlExpressionParameter node);
        T VisitExpressionQuantified(WitSqlExpressionQuantified node);
        T VisitExpressionCollate(WitSqlExpressionCollate node);

        #endregion
    }
}
