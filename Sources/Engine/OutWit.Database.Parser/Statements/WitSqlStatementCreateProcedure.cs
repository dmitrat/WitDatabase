using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Interfaces;
using OutWit.Database.Parser.Schema;

namespace OutWit.Database.Parser.Statements
{
    /// <summary>
    /// <c>CREATE PROCEDURE name(params) AS BEGIN statement; … END</c>
    /// </summary>
    /// <remarks>
    /// The body is a statement list, exactly as a trigger body has been since 9.0.0 - stored as
    /// statements so there is nothing to render and nothing to split. What the list may contain is
    /// enforced by the executor, not by the grammar: the body rule references the top-level
    /// statement rule, so the grammar admits anything and the refusal is a deliberate, named one that
    /// a caller can act on. See <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 3.
    /// </remarks>
    [MemoryPackable]
    public partial class WitSqlStatementCreateProcedure : WitSqlStatement
    {
        #region Functions

        public override T Accept<T>(IWitSqlVisitor<T> visitor)
        {
            return visitor.VisitStatementCreateProcedure(this);
        }

        #endregion

        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlStatementCreateProcedure create)
                return false;

            return base.Is(create, tolerance)
                   && ProcedureName.Is(create.ProcedureName)
                   && IfNotExists.Is(create.IfNotExists)
                   && Language.Is(create.Language)
                   && Parameters.Is(create.Parameters)
                   && Body.Is(create.Body);
        }

        public override WitSqlStatementCreateProcedure Clone()
        {
            return new WitSqlStatementCreateProcedure
            {
                Line = Line,
                Column = Column,
                ProcedureName = ProcedureName,
                IfNotExists = IfNotExists,
                Language = Language,
                Parameters = Parameters?.Select(p => p.Clone()).ToList(),
                Body = Body.Select(statement => (WitSqlStatement)statement.Clone()).ToList()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ProcedureName { get; init; }

        public bool IfNotExists { get; init; }

        /// <summary>
        /// The declared parameters, in order. Null when there are none - a procedure may be declared
        /// without a parameter list at all, which is SQL Server's spelling and the one the oracle
        /// corpus pins.
        /// </summary>
        public IReadOnlyList<WitSqlRoutineParameter>? Parameters { get; init; }

        /// <summary>
        /// The body statements.
        /// </summary>
        public required IReadOnlyList<WitSqlStatement> Body { get; init; }

        /// <summary>
        /// The <c>LANGUAGE</c> clause as written, or null. Validated by the executor - see
        /// <see cref="WitSqlStatementCreateFunction.Language"/>.
        /// </summary>
        public string? Language { get; init; }

        #endregion
    }
}
