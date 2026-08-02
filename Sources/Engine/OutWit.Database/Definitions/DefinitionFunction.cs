using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Types;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// A user-defined scalar function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The body is an expression, not a statement list.</b> That is the load-bearing decision of
    /// phase 9d and it is what makes the rest of the subsystem small: invoking a function becomes a
    /// substitution inside <c>ExpressionEvaluator</c>, evaluated against a row built from the
    /// arguments, rather than a re-entry into <c>StatementExecutor</c>. So a function consumes no
    /// execution nesting, cannot open a transaction, and cannot reach the row-loop hazards that
    /// constrain a trigger body. Measured before it was chosen: a parsed expression evaluates
    /// against a synthetic parameter row, and that row shadows the caller's outer row correctly.
    /// </para>
    /// <para>
    /// The cost, stated where it is paid: PostgreSQL's <c>LANGUAGE SQL</c> functions may be
    /// <c>SELECT</c>-bodied and may return a table, and neither is expressible here. See
    /// <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 1.
    /// </para>
    /// <para>
    /// Stored as a tree with no text beside it, per phase 8: <c>INFORMATION_SCHEMA.ROUTINES</c>
    /// renders <see cref="DisplayBody"/> on demand. There is no legacy text field here because there
    /// is no legacy - this type is new in the release that introduces it, so the two-copy hazard
    /// never gets a chance to start.
    /// </para>
    /// </remarks>
    [MemoryPackable]
    public sealed partial class DefinitionFunction : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not DefinitionFunction other)
                return false;

            return Name.Is(other.Name)
                   && ReturnType.Is(other.ReturnType)
                   && IsDeterministic.Is(other.IsDeterministic)
                   && Parameters.Is(other.Parameters)
                   && Body.Check(other.Body);
        }

        public override DefinitionFunction Clone()
        {
            return new DefinitionFunction
            {
                Name = Name,
                ReturnType = ReturnType,
                IsDeterministic = IsDeterministic,
                Parameters = Parameters?.Select(p => p.Clone()).ToArray(),
                Body = Body.Clone() as WitSqlExpression ?? Body
            };
        }

        #endregion

        #region Functions

        public override string ToString() => $"FUNCTION {Name} RETURNS {ReturnType}";

        /// <summary>
        /// The body as SQL for <c>INFORMATION_SCHEMA.ROUTINES</c> to report, rendered from the tree.
        /// </summary>
        /// <remarks>
        /// Rendered on demand and memoised, never stored. Storing a description beside the thing it
        /// describes is what broke <c>ALTER COLUMN SET DEFAULT</c> in phase 8: one write path updated
        /// the text and not the tree, and the catalog then described something the engine was not
        /// doing. And nothing may ask this rendering a question about the routine -
        /// <see cref="IsDeterministic"/> is decided from the tree.
        /// </remarks>
        public string? DisplayBody() => m_displayBody ??= SchemaText.Render(Body);

        #endregion

        #region Fields

        private string? m_displayBody;

        #endregion

        #region Properties

        /// <summary>
        /// The function name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// The parameters, in declaration order.
        /// </summary>
        [MemoryPackOrder(1)]
        public IReadOnlyList<DefinitionRoutineParameter>? Parameters { get; init; }

        /// <summary>
        /// The declared return type.
        /// </summary>
        [MemoryPackOrder(2)]
        public required WitDataType ReturnType { get; init; }

        /// <summary>
        /// The body: one expression over the parameters.
        /// </summary>
        [MemoryPackOrder(3)]
        public required WitSqlExpression Body { get; init; }

        /// <summary>
        /// Whether the body gives the same answer every time it is evaluated for the same arguments.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Decided once, at declaration, by walking the body - a subquery or a clock/generator
        /// function makes it false - and stored, because the answer cannot change afterwards: the
        /// body is immutable and a function may not call another function that could.
        /// </para>
        /// <para>
        /// It is what decides whether the function may appear in an index expression, where a key is
        /// computed once at write time and never recomputed. Same rule, same predicate, as the one
        /// that refuses a raw subquery or <c>RANDOM()</c> there.
        /// </para>
        /// </remarks>
        [MemoryPackOrder(4)]
        public required bool IsDeterministic { get; init; }

        #endregion
    }
}
