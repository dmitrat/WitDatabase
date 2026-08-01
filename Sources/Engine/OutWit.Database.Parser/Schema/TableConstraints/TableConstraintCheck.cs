using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.TableConstraints
{
    [MemoryPackable]
    public sealed partial class TableConstraintCheck : TableConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not TableConstraintCheck constraint)
                return false;

            return base.Is(constraint, tolerance)
                   && Condition.Is(constraint.Condition, tolerance);
        }

        public override TableConstraintCheck Clone()
        {
            return new TableConstraintCheck
            {
                Name = Name,
                Condition = (WitSqlExpression)Condition.Clone()
            };
        }

        #endregion

        #region Proeprties

        public required WitSqlExpression Condition { get; init; }

        #endregion
    }
}
