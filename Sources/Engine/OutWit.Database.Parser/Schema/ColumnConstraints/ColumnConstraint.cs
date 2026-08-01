using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Database.Parser.Schema.ColumnConstraints
{
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(ColumnConstraintCheck))]
    [MemoryPackUnion(1, typeof(ColumnConstraintDefault))]
    [MemoryPackUnion(2, typeof(ColumnConstraintNotNull))]
    [MemoryPackUnion(3, typeof(ColumnConstraintPrimaryKey))]
    [MemoryPackUnion(4, typeof(ColumnConstraintReferences))]
    [MemoryPackUnion(5, typeof(ColumnConstraintUnique))]
    public abstract partial class ColumnConstraint : ModelBase
    {
    }
}
