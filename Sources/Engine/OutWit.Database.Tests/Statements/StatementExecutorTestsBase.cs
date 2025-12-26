using NSubstitute;
using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Statements;

/// <summary>
/// Base class for StatementExecutor tests providing common test infrastructure.
/// </summary>
public abstract class StatementExecutorTestsBase
{
    #region Fields

    protected ContextExecution m_context = null!;
    protected IDatabase m_database = null!;

    #endregion

    #region Setup

    [SetUp]
    public virtual void Setup()
    {
        m_database = Substitute.For<IDatabase>();
        m_context = new ContextExecution
        {
            Database = m_database,
            CancellationToken = CancellationToken.None
        };
    }

    #endregion

    #region Table Helpers

    /// <summary>
    /// Creates a simple table definition with the specified columns.
    /// </summary>
    protected static DefinitionTable CreateTableDef(string tableName, params (string name, WitDataType type, bool isPk)[] columns)
    {
        return new DefinitionTable
        {
            Name = tableName,
            Columns = columns.Select((c, i) => new DefinitionColumn
            {
                Name = c.name,
                Type = c.type,
                IsPrimaryKey = c.isPk,
                IsAutoIncrement = c.isPk && c.type is WitDataType.Int32 or WitDataType.Int64,
                Ordinal = i
            }).ToList()
        };
    }

    /// <summary>
    /// Creates a table with Id (PK), Name columns.
    /// </summary>
    protected static DefinitionTable CreateUsersTable()
    {
        return new DefinitionTable
        {
            Name = "Users",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DefinitionColumn { Name = "Name", Type = WitDataType.StringVariable, Ordinal = 1 },
                new DefinitionColumn { Name = "Email", Type = WitDataType.StringVariable, Ordinal = 2 }
            ],
            PrimaryKey = ["Id"]
        };
    }

    /// <summary>
    /// Creates a table with constraints for testing.
    /// </summary>
    protected static DefinitionTable CreateUsersTableWithConstraints()
    {
        return new DefinitionTable
        {
            Name = "Users",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DefinitionColumn { Name = "Name", Type = WitDataType.StringVariable, Nullable = false, Ordinal = 1 },
                new DefinitionColumn { Name = "Email", Type = WitDataType.StringVariable, IsUnique = true, Ordinal = 2 },
                new DefinitionColumn { Name = "Age", Type = WitDataType.Int32, CheckExpression = "Age >= 0 AND Age <= 150", Ordinal = 3 }
            ],
            PrimaryKey = ["Id"]
        };
    }

    /// <summary>
    /// Creates Orders table with FK to Users.
    /// </summary>
    protected static DefinitionTable CreateOrdersTableWithFK()
    {
        return new DefinitionTable
        {
            Name = "Orders",
            Columns =
            [
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int64, IsPrimaryKey = true, IsAutoIncrement = true, Ordinal = 0 },
                new DefinitionColumn { Name = "UserId", Type = WitDataType.Int64, Ordinal = 1 },
                new DefinitionColumn { Name = "Total", Type = WitDataType.Decimal, Ordinal = 2 }
            ],
            PrimaryKey = ["Id"],
            ForeignKeys =
            [
                new DefinitionForeignKey
                {
                    Columns = ["UserId"],
                    ForeignTable = "Users",
                    ForeignColumns = ["Id"]
                }
            ]
        };
    }

    #endregion

    #region Row Helpers

    /// <summary>
    /// Creates an empty row.
    /// </summary>
    protected static WitSqlRow CreateEmptyRow()
    {
        return new WitSqlRow([], []);
    }

    /// <summary>
    /// Creates a row with the specified columns.
    /// </summary>
    protected static WitSqlRow CreateRow(params (string name, WitSqlValue value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => c.value).ToArray();
        return new WitSqlRow(values, names);
    }

    /// <summary>
    /// Creates a user row.
    /// </summary>
    protected static WitSqlRow CreateUserRow(long id, string name, string email)
    {
        return CreateRow(
            ("_rowid", WitSqlValue.FromInt(id)),
            ("Id", WitSqlValue.FromInt(id)),
            ("Name", WitSqlValue.FromText(name)),
            ("Email", WitSqlValue.FromText(email))
        );
    }

    #endregion

    #region Mock Iterator

    /// <summary>
    /// Creates a mock iterator returning the specified rows.
    /// </summary>
    protected static MockIterator CreateMockIterator(params WitSqlRow[] rows)
    {
        return new MockIterator(rows);
    }

    /// <summary>
    /// Creates an empty mock iterator.
    /// </summary>
    protected static MockIterator CreateEmptyIterator()
    {
        return new MockIterator([]);
    }

    /// <summary>
    /// Mock iterator for testing.
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

    #region Result Helpers

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
}
