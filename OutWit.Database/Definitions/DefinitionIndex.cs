using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Common.Collections;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines an index on a table.
    /// </summary>
    public sealed class DefinitionIndex : ModelBase
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
                && IsPrimaryKey.Is(other.IsPrimaryKey);
        }

        public override DefinitionIndex Clone()
        {
            return new DefinitionIndex
            {
                Name = Name,
                TableName = TableName,
                Columns = Columns.ToArray(),
                IsUnique = IsUnique,
                IsPrimaryKey = IsPrimaryKey
            };
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"INDEX {Name} ON {TableName}({string.Join(", ", Columns)}){(IsUnique ? " UNIQUE" : "")}";
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the index name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the table this index belongs to.
        /// </summary>
        public required string TableName { get; init; }

        /// <summary>
        /// Gets the columns in this index.
        /// </summary>
        public required IReadOnlyList<string> Columns { get; init; }

        /// <summary>
        /// Gets whether this is a unique index.
        /// </summary>
        public bool IsUnique { get; init; }

        /// <summary>
        /// Gets whether this is the primary key index.
        /// </summary>
        public bool IsPrimaryKey { get; init; }

        #endregion
    }
}