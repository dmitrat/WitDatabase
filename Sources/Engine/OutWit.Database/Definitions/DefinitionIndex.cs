using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Common.Collections;

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
                && ExpressionColumns.Is(other.ExpressionColumns)
                && IncludeColumns.Is(other.IncludeColumns)
                && ColumnDescending.Is(other.ColumnDescending);
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
        /// Gets whether this is a partial/filtered index.
        /// </summary>
        [MemoryPackIgnore]
        public bool IsFiltered => !string.IsNullOrEmpty(WhereExpression);

        /// <summary>
        /// Gets whether this index has expression columns.
        /// </summary>
        [MemoryPackIgnore]
        public bool HasExpressions => ExpressionColumns != null && ExpressionColumns.Any(e => e != null);

        /// <summary>
        /// Gets whether this is a covering index (has INCLUDE columns).
        /// </summary>
        [MemoryPackIgnore]
        public bool IsCovering => IncludeColumns is { Count: > 0 };

        #endregion
    }
}