using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    [MemoryPackable]
    public sealed partial class AlterActionAddColumn : AlterAction
    {
        #region ModelBase

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionAddColumn action)
                return false;

            return WitSqlColumn.Is(action.WitSqlColumn, tolerance);
        }

        public override AlterActionAddColumn Clone()
        {
            return new AlterActionAddColumn
            {
                WitSqlColumn = WitSqlColumn.Clone()
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required WitSqlColumn WitSqlColumn { get; init; }

        #endregion
    }
}
