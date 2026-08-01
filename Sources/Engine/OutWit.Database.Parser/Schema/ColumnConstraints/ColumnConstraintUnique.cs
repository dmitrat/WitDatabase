using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    public sealed partial class ColumnConstraintUnique : ColumnConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            return other is ColumnConstraintUnique;
        }

        public override ColumnConstraintUnique Clone()
        {
            return new ColumnConstraintUnique();
        }

        #endregion
    }
}
