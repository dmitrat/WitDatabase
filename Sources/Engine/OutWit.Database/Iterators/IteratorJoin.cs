using OutWit.Database.Context;
using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Iterator for JOIN operations (INNER, LEFT, RIGHT, FULL, CROSS).
/// Uses nested-loop join algorithm which buffers the right side.
/// </summary>
public sealed class IteratorJoin : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_left;
    private readonly IResultIterator m_right;
    private readonly JoinType m_joinType;
    private readonly WitSqlExpression? m_onCondition;
    private readonly ExpressionEvaluator m_evaluator;
    private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;

    private List<WitSqlRow>? m_rightRows;
    private int m_rightIndex;
    private bool m_leftMatched;
    private bool m_leftExhausted;
    private bool m_hasLeftRow;
    private int m_unmatchedRightIndex;
    private HashSet<int>? m_matchedRightIndices;
    private WitSqlRow m_current;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new JOIN iterator.
    /// </summary>
    /// <param name="left">The left (outer) iterator.</param>
    /// <param name="right">The right (inner) iterator.</param>
    /// <param name="joinType">The type of join to perform.</param>
    /// <param name="onCondition">The ON condition expression (null for CROSS JOIN).</param>
    /// <param name="context">The execution context.</param>
    public IteratorJoin(
        IResultIterator left,
        IResultIterator right,
        JoinType joinType,
        WitSqlExpression? onCondition,
        ContextExecution context)
    {
        m_left = left;
        m_right = right;
        m_joinType = joinType;
        m_onCondition = onCondition;
        m_evaluator = new ExpressionEvaluator(context);
        m_schema = BuildSchema();
    }

    #endregion

    #region Functions

    private IReadOnlyList<WitSqlColumnInfo> BuildSchema()
    {
        var schema = new List<WitSqlColumnInfo>(m_left.Schema.Count + m_right.Schema.Count);

        foreach (var col in m_left.Schema)
            schema.Add(col);

        foreach (var col in m_right.Schema)
            schema.Add(col);

        return schema;
    }

    private bool MatchesCondition(WitSqlRow row)
    {
        if (m_onCondition == null)
            return true;

        var result = m_evaluator.Evaluate(m_onCondition, row);
        return !result.IsNull && result.AsBool();
    }

    private WitSqlRow CombineRows(WitSqlRow left, WitSqlRow right)
    {
        var leftCount = m_left.Schema.Count;
        var rightCount = m_right.Schema.Count;

        // Calculate total columns: each side contributes simple names + qualified names
        var columnCount = 0;
        foreach (var col in m_left.Schema)
            columnCount += col.TableName != null ? 2 : 1;
        foreach (var col in m_right.Schema)
            columnCount += col.TableName != null ? 2 : 1;

        var columns = new string[columnCount];
        var values = new WitSqlValue[columnCount];
        var index = 0;

        // Add left columns
        AddColumnsFromSchema(m_left.Schema, left, columns, values, ref index);

        // Add right columns
        AddColumnsFromSchema(m_right.Schema, right, columns, values, ref index);

        return new WitSqlRow(values, columns);
    }

    private static void AddColumnsFromSchema(
        IReadOnlyList<WitSqlColumnInfo> schema,
        WitSqlRow row,
        string[] columns,
        WitSqlValue[] values,
        ref int index)
    {
        foreach (var col in schema)
        {
            // Try to get value - first by qualified name, then by simple name
            WitSqlValue value;
            var qualifiedName = col.TableName != null ? $"{col.TableName}.{col.Name}" : null;

            if (qualifiedName != null && row.TryGetValue(qualifiedName, out value))
            {
                // Found by qualified name
            }
            else if (row.TryGetValue(col.Name, out value))
            {
                // Found by simple name
            }
            else
            {
                value = WitSqlValue.Null;
            }

            // Add simple name
            columns[index] = col.Name;
            values[index] = value;
            index++;

            // Add qualified name if table name exists
            if (col.TableName != null)
            {
                columns[index] = qualifiedName!;
                values[index] = value;
                index++;
            }
        }
    }

    private WitSqlRow CreateNullRow(IReadOnlyList<WitSqlColumnInfo> schema)
    {
        var columnCount = 0;
        foreach (var col in schema)
            columnCount += col.TableName != null ? 2 : 1;

        var columns = new string[columnCount];
        var values = new WitSqlValue[columnCount];
        var index = 0;

        foreach (var col in schema)
        {
            columns[index] = col.Name;
            values[index] = WitSqlValue.Null;
            index++;

            if (col.TableName != null)
            {
                columns[index] = $"{col.TableName}.{col.Name}";
                values[index] = WitSqlValue.Null;
                index++;
            }
        }

        return new WitSqlRow(values, columns);
    }

    #endregion

    #region Join Algorithms

    private bool MoveNextCross()
    {
        while (true)
        {
            if (!m_hasLeftRow)
            {
                if (!m_left.MoveNext())
                    return false;
                m_hasLeftRow = true;
            }

            if (m_rightIndex < m_rightRows!.Count)
            {
                m_current = CombineRows(m_left.Current, m_rightRows[m_rightIndex]);
                m_rightIndex++;
                return true;
            }

            if (!m_left.MoveNext())
                return false;

            m_rightIndex = 0;
        }
    }

    private bool MoveNextInner()
    {
        while (true)
        {
            if (!m_hasLeftRow)
            {
                if (!m_left.MoveNext())
                    return false;
                m_hasLeftRow = true;
            }

            while (m_rightIndex < m_rightRows!.Count)
            {
                var rightRow = m_rightRows[m_rightIndex];
                m_rightIndex++;

                var combined = CombineRows(m_left.Current, rightRow);
                if (MatchesCondition(combined))
                {
                    m_current = combined;
                    return true;
                }
            }

            if (!m_left.MoveNext())
                return false;

            m_rightIndex = 0;
        }
    }

    private bool MoveNextLeft()
    {
        while (true)
        {
            if (!m_hasLeftRow)
            {
                if (!m_left.MoveNext())
                    return false;
                m_hasLeftRow = true;
            }

            while (m_rightIndex < m_rightRows!.Count)
            {
                var rightRow = m_rightRows[m_rightIndex];
                m_rightIndex++;

                var combined = CombineRows(m_left.Current, rightRow);
                if (MatchesCondition(combined))
                {
                    m_leftMatched = true;
                    m_current = combined;
                    return true;
                }
            }

            // If left row didn't match any right row, emit null-padded row
            if (!m_leftMatched)
            {
                m_current = CombineRows(m_left.Current, CreateNullRow(m_right.Schema));
                m_leftMatched = true;
                return true;
            }

            if (!m_left.MoveNext())
                return false;

            m_rightIndex = 0;
            m_leftMatched = false;
        }
    }

    private bool MoveNextRight()
    {
        while (true)
        {
            if (!m_hasLeftRow && !m_leftExhausted)
            {
                if (!m_left.MoveNext())
                    m_leftExhausted = true;
                else
                    m_hasLeftRow = true;
            }

            if (m_leftExhausted)
                return EmitUnmatchedRightRows();

            while (m_rightIndex < m_rightRows!.Count)
            {
                var rightRow = m_rightRows[m_rightIndex];
                var currentRightIndex = m_rightIndex;
                m_rightIndex++;

                var combined = CombineRows(m_left.Current, rightRow);
                if (MatchesCondition(combined))
                {
                    m_matchedRightIndices!.Add(currentRightIndex);
                    m_current = combined;
                    return true;
                }
            }

            if (!m_left.MoveNext())
            {
                m_leftExhausted = true;
                return EmitUnmatchedRightRows();
            }

            m_rightIndex = 0;
        }
    }

    private bool MoveNextFull()
    {
        while (true)
        {
            if (!m_hasLeftRow && !m_leftExhausted)
            {
                if (!m_left.MoveNext())
                    m_leftExhausted = true;
                else
                    m_hasLeftRow = true;
            }

            if (m_leftExhausted)
                return EmitUnmatchedRightRows();

            while (m_rightIndex < m_rightRows!.Count)
            {
                var rightRow = m_rightRows[m_rightIndex];
                var currentRightIndex = m_rightIndex;
                m_rightIndex++;

                var combined = CombineRows(m_left.Current, rightRow);
                if (MatchesCondition(combined))
                {
                    m_leftMatched = true;
                    m_matchedRightIndices!.Add(currentRightIndex);
                    m_current = combined;
                    return true;
                }
            }

            // If left row didn't match any right row, emit null-padded row
            if (!m_leftMatched)
            {
                m_current = CombineRows(m_left.Current, CreateNullRow(m_right.Schema));
                m_leftMatched = true;
                return true;
            }

            if (!m_left.MoveNext())
            {
                m_leftExhausted = true;
                return EmitUnmatchedRightRows();
            }

            m_rightIndex = 0;
            m_leftMatched = false;
        }
    }

    private bool EmitUnmatchedRightRows()
    {
        while (m_unmatchedRightIndex < m_rightRows!.Count)
        {
            if (!m_matchedRightIndices!.Contains(m_unmatchedRightIndex))
            {
                m_current = CombineRows(CreateNullRow(m_left.Schema), m_rightRows[m_unmatchedRightIndex]);
                m_unmatchedRightIndex++;
                return true;
            }
            m_unmatchedRightIndex++;
        }
        return false;
    }

    #endregion

    #region IResultIterator

    /// <inheritdoc/>
    public override void Open()
    {
        base.Open();
        m_left.Open();
        m_right.Open();

        // Buffer right side for nested-loop join
        m_rightRows = [];
        while (m_right.MoveNext())
        {
            m_rightRows.Add(m_right.Current);
        }

        m_rightIndex = 0;
        m_leftMatched = false;
        m_leftExhausted = false;
        m_hasLeftRow = false;
        m_unmatchedRightIndex = 0;

        // For RIGHT and FULL OUTER JOIN, track which right rows matched
        if (m_joinType is JoinType.Right or JoinType.Full)
        {
            m_matchedRightIndices = [];
        }
    }

    /// <inheritdoc/>
    public override bool MoveNext()
    {
        return m_joinType switch
        {
            JoinType.Cross => MoveNextCross(),
            JoinType.Inner => MoveNextInner(),
            JoinType.Left => MoveNextLeft(),
            JoinType.Right => MoveNextRight(),
            JoinType.Full => MoveNextFull(),
            _ => throw new NotSupportedException($"Join type {m_joinType} not supported")
        };
    }

    /// <inheritdoc/>
    public override void Reset()
    {
        base.Reset();
        m_left.Reset();
        m_right.Reset();
        m_rightRows = null;
        m_rightIndex = 0;
        m_leftMatched = false;
        m_leftExhausted = false;
        m_hasLeftRow = false;
        m_unmatchedRightIndex = 0;
        m_matchedRightIndices?.Clear();
        m_current = default;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public override void Dispose()
    {
        m_left.Dispose();
        m_right.Dispose();
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    /// <inheritdoc/>
    public override WitSqlRow Current => m_current;


    #endregion
}
