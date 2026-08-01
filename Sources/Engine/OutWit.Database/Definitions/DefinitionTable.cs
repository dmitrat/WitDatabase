using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Attributes;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Serializers;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database table.
    /// </summary>
    [MemoryPackable]
    public sealed partial class DefinitionTable : ModelBase
    {
        #region Constants

        private const string ROW_ID_COLUMN = "_rowid";

        #endregion
        
        #region ModelBase

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not DefinitionTable other)
                return false;

            return Name.Is(other.Name)
                   && Columns.Is(other.Columns)
                   && PrimaryKey.Is(other.PrimaryKey)
                   && RowIdColumn.Is(other.RowIdColumn)
                   && AutoIncrementRowId.Is(other.AutoIncrementRowId)
                   && CheckExpressions.Is(other.CheckExpressions)
                   && ChecksAre(other)
                   && ForeignKeys.Is(other.ForeignKeys)
                   && NamedConstraints.Is(other.NamedConstraints)
                   && UniqueConstraints?
                       .SelectMany(x=>x)
                       .ToList()
                       .Check(other.UniqueConstraints?
                           .SelectMany(x=>x)
                           .ToList()) == true;
        }

        public override DefinitionTable Clone()
        {
            return new DefinitionTable
            {
                Name = Name,
                Columns = Columns.Select(column => column.Clone()).ToList(),
                PrimaryKey = PrimaryKey?.ToList(),
                RowIdColumn = RowIdColumn,
                AutoIncrementRowId = AutoIncrementRowId,
                CheckExpressions = CheckExpressions?.ToList(),
                Checks = Checks?.Select(c => c.Clone() as WitSqlExpression).ToList()!,
                ForeignKeys = ForeignKeys?.Select(key => key.Clone()).ToList(),
                UniqueConstraints = UniqueConstraints?.Select(list => list.ToList()).ToList(),
                NamedConstraints = NamedConstraints?.Select(c => c.Clone()).ToList()
            };
        }

        #endregion

        #region Functions

        /// <summary>
        /// Gets a column by name.
        /// </summary>
        public DefinitionColumn? GetColumn(string name)
        {
            return Columns.FirstOrDefault(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the ordinal of a column by name.
        /// </summary>
        public int GetOrdinal(string name)
        {
            for (int i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Gets a named constraint by name.
        /// </summary>
        public DefinitionNamedConstraint? GetConstraint(string constraintName)
        {
            return NamedConstraints?.FirstOrDefault(c => 
                c.Name.Equals(constraintName, StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString()
        {
            return $"TABLE {Name} ({Columns.Count} columns)";
        }

        #endregion

        #region Fields

        private IReadOnlyList<WitSqlExpression>? m_legacyChecks;
        private IReadOnlyList<string?>? m_displayChecks;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the table name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the columns in this table.
        /// </summary>
        [MemoryPackOrder(1)]
        public required IReadOnlyList<DefinitionColumn> Columns { get; init; }

        /// <summary>
        /// Gets the primary key columns (if any).
        /// </summary>
        [MemoryPackOrder(2)]
        public IReadOnlyList<string>? PrimaryKey { get; init; }

        /// <summary>
        /// Gets the row ID column name (auto-generated if not specified).
        /// </summary>
        [MemoryPackOrder(3)]
        public string RowIdColumn { get; init; } = ROW_ID_COLUMN;

        /// <summary>
        /// Gets whether the table uses auto-incrementing row IDs.
        /// </summary>
        [MemoryPackOrder(4)]
        public bool AutoIncrementRowId { get; init; } = true;

        /// <summary>
        /// Gets the table-level CHECK constraint expressions as SQL text.
        /// </summary>
        [MemoryPackOrder(5)]
        public IReadOnlyList<string>? CheckExpressions { get; init; }

        /// <summary>
        /// The table-level <c>CHECK</c> conditions, stored as trees.
        /// <see cref="CheckExpressions"/> renders them for <c>INFORMATION_SCHEMA</c>.
        /// </summary>
        [MemoryPackOrder(9)]
        public IReadOnlyList<WitSqlExpression>? Checks { get; init; }

        #endregion

        #region Resolving

        /// <summary>
        /// The table-level conditions as trees, parsing the stored text only for a table written
        /// before 9.0.0.
        /// </summary>
        /// <summary>The table-level conditions as SQL, rendered from the trees.</summary>
        public IReadOnlyList<string?> DisplayChecks() =>
            m_displayChecks ??= ResolveChecks().Select(SchemaText.Render).ToList();

        public IReadOnlyList<WitSqlExpression> ResolveChecks()
        {
            if (Checks is not null)
                return Checks;

            if (CheckExpressions is null)
                return [];

            return m_legacyChecks ??= CheckExpressions
                .Where(text => !string.IsNullOrEmpty(text))
                .Select(WitSql.ParseExpression)
                .ToList();
        }

        private bool ChecksAre(DefinitionTable other)
        {
            if (Checks is null || other.Checks is null)
                return Checks is null && other.Checks is null;

            if (Checks.Count != other.Checks.Count)
                return false;

            return !Checks.Where((c, i) => !c.Is(other.Checks[i])).Any();
        }

        /// <summary>
        /// Gets the table-level foreign key constraints.
        /// </summary>
        [MemoryPackOrder(6)]
        public IReadOnlyList<DefinitionForeignKey>? ForeignKeys { get; init; }

        /// <summary>
        /// Gets the table-level UNIQUE constraints (each is a list of column names).
        /// </summary>
        [MemoryPackOrder(7)]
        [ReadOnlyStringMatrixFormatter]
        public IReadOnlyList<IReadOnlyList<string>>? UniqueConstraints { get; init; }

        /// <summary>
        /// Gets the named constraints on this table.
        /// Named constraints can be dropped by name via ALTER TABLE DROP CONSTRAINT.
        /// </summary>
        [MemoryPackOrder(8)]
        public IReadOnlyList<DefinitionNamedConstraint>? NamedConstraints { get; init; }

        #endregion
    }
}