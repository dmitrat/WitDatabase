using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

/// <summary>
/// Represents a ROLLBACK statement, optionally to a savepoint.
/// </summary>
public class WitSqlStatementRollback : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementRollback(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementRollback rollback)
            return false;

        return base.Is(rollback, tolerance)
               && SavepointName.Is(rollback.SavepointName);
    }

    public override WitSqlStatementRollback Clone()
    {
        return new WitSqlStatementRollback
        {
            Line = Line,
            Column = Column,
            SavepointName = SavepointName
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Optional savepoint name to rollback to.
    /// If null, rolls back the entire transaction.
    /// </summary>
    [ToString]
    public string? SavepointName { get; init; }

    #endregion
}
