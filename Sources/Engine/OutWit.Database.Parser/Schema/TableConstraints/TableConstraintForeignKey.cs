using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.TableConstraints
{
    [MemoryPackable]
    public sealed partial class TableConstraintForeignKey : TableConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not TableConstraintForeignKey constraint)
                return false;

            return base.Is(constraint, tolerance)
                   && Columns.Is(constraint.Columns)
                   && ForeignTable.Is(constraint.ForeignTable)
                   && ForeignColumns.Is(constraint.ForeignColumns)
                   && OnDelete.Is(constraint.OnDelete)
                   && OnUpdate.Is(constraint.OnUpdate);
        }

        public override TableConstraintForeignKey Clone()
        {
            return new TableConstraintForeignKey
            {
                Name = Name,
                Columns = Columns.ToList(),
                ForeignTable = ForeignTable,
                ForeignColumns = ForeignColumns?.ToList(),
                OnDelete = OnDelete,
                OnUpdate = OnUpdate
            };
        }

        #endregion

        #region Properties

        public required IReadOnlyList<string> Columns { get; init; }
        public required string ForeignTable { get; init; }
        public IReadOnlyList<string>? ForeignColumns { get; init; }
        public ReferenceActionType OnDelete { get; init; }
        public ReferenceActionType OnUpdate { get; init; }

        #endregion
    }
}
