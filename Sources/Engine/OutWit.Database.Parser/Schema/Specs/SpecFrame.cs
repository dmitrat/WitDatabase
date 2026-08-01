using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Parser.Schema.Specs;

/// <summary>
/// Represents a window frame clause (ROWS/RANGE BETWEEN ... AND ...).
/// </summary>
[MemoryPackable]
public sealed partial class SpecFrame : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase? other, double tolerance = DEFAULT_TOLERANCE)
    {
        if (other is not SpecFrame frame)
            return false;

        return FrameType.Is(frame.FrameType)
               && Start.Is(frame.Start)
               && End.Check(frame.End);
    }

    public override SpecFrame Clone()
    {
        return new SpecFrame
        {
            FrameType = FrameType,
            Start = Start.Clone(),
            End = End?.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// The frame type (ROWS or RANGE).
    /// </summary>
    [ToString]
    public required FrameType FrameType { get; init; }

    /// <summary>
    /// The start bound of the frame.
    /// </summary>
    [ToString]
    public required SpecFrameBound Start { get; init; }

    /// <summary>
    /// The end bound of the frame. If null, the frame ends at CURRENT ROW.
    /// </summary>
    [ToString]
    public SpecFrameBound? End { get; init; }

    #endregion
}
