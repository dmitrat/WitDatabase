using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.Specs;

/// <summary>
/// Represents a frame bound (start or end) in a window frame clause.
/// </summary>
[MemoryPackable]
public sealed partial class SpecFrameBound : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not SpecFrameBound bound)
            return false;

        return BoundType.Is(bound.BoundType)
               && Offset.Is(bound.Offset);
    }

    public override SpecFrameBound Clone()
    {
        return new SpecFrameBound
        {
            BoundType = BoundType,
            Offset = Offset
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The type of frame bound.
    /// </summary>
    [ToString]
    public required FrameBoundType BoundType { get; init; }

    /// <summary>
    /// The offset for PRECEDING/FOLLOWING bounds. Null for UNBOUNDED and CURRENT ROW.
    /// </summary>
    [ToString]
    public int? Offset { get; init; }

    #endregion
}
