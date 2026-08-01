using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    /// <summary>
    /// Represents ALTER TABLE ... DROP CONSTRAINT constraint_name action.
    /// </summary>
    [MemoryPackable]
    public partial class AlterActionDropConstraint : AlterAction
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionDropConstraint dropConstraint)
                return false;

            return ConstraintName.Is(dropConstraint.ConstraintName);
        }

        public override AlterActionDropConstraint Clone()
        {
            return new AlterActionDropConstraint
            {
                ConstraintName = ConstraintName
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The name of the constraint to drop.
        /// </summary>
        [ToString]
        public required string ConstraintName { get; init; }

        #endregion
    }
}
