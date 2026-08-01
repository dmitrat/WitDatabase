using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    public sealed partial class ColumnConstraintReferences : ColumnConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not ColumnConstraintReferences constraint)
                return false;

            return ForeignTable.Is(constraint.ForeignTable)
                   && ForeignColumn.Is(constraint.ForeignColumn)
                   && OnDelete.Is(constraint.OnDelete)
                   && OnUpdate.Is(constraint.OnUpdate);
        }

        public override ColumnConstraintReferences Clone()
        {
            return new ColumnConstraintReferences
            {
                ForeignTable = ForeignTable,
                ForeignColumn = ForeignColumn,
                OnDelete = OnDelete,
                OnUpdate = OnUpdate
            };
        }

        #endregion


        #region Properties

        [ToString]
        public required string ForeignTable { get; init; }

        [ToString]
        public string? ForeignColumn { get; init; }
        public ReferenceActionType OnDelete { get; init; }
        public ReferenceActionType OnUpdate { get; init; }

        #endregion
    }
}
