using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Common.Collections;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.ColumnConstraints;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema
{
    [MemoryPackable]
    public sealed partial class WitSqlColumn : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not WitSqlColumn definition)
                return false;

            return Name.Is(definition.Name)
                   && DataType.Check(definition.DataType)
                   && Constraints.Is(definition.Constraints)
                   && ComputedExpression.Check(definition.ComputedExpression)
                   && ComputedType.Is(definition.ComputedType);
        }

        public override WitSqlColumn Clone()
        {
            return new WitSqlColumn
            {
                Name = Name,
                DataType = DataType?.Clone(),
                Constraints = Constraints?.Select(constraint => (ColumnConstraint)constraint.Clone()).ToList(),
                ComputedExpression = (WitSqlExpression?)ComputedExpression?.Clone(),
                ComputedType = ComputedType
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// Column name.
        /// </summary>
        [ToString]
        public required string Name { get; init; }

        /// <summary>
        /// Data type. Null for computed columns.
        /// </summary>
        [ToString]
        public WitSqlDataType? DataType { get; init; }

        /// <summary>
        /// Column constraints (NOT NULL, UNIQUE, etc.). Only for regular columns.
        /// </summary>
        public IReadOnlyList<ColumnConstraint>? Constraints { get; init; }

        /// <summary>
        /// Expression for computed column. Null for regular columns.
        /// </summary>
        public WitSqlExpression? ComputedExpression { get; init; }

        /// <summary>
        /// Type of computed column (Virtual, Stored, or None for regular columns).
        /// </summary>
        public ComputedColumnType ComputedType { get; init; }

        /// <summary>
        /// Returns true if this is a computed column.
        /// </summary>
        public bool IsComputed => ComputedExpression != null;

        #endregion
    }
}
