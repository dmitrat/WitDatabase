using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.TableSources
{
    [MemoryPackable]
    public sealed partial class TableSourceSimple : TableSource
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not TableSourceSimple simple)
                return false;

            return base.Is(simple, tolerance) 
                   && TableName.Is(simple.TableName);
        }

        public override TableSourceSimple Clone()
        {
            return new TableSourceSimple
            {
                TableName = TableName,
                Alias = Alias
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string TableName { get; init; }

        #endregion
    }
}
