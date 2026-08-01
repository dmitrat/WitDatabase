using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionIif : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionIif(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionIif iif)
                return false;

            return base.Is(other, tolerance) 
                   && Condition.Is(iif.Condition, tolerance)
                   && TrueValue.Is(iif.TrueValue, tolerance)
                   && FalseValue.Is(iif.FalseValue, tolerance);
        }

        public override WitSqlExpressionIif Clone()
        {
            return new WitSqlExpressionIif
            {
                Line = Line,
                Column = Column,
                Condition = (WitSqlExpression)Condition.Clone(),
                TrueValue = (WitSqlExpression)TrueValue.Clone(),
                FalseValue = (WitSqlExpression)FalseValue.Clone()
            };
        }

        #endregion

        #region Properties

        public required WitSqlExpression Condition { get; init; }
        public required WitSqlExpression TrueValue { get; init; }
        public required WitSqlExpression FalseValue { get; init; }

        #endregion
    }
}