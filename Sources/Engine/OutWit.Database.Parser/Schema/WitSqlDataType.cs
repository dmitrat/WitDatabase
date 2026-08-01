using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema
{
    [MemoryPackable]
    public sealed partial class WitSqlDataType : ModelBase
    {
        #region Model Base

        public override bool Is(ModelBase? modelBase, double tolerance = DEFAULT_TOLERANCE)
        {
            if (modelBase is not WitSqlDataType dataType)
                return false;

            return TypeName.Is(dataType.TypeName)
                   && Length.Is(dataType.Length)
                   && Precision.Is(dataType.Precision)
                   && Scale.Is(dataType.Scale);
        }

        public override WitSqlDataType Clone()
        {
            return new WitSqlDataType
            {
                TypeName = TypeName,
                Length = Length,
                Precision = Precision,
                Scale = Scale
            };
        }

        #endregion


        #region Propeties

        [ToString]
        public required string TypeName { get; init; }
        public int? Length { get; init; }
        public int? Precision { get; init; }
        public int? Scale { get; init; }

        #endregion
    }
}
