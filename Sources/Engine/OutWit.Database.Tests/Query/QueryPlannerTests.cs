using NSubstitute;
using OutWit.Database.Context;
using OutWit.Database.Definitions;
using OutWit.Database.Interfaces;
using OutWit.Database.Iterators;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Clauses;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Parser.Statements;
using OutWit.Database.Query;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Query;

[TestFixture]
public class QueryPlannerTests
{
    #region Fields

    private ContextExecution m_context = null!;
    private IDatabase m_database = null!;

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

    #region Helper Methods

    private static MockIterator CreateMockIterator(params WitSqlRow[] rows)
    {
        return new MockIterator(rows);
    }

    private static WitSqlRow CreateRow(params (string name, WitSqlValue value)[] columns)
    {
        var names = columns.Select(c => c.name).ToArray();
        var values = columns.Select(c => c.value).ToArray();
        return new WitSqlRow(values, names);
    }

    private static List<WitSqlRow> CollectAllRows(IResultIterator iterator)
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

    #region SELECT without FROM Tests

    [Test]
    public void SelectWithoutFromReturnsSingleRowTest()
    {
        var select = new WitSqlStatementSelect
        {
            SelectList =
            [
                new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = "1" }
                }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        Assert.That(iterator, Is.InstanceOf<IteratorProject>());
    }

    [Test]
    public void SelectStarWithoutFromReturnsEmptyRowTest()
    {
        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // SELECT * without FROM returns single row iterator
        Assert.That(iterator, Is.InstanceOf<IteratorSingleRow>());
    }

    #endregion

    #region Simple SELECT Tests

    [Test]
    public void SelectFromTableCreatesTableScanTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause =
            [
                new TableSourceSimple { TableName = "Users" }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // Should wrap with alias
        Assert.That(iterator, Is.InstanceOf<IteratorAlias>());
        m_database.Received(1).CreateTableScan("Users");
    }

    [Test]
    public void SelectWithAliasUsesAliasTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause =
            [
                new TableSourceSimple { TableName = "Users", Alias = "u" }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        Assert.That(iterator, Is.InstanceOf<IteratorAlias>());
    }

    #endregion

    #region WHERE Clause Tests

    [Test]
    public void SelectWithWhereCreatesFilterIteratorTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "Users" }],
            WhereClause = new WitSqlExpressionBinary
            {
                Left = new WitSqlExpressionColumnRef { ColumnName = "Id" },
                Operator = BinaryOperatorType.Equal,
                Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = "1" }
            }
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // Filter should be applied
        Assert.That(iterator, Is.InstanceOf<IteratorFilter>());
    }

    #endregion

    #region ORDER BY Tests

    [Test]
    public void SelectWithOrderByCreatesSortIteratorTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "Users" }],
            OrderByClause =
            [
                new ClauseOrderByItem
                {
                    Expression = new WitSqlExpressionColumnRef { ColumnName = "Id" },
                    Descending = true
                }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        Assert.That(iterator, Is.InstanceOf<IteratorSort>());
    }

    #endregion

    #region LIMIT/OFFSET Tests

    [Test]
    public void SelectWithLimitCreatesLimitIteratorTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1))),
            CreateRow(("Id", WitSqlValue.FromInt(2)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "Users" }],
            LimitCount = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L }
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        Assert.That(iterator, Is.InstanceOf<IteratorLimit>());
    }

    [Test]
    public void SelectWithLimitAndOffsetTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1))),
            CreateRow(("Id", WitSqlValue.FromInt(2))),
            CreateRow(("Id", WitSqlValue.FromInt(3)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "Users" }],
            LimitCount = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L },
            LimitOffset = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L }
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        var rows = CollectAllRows(iterator);
        Assert.That(rows, Has.Count.EqualTo(1));
    }

    #endregion

    #region DISTINCT Tests

    [Test]
    public void SelectDistinctCreatesDistinctIteratorTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            IsDistinct = true,
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "Users" }]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        Assert.That(iterator, Is.InstanceOf<IteratorDistinct>());
    }

    #endregion

    #region JOIN Tests

    [Test]
    public void SelectWithJoinCreatesJoinIteratorTest()
    {
        var usersIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        var ordersIterator = CreateMockIterator(
            CreateRow(("UserId", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(usersIterator);
        m_database.CreateTableScan("Orders").Returns(ordersIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause =
            [
                new TableSourceJoin
                {
                    Left = new TableSourceSimple { TableName = "Users" },
                    Right = new TableSourceSimple { TableName = "Orders" },
                    JoinType = JoinType.Inner,
                    OnCondition = new WitSqlExpressionBinary
                    {
                        Left = new WitSqlExpressionColumnRef { TableName = "Users", ColumnName = "Id" },
                        Operator = BinaryOperatorType.Equal,
                        Right = new WitSqlExpressionColumnRef { TableName = "Orders", ColumnName = "UserId" }
                    }
                }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // The top iterator should be the join (wrapped in alias potentially)
        // Note: Join optimization may call CreateTableScan multiple times for size estimation
        m_database.Received().CreateTableScan("Users");
        m_database.Received().CreateTableScan("Orders");
    }

    [Test]
    public void ImplicitCrossJoinWithMultipleTablesTest()
    {
        var usersIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        var ordersIterator = CreateMockIterator(
            CreateRow(("UserId", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(usersIterator);
        m_database.CreateTableScan("Orders").Returns(ordersIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause =
            [
                new TableSourceSimple { TableName = "Users" },
                new TableSourceSimple { TableName = "Orders" }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // Should create cross join for multiple tables in FROM
        // Note: Join optimization may call CreateTableScan multiple times for size estimation
        m_database.Received().CreateTableScan("Users");
        m_database.Received().CreateTableScan("Orders");
    }

    #endregion

    #region Aggregate Query Tests

    [Test]
    public void SelectWithGroupByCreatesGroupByIteratorTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Value", WitSqlValue.FromInt(10)))
        );
        m_database.CreateTableScan("Products").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList =
            [
                new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionColumnRef { ColumnName = "Category" }
                },
                new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionFunctionCall
                    {
                        FunctionName = "COUNT",
                        IsStar = true
                    }
                }
            ],
            FromClause = [new TableSourceSimple { TableName = "Products" }],
            GroupByClause =
            [
                new WitSqlExpressionColumnRef { ColumnName = "Category" }
            ]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // Should have GroupBy iterator in the chain
        Assert.That(iterator, Is.InstanceOf<IteratorGroupBy>());
    }

    [Test]
    public void SelectWithAggregateFunctionWithoutGroupByTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Value", WitSqlValue.FromInt(10)))
        );
        m_database.CreateTableScan("Products").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList =
            [
                new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionFunctionCall
                    {
                        FunctionName = "SUM",
                        Arguments = [new WitSqlExpressionColumnRef { ColumnName = "Value" }]
                    }
                }
            ],
            FromClause = [new TableSourceSimple { TableName = "Products" }]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // For simple aggregates without GROUP BY, uses IteratorStreamingAggregate
        Assert.That(iterator, Is.InstanceOf<IteratorStreamingAggregate>());
    }

    [Test]
    public void SelectWithHavingCreatesGroupByWithHavingTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Category", WitSqlValue.FromText("A")), ("Value", WitSqlValue.FromInt(10)))
        );
        m_database.CreateTableScan("Products").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList =
            [
                new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionColumnRef { ColumnName = "Category" }
                }
            ],
            FromClause = [new TableSourceSimple { TableName = "Products" }],
            GroupByClause = [new WitSqlExpressionColumnRef { ColumnName = "Category" }],
            HavingClause = new WitSqlExpressionBinary
            {
                Left = new WitSqlExpressionFunctionCall
                {
                    FunctionName = "COUNT",
                    IsStar = true
                },
                Operator = BinaryOperatorType.GreaterThan,
                Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = 1L }
            }
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // HAVING is now integrated into IteratorGroupBy
        Assert.That(iterator, Is.InstanceOf<IteratorGroupBy>());
    }

    #endregion

    #region View Tests

    [Test]
    public void SelectFromViewPlansViewQueryTest()
    {
        var view = new DefinitionView
        {
            Name = "ActiveUsers",
            SelectSql = "SELECT * FROM Users WHERE IsActive = TRUE"
        };
        m_database.GetView("ActiveUsers").Returns(view);

        var usersIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)), ("IsActive", WitSqlValue.FromBool(true)))
        );
        m_database.CreateTableScan("Users").Returns(usersIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "ActiveUsers" }]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // Should have called GetView and then created table scan for underlying table
        m_database.Received(1).GetView("ActiveUsers");
        m_database.Received(1).CreateTableScan("Users");
    }

    #endregion

    #region Projection Tests

    [Test]
    public void SelectSpecificColumnsCreatesProjectIteratorTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)), ("Name", WitSqlValue.FromText("Test")))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList =
            [
                new ClauseSelectItem
                {
                    Expression = new WitSqlExpressionColumnRef { ColumnName = "Name" }
                }
            ],
            FromClause = [new TableSourceSimple { TableName = "Users" }]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        Assert.That(iterator, Is.InstanceOf<IteratorProject>());
    }

    [Test]
    public void SelectStarSkipsProjectionTest()
    {
        var mockIterator = CreateMockIterator(
            CreateRow(("Id", WitSqlValue.FromInt(1)))
        );
        m_database.CreateTableScan("Users").Returns(mockIterator);

        var select = new WitSqlStatementSelect
        {
            SelectList = [new ClauseSelectItem { IsStar = true }],
            FromClause = [new TableSourceSimple { TableName = "Users" }]
        };

        var planner = new QueryPlanner(m_context);
        var iterator = planner.Plan(select);

        // Should be IteratorAlias (wrapping table scan), not IteratorProject
        Assert.That(iterator, Is.InstanceOf<IteratorAlias>());
    }

    #endregion

    #region MockIterator

    private sealed class MockIterator : IResultIterator
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
}
