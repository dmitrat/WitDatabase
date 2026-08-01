using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    [MemoryPackable]
    public sealed partial class AlterActionRenameTable : AlterAction
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionRenameTable action)
                return false;

            return NewName.Is(action.NewName);
        }

        public override AlterActionRenameTable Clone()
        {
            return new AlterActionRenameTable
            {
                NewName = NewName
            };
        }

        #endregion

        #region Properties

        public required string NewName { get; init; }

        #endregion
    }
}
