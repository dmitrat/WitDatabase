using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Expressions
{
    /// <summary>
    /// Represents a COLLATE expression: expression COLLATE collation_name.
    /// </summary>
    [MemoryPackable]
    public sealed partial class WitSqlExpressionCollate : WitSqlExpression
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitExpressionCollate(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlExpressionCollate collate)
                return false;

            // base.Is compares Line/Column, which every other node in the AST compares and this one
            // omitted until 2026-07-31.
            return base.Is(collate, tolerance)
                   && Operand.Check(collate.Operand)
                   && CollationName.Is(collate.CollationName);
        }

        public override WitSqlExpressionCollate Clone()
        {
            return new WitSqlExpressionCollate
            {
                Operand = (WitSqlExpression?)Operand?.Clone(),
                CollationName = CollationName
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The expression to apply collation to.
        /// </summary>
        public WitSqlExpression? Operand { get; init; }

        /// <summary>
        /// The collation name (e.g., NOCASE, BINARY, UNICODE).
        /// </summary>
        [ToString]
        public required string CollationName { get; init; }

        #endregion
    }
}
