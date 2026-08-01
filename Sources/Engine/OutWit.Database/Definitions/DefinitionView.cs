using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database view.
    /// </summary>
    [MemoryPackable]
    public sealed partial class DefinitionView : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if(modelBase is not DefinitionView other)
                return false;

            return Name.Is(other.Name)
                && SelectSql.Is(other.SelectSql)
                && Query.Check(other.Query)
                && ColumnAliases.Is(other.ColumnAliases);
        }

        public override DefinitionView Clone()
        {
            return new DefinitionView
            {
                Name = Name,
                SelectSql = SelectSql,
                Query = Query?.Clone(),
                ColumnAliases = ColumnAliases?.ToArray(),
            };
        }

        #endregion

        #region Functions

        /// <summary>
        /// The body as SQL for <c>INFORMATION_SCHEMA.VIEWS</c> to report, or <c>null</c> when it
        /// cannot be written down faithfully.
        /// </summary>
        /// <remarks>
        /// Rendered from the tree on demand rather than stored beside it. Storing both is what broke
        /// <c>ALTER COLUMN SET DEFAULT</c> during this phase: one write path updated the text and
        /// not the tree, and the catalog then described something the engine was not doing. One copy
        /// of a fact cannot disagree with itself.
        /// </remarks>
        public string? DisplayQuery() => m_display ??= SchemaText.Render(Query) ?? SelectSql;

        /// <summary>
        /// The view's body as a tree, which is what the planner needs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Prefers <see cref="Query"/>, the stored tree. Falls back to parsing
        /// <see cref="SelectSql"/> for a view written before 9.0.0 - that text is what the old
        /// serializer produced, so the fallback inherits whatever that serializer lost. It cannot do
        /// better: the loss happened when the view was created and the information is not in the
        /// file.
        /// </para>
        /// <para>
        /// One implementation, called by both readers. Two would be one route that reaches the tree
        /// and one that does not, which is how this project has lost four defects before.
        /// </para>
        /// </remarks>
        public WitSqlStatementSelect ResolveQuery()
        {
            if (Query is not null)
                return Query;

            return WitSql.ParseStatement(SelectSql ?? string.Empty) as WitSqlStatementSelect
                   ?? throw new InvalidOperationException(
                       $"View '{Name}' does not contain a SELECT statement.");
        }

        public override string ToString()
        {
            return $"VIEW {Name} AS {SelectSql}";
        }

        #endregion

        #region Fields

        private string? m_display;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the view name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the SELECT SQL that defined the view in a database written before 9.0.0.
        /// </summary>
        /// <remarks>
        /// <b>Legacy.</b> Written by 8.x and earlier, when this text was the schema. Nothing writes
        /// it from 9.0.0 - the tree is the schema and the text for <c>INFORMATION_SCHEMA</c> is
        /// rendered from it on demand. Kept so a file written before 9.0.0 still opens.
        /// </remarks>
        [MemoryPackOrder(1)]
        public string? SelectSql { get; init; }

        /// <summary>
        /// Gets the optional column aliases for the view.
        /// </summary>
        [MemoryPackOrder(2)]
        public IReadOnlyList<string>? ColumnAliases { get; init; }

        /// <summary>
        /// The view's body, stored as a tree. This is what the view <b>is</b>;
        /// <see cref="SelectSql"/> is a rendering of it for <c>INFORMATION_SCHEMA</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Added in 9.0.0. Until then a view was persisted only as text produced by the expression
        /// serializer, and re-parsed on every query - so anything that serializer could not write
        /// was lost at creation time and never recoverable. Measured: a view over
        /// <c>SELECT … UNION SELECT …</c> was stored as its first branch alone, and then answered
        /// queries, without error, from half its rows.
        /// </para>
        /// <para>
        /// Null in files written before 9.0.0; see <see cref="ResolveQuery"/>.
        /// </para>
        /// </remarks>
        [MemoryPackOrder(3)]
        public WitSqlStatementSelect? Query { get; init; }

        #endregion
    }
}