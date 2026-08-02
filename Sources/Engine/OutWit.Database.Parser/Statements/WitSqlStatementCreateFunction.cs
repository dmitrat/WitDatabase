using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// <c>CREATE FUNCTION name(params) RETURNS type AS BEGIN RETURN expression; END</c>
    /// </summary>
    /// <remarks>
    /// <b>The body is one expression, not a statement list.</b> That is the decision phase 9d rests
    /// on: invoking a function becomes substitution inside the expression evaluator rather than
    /// re-entry into the statement executor, so it consumes no execution nesting, cannot open a
    /// transaction, and cannot reach the row-loop hazards a trigger body has. <c>RETURN</c> is
    /// therefore part of this node and has no statement type of its own - it has exactly one legal
    /// position, and a type for it would be a permanent MemoryPack union tag bought for nothing.
    /// </remarks>
    [MemoryPackable]
    public partial class WitSqlStatementCreateFunction : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateFunction(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateFunction create)
                return false;

            return base.Is(create, tolerance)
                   && FunctionName.Is(create.FunctionName)
                   && IfNotExists.Is(create.IfNotExists)
                   && Language.Is(create.Language)
                   && ReturnType.Check(create.ReturnType)
                   && Body.Check(create.Body)
                   && Parameters.Is(create.Parameters);
        }

        public override WitSqlStatementCreateFunction Clone()
        {
            return new WitSqlStatementCreateFunction
            {
                Line = Line,
                Column = Column,
                FunctionName = FunctionName,
                IfNotExists = IfNotExists,
                Language = Language,
                ReturnType = ReturnType.Clone(),
                Parameters = Parameters?.Select(p => p.Clone()).ToList(),
                Body = (WitSqlExpression)Body.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string FunctionName { get; init; }

        public bool IfNotExists { get; init; }

        /// <summary>
        /// The declared parameters, in order. Null when the list is empty.
        /// </summary>
        public IReadOnlyList<WitSqlRoutineParameter>? Parameters { get; init; }

        /// <summary>
        /// The declared return type.
        /// </summary>
        public required WitSqlDataType ReturnType { get; init; }

        /// <summary>
        /// The body: one expression over the parameters.
        /// </summary>
        public required WitSqlExpression Body { get; init; }

        /// <summary>
        /// The <c>LANGUAGE</c> clause as written, or null when it was omitted.
        /// </summary>
        /// <remarks>
        /// Carried as written rather than validated here. The grammar admits any identifier so that
        /// <c>LANGUAGE plpgsql</c> is refused by the executor with a sentence a caller can act on,
        /// instead of a parse error pointing at a token. An accepted-and-ignored language clause
        /// would be the "accepted, not enforced" class phase 7 closed across the DDL surface.
        /// </remarks>
        public string? Language { get; init; }

        #endregion
    }
}
