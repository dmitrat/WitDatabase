using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.TableConstraints
{
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(TableConstraintCheck))]
    [MemoryPackUnion(1, typeof(TableConstraintForeignKey))]
    [MemoryPackUnion(2, typeof(TableConstraintPrimaryKey))]
    [MemoryPackUnion(3, typeof(TableConstraintUnique))]
    public abstract partial class TableConstraint : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if (modelBase is not TableConstraint other)
                return false;

            return Name.Is(other.Name);
        }

        #endregion

        #region Properties

        [ToString]
        public string? Name { get; init; }

        #endregion
    }
}
