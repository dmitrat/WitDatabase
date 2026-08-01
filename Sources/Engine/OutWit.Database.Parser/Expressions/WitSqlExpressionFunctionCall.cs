using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Specs;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionFunctionCall : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionFunctionCall(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionFunctionCall function)
                return false;

            return base.Is(function, tolerance)
                   && FunctionName.Is(function.FunctionName)
                   && Arguments.Is(function.Arguments)
                   && IsDistinct.Is(function.IsDistinct)
                   && IsStar.Is(function.IsStar)
                   && Over.Check(function.Over);
        }

        public override WitSqlExpressionFunctionCall Clone()
        {
            return new WitSqlExpressionFunctionCall
            {
                Line = Line,
                Column = Column,
                FunctionName = FunctionName,
                Arguments = Arguments?.Select(expression => (WitSqlExpression)expression.Clone()).ToList(),
                IsDistinct = IsDistinct,
                IsStar = IsStar,
                Over = Over?.Clone()
            };


        }

        #endregion

        #region Properties

        [ToString]
        public required string FunctionName { get; init; }
        public IReadOnlyList<WitSqlExpression>? Arguments { get; init; }
        public bool IsDistinct { get; init; }
        public bool IsStar { get; init; }
        public SpecWindow? Over { get; init; }

        #endregion
    }
}