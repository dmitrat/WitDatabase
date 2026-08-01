using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    public sealed partial class ColumnConstraintNotNull : ColumnConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ColumnConstraintNotNull constraint)
                return false;

            return IsNotNull.Is(constraint.IsNotNull);
        }

        public override ColumnConstraintNotNull Clone()
        {
            return new ColumnConstraintNotNull
            {
                IsNotNull = IsNotNull
            };
        }

        #endregion

        #region Properties

        [ToString]
        public bool IsNotNull { get; init; } = true;

        #endregion
    }
}
