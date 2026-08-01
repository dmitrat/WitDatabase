using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.TableConstraints;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    /// <summary>
    /// Represents ALTER TABLE ... ADD [CONSTRAINT name] constraint action.
    /// </summary>
    [MemoryPackable]
    public partial class AlterActionAddConstraint : AlterAction
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionAddConstraint addConstraint)
                return false;

            return Constraint.Check(addConstraint.Constraint);
        }

        public override AlterActionAddConstraint Clone()
        {
            return new AlterActionAddConstraint
            {
                Constraint = (TableConstraint?)Constraint?.Clone()
            };
        }

        #endregion

        #region Properties

        /// <summary>
        /// The constraint to add.
        /// </summary>
        public TableConstraint? Constraint { get; init; }

        #endregion
    }
}
