using OutWit.Database.Expressions;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Helper methods and nested types for window functions.
/// </summary>
public sealed partial class IteratorWindow
{
    #region Helper Methods

    private WitSqlValue ComputeOrderKey(WitSqlRow row, IReadOnlyList<ClauseOrderByItem>? orderBy)
    {
        if (orderBy == null || orderBy.Count == 0)
            return WitSqlValue.FromInt(0);

        // For single ORDER BY, return the value directly
        if (orderBy.Count == 1)
        {
            return m_evaluator.Evaluate(orderBy[0].Expression, row);
        }

        // For multiple ORDER BY, create a composite key
        var parts = new string[orderBy.Count];
        for (int i = 0; i < orderBy.Count; i++)
        {
            var value = m_evaluator.Evaluate(orderBy[i].Expression, row);
            parts[i] = value.AsString();
        }

        return WitSqlValue.FromText(string.Join("\0", parts));
    }

    private int CompareOrderKeys(
        WitSqlValue a,
        WitSqlValue b,
        IReadOnlyList<ClauseOrderByItem>? orderBy)
    {
        if (orderBy == null || orderBy.Count == 0)
            return 0;

        // Compare values
        int result = a.CompareTo(b);

        // Apply DESC if specified (only for single ORDER BY)
        if (orderBy.Count == 1 && orderBy[0].Descending)
        {
            result = -result;
        }

        return result;
    }

    private WitSqlRow BuildResultRow(WindowedRow windowedRow)
    {
        var values = new WitSqlValue[m_selectList.Count];
        var names = new string[m_selectList.Count];

        for (int i = 0; i < m_selectList.Count; i++)
        {
            var item = m_selectList[i];
            names[i] = m_schema![i].Name;

            // Check if this is a window function result
            if (windowedRow.WindowValues.TryGetValue(i, out var windowValue))
            {
                values[i] = windowValue;
            }
            else if (item.Expression != null)
            {
                // Evaluate non-window expression against source row
                values[i] = m_evaluator.Evaluate(item.Expression, windowedRow.SourceRow);
            }
            else
            {
                values[i] = WitSqlValue.Null;
            }
        }

        return new WitSqlRow(values, names);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Represents a row with computed window function values.
    /// </summary>
    private sealed class WindowedRow
    {
        public required WitSqlRow SourceRow { get; init; }
        public required int OriginalIndex { get; init; }
        public required Dictionary<int, WitSqlValue> WindowValues { get; init; }
    }

    /// <summary>
    /// Comparer for sorting partition rows by ORDER BY expressions.
    /// </summary>
    private sealed class WindowOrderComparer(
        List<WindowedRow> windowedRows,
        IReadOnlyList<ClauseOrderByItem> orderBy,
        ExpressionEvaluator evaluator) : IComparer<int>
    {
        public int Compare(int x, int y)
        {
            var rowX = windowedRows[x].SourceRow;
            var rowY = windowedRows[y].SourceRow;

            foreach (var item in orderBy)
            {
                var valueX = evaluator.Evaluate(item.Expression, rowX);
                var valueY = evaluator.Evaluate(item.Expression, rowY);

                // Handle NULLS FIRST/LAST
                if (valueX.IsNull || valueY.IsNull)
                {
                    if (valueX.IsNull && valueY.IsNull)
                        continue;

                    // Determine if nulls should be first or last
                    // Default behavior: NULLS LAST for ASC, NULLS FIRST for DESC
                    bool nullsFirst = item.NullsOrder switch
                    {
                        NullsOrderType.First => true,
                        NullsOrderType.Last => false,
                        _ => item.Descending // Default: nulls first for DESC, last for ASC
                    };

                    if (valueX.IsNull)
                        return nullsFirst ? -1 : 1;

                    return nullsFirst ? 1 : -1;
                }

                int result = valueX.CompareTo(valueY);

                if (item.Descending)
                    result = -result;

                if (result != 0)
                    return result;
            }

            return 0;
        }
    }

    #endregion
}
