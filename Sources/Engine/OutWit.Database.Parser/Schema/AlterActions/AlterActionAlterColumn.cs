using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;

namespace OutWit.Database.Parser.Schema.AlterActions
{
    /// <summary>
    /// ALTER TABLE ALTER COLUMN action - change type, default, nullability.
    /// </summary>
    [MemoryPackable]
    public sealed partial class AlterActionAlterColumn : AlterAction
    {
        #region Model Base

        public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
        {
            if (other is not AlterActionAlterColumn action)
                return false;

            return ColumnName.Is(action.ColumnName)
                   && NewType.Check(action.NewType)
                   && NewDefault.Check(action.NewDefault)
                   && DropDefault.Is(action.DropDefault)
                   && SetNotNull.Is(action.SetNotNull);
        }

        public override AlterActionAlterColumn Clone()
        {
            return new AlterActionAlterColumn
            {
                ColumnName = ColumnName,
                NewType = NewType?.Clone(),
                NewDefault = (WitSqlExpression?)NewDefault?.Clone(),
                DropDefault = DropDefault,
                SetNotNull = SetNotNull
            };
        }

        #endregion

        #region Properties

        [ToString]
        public required string ColumnName { get; init; }

        [ToString]
        public WitSqlDataType? NewType { get; init; }

        public WitSqlExpression? NewDefault { get; init; }

        public bool DropDefault { get; init; }

        /// <summary>
        /// true = SET NOT NULL, false = DROP NOT NULL, null = no change
        /// </summary>
        public bool? SetNotNull { get; init; }

        #endregion
    }
}
