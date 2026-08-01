using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.TableSources
{
    [MemoryPackable]
    // Persisted format. Tags are append-only: never renumber one and
    // never reuse a retired one, or old files read back as the wrong type.
    [MemoryPackUnion(0, typeof(TableSourceJoin))]
    [MemoryPackUnion(1, typeof(TableSourceSimple))]
    [MemoryPackUnion(2, typeof(TableSourceSubquery))]
    // Appended 2026-08-01. Tags are a persisted format: append only, never renumber.
    [MemoryPackUnion(3, typeof(TableSourceLateral))]
    public abstract partial class TableSource : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase modelBase, double tolerance = 1E-07)
        {
            if(modelBase is not TableSource other)
                return false;

            return Alias.Is(other.Alias);
        }

        #endregion

        #region Properties

        [ToString]
        public string? Alias { get; init; }

        #endregion
    }
}
