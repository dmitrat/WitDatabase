using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Model;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that performs GROUP BY aggregation.
/// Collects all rows, groups them by key expressions, and computes aggregate functions.
/// This is a blocking operator - it must read all rows before returning any.
/// </summary>
public sealed class IteratorGroupBy : IteratorBase
{
    #region Constants

    private static readonly HashSet<string> AGGREGATE_FUNCTIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "GROUP_CONCAT"
    };

    #endregion

    #region Fields

    private readonly IResultIterator m_source;
    private readonly IReadOnlyList<WitSqlExpression>? m_groupByExpressions;
    private readonly IReadOnlyList<ClauseSelectItem> m_selectList;
    private readonly WitSqlExpression? m_havingClause;
    private readonly ExpressionEvaluator m_evaluator;
    private readonly AggregateExpressionEvaluator? m_aggregateEvaluator;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;

    private Dictionary<string, AggregateGroup>? m_groups;
    private IEnumerator<KeyValuePair<string, AggregateGroup>>? m_groupEnumerator;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new GROUP BY iterator.
    /// </summary>
    /// <param name="source">The source iterator to aggregate.</param>
    /// <param name="groupByExpressions">The GROUP BY expressions (null for aggregate without grouping).</param>
    /// <param name="selectList">The SELECT list containing aggregate and non-aggregate expressions.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="havingClause">Optional HAVING clause for filtering groups.</param>
    public IteratorGroupBy(
        IResultIterator source,
        IReadOnlyList<WitSqlExpression>? groupByExpressions,
        IReadOnlyList<ClauseSelectItem> selectList,
        ContextExecution context,
        WitSqlExpression? havingClause = null)
    {
        m_source = source;
        m_groupByExpressions = groupByExpressions;
        m_selectList = selectList;
        m_havingClause = havingClause;
        m_evaluator = new ExpressionEvaluator(context);
        m_aggregateEvaluator = havingClause != null ? new AggregateExpressionEvaluator(context) : null;
        m_schema = BuildSchema(selectList);
    }

    #endregion

    #region Functions

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
        return funcName switch
        {
            "COUNT" => WitSqlType.Integer,
            "SUM" or "AVG" => WitSqlType.Real,
            "MIN" or "MAX" => WitSqlType.Text, // Type depends on input
            "GROUP_CONCAT" => WitSqlType.Text,
            _ => WitSqlType.Text
        };
    }

    private string ComputeGroupKey(WitSqlRow row)
    {
        if (m_groupByExpressions == null || m_groupByExpressions.Count == 0)
            return string.Empty;

        var parts = new string[m_groupByExpressions.Count];
        for (int i = 0; i < m_groupByExpressions.Count; i++)
        {
            var value = m_evaluator.Evaluate(m_groupByExpressions[i], row);
            parts[i] = value.AsString();
        }
        return string.Join("\0", parts);
    }

    private static bool IsAggregateFunction(WitSqlExpressionFunctionCall func)
    {
        return AGGREGATE_FUNCTIONS.Contains(func.FunctionName);
    }

    private void UpdateAggregate(Accumulator acc, WitSqlExpressionFunctionCall func, WitSqlRow row)
    {
        WitSqlValue value;
        if (func.IsStar)
        {
            value = WitSqlValue.FromInt(1); // COUNT(*)
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
                if (!value.IsNull)
                {
                    if (func.IsDistinct)
                    {
                        acc.DistinctValues ??= [];
                        acc.DistinctValues.Add(value);
                    }
                    else
                    {
                        acc.Count++;
                    }
                }
                break;

            case "SUM":
                if (!value.IsNull)
                {
                    acc.Sum = acc.Sum == null ? value : acc.Sum.Value.Add(value);
                }
                break;

            case "AVG":
                if (!value.IsNull)
                {
                    acc.Sum = acc.Sum == null ? value : acc.Sum.Value.Add(value);
                    acc.Count++;
                }
                break;

            case "MIN":
                if (!value.IsNull && (acc.Min == null || value < acc.Min.Value))
                    acc.Min = value;
                break;

            case "MAX":
                if (!value.IsNull && (acc.Max == null || value > acc.Max.Value))
                    acc.Max = value;
                break;

            case "GROUP_CONCAT":
                if (!value.IsNull)
                {
                    acc.Values ??= [];
                    acc.Values.Add(value.AsString());
                }
                break;
        }
    }

    private static WitSqlValue GetAggregateResult(Accumulator acc, WitSqlExpressionFunctionCall func)
    {
        var funcName = func.FunctionName.ToUpperInvariant();

        return funcName switch
        {
            "COUNT" => func.IsDistinct && acc.DistinctValues != null
                ? WitSqlValue.FromInt(acc.DistinctValues.Count)
                : WitSqlValue.FromInt(acc.Count),
            "SUM" => acc.Sum ?? WitSqlValue.Null,
            "AVG" => acc.Count > 0 && acc.Sum != null
                ? acc.Sum.Value.Divide(WitSqlValue.FromInt(acc.Count))
                : WitSqlValue.Null,
            "MIN" => acc.Min ?? WitSqlValue.Null,
            "MAX" => acc.Max ?? WitSqlValue.Null,
            "GROUP_CONCAT" => acc.Values != null
                ? WitSqlValue.FromText(string.Join(",", acc.Values))
                : WitSqlValue.Null,
            _ => WitSqlValue.Null
        };
    }

    private WitSqlRow BuildResultRow(AggregateGroup group)
    {
        var values = new WitSqlValue[m_selectList.Count];
        var names = new string[m_selectList.Count];

        for (int i = 0; i < m_selectList.Count; i++)
        {
            var item = m_selectList[i];
            names[i] = m_schema[i].Name;

            if (item.Expression is WitSqlExpressionFunctionCall func && IsAggregateFunction(func))
            {
                values[i] = GetAggregateResult(group.Accumulators[i], func);
            }
            else if (item.Expression != null && group.FirstRow.HasValue)
            {
                values[i] = m_evaluator.Evaluate(item.Expression, group.FirstRow.Value);
            }
            else
            {
                values[i] = WitSqlValue.Null;
            }
        }

        return new WitSqlRow(values, names);
    }

    private bool PassesHavingFilter(AggregateGroup group, WitSqlRow resultRow)
    {
        if (m_havingClause == null || m_aggregateEvaluator == null)
            return true;

        var result = m_aggregateEvaluator.Evaluate(m_havingClause, group.AllRows, resultRow);
        return !result.IsNull && result.AsBool();
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();

        m_groups = new Dictionary<string, AggregateGroup>();

        // Process all input rows
        while (m_source.MoveNext())
        {
            var row = m_source.Current;
            var groupKey = ComputeGroupKey(row);

            if (!m_groups.TryGetValue(groupKey, out var group))
            {
                group = new AggregateGroup(row, m_selectList.Count);
                m_groups[groupKey] = group;
            }

            // Store the row for HAVING clause evaluation
            group.AllRows.Add(row);

            // Update aggregates
            for (int i = 0; i < m_selectList.Count; i++)
            {
                var item = m_selectList[i];
                if (item.Expression is WitSqlExpressionFunctionCall func && IsAggregateFunction(func))
                {
                    UpdateAggregate(group.Accumulators[i], func, row);
                }
            }

            group.RowCount++;
        }

        // If no groups and no GROUP BY, create one empty group (for aggregates without GROUP BY)
        if (m_groups.Count == 0 && (m_groupByExpressions == null || m_groupByExpressions.Count == 0))
        {
            m_groups[string.Empty] = new AggregateGroup(null, m_selectList.Count) { RowCount = 0 };
        }

        m_groupEnumerator = m_groups.GetEnumerator();
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        if (m_groupEnumerator == null)
            return false;

        while (m_groupEnumerator.MoveNext())
        {
            var group = m_groupEnumerator.Current.Value;
            var resultRow = BuildResultRow(group);

            // Apply HAVING filter
            if (PassesHavingFilter(group, resultRow))
            {
                m_current = resultRow;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_source.Reset();
        m_groupEnumerator?.Dispose();
        m_groupEnumerator = null;
        m_groups = null;
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_groupEnumerator?.Dispose();
        m_source.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;

    #endregion
}