using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Model;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;
using OutWit.Database.Parser.Analysis;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator that performs GROUP BY aggregation.
/// Collects all rows, groups them by key expressions, and computes aggregate functions.
/// This is a blocking operator - it must read all rows before returning any.
/// </summary>
/// <remarks>
/// Optimization: AllRows list is only populated when HAVING clause exists.
/// This reduces memory usage by 10-50x for queries without HAVING.
/// </remarks>
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
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;
    private readonly bool m_needsAllRows;

    private Dictionary<GroupKey, AggregateGroup>? m_groups;
    private IEnumerator<KeyValuePair<GroupKey, AggregateGroup>>? m_groupEnumerator;
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
        m_schema = BuildSchema(selectList);
        
        // Only store all rows if HAVING clause exists (needed for aggregate evaluation in HAVING)
        // HAVING is not the only thing that needs the group's rows. A select item that CONTAINS an
        // aggregate without BEING one - MAX(Age) BETWEEN 1 AND 200 - is evaluated over the group
        // rather than from an accumulator, and with this left at "HAVING only" it was computed over
        // an empty list and quietly returned NULL. A wrong answer, not an error.
        m_needsAllRows = havingClause != null || selectList.Any(NeedsGroupRows);
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
            "MIN" or "MAX" => WitSqlType.Text,
            "GROUP_CONCAT" => WitSqlType.Text,
            _ => WitSqlType.Text
        };
    }

    private GroupKey ComputeGroupKey(WitSqlRow row)
    {
        if (m_groupByExpressions == null || m_groupByExpressions.Count == 0)
            return GroupKey.Empty;

        var count = m_groupByExpressions.Count;
        
        // Optimized path for 1-4 columns (most common cases)
        if (count == 1)
        {
            var v0 = m_evaluator.Evaluate(m_groupByExpressions[0], row);
            return new GroupKey(v0);
        }
        
        if (count == 2)
        {
            var v0 = m_evaluator.Evaluate(m_groupByExpressions[0], row);
            var v1 = m_evaluator.Evaluate(m_groupByExpressions[1], row);
            return new GroupKey(v0, v1);
        }
        
        if (count == 3)
        {
            var v0 = m_evaluator.Evaluate(m_groupByExpressions[0], row);
            var v1 = m_evaluator.Evaluate(m_groupByExpressions[1], row);
            var v2 = m_evaluator.Evaluate(m_groupByExpressions[2], row);
            return new GroupKey(v0, v1, v2);
        }
        
        if (count == 4)
        {
            var v0 = m_evaluator.Evaluate(m_groupByExpressions[0], row);
            var v1 = m_evaluator.Evaluate(m_groupByExpressions[1], row);
            var v2 = m_evaluator.Evaluate(m_groupByExpressions[2], row);
            var v3 = m_evaluator.Evaluate(m_groupByExpressions[3], row);
            return new GroupKey(v0, v1, v2, v3);
        }

        // Fallback for 5+ columns
        var values = new WitSqlValue[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = m_evaluator.Evaluate(m_groupByExpressions[i], row);
        }
        return new GroupKey(values);
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

    /// <summary>
    /// Whether this select item has to be evaluated over the whole group rather than from an
    /// accumulator: it mentions an aggregate somewhere but is not itself a single aggregate call.
    /// </summary>
    private static bool NeedsGroupRows(ClauseSelectItem item) =>
        item.Expression is not null
        && !(item.Expression is WitSqlExpressionFunctionCall direct && IsAggregateFunction(direct))
        && WitSqlNodes.SelfAndDescendants(item.Expression)
            .OfType<WitSqlExpressionFunctionCall>()
            .Any(IsAggregateFunction);

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
                // EvaluateAggregate, not Evaluate: the select item may CONTAIN an aggregate without
                // being one - MAX(Age) BETWEEN 1 AND 200, IIF(COUNT(*) > 1, …), CAST(SUM(x) AS INT).
                // The plain evaluator refuses aggregates, so those threw "Function not supported".
                // It falls back to plain evaluation when there is no aggregate in the expression.
                values[i] = m_evaluator.EvaluateAggregate(item.Expression, group.AllRows, group.FirstRow.Value);
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
        if (m_havingClause == null)
            return true;

        var result = m_evaluator.EvaluateAggregate(m_havingClause, group.AllRows, resultRow);
        return !result.IsNull && result.AsBool();
    }

    /// <summary>
    /// Estimates the initial capacity for the groups dictionary (P1.6 optimization).
    /// Uses source iterator's estimated row count to reduce dictionary resizes.
    /// </summary>
    private int EstimateDictionaryCapacity()
    {
        // No GROUP BY means single group
        if (m_groupByExpressions == null || m_groupByExpressions.Count == 0)
            return 1;

        // Get estimated row count from source
        long estimatedRows = m_source.EstimatedRowCount;
        
        if (estimatedRows <= 0)
            return 16; // Default initial capacity
        
        // Heuristic: estimate ~10% of rows will be unique groups
        // This is a reasonable assumption for most GROUP BY queries
        int estimatedGroups = (int)Math.Min(estimatedRows / 10 + 1, 10000);
        
        // Ensure minimum capacity of 16 and maximum of 10000
        return Math.Clamp(estimatedGroups, 16, 10000);
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_source.Open();

        // P1.6 optimization: Pre-allocate dictionary capacity based on estimated row count
        // Estimate ~10% of rows will be unique groups (common heuristic)
        // Cap at reasonable size to avoid over-allocation for small tables
        int estimatedCapacity = EstimateDictionaryCapacity();
        m_groups = new Dictionary<GroupKey, AggregateGroup>(estimatedCapacity);

        // Process all input rows
        while (m_source.MoveNext())
        {
            var row = m_source.Current;
            var groupKey = ComputeGroupKey(row);

            if (!m_groups.TryGetValue(groupKey, out var group))
            {
                group = new AggregateGroup(row, m_selectList.Count, m_needsAllRows);
                m_groups[groupKey] = group;
            }

            // Only store row if HAVING clause exists (P0.1 optimization)
            if (m_needsAllRows)
            {
                group.AllRows.Add(row);
            }

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
            m_groups[GroupKey.Empty] = new AggregateGroup(null, m_selectList.Count, m_needsAllRows) { RowCount = 0 };
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

    #region Nested Types

    /// <summary>
    /// Struct-based composite key for GROUP BY (P0.2 optimization).
    /// Avoids string allocation for key computation.
    /// </summary>
    private readonly struct GroupKey : IEquatable<GroupKey>
    {
        public static readonly GroupKey Empty = new();

        private readonly WitSqlValue m_v0;
        private readonly WitSqlValue m_v1;
        private readonly WitSqlValue m_v2;
        private readonly WitSqlValue m_v3;
        private readonly WitSqlValue[]? m_extra;
        private readonly int m_count;
        private readonly int m_hashCode;

        public GroupKey()
        {
            m_count = 0;
            m_hashCode = 0;
        }

        public GroupKey(WitSqlValue v0)
        {
            m_v0 = v0;
            m_count = 1;
            m_hashCode = v0.GetHashCode();
        }

        public GroupKey(WitSqlValue v0, WitSqlValue v1)
        {
            m_v0 = v0;
            m_v1 = v1;
            m_count = 2;
            m_hashCode = HashCode.Combine(v0, v1);
        }

        public GroupKey(WitSqlValue v0, WitSqlValue v1, WitSqlValue v2)
        {
            m_v0 = v0;
            m_v1 = v1;
            m_v2 = v2;
            m_count = 3;
            m_hashCode = HashCode.Combine(v0, v1, v2);
        }

        public GroupKey(WitSqlValue v0, WitSqlValue v1, WitSqlValue v2, WitSqlValue v3)
        {
            m_v0 = v0;
            m_v1 = v1;
            m_v2 = v2;
            m_v3 = v3;
            m_count = 4;
            m_hashCode = HashCode.Combine(v0, v1, v2, v3);
        }

        public GroupKey(WitSqlValue[] values)
        {
            m_count = values.Length;
            if (m_count > 0) m_v0 = values[0];
            if (m_count > 1) m_v1 = values[1];
            if (m_count > 2) m_v2 = values[2];
            if (m_count > 3) m_v3 = values[3];
            if (m_count > 4)
            {
                m_extra = new WitSqlValue[m_count - 4];
                Array.Copy(values, 4, m_extra, 0, m_count - 4);
            }

            var hash = new HashCode();
            foreach (var v in values)
            {
                hash.Add(v);
            }
            m_hashCode = hash.ToHashCode();
        }

        public bool Equals(GroupKey other)
        {
            if (m_count != other.m_count)
                return false;

            if (m_count == 0)
                return true;

            if (m_v0 != other.m_v0)
                return false;

            if (m_count == 1)
                return true;

            if (m_v1 != other.m_v1)
                return false;

            if (m_count == 2)
                return true;

            if (m_v2 != other.m_v2)
                return false;

            if (m_count == 3)
                return true;

            if (m_v3 != other.m_v3)
                return false;

            if (m_count == 4)
                return true;

            // Compare extra values
            if (m_extra == null || other.m_extra == null)
                return m_extra == other.m_extra;

            for (int i = 0; i < m_extra.Length; i++)
            {
                if (m_extra[i] != other.m_extra[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is GroupKey other && Equals(other);

        public override int GetHashCode() => m_hashCode;
    }

    #endregion
}