using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Optimizers;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Query;

/// <summary>
/// Answers where a value sits inside the range of keys an index holds, by asking the index for its
/// smallest and largest key.
/// </summary>
/// <remarks>
/// <para>
/// <b>It interpolates on the encoded key bytes rather than on the values.</b> Index keys are encoded so
/// that byte order is value order - every range scan in the engine depends on that - so a value's
/// position between the smallest and the largest key can be computed without knowing what type it is.
/// That keeps this free of a per-type ladder, and it is the reason a text column gets an estimate as
/// readily as an integer one.
/// </para>
/// <para>
/// <b>It is a linear interpolation, so it assumes the values are spread evenly.</b> They often are not,
/// and the estimate is then wrong - but wrong by the shape of the data rather than by a constant, which
/// is the difference between an estimate that is 200x out on a one-row range and one that is a few times
/// out on a skewed one. A histogram would do better and costs writes to maintain; this costs two
/// descents and no bookkeeping at all.
/// </para>
/// <para>
/// <b>Nothing here may become expensive.</b> Both keys come from <c>ISecondaryIndex</c>, whose
/// implementation descends the tree; if that ever became a scan again this would be paid on every query,
/// which is the defect 11.1.0 removed. <c>KeyRangeDescentTests</c> is what holds that line.
/// </para>
/// </remarks>
public sealed class IndexRangeStatistics : IIndexRangeStatistics
{
    #region Fields

    private readonly IDatabase m_database;
    private readonly DefinitionTable m_table;

    /// <summary>
    /// Bounds already read in this planning pass, so several predicates on one index descend once.
    /// </summary>
    private readonly Dictionary<string, (byte[] Min, byte[] Max)?> m_bounds = new(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Constructors

    public IndexRangeStatistics(IDatabase database, DefinitionTable table)
    {
        m_database = database;
        m_table = table;
    }

    #endregion

    #region IIndexRangeStatistics

    /// <inheritdoc/>
    public double? FractionBelow(string indexName, object? value)
    {
        if (value == null)
            return null;

        var bounds = Bounds(indexName);

        if (bounds == null)
            return null;

        var encoded = Encode(indexName, value);

        if (encoded == null)
            return null;

        return Fraction(bounds.Value.Min, bounds.Value.Max, encoded);
    }

    #endregion

    #region Tools

    /// <summary>The smallest and largest key in the index, read once per planning pass.</summary>
    private (byte[] Min, byte[] Max)? Bounds(string indexName)
    {
        if (m_bounds.TryGetValue(indexName, out var cached))
            return cached;

        (byte[] Min, byte[] Max)? bounds = null;

        var index = m_database.GetPhysicalIndex(indexName);

        if (index != null)
        {
            var first = index.GetFirstEntry();
            var last = index.GetLastEntry();

            if (first != null && last != null)
                bounds = (first.Value.IndexKey, last.Value.IndexKey);
        }

        m_bounds[indexName] = bounds;
        return bounds;
    }

    /// <summary>
    /// Encodes the predicate's value the way the index encoded its keys, or null when it cannot be.
    /// </summary>
    private byte[]? Encode(string indexName, object value)
    {
        var definition = m_database.GetIndex(indexName);

        if (definition == null || definition.Columns.Count == 0)
            return null;

        var column = m_table.GetColumn(definition.Columns[0]);

        if (column == null)
            return null;

        try
        {
            return WitTypeConverter.SerializeIndexKey([WitSqlValue.FromObject(value)], [column.Type]);
        }
        catch
        {
            // A value the column's type cannot hold says nothing about where it sits in the index, and
            // an estimate is never worth an exception on the planning path.
            return null;
        }
    }

    /// <summary>
    /// Where <paramref name="value"/> sits between <paramref name="min"/> and <paramref name="max"/>,
    /// as a fraction, by reading the first bytes that distinguish them.
    /// </summary>
    /// <remarks>
    /// The common prefix of the two bounds carries no information - every key in the index shares it -
    /// so it is skipped, and the eight bytes after it are read as a big-endian number. Eight bytes is
    /// enough to separate any two keys that differ early and gives up gracefully on ones that differ
    /// only in a long tail, where the answer is 0.5 and the optimizer is no worse off than it was.
    /// </remarks>
    private static double Fraction(byte[] min, byte[] max, byte[] value)
    {
        var prefix = CommonPrefix(min, max);

        var lo = Window(min, prefix);
        var hi = Window(max, prefix);
        var at = Window(value, prefix);

        if (hi <= lo)
            return 0.5;

        if (at <= lo)
            return 0.0;

        if (at >= hi)
            return 1.0;

        return (double)(at - lo) / (hi - lo);
    }

    private static int CommonPrefix(byte[] first, byte[] second)
    {
        var length = Math.Min(first.Length, second.Length);
        var i = 0;

        while (i < length && first[i] == second[i])
            i++;

        return i;
    }

    /// <summary>Eight bytes from <paramref name="offset"/>, big-endian, zero-padded.</summary>
    private static ulong Window(byte[] key, int offset)
    {
        ulong result = 0;

        for (var i = 0; i < 8; i++)
        {
            var index = offset + i;
            result = (result << 8) | (index < key.Length ? key[index] : 0UL);
        }

        return result;
    }

    #endregion
}
