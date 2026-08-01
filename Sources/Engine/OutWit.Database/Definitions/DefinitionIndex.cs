using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Common.Collections;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Analysis;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines an index on a table.
    /// </summary>
    [MemoryPackable]
    public sealed partial class DefinitionIndex : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not DefinitionIndex other)
                return false;

            return Name.Is(other.Name)
                && TableName.Is(other.TableName)
                && Columns.Is(other.Columns)
                && IsUnique.Is(other.IsUnique)
                && IsPrimaryKey.Is(other.IsPrimaryKey)
                && IsImplicit.Is(other.IsImplicit)
                && WhereExpression.Is(other.WhereExpression)
                && Where.Check(other.Where)
                && ExpressionsAre(other)
                && ExpressionColumns.Is(other.ExpressionColumns)
                && IncludeColumns.Is(other.IncludeColumns)
                && ColumnDescending.Is(other.ColumnDescending);
        }

        /// <summary>
        /// The filter as a tree, parsing the stored text only for an index written before 9.0.0.
        /// </summary>
        /// <remarks>
        /// One implementation, so no caller can be the route that misses the stored tree and keeps
        /// paying for a parse.
        /// </remarks>
        /// <summary>
        /// The filter as SQL for <c>INFORMATION_SCHEMA.INDEXES</c>, rendered from the tree.
        /// </summary>
        public string? DisplayWhere() => m_displayWhere ??= SchemaText.Render(Where) ?? WhereExpression;

        /// <summary>The indexed expression at <paramref name="columnIndex"/>, as SQL.</summary>
        public string? DisplayColumnExpression(int columnIndex) =>
            SchemaText.Render(ResolveColumnExpression(columnIndex));

        public WitSqlExpression? ResolveWhere()
        {
            if (Where is not null)
                return Where;

            if (string.IsNullOrEmpty(WhereExpression))
                return null;

            // Cached because this is called once per row on the write path. Not serialized: it is
            // derived from WhereExpression, which is.
            return m_legacyWhere ??= WitSql.ParseExpression(WhereExpression);
        }

        public override DefinitionIndex Clone()
        {
            return new DefinitionIndex
            {
                Name = Name,
                TableName = TableName,
                Columns = Columns.ToArray(),
                IsUnique = IsUnique,
                IsPrimaryKey = IsPrimaryKey,
                IsImplicit = IsImplicit,
                WhereExpression = WhereExpression,
                Where = Where?.Clone() as WitSqlExpression,
                Expressions = Expressions?.Select(e => e?.Clone() as WitSqlExpression).ToList(),
                ExpressionColumns = ExpressionColumns?.ToArray(),
                IncludeColumns = IncludeColumns?.ToArray(),
                ColumnDescending = ColumnDescending?.ToArray()
            };
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            var columnsStr = string.Join(", ", Columns.Select((col, idx) =>
            {
                var expr = ExpressionColumns != null && idx < ExpressionColumns.Count 
                    ? ExpressionColumns[idx] 
                    : null;
                var desc = ColumnDescending != null && idx < ColumnDescending.Count && ColumnDescending[idx]
                    ? " DESC"
                    : "";
                return (expr ?? col) + desc;
            }));
            
            var parts = new List<string>
            {
                IsUnique ? "UNIQUE INDEX" : "INDEX",
                Name,
                "ON",
                TableName,
                $"({columnsStr})"
            };

            if (IncludeColumns is { Count: > 0 })
                parts.Add($"INCLUDE ({string.Join(", ", IncludeColumns)})");
            
            if (!string.IsNullOrEmpty(WhereExpression))
                parts.Add($"WHERE {WhereExpression}");

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Gets whether a specific column is descending.
        /// </summary>
        public bool IsColumnDescending(int columnIndex)
        {
            return ColumnDescending != null && 
                   columnIndex < ColumnDescending.Count && 
                   ColumnDescending[columnIndex];
        }

        /// <summary>
        /// Gets the expression for a column, or null if it's a simple column reference.
        /// </summary>
        public string? GetColumnExpression(int columnIndex)
        {
            return ExpressionColumns != null && 
                   columnIndex < ExpressionColumns.Count 
                ? ExpressionColumns[columnIndex] 
                : null;
        }

        /// <summary>
        /// The expression indexed at <paramref name="columnIndex"/>, as a tree, falling back to the
        /// stored text for an index written before 9.0.0.
        /// </summary>
        /// <remarks>
        /// Called once per indexed row on the write path, which is why the legacy parse is cached
        /// rather than repeated.
        /// </remarks>
        public WitSqlExpression? ResolveColumnExpression(int columnIndex)
        {
            if (Expressions != null && columnIndex < Expressions.Count && Expressions[columnIndex] != null)
                return Expressions[columnIndex];

            var text = GetColumnExpression(columnIndex);

            if (string.IsNullOrEmpty(text))
                return null;

            m_legacyExpressions ??= new Dictionary<string, WitSqlExpression>(StringComparer.Ordinal);

            if (!m_legacyExpressions.TryGetValue(text, out var parsed))
                m_legacyExpressions[text] = parsed = WitSql.ParseExpression(text);

            return parsed;
        }

        /// <summary>
        /// Every column this index reads - its key columns, its filter, and any indexed expressions.
        /// </summary>
        /// <remarks>
        /// Used to decide whether a write has to maintain this index. Computed once and kept,
        /// because the answer depends only on the definition and the question is asked per write.
        /// </remarks>
        public IReadOnlySet<string> ReferencedColumns()
        {
            if (m_referencedColumns is not null)
                return m_referencedColumns;

            var names = new HashSet<string>(Columns, StringComparer.OrdinalIgnoreCase);

            names.UnionWith(WitSqlColumnReferences.Collect(ResolveWhere()));

            for (var i = 0; i < Columns.Count; i++)
                names.UnionWith(WitSqlColumnReferences.Collect(ResolveColumnExpression(i)));

            return m_referencedColumns = names;
        }

        private bool ExpressionsAre(DefinitionIndex other)
        {
            if (Expressions is null || other.Expressions is null)
                return Expressions is null && other.Expressions is null;

            if (Expressions.Count != other.Expressions.Count)
                return false;

            return !Expressions.Where((e, i) => !e.Check(other.Expressions[i])).Any();
        }

        #endregion

        #region Fields

        private WitSqlExpression? m_legacyWhere;
        private Dictionary<string, WitSqlExpression>? m_legacyExpressions;
        private IReadOnlySet<string>? m_referencedColumns;
        private string? m_displayWhere;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the index name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the table this index belongs to.
        /// </summary>
        [MemoryPackOrder(1)]
        public required string TableName { get; init; }

        /// <summary>
        /// Gets the column names in this index.
        /// For expression indexes, this contains the base column names (may be empty for pure expressions).
        /// </summary>
        [MemoryPackOrder(2)]
        public required IReadOnlyList<string> Columns { get; init; }

        /// <summary>
        /// Gets whether this is a unique index.
        /// </summary>
        [MemoryPackOrder(3)]
        public bool IsUnique { get; init; }

        /// <summary>
        /// Gets whether this is the primary key index.
        /// </summary>
        [MemoryPackOrder(4)]
        public bool IsPrimaryKey { get; init; }

        /// <summary>
        /// Gets the WHERE expression for partial/filtered indexes.
        /// Null for non-filtered indexes.
        /// Example: "Status = 'active'" for CREATE INDEX ... WHERE Status = 'active'
        /// </summary>
        [MemoryPackOrder(5)]
        public string? WhereExpression { get; init; }

        /// <summary>
        /// Gets the expressions for expression-based indexes.
        /// Each entry corresponds to a column in Columns.
        /// Null entry means the column itself, non-null is an expression like "LOWER(Email)".
        /// </summary>
        [MemoryPackOrder(6)]
        public IReadOnlyList<string?>? ExpressionColumns { get; init; }

        /// <summary>
        /// Gets the INCLUDE columns for covering indexes.
        /// These columns are stored in the leaf pages but not in the index keys.
        /// </summary>
        [MemoryPackOrder(7)]
        public IReadOnlyList<string>? IncludeColumns { get; init; }

        /// <summary>
        /// Gets whether each column is descending (true) or ascending (false, default).
        /// Each entry corresponds to a column in Columns.
        /// </summary>
        [MemoryPackOrder(8)]
        public IReadOnlyList<bool>? ColumnDescending { get; init; }

        /// <summary>
        /// Gets whether this is an implicit index (auto-created for PRIMARY KEY).
        /// Implicit indexes are not shown in INFORMATION_SCHEMA.
        /// </summary>
        [MemoryPackOrder(9)]
        public bool IsImplicit { get; init; }

        /// <summary>
        /// The partial index's filter, stored as a tree. <see cref="WhereExpression"/> is a
        /// rendering of it for <c>INFORMATION_SCHEMA</c> to report.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Added in 9.0.0. Until then the filter existed only as text, and was re-parsed with ANTLR
        /// <b>once per row written</b> - see <c>EvaluatePartialIndexCondition</c>, which takes a
        /// single row. A subquery in the filter was also stored as the literal <c>SELECT ...</c>,
        /// so it could never be evaluated again at all.
        /// </para>
        /// <para>Null in files written before 9.0.0; see <see cref="ResolveWhere"/>.</para>
        /// </remarks>
        [MemoryPackOrder(10)]
        public WitSqlExpression? Where { get; init; }

        /// <summary>
        /// The indexed expressions, stored as trees, positionally matching
        /// <see cref="ExpressionColumns"/> - which renders them for <c>INFORMATION_SCHEMA</c>.
        /// </summary>
        [MemoryPackOrder(11)]
        public IReadOnlyList<WitSqlExpression?>? Expressions { get; init; }

        /// <summary>
        /// Gets whether this is a partial/filtered index.
        /// </summary>
        [MemoryPackIgnore]
        /// <remarks>
        /// Answers from the stored filter, not from its description. It read the description until
        /// 9.0.0, and the description is now rendered on demand and may be absent - a partial index
        /// would then have reported itself as unfiltered.
        /// </remarks>
        public bool IsFiltered => ResolveWhere() is not null;

        /// <summary>
        /// Gets whether this index has expression columns.
        /// </summary>
        [MemoryPackIgnore]
        /// <remarks>
        /// Answers from the stored expressions rather than from their description, for the same
        /// reason as <see cref="IsFiltered"/>.
        /// </remarks>
        public bool HasExpressions =>
            (Expressions != null && Expressions.Any(e => e != null))
            || (ExpressionColumns != null && ExpressionColumns.Any(e => e != null));

        /// <summary>
        /// Gets whether this is a covering index (has INCLUDE columns).
        /// </summary>
        [MemoryPackIgnore]
        public bool IsCovering => IncludeColumns is { Count: > 0 };

        #endregion
    }
}