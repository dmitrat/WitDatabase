using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

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
                && ColumnAliases.Is(other.ColumnAliases);
        }

        public override DefinitionView Clone()
        {
            return new DefinitionView
            {
                Name = Name,
                SelectSql = SelectSql,
                ColumnAliases = ColumnAliases?.ToArray(),
            };
        }

        #endregion

        #region Functions

        public override string ToString()
        {
            return $"VIEW {Name} AS {SelectSql}";
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the view name.
        /// </summary>
        [MemoryPackOrder(0)]
        public required string Name { get; init; }

        /// <summary>
        /// Gets the SELECT SQL that defines the view.
        /// </summary>
        [MemoryPackOrder(1)]
        public required string SelectSql { get; init; }

        /// <summary>
        /// Gets the optional column aliases for the view.
        /// </summary>
        [MemoryPackOrder(2)]
        public IReadOnlyList<string>? ColumnAliases { get; init; }

        #endregion
    }
}