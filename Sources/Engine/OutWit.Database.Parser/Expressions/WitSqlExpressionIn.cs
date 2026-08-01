using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Expressions
{
    [MemoryPackable]
    public partial class WitSqlExpressionIn : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionIn(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionIn inExpr)
                return false;

            return base.Is(other, tolerance) 
                   && Expression.Is(inExpr.Expression, tolerance)
                   && Values.Check(inExpr.Values)
                   && Subquery.Check(inExpr.Subquery)
                   && IsNot.Is(inExpr.IsNot);
        }

        public override WitSqlExpressionIn Clone()
        {
            return new WitSqlExpressionIn
            {
                Line = Line,
                Column = Column,
                Expression = (WitSqlExpression)Expression.Clone(),
                Values = Values?.Select(expression => (WitSqlExpression)expression.Clone()).ToList(),
                Subquery = Subquery?.Clone(),
                IsNot = IsNot
            };
        }

        #endregion

        #region Properties

        public required WitSqlExpression Expression { get; init; }
        public IReadOnlyList<WitSqlExpression>? Values { get; init; }
        public WitSqlStatementSelect? Subquery { get; init; }
        public bool IsNot { get; init; }

        #endregion
    }
}