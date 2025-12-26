using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Database.Values;

namespace OutWit.Database.Model;

/// <summary>
/// Accumulates values for a single aggregate function during GROUP BY processing.
/// </summary>
public sealed class Accumulator : ModelBase
{
    #region ModelBase

    /// <inheritdoc/>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not Accumulator other)
            return false;

        return Count.Is(other.Count)
               && Sum.Is(other.Sum)
               && Min.Is(other.Min)
               && Max.Is(other.Max)
               && ValuesEqual(Values, other.Values)
               && DistinctValuesEqual(DistinctValues, other.DistinctValues);
    }

    /// <inheritdoc/>
    public override Accumulator Clone()
    {
        return new Accumulator
        {
            Count = Count,
            Sum = Sum,
            Min = Min,
            Max = Max,
            Values = Values?.ToList(),
            DistinctValues = DistinctValues?.ToHashSet()
        };
    }

    #endregion

    #region Functions

    private static bool ValuesEqual(List<string>? a, List<string>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.SequenceEqual(b);
    }

    private static bool DistinctValuesEqual(HashSet<WitSqlValue>? a, HashSet<WitSqlValue>? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.SetEquals(b);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the count of non-null values (for COUNT and AVG).
    /// </summary>
    public long Count { get; set; }

    /// <summary>
    /// Gets or sets the running sum (for SUM and AVG).
    /// </summary>
    public WitSqlValue? Sum { get; set; }

    /// <summary>
    /// Gets or sets the minimum value (for MIN).
    /// </summary>
    public WitSqlValue? Min { get; set; }

    /// <summary>
    /// Gets or sets the maximum value (for MAX).
    /// </summary>
    public WitSqlValue? Max { get; set; }

    /// <summary>
    /// Gets or sets the list of string values (for GROUP_CONCAT).
    /// </summary>
    public List<string>? Values { get; set; }

    /// <summary>
    /// Gets or sets the set of distinct values (for COUNT DISTINCT).
    /// </summary>
    public HashSet<WitSqlValue>? DistinctValues { get; set; }

    #endregion
}