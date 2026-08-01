using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;

namespace OutWit.Database.Parser.Schema.Specs;

[MemoryPackable]
public sealed partial class SpecWindow : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not SpecWindow spec)
            return false;

        return PartitionBy.Is(spec.PartitionBy)
               && OrderBy.Is(spec.OrderBy)
               && Frame.Check(spec.Frame);
    }

    public override SpecWindow Clone()
    {
        return new SpecWindow
        {
            PartitionBy = PartitionBy?.Select(expression => (WitSqlExpression)expression.Clone()).ToList(),
            OrderBy = OrderBy?.Select(item => item.Clone()).ToList(),
            Frame = Frame?.Clone()
        };
    }

    #endregion


    #region Properties

    public IReadOnlyList<WitSqlExpression>? PartitionBy { get; init; }
    public IReadOnlyList<ClauseOrderByItem>? OrderBy { get; init; }
    
    /// <summary>
    /// Optional frame clause (ROWS/RANGE BETWEEN ... AND ...).
    /// </summary>
    public SpecFrame? Frame { get; init; }

    #endregion
}
