using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Database.Model;

/// <summary>
/// Represents a group of rows during GROUP BY aggregation.
/// Stores the first row for non-aggregate column access and accumulators for each select item.
/// </summary>
public sealed class AggregateGroup : ModelBase
{
    #region Constructors

    /// <summary>
    /// Creates a new aggregate group.
    /// </summary>
    /// <param name="firstRow">The first row in this group (for non-aggregate column values).</param>
    /// <param name="selectCount">The number of items in the SELECT list.</param>
    public AggregateGroup(WitSqlRow? firstRow, int selectCount)
    {
        FirstRow = firstRow;
        Accumulators = new Accumulator[selectCount];
        for (int i = 0; i < selectCount; i++)
        {
            Accumulators[i] = new Accumulator();
        }
    }

    private AggregateGroup(WitSqlRow? firstRow, Accumulator[] accumulators, int rowCount)
    {
        FirstRow = firstRow;
        Accumulators = accumulators;
        RowCount = rowCount;
    }

    #endregion

    #region ModelBase

    /// <inheritdoc/>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not AggregateGroup other)
            return false;

        return FirstRow.Check(other.FirstRow)
               && Accumulators.Check(other.Accumulators)
               && RowCount.Is(other.RowCount);
    }

    /// <inheritdoc/>
    public override AggregateGroup Clone()
    {
        return new AggregateGroup(
            FirstRow,
            Accumulators.Select(acc => acc.Clone()).ToArray(),
            RowCount);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the first row in this group, used for accessing non-aggregate column values.
    /// </summary>
    public WitSqlRow? FirstRow { get; }

    /// <summary>
    /// Gets the accumulators for each item in the SELECT list.
    /// </summary>
    public Accumulator[] Accumulators { get; }

    /// <summary>
    /// Gets or sets the total count of rows in this group.
    /// </summary>
    public int RowCount { get; set; }

    #endregion
}