using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Interfaces;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// <c>CALL name(args)</c> - invoking a procedure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A statement rather than a DDL one, because that is what it is: it runs a body, it does not
    /// declare anything.
    /// </para>
    /// <para>
    /// <b>A trigger body may not contain one.</b> A procedure is allowed DDL precisely because a
    /// <c>CALL</c> at the top level is a statement and not a loop over rows; letting a trigger reach
    /// one would put the row loop back underneath it, where <c>DROP TABLE</c> against the table being
    /// written reports success and destroys it. Refusing <c>CALL</c> in a trigger body is one check
    /// at declaration and needs no analysis of the call graph.
    /// </para>
    /// </remarks>
    [MemoryPackable]
    public partial class WitSqlStatementCall : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCall(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCall call)
                return false;

            return base.Is(call, tolerance)
                   && ProcedureName.Is(call.ProcedureName)
                   && Arguments.Is(call.Arguments);
        }

        public override WitSqlStatementCall Clone()
        {
            return new WitSqlStatementCall
            {
                Line = Line,
                Column = Column,
                ProcedureName = ProcedureName,
                Arguments = Arguments?.Select(a => (WitSqlExpression)a.Clone()).ToList()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ProcedureName { get; init; }

        /// <summary>
        /// The argument expressions, in order. Null when the call has no arguments.
        /// </summary>
        public IReadOnlyList<WitSqlExpression>? Arguments { get; init; }

        #endregion
    }
}
