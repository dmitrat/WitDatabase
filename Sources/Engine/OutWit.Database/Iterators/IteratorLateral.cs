using OutWit.Database.Context;
using OutWit.Database.Interfaces;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Query;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Runs a subquery once per row of its left source, with that row in scope.
/// </summary>
/// <remarks>
/// <para>
/// This is <c>LATERAL</c> and <c>CROSS</c>/<c>OUTER APPLY</c> - one capability with two spellings,
/// both drop-in targets have it, and the correlation machinery already existed: a subquery in
/// <c>EXISTS</c>, in <c>IN</c> or in a scalar position is planned with
/// <c>ContextExecution.OuterRow</c> set, and column resolution falls back to it. All this does is
/// reach the same mechanism from a table source.
/// </para>
/// <para>
/// The subquery is planned per outer row rather than once. It has to be: its plan can depend on the
/// outer values - an index seek chosen for one row is wrong for another - and that is the whole
/// point of the construct. It is also why this is the expensive shape of join and why the planner
/// should not reach for it on its own.
/// </para>
/// </remarks>
public sealed class IteratorLateral : IteratorBase
{
    #region Fields

    private readonly IResultIterator m_left;
    private readonly WitSqlStatementSelect m_subquery;
    private readonly ContextExecution m_context;
    private readonly bool m_isOuter;
    private readonly string? m_alias;
    private readonly IReadOnlyList<string>? m_columnAliases;

    private IReadOnlyList<WitSqlColumnInfo> m_schema = [];
    private WitSqlRow m_current;

    private IResultIterator? m_right;
    private WitSqlRow m_outerRow;
    private bool m_outerRowEmitted;
    private bool m_hasOuterRow;

    #endregion

    #region Constructors

    public IteratorLateral(IResultIterator left, WitSqlStatementSelect subquery, ContextExecution context,
        bool isOuter, string? alias, IReadOnlyList<string>? columnAliases)
    {
        m_left = left;
        m_subquery = subquery;
        m_context = context;
        m_isOuter = isOuter;
        m_alias = alias;
        m_columnAliases = columnAliases;
        m_current = new WitSqlRow([], []);
        m_outerRow = new WitSqlRow([], []);
    }

    #endregion

    #region IResultIterator

    public override void Open()
    {
        base.Open();
        m_left.Open();

        m_right = null;
        m_hasOuterRow = false;
        m_outerRowEmitted = false;

        // The schema needs the right side's shape, and the right side does not exist until an outer
        // row does. Planning it once with no outer row gives the column list, which does not depend
        // on the values - only the plan does.
        m_schema = [.. m_left.Schema, .. RightSchema()];
    }

    public override bool MoveNext()
    {
        while (true)
        {
            if (m_right is not null)
            {
                if (MoveRight())
                    return true;

                if (m_isOuter && !m_outerRowEmitted)
                {
                    m_current = Combine(m_outerRow, null);
                    m_outerRowEmitted = true;
                    return true;
                }

                CloseRight();
            }

            if (!m_left.MoveNext())
                return false;

            m_outerRow = m_left.Current;
            m_hasOuterRow = true;
            m_outerRowEmitted = false;
            OpenRight();
        }
    }

    public override void Reset()
    {
        CloseRight();
        m_left.Reset();
        m_hasOuterRow = false;
        base.Reset();
    }

    public override void Dispose()
    {
        CloseRight();
        m_left.Dispose();
        base.Dispose();
    }

    public override long EstimatedRowCount => m_left.EstimatedRowCount;

    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    public override WitSqlRow Current => m_current;

    #endregion

    #region The right side

    private void OpenRight()
    {
        var saved = m_context.OuterRow;
        m_context.OuterRow = m_outerRow;

        try
        {
            m_right = new QueryPlanner(m_context).Plan(m_subquery);
            m_right.Open();
        }
        finally
        {
            m_context.OuterRow = saved;
        }
    }

    private bool MoveRight()
    {
        var saved = m_context.OuterRow;
        m_context.OuterRow = m_outerRow;

        try
        {
            if (!m_right!.MoveNext())
                return false;

            m_current = Combine(m_outerRow, m_right.Current);
            m_outerRowEmitted = true;
            return true;
        }
        finally
        {
            m_context.OuterRow = saved;
        }
    }

    private void CloseRight()
    {
        m_right?.Dispose();
        m_right = null;
    }

    private IReadOnlyList<WitSqlColumnInfo> RightSchema()
    {
        var saved = m_context.OuterRow;

        try
        {
            using var probe = new QueryPlanner(m_context).Plan(m_subquery);
            var columns = probe.Schema
                .Where(column => !IteratorExcludeInternal.IsInternalColumn(column.Name))
                .ToArray();

            if (m_columnAliases is { Count: > 0 } names && names.Count != columns.Length)
            {
                throw new InvalidOperationException(
                    $"The derived column list names {names.Count} column(s) but the subquery " +
                    $"produces {columns.Length}.");
            }

            return columns
                .Select((column, i) => new WitSqlColumnInfo
                {
                    Name = m_columnAliases is { Count: > 0 } aliases ? aliases[i] : column.Name,
                    Type = column.Type,
                    IsNullable = true,
                    TableName = m_alias
                })
                .ToList();
        }
        finally
        {
            m_context.OuterRow = saved;
        }
    }

    #endregion

    #region Rows

    /// <summary>
    /// The outer row followed by the inner one, or by nulls when the subquery gave nothing and this
    /// is the outer form.
    /// </summary>
    private WitSqlRow Combine(WitSqlRow outer, WitSqlRow? inner)
    {
        var rightColumns = m_schema.Count - m_left.Schema.Count;
        var values = new WitSqlValue[outer.Values.Count + rightColumns];
        var names = new string[values.Length];

        for (var i = 0; i < outer.Values.Count; i++)
        {
            values[i] = outer.Values[i];
            names[i] = outer.ColumnNames[i];
        }

        for (var i = 0; i < rightColumns; i++)
        {
            var column = m_schema[m_left.Schema.Count + i];
            var qualified = m_alias is null ? column.Name : $"{m_alias}.{column.Name}";

            values[outer.Values.Count + i] = inner is { } innerRow && i < innerRow.Values.Count
                ? innerRow.Values[i]
                : WitSqlValue.Null;

            names[outer.Values.Count + i] = qualified;
        }

        return new WitSqlRow(values, names);
    }

    #endregion
}
