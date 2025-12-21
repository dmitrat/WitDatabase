using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Expressions;

/// <summary>
/// Represents an EXISTS or NOT EXISTS expression with a subquery.
/// </summary>
public class WitSqlExpressionExists : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitExpressionExists(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not WitSqlExpressionExists exists)
            return false;

        return base.Is(exists, tolerance)
               && Query.Is(exists.Query, tolerance)
               && IsNot.Is(exists.IsNot);
    }

    public override WitSqlExpressionExists Clone()
    {
        return new WitSqlExpressionExists
        {
            Line = Line,
            Column = Column,
            Query = Query.Clone(),
            IsNot = IsNot
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The subquery to check for existence.
    /// </summary>
    public required WitSqlStatementSelect Query { get; init; }

    /// <summary>
    /// True if this is NOT EXISTS, false for EXISTS.
    /// </summary>
    public bool IsNot { get; init; }

    #endregion
}
