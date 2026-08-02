using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// A user-defined procedure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The body is a statement list</b>, exactly as a trigger body has been since 9.0.0, and for
    /// the reason that was measured then: storing statements as statements means there is nothing to
    /// render and nothing to split. The text-and-split version lost an <c>ON CONFLICT</c> clause
    /// silently and cut a statement in half on a semicolon inside a string literal.
    /// </para>
    /// <para>
    /// What the list may contain is decided in <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 3:
    /// DML and DDL, plus <c>CALL</c> of another procedure. Transaction control is refused, because a
    /// nested <c>COMMIT</c> is stopped by nothing at runtime - it commits the calling statement's
    /// transaction and leaves the rest of that statement running outside one. DDL is allowed here and
    /// refused in a trigger body, and the asymmetry is measured rather than arbitrary: a trigger runs
    /// inside a loop over rows, and <c>DROP TABLE</c> against the table that loop is walking reports
    /// success and destroys it. A <c>CALL</c> at the top level is a statement, not a row loop - which
    /// is why a trigger body may not contain one.
    /// </para>
    /// <para>
    /// Stored as trees with no text beside them, per phase 8. There is no legacy text field because
    /// there is no legacy: this type is new in the release that introduces it.
    /// </para>
    /// </remarks>
    [MemoryPackable]
    public sealed partial class DefinitionProcedure : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not DefinitionProcedure other)
                return false;

            return Name.Is(other.Name)
                   && Parameters.Is(other.Parameters)
                   && StatementsAre(other);
        }

        public override DefinitionProcedure Clone()
        {
            return new DefinitionProcedure
            {
                Name = Name,
                Parameters = Parameters?.Select(p => p.Clone()).ToArray(),
                Statements = Statements.Select(s => (WitSqlStatement)s.Clone()).ToList()
            };
        }

        private bool StatementsAre(DefinitionProcedure other)
        {
            if (Statements.Count != other.Statements.Count)
                return false;

            return !Statements.Where((s, i) => !s.Is(other.Statements[i])).Any();
        }

        #endregion

        #region Functions

        public override string ToString() => $"PROCEDURE {Name}";

        /// <summary>
        /// The body as SQL for <c>INFORMATION_SCHEMA.ROUTINES</c> to report, rendered from the trees.
        /// </summary>
        /// <remarks>
        /// Null when the renderer cannot express a statement faithfully, never a placeholder comment:
        /// a comment reads as rendered SQL to anything consuming the column, and "something was
        /// emitted" is exactly the mistake to avoid. A description must also never be able to refuse
        /// a write - measured in phase 8, when <c>CREATE TRIGGER</c> failed inside the code that
        /// exists only to fill a catalog column.
        /// </remarks>
        public string? DisplayBody() => m_displayBody ??= SchemaText.Render(Statements);

        #endregion

        #region Fields

        private string? m_displayBody;

        #endregion

        #region Properties

        /// <summary>
        /// The procedure name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// The parameters, in declaration order.
        /// </summary>
        [MemoryPackOrder(1)]
        public IReadOnlyList<DefinitionRoutineParameter>? Parameters { get; init; }

        /// <summary>
        /// The body, stored as statements.
        /// </summary>
        [MemoryPackOrder(2)]
        public required IReadOnlyList<WitSqlStatement> Statements { get; init; }

        #endregion
    }
}
