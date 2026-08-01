using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    [MemoryPackable]
    public sealed partial class AlterActionRenameColumn : AlterAction
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionRenameColumn action)
                return false;

            return OldName.Is(action.OldName)
                   && NewName.Is(action.NewName);
        }

        public override AlterActionRenameColumn Clone()
        {
            return new AlterActionRenameColumn
            {
                OldName = OldName,
                NewName = NewName
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string OldName { get; init; }

        [ToString]
        public required string NewName { get; init; }

        #endregion
    }
}
