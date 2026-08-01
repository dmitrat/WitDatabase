using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database trigger.
    /// </summary>
    [MemoryPackable]
    public sealed partial class DefinitionTrigger : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if(modelBase is not DefinitionTrigger other)
                return false;

            return Name.Is(other.Name)
                   && TableName.Is(other.TableName)
                   && Time.Is(other.Time)
                   && Event.Is(other.Event)
                   && UpdateColumns.Is(other.UpdateColumns)
                   && ForEachRow.Is(other.ForEachRow)
                   && WhenCondition.Is(other.WhenCondition)
                   && Body.Is(other.Body)
                   && When.Check(other.When)
                   && StatementsAre(other);
        }

        public override DefinitionTrigger Clone()
        {
            return new DefinitionTrigger
            {
                Name = Name,
                TableName = TableName,
                Time = Time,
                Event = Event,
                UpdateColumns = UpdateColumns?.ToArray(),
                ForEachRow = ForEachRow,
                WhenCondition = WhenCondition,
                Body = Body,
                When = When?.Clone() as WitSqlExpression,
                Statements = Statements?.Select(s => (WitSqlStatement)s.Clone()).ToList()
            };
        }

        private bool StatementsAre(DefinitionTrigger other)
        {
            if (Statements is null || other.Statements is null)
                return Statements is null && other.Statements is null;

            if (Statements.Count != other.Statements.Count)
                return false;

            return !Statements.Where((s, i) => !s.Is(other.Statements[i])).Any();
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"TRIGGER {Name} {Time} {Event} ON {TableName}";
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the trigger name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the table this trigger is attached to.
        /// </summary>
        [MemoryPackOrder(1)]
        public required string TableName { get; init; }

        /// <summary>
        /// Gets when the trigger fires relative to the operation.
        /// </summary>
        [MemoryPackOrder(2)]
        public required TriggerTime Time { get; init; }

        /// <summary>
        /// Gets the event that fires the trigger.
        /// </summary>
        [MemoryPackOrder(3)]
        public required TriggerEvent Event { get; init; }

        /// <summary>
        /// Gets the columns for UPDATE OF triggers. Null means all columns.
        /// </summary>
        [MemoryPackOrder(4)]
        public IReadOnlyList<string>? UpdateColumns { get; init; }

        /// <summary>
        /// Gets whether this is a FOR EACH ROW trigger.
        /// </summary>
        [MemoryPackOrder(5)]
        public bool ForEachRow { get; init; }

        /// <summary>
        /// Gets the optional WHEN condition (SQL expression).
        /// </summary>
        [MemoryPackOrder(6)]
        public string? WhenCondition { get; init; }

        /// <summary>
        /// Gets the trigger body (SQL statements).
        /// </summary>
        [MemoryPackOrder(7)]
        public string? Body { get; init; }

        /// <summary>
        /// The <c>WHEN</c> condition, stored as a tree. <see cref="WhenCondition"/> renders it for
        /// <c>INFORMATION_SCHEMA</c>.
        /// </summary>
        [MemoryPackOrder(8)]
        public WitSqlExpression? When { get; init; }

        /// <summary>
        /// The trigger body, stored as statements. <see cref="Body"/> renders them for
        /// <c>INFORMATION_SCHEMA</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Added in 9.0.0. Until then the body was a single string of statements joined with
        /// <c>;</c> and split apart again on <c>;</c> at execution time - so a statement containing
        /// a semicolon inside a string literal was torn in half. Storing the statements keeps them
        /// statements, and no splitting is needed.
        /// </para>
        /// <para>
        /// It also carried the rendering losses: a body of
        /// <c>INSERT … ON CONFLICT DO NOTHING</c> was stored as a plain <c>INSERT</c>, measured
        /// 2026-07-31, so the trigger's conflict handling vanished without a word.
        /// </para>
        /// </remarks>
        [MemoryPackOrder(9)]
        public IReadOnlyList<WitSqlStatement>? Statements { get; init; }

        #endregion

        #region Fields

        private string? m_displayBody;

        #endregion

        #region Resolving

        /// <summary>The <c>WHEN</c> condition as a tree, falling back to the stored text.</summary>
        /// <summary>The <c>WHEN</c> condition as SQL for <c>INFORMATION_SCHEMA</c>.</summary>
        public string? DisplayWhen() => SchemaText.Render(When) ?? WhenCondition;

        /// <summary>The body as SQL for <c>INFORMATION_SCHEMA</c>, rendered from the statements.</summary>
        public string? DisplayBody() => m_displayBody ??= SchemaText.Render(Statements) ?? Body;

        public WitSqlExpression? ResolveWhen() =>
            When ?? (string.IsNullOrEmpty(WhenCondition) ? null : WitSql.ParseExpression(WhenCondition));

        /// <summary>
        /// The body as statements, falling back to splitting and parsing the stored text for a
        /// trigger written before 9.0.0.
        /// </summary>
        public IReadOnlyList<WitSqlStatement> ResolveStatements()
        {
            if (Statements is not null)
                return Statements;

            return (Body ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(sql => !string.IsNullOrWhiteSpace(sql) && !sql.StartsWith("/*"))
                .Select(WitSql.ParseStatement)
                .ToList();
        }

        #endregion
    }

    /// <summary>
    /// The event that fires a trigger.
    /// </summary>
    public enum TriggerEvent
    {
        /// <summary>
        /// Trigger fires on INSERT operations.
        /// </summary>
        Insert,

        /// <summary>
        /// Trigger fires on UPDATE operations.
        /// </summary>
        Update,

        /// <summary>
        /// Trigger fires on DELETE operations.
        /// </summary>
        Delete
    }

    /// <summary>
    /// When a trigger fires relative to the operation.
    /// </summary>
    public enum TriggerTime
    {
        /// <summary>
        /// Trigger fires before the operation (can modify or cancel).
        /// </summary>
        Before,

        /// <summary>
        /// Trigger fires after the operation completes.
        /// </summary>
        After,

        /// <summary>
        /// Trigger replaces the operation entirely.
        /// </summary>
        InsteadOf
    }
}