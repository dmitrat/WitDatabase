using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionGlob : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionGlob(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionGlob glob)
                return false;

            return base.Is(glob, tolerance)
                   && Expression.Is(glob.Expression, tolerance)
                   && Pattern.Is(glob.Pattern, tolerance)
                   && IsNot.Is(glob.IsNot);
        }

        public override ModelBase Clone()
        {
            return new WitSqlExpressionGlob
            {
                Line = Line,
                Column = Column,
                Expression = (WitSqlExpression)Expression.Clone(),
                Pattern = (WitSqlExpression)Pattern.Clone(),
                IsNot = IsNot
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required WitSqlExpression Expression { get; init; }

        [ToString]
        public required WitSqlExpression Pattern { get; init; }

        [ToString]
        public bool IsNot { get; init; }

        #endregion
    }
}