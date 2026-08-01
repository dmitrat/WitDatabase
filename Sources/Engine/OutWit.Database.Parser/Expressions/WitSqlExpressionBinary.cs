using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionBinary : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionBinary(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? modelBase, double tolerance = DEFAULT_TOLERANCE)
        {
            if (modelBase is not WitSqlExpressionBinary binary)
                return false;

            return base.Is(modelBase, tolerance)
                   && Left.Is(binary.Left, tolerance)
                   && Operator.Is(binary.Operator)
                   && Right.Is(binary.Right, tolerance);
        }

        public override WitSqlExpressionBinary Clone()
        {
            return new WitSqlExpressionBinary
            {
                Line = Line,
                Column = Column,
                Left = (WitSqlExpression)Left.Clone(),
                Operator = Operator,
                Right = (WitSqlExpression)Right.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required WitSqlExpression Left { get; init; }

        [ToString]
        public required BinaryOperatorType Operator { get; init; }

        [ToString]
        public required WitSqlExpression Right { get; init; }

        #endregion
    }
}