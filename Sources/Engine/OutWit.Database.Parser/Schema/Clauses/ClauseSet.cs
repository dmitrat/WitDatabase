using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.Clauses
{
    [MemoryPackable]
    public sealed partial class ClauseSet : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ClauseSet clause)
                return false;

            return ColumnName.Is(clause.ColumnName)
                   && Value.Is(clause.Value, tolerance);
        }

        public override ClauseSet Clone()
        {
            return new ClauseSet
            {
                ColumnName = ColumnName,
                Value = (WitSqlExpression)Value.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ColumnName { get; init; }

        [ToString]
        public required WitSqlExpression Value { get; init; }

        #endregion
    }
}
