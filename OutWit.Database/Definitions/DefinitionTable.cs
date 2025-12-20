using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Common.Collections;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database table.
    /// </summary>
    public sealed class DefinitionTable : ModelBase
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
                   && ForeignKeys.Is(other.ForeignKeys)
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
                ForeignKeys = ForeignKeys?.Select(key => key.Clone()).ToList(),
                UniqueConstraints = UniqueConstraints?.Select(list => list.ToList()).ToList()
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

        public override string ToString()
        {
            return $"TABLE {Name} ({Columns.Count} columns)";
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the table name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the columns in this table.
        /// </summary>
        public required IReadOnlyList<DefinitionColumn> Columns { get; init; }

        /// <summary>
        /// Gets the primary key columns (if any).
        /// </summary>
        public IReadOnlyList<string>? PrimaryKey { get; init; }

        /// <summary>
        /// Gets the row ID column name (auto-generated if not specified).
        /// </summary>
        public string RowIdColumn { get; init; } = ROW_ID_COLUMN;

        /// <summary>
        /// Gets whether the table uses auto-incrementing row IDs.
        /// </summary>
        public bool AutoIncrementRowId { get; init; } = true;

        /// <summary>
        /// Gets the table-level CHECK constraint expressions as SQL text.
        /// </summary>
        public IReadOnlyList<string>? CheckExpressions { get; init; }

        /// <summary>
        /// Gets the table-level foreign key constraints.
        /// </summary>
        public IReadOnlyList<DefinitionForeignKey>? ForeignKeys { get; init; }

        /// <summary>
        /// Gets the table-level UNIQUE constraints (each is a list of column names).
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>>? UniqueConstraints { get; init; }

        #endregion
    }
}