using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    public sealed partial class ColumnConstraintDefault : ColumnConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ColumnConstraintDefault constraint)
                return false;

            return Value.Is(constraint.Value, tolerance);
        }

        public override ColumnConstraintDefault Clone()
        {
            return new ColumnConstraintDefault
            {
                Value = (WitSqlExpression)Value.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required WitSqlExpression Value { get; init; }

        #endregion
    }
}
