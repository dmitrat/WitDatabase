using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Database.Parser.Schema.TableConstraints
{
    [MemoryPackable]
    public sealed partial class TableConstraintPrimaryKey : TableConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not TableConstraintPrimaryKey constraint)
                return false;

            return base.Is(constraint, tolerance)
                   && Columns.Is(constraint.Columns);
        }

        public override TableConstraintPrimaryKey Clone()
        {
            return new TableConstraintPrimaryKey
            {
                Name = Name,
                Columns = Columns.ToList()
            };
        }

        #endregion

        #region Properties

        public required IReadOnlyList<string> Columns { get; init; }

        #endregion
    }
}
