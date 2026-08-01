using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    public sealed partial class ColumnConstraintCheck : ColumnConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ColumnConstraintCheck constraint)
                return false;

            return Condition.Is(constraint.Condition, tolerance);
        }

        public override ColumnConstraintCheck Clone()
        {
            return new ColumnConstraintCheck
            {
                Condition = (WitSqlExpression)Condition.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required WitSqlExpression Condition { get; init; }

        #endregion
    }
}
