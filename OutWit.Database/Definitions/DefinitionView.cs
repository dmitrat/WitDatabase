using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Database.Definitions
{
    /// <summary>
    /// Defines a database view.
    /// </summary>
    public sealed class DefinitionView : ModelBase
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

        #region Properies

        /// <summary>
        /// Gets the view name.
        /// </summary>
        public required string Name { get; init; }

        /// <summary>
        /// Gets the SELECT SQL that defines the view.
        /// </summary>
        public required string SelectSql { get; init; }

        /// <summary>
        /// Gets the optional column aliases for the view.
        /// </summary>
        public IReadOnlyList<string>? ColumnAliases { get; init; }

        #endregion
    }
}