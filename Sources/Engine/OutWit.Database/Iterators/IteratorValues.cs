using OutWit.Database.Expressions;
using OutWit.Database.Interfaces;
using OutWit.Database.Model;
using OutWit.Database.Context;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Iterators;

/// <summary>
/// Yields the literal rows of a <c>VALUES</c> query.
/// </summary>
/// <remarks>
/// <para>
/// <c>VALUES (1), (2)</c> is a query in its own right in both drop-in targets, and
/// <c>SELECT * FROM (VALUES …) AS V</c> is the shape it is used in. The rows are expressions rather
/// than constants - <c>VALUES (1 + 1)</c> is legal - so each is evaluated once, against an empty
/// row, when the iterator opens.
/// </para>
/// <para>
/// Columns are named <c>column1</c>, <c>column2</c>, … after PostgreSQL. SQL Server requires a
/// derived column list and names nothing; PostgreSQL's names give a caller something to select by
/// when none was supplied, and a derived column list overrides them.
/// </para>
/// </remarks>
public sealed class IteratorValues : IteratorBase
{
    #region Constants

    private const string DEFAULT_COLUMN_PREFIX = "column";

    #endregion

    #region Fields

    private readonly IReadOnlyList<IReadOnlyList<WitSqlExpression>> m_rows;
    private readonly ContextExecution m_context;
    private readonly string[] m_columnNames;

    private IReadOnlyList<WitSqlColumnInfo> m_schema;
    private WitSqlRow m_current;
    private WitSqlRow[]? m_evaluated;
    private int m_position;

    #endregion

    #region Constructors

    public IteratorValues(IReadOnlyList<IReadOnlyList<WitSqlExpression>> rows, ContextExecution context)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("A VALUES query must have at least one row.");

        var width = rows[0].Count;

        foreach (var row in rows)
        {
            if (row.Count != width)
            {
                throw new InvalidOperationException(
                    $"Every row of a VALUES query must have the same number of values; " +
                    $"one has {row.Count} where the first has {width}.");
            }
        }

        m_rows = rows;
        m_context = context;
        m_columnNames = Enumerable.Range(1, width)
            .Select(i => $"{DEFAULT_COLUMN_PREFIX}{i}")
            .ToArray();

        m_schema = m_columnNames
            .Select(name => new WitSqlColumnInfo { Name = name, Type = WitSqlType.Null, IsNullable = true })
            .ToList();

        m_current = new WitSqlRow([], []);
    }

    #endregion

    #region IResultIterator

    public override void Open()
    {
        base.Open();

        var evaluator = new ExpressionEvaluator(m_context);
        var empty = new WitSqlRow([], []);

        m_evaluated = m_rows
            .Select(row => new WitSqlRow(
                row.Select(value => evaluator.Evaluate(value, empty)).ToArray(),
                m_columnNames))
            .ToArray();

        // The declared types follow the first row, which is what a caller can observe about a
        // literal set. A column whose rows disagree stays whatever the first row made it; the values
        // themselves carry their own types.
        if (m_evaluated.Length > 0)
        {
            m_schema = m_columnNames
                .Select((name, i) => new WitSqlColumnInfo
                {
                    Name = name,
                    Type = m_evaluated[0][i].Type,
                    IsNullable = true
                })
                .ToList();
        }

        m_position = 0;
    }

    public override bool MoveNext()
    {
        if (m_evaluated is null || m_position >= m_evaluated.Length)
            return false;

        m_current = m_evaluated[m_position];
        m_position++;
        return true;
    }

    public override void Reset()
    {
        m_position = 0;
        base.Reset();
    }

    public override long EstimatedRowCount => m_rows.Count;

    public override IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;

    public override WitSqlRow Current => m_current;

    #endregion
}
