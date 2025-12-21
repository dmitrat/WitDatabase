using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements;

/// <summary>
/// Represents a RELEASE SAVEPOINT statement.
/// </summary>
public class WitSqlStatementReleaseSavepoint : WitSqlStatement
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitStatementReleaseSavepoint(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlStatementReleaseSavepoint release)
            return false;

        return base.Is(release, tolerance)
               && Name.Is(release.Name);
    }

    public override WitSqlStatementReleaseSavepoint Clone()
    {
        return new WitSqlStatementReleaseSavepoint
        {
            Line = Line,
            Column = Column,
            Name = Name
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The name of the savepoint to release.
    /// </summary>
    [ToString]
    public required string Name { get; init; }

    #endregion
}
