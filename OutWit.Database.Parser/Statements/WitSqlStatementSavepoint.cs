using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

/// <summary>
/// Represents a SAVEPOINT statement.
/// </summary>
public class WitSqlStatementSavepoint : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementSavepoint(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementSavepoint savepoint)
            return false;

        return base.Is(savepoint, tolerance)
               && Name.Is(savepoint.Name);
    }

    public override WitSqlStatementSavepoint Clone()
    {
        return new WitSqlStatementSavepoint
        {
            Line = Line,
            Column = Column,
            Name = Name
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The name of the savepoint.
    /// </summary>
    [ToString]
    public required string Name { get; init; }

    #endregion
}
