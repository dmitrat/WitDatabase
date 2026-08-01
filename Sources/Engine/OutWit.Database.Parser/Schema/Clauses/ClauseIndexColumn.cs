using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Database.Parser.Schema.Clauses;

[MemoryPackable]
public sealed partial class ClauseIndexColumn : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not ClauseIndexColumn column)
            return false;

        return ColumnName.Is(column.ColumnName)
               && Descending.Is(column.Descending);
    }

    public override ClauseIndexColumn Clone()
    {
        return new ClauseIndexColumn
        {
            ColumnName = ColumnName,
            Descending = Descending
        };
    }

    #endregion

    #region Properties

    [ToString]
    public required string ColumnName { get; init; }

    [ToString]
    public bool Descending { get; init; }

    #endregion
}
