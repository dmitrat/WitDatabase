using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Expressions
{
    /// <summary>
    /// Represents a quantified comparison expression: expression &lt;op&gt; ANY/SOME/ALL (subquery)
    /// </summary>
    public class WitSqlExpressionQuantified : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionQuantified(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionQuantified quantified)
                return false;

            return base.Is(other, tolerance)
                   && Expression.Is(quantified.Expression, tolerance)
                   && Operator.Is(quantified.Operator)
                   && QuantifierType.Is(quantified.QuantifierType)
                   && Subquery.Is(quantified.Subquery, tolerance);
        }

        public override WitSqlExpressionQuantified Clone()
        {
            return new WitSqlExpressionQuantified
            {
                Line = Line,
                Column = Column,
                Expression = (WitSqlExpression)Expression.Clone(),
                Operator = Operator,
                QuantifierType = QuantifierType,
                Subquery = Subquery.Clone()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The left-hand side expression.
        /// </summary>
        public required WitSqlExpression Expression { get; init; }

        /// <summary>
        /// The comparison operator (=, &lt;&gt;, &lt;, &lt;=, &gt;, &gt;=).
        /// </summary>
        public required BinaryOperatorType Operator { get; init; }

        /// <summary>
        /// The quantifier type (ANY, SOME, ALL).
        /// </summary>
        public required QuantifierType QuantifierType { get; init; }

        /// <summary>
        /// The subquery.
        /// </summary>
        public required WitSqlStatementSelect Subquery { get; init; }

        #endregion
    }
}
