using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema;

namespace OutWit.Database.Parser.Expressions;

[MemoryPackable]
public partial class WitSqlExpressionCast : WitSqlExpression
{
    #region Functions

    public override T Accept<T>(IWitSqlVisitor<T> visitor)
    {
        return visitor.VisitExpressionCast(this);
    }

    #endregion

    #region Model Base

    public override bool Is(ModelBase? modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitSqlExpressionCast cast)
            return false;

        return base.Is(cast, tolerance)
               && Expression.Is(cast.Expression, tolerance) 
               && TargetType.Is(cast.TargetType, tolerance);
    }

    public override WitSqlExpressionCast Clone()
    {
        return new WitSqlExpressionCast
        {
            Line = Line,
            Column = Column,
            Expression = (WitSqlExpression)Expression.Clone(),
            TargetType = TargetType.Clone()
        };
    }

    #endregion

    #region Properties

    [ToString]
    public required WitSqlExpression Expression { get; init; }

    [ToString]

    public required WitSqlDataType TargetType { get; init; }

    #endregion
}