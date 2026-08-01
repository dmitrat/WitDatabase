using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    [MemoryPackable]
    public sealed partial class AlterActionDropColumn : AlterAction
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionDropColumn action)
                return false;

            return ColumnName.Is(action.ColumnName);
        }

        public override AlterActionDropColumn Clone()
        {
            return new AlterActionDropColumn
            {
                ColumnName = ColumnName
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ColumnName { get; init; }

        #endregion
    }
}
