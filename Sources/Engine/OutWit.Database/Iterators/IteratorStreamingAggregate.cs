using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Streaming aggregate iterator for simple aggregation without GROUP BY.
/// Computes COUNT, SUM, AVG, MIN, MAX without materializing all rows.
/// This is a single-pass iterator that produces exactly one result row.
/// </summary>
/// <remarks>
/// This iterator is optimized for queries like:
/// - SELECT COUNT(*) FROM table
/// - SELECT SUM(column), AVG(column) FROM table
/// - SELECT MIN(column), MAX(column) FROM table WHERE condition
/// 
/// Unlike IteratorGroupBy which stores all rows in memory, this iterator
/// maintains only the running aggregates, using O(1) memory regardless of table size.
/// </remarks>
public sealed class IteratorStreamingAggregate : IteratorBase
{
    #region Constants

    private static readonly HashSet<string> SUPPORTED_AGGREGATES = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX"
    };

    #endregion

    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<ClauseSelectItem> m_selectList;
    private readonly ExpressionEvaluator m_evaluator;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;
    private readonly StreamingAccumulator[] m_accumulators;

    private bool m_resultReturned;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new streaming aggregate iterator.
    /// </summary>
    /// <param name="source">The source iterator.</param>
    /// <param name="selectList">The SELECT list with aggregate functions.</param>
    /// <param name="context">The execution context.</param>
    public IteratorStreamingAggregate(
        IResultIterator source,
        IReadOnlyList<ClauseSelectItem> selectList,
        ContextExecution context)
    {
        m_source = source;
        m_selectList = selectList;
        m_evaluator = new ExpressionEvaluator(context);
        m_schema = BuildSchema(selectList);
        m_accumulators = new StreamingAccumulator[selectList.Count];

        for (int i = 0; i < selectList.Count; i++)
        {
            m_accumulators[i] = new StreamingAccumulator();
        }
    }

    #endregion

    #region Static Methods

    /// <summary>
    /// Checks if a SELECT list is suitable for streaming aggregation.
    /// Requirements:
    /// - All items must be aggregate functions (no plain columns)
    /// - No GROUP_CONCAT (requires storing all values)
    /// - No DISTINCT aggregates (require storing all values for deduplication)
    /// </summary>
    public static bool CanUseStreamingAggregation(IReadOnlyList<ClauseSelectItem> selectList)
    {
        if (selectList == null || selectList.Count == 0)
            return false;

        foreach (var item in selectList)
        {
            if (item.Expression is not WitSqlExpressionFunctionCall func)
                return false;

            if (!SUPPORTED_AGGREGATES.Contains(func.FunctionName))
                return false;

            // DISTINCT requires storing all values
            if (func.IsDistinct)
                return false;
        }

        return true;
    }

    #endregion

    #region Private Methods

    private static List<WitSqlColumnInfo> BuildSchema(IReadOnlyList<ClauseSelectItem> selectList)
    {
        var schema = new List<WitSqlColumnInfo>(selectList.Count);

        for (int i = 0; i < selectList.Count; i++)
        {
            var item = selectList[i];
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
            WitSqlExpressionFunctionCall func => func.FunctionName,
            _ => $"column{index}"
        };
    }

    private static WitSqlType InferColumnType(WitSqlExpression? expression)
    {
        if (expression is not WitSqlExpressionFunctionCall func)
            return WitSqlType.Text;

        var funcName = func.FunctionName.ToUpperInvariant();
        return funcName switch
        {
            "COUNT" => WitSqlType.Integer,
            "SUM" or "AVG" => WitSqlType.Real,
            "MIN" or "MAX" => WitSqlType.Text, // Depends on input
            _ => WitSqlType.Text
        };
    }

    private void UpdateAccumulators(WitSqlRow row)
    {
        for (int i = 0; i < m_selectList.Count; i++)
        {
            var item = m_selectList[i];
            if (item.Expression is WitSqlExpressionFunctionCall func)
            {
                UpdateAccumulator(ref m_accumulators[i], func, row);
            }
        }
    }

    private void UpdateAccumulator(ref StreamingAccumulator acc, WitSqlExpressionFunctionCall func, WitSqlRow row)
    {
        WitSqlValue value;

        if (func.IsStar)
        {
            // COUNT(*) - always count
            value = WitSqlValue.FromInt(1);
        }
        else if (func.Arguments is { Count: > 0 })
        {
            value = m_evaluator.Evaluate(func.Arguments[0], row);
        }
        else
        {
            value = WitSqlValue.Null;
        }

        var funcName = func.FunctionName.ToUpperInvariant();

        switch (funcName)
        {
            case "COUNT":
                if (func.IsStar || !value.IsNull)
                {
                    acc.Count++;
                }
                break;

            case "SUM":
                if (!value.IsNull)
                {
                    acc.Sum = acc.Sum == null ? value : acc.Sum.Value.Add(value);
                    acc.HasValue = true;
                }
                break;

            case "AVG":
                if (!value.IsNull)
                {
                    acc.Sum = acc.Sum == null ? value : acc.Sum.Value.Add(value);
                    acc.Count++;
                    acc.HasValue = true;
                }
                break;

            case "MIN":
                if (!value.IsNull && (acc.Min == null || value < acc.Min.Value))
                {
                    acc.Min = value;
                    acc.HasValue = true;
                }
                break;

            case "MAX":
                if (!value.IsNull && (acc.Max == null || value > acc.Max.Value))
                {
                    acc.Max = value;
                    acc.HasValue = true;
                }
                break;
        }
    }

    private WitSqlValue GetAggregateResult(int index)
    {
        var acc = m_accumulators[index];
        var item = m_selectList[index];

        if (item.Expression is not WitSqlExpressionFunctionCall func)
            return WitSqlValue.Null;

        var funcName = func.FunctionName.ToUpperInvariant();

        return funcName switch
        {
            "COUNT" => WitSqlValue.FromInt(acc.Count),
            "SUM" => acc.Sum ?? WitSqlValue.Null,
            "AVG" => acc.Count > 0 && acc.Sum != null
                ? acc.Sum.Value.Divide(WitSqlValue.FromInt(acc.Count))
                : WitSqlValue.Null,
            "MIN" => acc.Min ?? WitSqlValue.Null,
            "MAX" => acc.Max ?? WitSqlValue.Null,
            _ => WitSqlValue.Null
        };
    }

    private WitSqlRow BuildResultRow()
    {
        var values = new WitSqlValue[m_selectList.Count];
        var names = new string[m_selectList.Count];

        for (int i = 0; i < m_selectList.Count; i++)
        {
            names[i] = m_schema[i].Name;
            values[i] = GetAggregateResult(i);
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
        m_resultReturned = false;

        // Reset accumulators
        for (int i = 0; i < m_accumulators.Length; i++)
        {
            m_accumulators[i] = new StreamingAccumulator();
        }

        // Stream through all source rows, updating accumulators
        while (m_source.MoveNext())
        {
            UpdateAccumulators(m_source.Current);
        }

        // Even if no rows, we still return one result row (with COUNT=0, etc.)
        // This is SQL standard behavior for aggregates without GROUP BY
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_resultReturned)
            return false;

        m_resultReturned = true;
        m_current = BuildResultRow();
        return true;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_resultReturned = false;
        m_current = default;

        for (int i = 0; i < m_accumulators.Length; i++)
        {
            m_accumulators[i] = new StreamingAccumulator();
        }
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_source.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion

    #region Nested Types

    /// <summary>
    /// Accumulator for streaming aggregation.
    /// Uses O(1) memory regardless of input size.
    /// </summary>
    private struct StreamingAccumulator
    {
        public long Count;
        public WitSqlValue? Sum;
        public WitSqlValue? Min;
        public WitSqlValue? Max;
        public bool HasValue;
    }

    #endregion
}
