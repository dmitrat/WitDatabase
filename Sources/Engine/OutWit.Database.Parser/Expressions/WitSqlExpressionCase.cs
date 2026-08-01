using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Clauses;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionCase : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionCase(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionCase caseExpr)
                return false;

            return base.Is(other, tolerance) 
                   && Operand.Check(caseExpr.Operand)
                   && WhenClauses.Is(caseExpr.WhenClauses)
                   && ElseResult.Check(caseExpr.ElseResult);
        }

        public override WitSqlExpressionCase Clone()
        {
            return new WitSqlExpressionCase
            {
                Line = Line,
                Column = Column,
                Operand = (WitSqlExpression?)Operand?.Clone(),
                WhenClauses = WhenClauses.Select(when => when.Clone()).ToList(),
                ElseResult = (WitSqlExpression?)ElseResult?.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public WitSqlExpression? Operand { get; init; }

        public required IReadOnlyList<ClauseWhen> WhenClauses { get; init; }

        [ToString]
        public WitSqlExpression? ElseResult { get; init; }

        #endregion
    }
}