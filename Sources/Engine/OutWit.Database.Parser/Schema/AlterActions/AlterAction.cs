using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(AlterActionAddColumn))]
    [MemoryPackUnion(1, typeof(AlterActionAddConstraint))]
    [MemoryPackUnion(2, typeof(AlterActionAlterColumn))]
    [MemoryPackUnion(3, typeof(AlterActionDropColumn))]
    [MemoryPackUnion(4, typeof(AlterActionDropConstraint))]
    [MemoryPackUnion(5, typeof(AlterActionRenameColumn))]
    [MemoryPackUnion(6, typeof(AlterActionRenameTable))]
    public abstract partial class AlterAction : ModelBase
    {
    }
}
