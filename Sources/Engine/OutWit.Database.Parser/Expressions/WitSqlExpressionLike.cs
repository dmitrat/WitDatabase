using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionLike : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionLike(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionLike like)
                return false;

            return base.Is(like, tolerance)
                   && Expression.Is(like.Expression, tolerance)
                   && Pattern.Is(like.Pattern, tolerance)
                   && Escape.Check(like.Escape)
                   && IsNot.Is(like.IsNot);
        }

        public override WitSqlExpressionLike Clone()
        {
            return new WitSqlExpressionLike
            {
                Line = Line,
                Column = Column,
                Expression = (WitSqlExpression)Expression.Clone(),
                Pattern = (WitSqlExpression)Pattern.Clone(),
                Escape = (WitSqlExpression?)Escape?.Clone(),
                IsNot = IsNot
            };
        }

        #endregion

        #region Properties

        public required WitSqlExpression Expression { get; init; }
        public required WitSqlExpression Pattern { get; init; }
        public WitSqlExpression? Escape { get; init; }
        public bool IsNot { get; init; }

        #endregion
    }
}