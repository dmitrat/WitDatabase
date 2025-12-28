using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.Specs;
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
public sealed partial class IteratorWindow : IteratorBase
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
            EvaluateWindowFunction(selectIndex, funcName, func, sortedPartition, windowSpec.Frame);
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

        // Use Order() with comparer to sort the indices based on the ORDER BY expressions
        return [.. partitionIndices.Order(new WindowOrderComparer(
            m_windowedRows!,
            orderBy,
            m_evaluator))];
    }

    private void EvaluateWindowFunction(
        int selectIndex,
        string funcName,
        WitSqlExpressionFunctionCall func,
        List<int> sortedIndices,
        SpecFrame? frame)
    {
        var partitionSize = sortedIndices.Count;

        if (RANKING_FUNCTIONS.Contains(funcName))
        {
            EvaluateRankingFunction(selectIndex, funcName, func, sortedIndices, partitionSize);
        }
        else if (VALUE_FUNCTIONS.Contains(funcName))
        {
            EvaluateValueFunction(selectIndex, funcName, func, sortedIndices, frame);
        }
        else if (AGGREGATE_FUNCTIONS.Contains(funcName))
        {
            EvaluateAggregateWindowFunction(selectIndex, funcName, func, sortedIndices, frame);
        }
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
}
