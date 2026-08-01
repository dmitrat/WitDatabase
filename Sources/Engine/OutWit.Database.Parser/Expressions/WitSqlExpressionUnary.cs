using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionUnary : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionUnary(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionUnary unary)
                return false;

            return base.Is(unary, tolerance) 
                   && Operator.Is(unary.Operator) 
                   && Operand.Is(unary.Operand, tolerance);
        }

        public override WitSqlExpressionUnary Clone()
        {
            return new WitSqlExpressionUnary
            {
                Line = Line,
                Column = Column,
                Operator = Operator,
                Operand = (WitSqlExpression)Operand.Clone()
            };
        }

        #endregion
        
        #region Properties

        [ToString]
        public required UnaryOperatorType Operator { get; init; }

        [ToString]
        public required WitSqlExpression Operand { get; init; }

        #endregion
    }
}