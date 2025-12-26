using NSubstitute;
using OutWit.Database.Context;
using OutWit.Database.Interfaces;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Iterators;

/// <summary>
/// Base class for iterator tests providing common test infrastructure.
/// </summary>
public abstract class IteratorTestsBase
{
    #region Fields

    protected ContextExecution m_context = null!;
    protected IDatabase m_database = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_database = Substitute.For<IDatabase>();
        m_context = new ContextExecution
        {
            Database = m_database,
            CancellationToken = CancellationToken.None
        };
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates an in-memory iterator from the specified rows.
    /// </summary>
    protected static MockIterator CreateMockIterator(params WitSqlRow[] rows)
    {
        return new MockIterator(rows);
    }

    /// <summary>
    /// Creates an in-memory iterator with the specified schema and rows.
    /// </summary>
    protected static MockIterator CreateMockIterator(IReadOnlyList<WitSqlColumnInfo> schema, params WitSqlRow[] rows)
    {
        return new MockIterator(schema, rows);
    }

    /// <summary>
    /// Creates a row with the specified columns and values.
    /// </summary>
    protected static WitSqlRow CreateRow(params (string name, WitSqlValue value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => c.value).ToArray();
        return new WitSqlRow(values, names);
    }

    /// <summary>
    /// Creates a row with integer values.
    /// </summary>
    protected static WitSqlRow CreateRowWithInts(params (string name, long value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => WitSqlValue.FromInt(c.value)).ToArray();
        return new WitSqlRow(values, names);
    }

    /// <summary>
    /// Creates a simple schema with the specified column names (all as Text type).
    /// </summary>
    protected static IReadOnlyList<WitSqlColumnInfo> CreateSchema(params string[] columnNames)
    {
        return columnNames.Select(n => new WitSqlColumnInfo { Name = n, Type = WitSqlType.Text }).ToList();
    }

    /// <summary>
    /// Collects all rows from an iterator.
    /// </summary>
    protected static List<WitSqlRow> CollectAllRows(IResultIterator iterator)
    {
        var rows = new List<WitSqlRow>();
        iterator.Open();
        while (iterator.MoveNext())
        {
            rows.Add(iterator.Current);
        }
        return rows;
    }

    #endregion

    #region Mock Iterator

    /// <summary>
    /// Simple mock iterator for testing.
    /// </summary>
    protected sealed class MockIterator : IResultIterator
    {
        private readonly WitSqlRow[] m_rows;
        private readonly IReadOnlyList<WitSqlColumnInfo> m_schema;
        private int m_position;
        private bool m_isOpen;

        public MockIterator(WitSqlRow[] rows)
        {
            m_rows = rows;
            m_schema = rows.Length > 0
                ? rows[0].ColumnNames.Select(n => new WitSqlColumnInfo { Name = n, Type = WitSqlType.Text }).ToList()
                : [];
            m_position = -1;
        }

        public MockIterator(IReadOnlyList<WitSqlColumnInfo> schema, WitSqlRow[] rows)
        {
            m_rows = rows;
            m_schema = schema;
            m_position = -1;
        }

        public IReadOnlyList<WitSqlColumnInfo> Schema => m_schema;
        public WitSqlRow Current => m_rows[m_position];
        public long EstimatedRowCount => m_rows.Length;

        public void Open()
        {
            m_isOpen = true;
            m_position = -1;
        }

        public bool MoveNext()
        {
            if (!m_isOpen)
                throw new InvalidOperationException("Iterator not open");
            m_position++;
            return m_position < m_rows.Length;
        }

        public void Reset()
        {
            m_isOpen = false;
            m_position = -1;
        }

        public void Dispose() { }
    }

    #endregion
}
