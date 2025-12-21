using OutWit.Common.Abstract;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

/// <summary>
/// Represents a COMMIT statement.
/// </summary>
public class WitSqlStatementCommit : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementCommit(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementCommit commit)
            return false;

        return base.Is(commit, tolerance);
    }

    public override WitSqlStatementCommit Clone()
    {
        return new WitSqlStatementCommit
        {
            Line = Line,
            Column = Column
        };
    }

    #endregion
}
