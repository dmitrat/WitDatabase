using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    public sealed partial class ColumnConstraintPrimaryKey : ColumnConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ColumnConstraintPrimaryKey constraint)
                return false;

            return AutoIncrement.Is(constraint.AutoIncrement);
        }

        public override ColumnConstraintPrimaryKey Clone()
        {
            return new ColumnConstraintPrimaryKey
            {
                AutoIncrement = AutoIncrement
            };
        }

        #endregion

        #region Properties

        [ToString]
        public bool AutoIncrement { get; init; }

        #endregion
    }
}
