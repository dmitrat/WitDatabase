using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Specs;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that evaluates window functions over a result set.
/// Window functions are evaluated after the base query is complete
/// but before final projection, allowing functions like ROW_NUMBER(),
/// RANK(), LAG(), LEAD(), and aggregate window functions.
/// </summary>
/// <remarks>
/// This is a blocking operator - it must read all rows before returning any
/// because window functions require knowledge of the entire partition.
/// </remarks>
public sealed class IteratorWindow : IteratorBase
{
    #region Constants

    /// <summary>
    /// Window functions that assign ranking/position to rows.
    /// </summary>
    private static readonly HashSet<string> RANKING_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE", "PERCENT_RANK", "CUME_DIST"
    };

    /// <summary>
    /// Window functions that access values from other rows.
    /// </summary>
    private static readonly HashSet<string> VALUE_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "FIRST_VALUE", "LAST_VALUE", "NTH_VALUE", "LAG", "LEAD"
    };

    /// <summary>
    /// Aggregate functions that can be used as window functions.
    /// </summary>
    private static readonly HashSet<string> AGGREGATE_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

    #endregion

    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<ClauseSelectItem> m_selectList;
    private readonly ExpressionEvaluator m_evaluator;
    private readonly ContextExecution m_context;

    private List<WindowedRow>? m_windowedRows;
    private IEnumerator<WindowedRow>? m_rowEnumerator;
    private WitSqlRow m_current;
    private IReadOnlyList<WitSqlColumnInfo>? m_schema;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new window function iterator.
    /// </summary>
    /// <param name="source">The source iterator providing base rows.</param>
    /// <param name="selectList">The SELECT list containing window functions.</param>
    /// <param name="context">The execution context.</param>
    public IteratorWindow(
        IResultIterator source,
        IReadOnlyList<ClauseSelectItem> selectList,
        ContextExecution context)
    {
        m_source = source;
        m_selectList = selectList;
        m_context = context;
        m_evaluator = new ExpressionEvaluator(context);
    }

    #endregion

    #region Schema Building

    private IReadOnlyList<WitSqlColumnInfo> BuildSchema()
    {
        var schema = new List<WitSqlColumnInfo>(m_selectList.Count);

        for (int i = 0; i < m_selectList.Count; i++)
        {
            var item = m_selectList[i];
            var name = item.Alias ?? GetColumnName(item.Expression, i);
            var type = InferColumnType(item.Expression);
            schema.Add(new WitSqlColumnInfo { Name = name, Type = type });
        }

        return schema;
    }

    private static string GetColumnName(WitSqlExpression? expression, int index)
    {
        return expression switch
        {
            WitSqlExpressionColumnRef col => col.ColumnName,
            WitSqlExpressionFunctionCall func => func.FunctionName,
            _ => $"column{index}"
        };
    }

    private static WitSqlType InferColumnType(WitSqlExpression? expression)
    {
        if (expression is not WitSqlExpressionFunctionCall func)
            return WitSqlType.Text;

        var funcName = func.FunctionName.ToUpperInvariant();

        // Ranking functions return integers
        if (RANKING_FUNCTIONS.Contains(funcName))
        {
            return funcName switch
            {
                "PERCENT_RANK" or "CUME_DIST" => WitSqlType.Real,
                _ => WitSqlType.Integer
            };
        }

        // Aggregate functions
        return funcName switch
        {
            "COUNT" => WitSqlType.Integer,
            "SUM" or "AVG" => WitSqlType.Real,
            _ => WitSqlType.Text
        };
    }

    #endregion

    #region Window Processing

    private void ProcessWindows()
    {
        // Collect all rows from source
        var allRows = new List<(WitSqlRow Row, int OriginalIndex)>();
        int index = 0;

        while (m_source.MoveNext())
        {
            allRows.Add((m_source.Current, index++));
        }

        // Initialize windowed rows with computed values
        m_windowedRows = new List<WindowedRow>(allRows.Count);

        for (int i = 0; i < allRows.Count; i++)
        {
            m_windowedRows.Add(new WindowedRow
            {
                SourceRow = allRows[i].Row,
                OriginalIndex = allRows[i].OriginalIndex,
                WindowValues = new Dictionary<int, WitSqlValue>()
            });
        }

        // Process each window function in the select list
        for (int selectIndex = 0; selectIndex < m_selectList.Count; selectIndex++)
        {
            var item = m_selectList[selectIndex];
            if (item.Expression is WitSqlExpressionFunctionCall func && func.Over != null)
            {
                ProcessWindowFunction(selectIndex, func, allRows);
            }
        }
    }

    private void ProcessWindowFunction(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<(WitSqlRow Row, int OriginalIndex)> allRows)
    {
        var funcName = func.FunctionName.ToUpperInvariant();
        var windowSpec = func.Over!;

        // Group rows by partition key
        var partitions = GroupByPartition(allRows, windowSpec.PartitionBy);

        foreach (var partition in partitions.Values)
        {
            // Sort partition by ORDER BY if specified
            var sortedPartition = SortPartition(partition, windowSpec.OrderBy);

            // Evaluate window function for each row in the partition
            EvaluateWindowFunction(selectIndex, funcName, func, sortedPartition);
        }
    }

    private Dictionary<string, List<int>> GroupByPartition(
        List<(WitSqlRow Row, int OriginalIndex)> allRows,
        IReadOnlyList<WitSqlExpression>? partitionBy)
    {
        var partitions = new Dictionary<string, List<int>>();

        for (int i = 0; i < allRows.Count; i++)
        {
            var key = ComputePartitionKey(allRows[i].Row, partitionBy);

            if (!partitions.TryGetValue(key, out var list))
            {
                list = [];
                partitions[key] = list;
            }

            list.Add(i);
        }

        return partitions;
    }

    private string ComputePartitionKey(WitSqlRow row, IReadOnlyList<WitSqlExpression>? partitionBy)
    {
        if (partitionBy == null || partitionBy.Count == 0)
            return string.Empty;

        var parts = new string[partitionBy.Count];
        for (int i = 0; i < partitionBy.Count; i++)
        {
            var value = m_evaluator.Evaluate(partitionBy[i], row);
            parts[i] = value.AsString();
        }

        return string.Join("\0", parts);
    }

    private List<int> SortPartition(
        List<int> partitionIndices,
        IReadOnlyList<ClauseOrderByItem>? orderBy)
    {
        if (orderBy == null || orderBy.Count == 0)
            return partitionIndices;

        return [.. partitionIndices.OrderBy(idx => 0, new WindowOrderComparer(
            m_windowedRows!,
            orderBy,
            m_evaluator))];
    }

    private void EvaluateWindowFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        var partitionSize = sortedIndices.Count;

        if (RANKING_FUNCTIONS.Contains(funcName))
        {
            EvaluateRankingFunction(selectIndex, funcName, func, sortedIndices, partitionSize);
        }
        else if (VALUE_FUNCTIONS.Contains(funcName))
        {
            EvaluateValueFunction(selectIndex, funcName, func, sortedIndices);
        }
        else if (AGGREGATE_FUNCTIONS.Contains(funcName))
        {
            EvaluateAggregateWindowFunction(selectIndex, funcName, func, sortedIndices);
        }
    }

    #endregion

    #region Ranking Functions

    private void EvaluateRankingFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        int partitionSize)
    {
        switch (funcName)
        {
            case "ROW_NUMBER":
                EvaluateRowNumber(selectIndex, sortedIndices);
                break;

            case "RANK":
                EvaluateRank(selectIndex, func, sortedIndices);
                break;

            case "DENSE_RANK":
                EvaluateDenseRank(selectIndex, func, sortedIndices);
                break;

            case "NTILE":
                EvaluateNtile(selectIndex, func, sortedIndices);
                break;

            case "PERCENT_RANK":
                EvaluatePercentRank(selectIndex, func, sortedIndices, partitionSize);
                break;

            case "CUME_DIST":
                EvaluateCumeDist(selectIndex, func, sortedIndices, partitionSize);
                break;
        }
    }

    private void EvaluateRowNumber(int selectIndex, List<int> sortedIndices)
    {
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(i + 1);
        }
    }

    private void EvaluateRank(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return;

        var orderBy = func.Over?.OrderBy;
        int currentRank = 1;
        int sameRankCount = 0;
        WitSqlValue? previousKey = null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var row = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(row, orderBy);

            if (previousKey != null && !currentKey.Equals(previousKey.Value))
            {
                currentRank += sameRankCount;
                sameRankCount = 1;
            }
            else
            {
                sameRankCount++;
            }

            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(currentRank);

            previousKey = currentKey;
        }
    }

    private void EvaluateDenseRank(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return;

        var orderBy = func.Over?.OrderBy;
        int currentRank = 0;
        WitSqlValue? previousKey = null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var row = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(row, orderBy);

            if (previousKey == null || !currentKey.Equals(previousKey.Value))
            {
                currentRank++;
            }

            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(currentRank);

            previousKey = currentKey;
        }
    }

    private void EvaluateNtile(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        // Get the number of buckets from the first argument
        var buckets = (int)m_evaluator.Evaluate(func.Arguments[0],
            m_windowedRows![sortedIndices[0]].SourceRow).AsInt64();

        if (buckets <= 0)
            buckets = 1;

        int rowsPerBucket = sortedIndices.Count / buckets;
        int extraRows = sortedIndices.Count % buckets;

        int currentBucket = 1;
        int rowsInCurrentBucket = 0;
        int bucketSize = rowsPerBucket + (extraRows > 0 ? 1 : 0);

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromInt(currentBucket);

            rowsInCurrentBucket++;

            if (rowsInCurrentBucket >= bucketSize && currentBucket < buckets)
            {
                currentBucket++;
                rowsInCurrentBucket = 0;
                extraRows--;
                bucketSize = rowsPerBucket + (extraRows > 0 ? 1 : 0);
            }
        }
    }

    private void EvaluatePercentRank(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        int partitionSize)
    {
        if (partitionSize <= 1)
        {
            foreach (var idx in sortedIndices)
            {
                m_windowedRows![idx].WindowValues[selectIndex] = WitSqlValue.FromReal(0.0);
            }
            return;
        }

        var orderBy = func.Over?.OrderBy;
        int currentRank = 1;
        int sameRankCount = 0;
        WitSqlValue? previousKey = null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var row = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(row, orderBy);

            if (previousKey != null && !currentKey.Equals(previousKey.Value))
            {
                currentRank += sameRankCount;
                sameRankCount = 1;
            }
            else
            {
                sameRankCount++;
            }

            double percentRank = (double)(currentRank - 1) / (partitionSize - 1);
            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromReal(percentRank);

            previousKey = currentKey;
        }
    }

    private void EvaluateCumeDist(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        int partitionSize)
    {
        if (partitionSize == 0)
            return;

        var orderBy = func.Over?.OrderBy;

        // For CUME_DIST, we need to count how many rows have values <= current row
        for (int i = 0; i < sortedIndices.Count; i++)
        {
            var currentRow = m_windowedRows![sortedIndices[i]].SourceRow;
            var currentKey = ComputeOrderKey(currentRow, orderBy);

            // Count rows with value <= current (including ties)
            int countLessOrEqual = 0;
            for (int j = 0; j < sortedIndices.Count; j++)
            {
                var compareRow = m_windowedRows[sortedIndices[j]].SourceRow;
                var compareKey = ComputeOrderKey(compareRow, orderBy);

                if (CompareOrderKeys(compareKey, currentKey, orderBy) <= 0)
                {
                    countLessOrEqual++;
                }
            }

            double cumeDist = (double)countLessOrEqual / partitionSize;
            m_windowedRows[sortedIndices[i]].WindowValues[selectIndex] =
                WitSqlValue.FromReal(cumeDist);
        }
    }

    #endregion

    #region Value Functions

    private void EvaluateValueFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        switch (funcName)
        {
            case "FIRST_VALUE":
                EvaluateFirstValue(selectIndex, func, sortedIndices);
                break;

            case "LAST_VALUE":
                EvaluateLastValue(selectIndex, func, sortedIndices);
                break;

            case "NTH_VALUE":
                EvaluateNthValue(selectIndex, func, sortedIndices);
                break;

            case "LAG":
                EvaluateLag(selectIndex, func, sortedIndices);
                break;

            case "LEAD":
                EvaluateLead(selectIndex, func, sortedIndices);
                break;
        }
    }

    private void EvaluateFirstValue(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        var firstRow = m_windowedRows![sortedIndices[0]].SourceRow;
        var firstValue = m_evaluator.Evaluate(func.Arguments[0], firstRow);

        foreach (var idx in sortedIndices)
        {
            m_windowedRows[idx].WindowValues[selectIndex] = firstValue;
        }
    }

    private void EvaluateLastValue(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        var lastRow = m_windowedRows![sortedIndices[^1]].SourceRow;
        var lastValue = m_evaluator.Evaluate(func.Arguments[0], lastRow);

        foreach (var idx in sortedIndices)
        {
            m_windowedRows[idx].WindowValues[selectIndex] = lastValue;
        }
    }

    private void EvaluateNthValue(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count < 2)
            return;

        // Get N from second argument
        var n = (int)m_evaluator.Evaluate(func.Arguments[1],
            m_windowedRows![sortedIndices[0]].SourceRow).AsInt64();

        WitSqlValue nthValue;
        if (n >= 1 && n <= sortedIndices.Count)
        {
            var nthRow = m_windowedRows[sortedIndices[n - 1]].SourceRow;
            nthValue = m_evaluator.Evaluate(func.Arguments[0], nthRow);
        }
        else
        {
            nthValue = WitSqlValue.Null;
        }

        foreach (var idx in sortedIndices)
        {
            m_windowedRows[idx].WindowValues[selectIndex] = nthValue;
        }
    }

    private void EvaluateLag(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        // Get offset (default 1) and default value
        int offset = func.Arguments.Count > 1
            ? (int)m_evaluator.Evaluate(func.Arguments[1],
                m_windowedRows![sortedIndices[0]].SourceRow).AsInt64()
            : 1;

        WitSqlValue defaultValue = func.Arguments.Count > 2
            ? m_evaluator.Evaluate(func.Arguments[2],
                m_windowedRows![sortedIndices[0]].SourceRow)
            : WitSqlValue.Null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            WitSqlValue value;
            if (i - offset >= 0)
            {
                var lagRow = m_windowedRows![sortedIndices[i - offset]].SourceRow;
                value = m_evaluator.Evaluate(func.Arguments[0], lagRow);
            }
            else
            {
                value = defaultValue;
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = value;
        }
    }

    private void EvaluateLead(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0 || func.Arguments == null || func.Arguments.Count == 0)
            return;

        // Get offset (default 1) and default value
        int offset = func.Arguments.Count > 1
            ? (int)m_evaluator.Evaluate(func.Arguments[1],
                m_windowedRows![sortedIndices[0]].SourceRow).AsInt64()
            : 1;

        WitSqlValue defaultValue = func.Arguments.Count > 2
            ? m_evaluator.Evaluate(func.Arguments[2],
                m_windowedRows![sortedIndices[0]].SourceRow)
            : WitSqlValue.Null;

        for (int i = 0; i < sortedIndices.Count; i++)
        {
            WitSqlValue value;
            if (i + offset < sortedIndices.Count)
            {
                var leadRow = m_windowedRows![sortedIndices[i + offset]].SourceRow;
                value = m_evaluator.Evaluate(func.Arguments[0], leadRow);
            }
            else
            {
                value = defaultValue;
            }

            m_windowedRows![sortedIndices[i]].WindowValues[selectIndex] = value;
        }
    }

    #endregion

    #region Aggregate Window Functions

    private void EvaluateAggregateWindowFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (sortedIndices.Count == 0)
            return;

        // For now, evaluate over the entire partition (no frame support yet)
        // TODO: Add frame clause support (ROWS/RANGE BETWEEN)

        switch (funcName)
        {
            case "COUNT":
                EvaluateWindowCount(selectIndex, func, sortedIndices);
                break;

            case "SUM":
                EvaluateWindowSum(selectIndex, func, sortedIndices);
                break;

            case "AVG":
                EvaluateWindowAvg(selectIndex, func, sortedIndices);
                break;

            case "MIN":
                EvaluateWindowMin(selectIndex, func, sortedIndices);
                break;

            case "MAX":
                EvaluateWindowMax(selectIndex, func, sortedIndices);
                break;
        }
    }

    private void EvaluateWindowCount(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        long count;

        if (func.IsStar)
        {
            count = sortedIndices.Count;
        }
        else if (func.Arguments != null && func.Arguments.Count > 0)
        {
            count = sortedIndices.Count(idx =>
            {
                var value = m_evaluator.Evaluate(func.Arguments[0],
                    m_windowedRows![idx].SourceRow);
                return !value.IsNull;
            });
        }
        else
        {
            count = sortedIndices.Count;
        }

        var countValue = WitSqlValue.FromInt(count);
        foreach (var idx in sortedIndices)
        {
            m_windowedRows![idx].WindowValues[selectIndex] = countValue;
        }
    }

    private void EvaluateWindowSum(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        WitSqlValue? sum = null;

        foreach (var idx in sortedIndices)
        {
            var value = m_evaluator.Evaluate(func.Arguments[0],
                m_windowedRows![idx].SourceRow);

            if (!value.IsNull)
            {
                sum = sum == null ? value : sum.Value.Add(value);
            }
        }

        var sumValue = sum ?? WitSqlValue.Null;
        foreach (var idx in sortedIndices)
        {
            m_windowedRows![idx].WindowValues[selectIndex] = sumValue;
        }
    }

    private void EvaluateWindowAvg(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        WitSqlValue? sum = null;
        long count = 0;

        foreach (var idx in sortedIndices)
        {
            var value = m_evaluator.Evaluate(func.Arguments[0],
                m_windowedRows![idx].SourceRow);

            if (!value.IsNull)
            {
                sum = sum == null ? value : sum.Value.Add(value);
                count++;
            }
        }

        var avgValue = count > 0 && sum != null
            ? sum.Value.Divide(WitSqlValue.FromInt(count))
            : WitSqlValue.Null;

        foreach (var idx in sortedIndices)
        {
            m_windowedRows![idx].WindowValues[selectIndex] = avgValue;
        }
    }

    private void EvaluateWindowMin(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        WitSqlValue? min = null;

        foreach (var idx in sortedIndices)
        {
            var value = m_evaluator.Evaluate(func.Arguments[0],
                m_windowedRows![idx].SourceRow);

            if (!value.IsNull && (min == null || value < min.Value))
            {
                min = value;
            }
        }

        var minValue = min ?? WitSqlValue.Null;
        foreach (var idx in sortedIndices)
        {
            m_windowedRows![idx].WindowValues[selectIndex] = minValue;
        }
    }

    private void EvaluateWindowMax(
        int selectIndex,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices)
    {
        if (func.Arguments == null || func.Arguments.Count == 0)
            return;

        WitSqlValue? max = null;

        foreach (var idx in sortedIndices)
        {
            var value = m_evaluator.Evaluate(func.Arguments[0],
                m_windowedRows![idx].SourceRow);

            if (!value.IsNull && (max == null || value > max.Value))
            {
                max = value;
            }
        }

        var maxValue = max ?? WitSqlValue.Null;
        foreach (var idx in sortedIndices)
        {
            m_windowedRows![idx].WindowValues[selectIndex] = maxValue;
        }
    }

    #endregion

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

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();
        m_schema = BuildSchema();
        ProcessWindows();
        m_rowEnumerator = m_windowedRows?.GetEnumerator();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_rowEnumerator == null || !m_rowEnumerator.MoveNext())
            return false;

        m_current = BuildResultRow(m_rowEnumerator.Current);
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_rowEnumerator?.Dispose();
        m_rowEnumerator = null;
        m_windowedRows = null;
        m_schema = null;
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_rowEnumerator?.Dispose();
        m_source.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema
    {
        get
        {
            // Build schema lazily if not yet built (can happen before Open)
            m_schema ??= BuildSchema();
            return m_schema;
        }
    }

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

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
