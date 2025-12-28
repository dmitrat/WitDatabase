using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Iterators;

/// <summary>
/// Window frame calculation and management.
/// </summary>
public sealed partial class IteratorWindow
{
    #region Frame Calculation

    /// <summary>
    /// Calculates the frame boundaries for a given row within the partition.
    /// </summary>
    private (int Start, int End) GetFrameBounds(
        int rowIndex,
        int partitionSize,
        SpecFrame? frame)
    {
        // Default frame when no frame clause specified:
        // - If ORDER BY is present: RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
        // - If no ORDER BY: entire partition
        if (frame == null)
        {
            // Entire partition (no frame = whole partition for aggregate window functions)
            return (0, partitionSize - 1);
        }

        int start = CalculateFrameBoundIndex(frame.Start, rowIndex, partitionSize, isStart: true);
        int end = frame.End != null 
            ? CalculateFrameBoundIndex(frame.End, rowIndex, partitionSize, isStart: false)
            : rowIndex; // Default end is CURRENT ROW

        // Ensure bounds are valid
        start = Math.Max(0, Math.Min(start, partitionSize - 1));
        end = Math.Max(0, Math.Min(end, partitionSize - 1));

        // If start > end, return empty frame
        if (start > end)
        {
            return (0, -1); // Empty frame
        }

        return (start, end);
    }

    private static int CalculateFrameBoundIndex(
        SpecFrameBound bound,
        int currentIndex,
        int partitionSize,
        bool isStart)
    {
        return bound.BoundType switch
        {
            FrameBoundType.UnboundedPreceding => 0,
            FrameBoundType.UnboundedFollowing => partitionSize - 1,
            FrameBoundType.CurrentRow => currentIndex,
            FrameBoundType.Preceding => currentIndex - (bound.Offset ?? 1),
            FrameBoundType.Following => currentIndex + (bound.Offset ?? 1),
            _ => isStart ? 0 : partitionSize - 1
        };
    }

    #endregion
}
