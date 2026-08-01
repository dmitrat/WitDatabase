using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;

namespace OutWit.Database.Parser.Schema.TableConstraints
{
    [MemoryPackable]
    public sealed partial class TableConstraintUnique : TableConstraint
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not TableConstraintUnique constraint)
                return false;

            return base.Is(constraint, tolerance)
                   && Columns.Is(constraint.Columns);
        }

        public override TableConstraintUnique Clone()
        {
            return new TableConstraintUnique
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
