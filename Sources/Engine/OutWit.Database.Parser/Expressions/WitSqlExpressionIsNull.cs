using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionIsNull : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionIsNull(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionIsNull isNull)
                return false;

            return base.Is(other, tolerance) 
                   && Expression.Is(isNull.Expression, tolerance) 
                   && IsNot.Is(isNull.IsNot);
        }

        public override WitSqlExpressionIsNull Clone()
        {
            return new WitSqlExpressionIsNull
            {
                Line = Line,
                Column = Column,
                Expression = (WitSqlExpression)Expression.Clone(),
                IsNot = IsNot
            };
        }

        #endregion

        #region Properties

        public required WitSqlExpression Expression { get; init; }

        public bool IsNot { get; init; }

        #endregion
    }
}