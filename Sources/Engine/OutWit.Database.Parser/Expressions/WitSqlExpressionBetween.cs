using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionBetween : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionBetween(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? modelBase, double tolerance = DEFAULT_TOLERANCE)
        {
            if (modelBase is not WitSqlExpressionBetween between)
                return false;

            return base.Is(modelBase, tolerance)
                   && Expression.Is(between.Expression, tolerance)
                   && Low.Is(between.Low, tolerance)
                   && High.Is(between.High, tolerance)
                   && IsNot.Is(between.IsNot);
        }

        public override ModelBase Clone()
        {
            return new WitSqlExpressionBetween
            {
                Line = Line,
                Column = Column,
                Expression = (WitSqlExpression)Expression.Clone(),
                Low = (WitSqlExpression)Low.Clone(),
                High = (WitSqlExpression)High.Clone(),
                IsNot = IsNot
            };
        }

        #endregion


        #region Properties

        [ToString]
        public required WitSqlExpression Expression { get; init; }

        [ToString]
        public required WitSqlExpression Low { get; init; }

        [ToString]
        public required WitSqlExpression High { get; init; }

        [ToString]
        public bool IsNot { get; init; }

        #endregion
    }
}