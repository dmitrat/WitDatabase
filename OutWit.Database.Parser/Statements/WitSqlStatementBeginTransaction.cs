using OutWit.Common.Abstract;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

/// <summary>
/// Represents a BEGIN TRANSACTION statement.
/// </summary>
public class WitSqlStatementBeginTransaction : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementBeginTransaction(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementBeginTransaction begin)
            return false;

        return base.Is(begin, tolerance);
    }

    public override WitSqlStatementBeginTransaction Clone()
    {
        return new WitSqlStatementBeginTransaction
        {
            Line = Line,
            Column = Column
        };
    }

    #endregion
}
