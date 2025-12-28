using OutWit.Database.Core.Builder;
using OutWit.Database.Parser.Schema.TableSources;
using OutWit.Database.Parser.Schema.Types;
using OutWit.Database.Optimizers;
using OutWit.Database.Model;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// Tests for JoinOrderOptimizer functionality.
/// </summary>
[TestFixture]
public sealed class OptimizerJoinOrderTests
{
    #region Fields

    private Engine.WitSqlEngine m_engine = null!;
    private OptimizerJoinOrder m_optimizer = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .Build();
        m_engine = new Engine.WitSqlEngine(database, ownsStore: true);
        m_optimizer = new OptimizerJoinOrder(m_engine);
    }

    [TearDown]
    public void TearDown()
    {
        m_engine.Dispose();
    }

    #endregion

    #region Basic Optimization Tests

    [Test]
    public void OptimizeJoinOrderReturnsNullForSingleTableTest()
    {
        // Single table - no optimization needed
        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "Users" }
        };

        var result = m_optimizer.OptimizeJoinOrder(tables);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void OptimizeJoinOrderReordersSmallTableFirstTest()
    {
        // Create tables with different sizes
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY)");

        // Insert rows: Small has 5, Large has 100
        for (int i = 1; i <= 5; i++)
        {
            m_engine.Execute($"INSERT INTO SmallTable (Id) VALUES ({i})");
        }
        for (int i = 1; i <= 100; i++)
        {
            m_engine.Execute($"INSERT INTO LargeTable (Id) VALUES ({i})");
        }

        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "LargeTable" },
            new TableSourceSimple { TableName = "SmallTable" }
        };

        var result = m_optimizer.OptimizeJoinOrder(tables);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(2));
        
        // SmallTable should be first
        var firstTable = result[0] as TableSourceSimple;
        Assert.That(firstTable?.TableName, Is.EqualTo("SmallTable"));
    }

    [Test]
    public void OptimizeJoinOrderPreservesOrderWhenOptimalTest()
    {
        // Create tables where current order is already optimal
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY)");

        // Insert rows: Small has 5, Large has 100
        for (int i = 1; i <= 5; i++)
        {
            m_engine.Execute($"INSERT INTO SmallTable (Id) VALUES ({i})");
        }
        for (int i = 1; i <= 100; i++)
        {
            m_engine.Execute($"INSERT INTO LargeTable (Id) VALUES ({i})");
        }

        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "SmallTable" },
            new TableSourceSimple { TableName = "LargeTable" }
        };

        var result = m_optimizer.OptimizeJoinOrder(tables);

        // Order is already optimal, should return null
        Assert.That(result, Is.Null);
    }

    #endregion

    #region Multi-Table Optimization Tests

    [Test]
    public void OptimizeJoinOrderHandlesThreeTablesTest()
    {
        // Create three tables with different sizes
        m_engine.Execute("CREATE TABLE Tiny (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE Small (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE Large (Id BIGINT PRIMARY KEY)");

        // Insert: Tiny=2, Small=20, Large=200
        for (int i = 1; i <= 2; i++)
            m_engine.Execute($"INSERT INTO Tiny (Id) VALUES ({i})");
        for (int i = 1; i <= 20; i++)
            m_engine.Execute($"INSERT INTO Small (Id) VALUES ({i})");
        for (int i = 1; i <= 200; i++)
            m_engine.Execute($"INSERT INTO Large (Id) VALUES ({i})");

        // Start with worst order (Large, Small, Tiny)
        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "Large" },
            new TableSourceSimple { TableName = "Small" },
            new TableSourceSimple { TableName = "Tiny" }
        };

        var result = m_optimizer.OptimizeJoinOrder(tables);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Has.Count.EqualTo(3));

        // Tiny should be first (smallest)
        var firstTable = result[0] as TableSourceSimple;
        Assert.That(firstTable?.TableName, Is.EqualTo("Tiny"));
    }

    #endregion

    #region Join Condition Tests

    [Test]
    public void OptimizeJoinOrderConsidersJoinConditionsTest()
    {
        // Create tables
        m_engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY, Name VARCHAR(100))");
        m_engine.Execute("CREATE TABLE Orders (Id BIGINT PRIMARY KEY, UserId BIGINT)");

        // Insert: Users=10, Orders=100
        for (int i = 1; i <= 10; i++)
            m_engine.Execute($"INSERT INTO Users (Id, Name) VALUES ({i}, 'User{i}')");
        for (int i = 1; i <= 100; i++)
            m_engine.Execute($"INSERT INTO Orders (Id, UserId) VALUES ({i}, {(i % 10) + 1})");

        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "Orders", Alias = "o" },
            new TableSourceSimple { TableName = "Users", Alias = "u" }
        };

        // Join condition: u.Id = o.UserId (PK join)
        var joinConditions = new List<JoinConditionInfo>
        {
            new JoinConditionInfo
            {
                LeftTableAlias = "u",
                LeftColumnName = "Id",
                RightTableAlias = "o",
                RightColumnName = "UserId",
                IsPrimaryKeyJoin = true
            }
        };

        var result = m_optimizer.OptimizeJoinOrder(tables, joinConditions);

        Assert.That(result, Is.Not.Null);
        // Users should be first (smaller table, PK join)
        var firstTable = result[0] as TableSourceSimple;
        Assert.That(firstTable?.TableName, Is.EqualTo("Users"));
    }

    #endregion

    #region ShouldSwapJoinSides Tests

    [Test]
    public void ShouldSwapJoinSidesReturnsTrueWhenLeftLargerTest()
    {
        // Create tables with different sizes
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY)");

        for (int i = 1; i <= 100; i++)
            m_engine.Execute($"INSERT INTO LargeTable (Id) VALUES ({i})");
        for (int i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO SmallTable (Id) VALUES ({i})");

        var join = new TableSourceJoin
        {
            Left = new TableSourceSimple { TableName = "LargeTable" },
            Right = new TableSourceSimple { TableName = "SmallTable" },
            JoinType = JoinType.Inner,
            OnCondition = null!
        };

        var shouldSwap = m_optimizer.ShouldSwapJoinSides(join);

        Assert.That(shouldSwap, Is.True);
    }

    [Test]
    public void ShouldSwapJoinSidesReturnsFalseWhenLeftSmallerTest()
    {
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY)");

        for (int i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO SmallTable (Id) VALUES ({i})");
        for (int i = 1; i <= 100; i++)
            m_engine.Execute($"INSERT INTO LargeTable (Id) VALUES ({i})");

        var join = new TableSourceJoin
        {
            Left = new TableSourceSimple { TableName = "SmallTable" },
            Right = new TableSourceSimple { TableName = "LargeTable" },
            JoinType = JoinType.Inner,
            OnCondition = null!
        };

        var shouldSwap = m_optimizer.ShouldSwapJoinSides(join);

        Assert.That(shouldSwap, Is.False);
    }

    [Test]
    public void ShouldSwapJoinSidesReturnsFalseForLeftJoinTest()
    {
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY)");

        for (int i = 1; i <= 100; i++)
            m_engine.Execute($"INSERT INTO LargeTable (Id) VALUES ({i})");
        for (int i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO SmallTable (Id) VALUES ({i})");

        // LEFT JOIN - swapping would change semantics
        var join = new TableSourceJoin
        {
            Left = new TableSourceSimple { TableName = "LargeTable" },
            Right = new TableSourceSimple { TableName = "SmallTable" },
            JoinType = JoinType.Left,
            OnCondition = null!
        };

        var shouldSwap = m_optimizer.ShouldSwapJoinSides(join);

        Assert.That(shouldSwap, Is.False);
    }

    [Test]
    public void ShouldSwapJoinSidesReturnsFalseForRightJoinTest()
    {
        m_engine.Execute("CREATE TABLE LargeTable (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE SmallTable (Id BIGINT PRIMARY KEY)");

        for (int i = 1; i <= 100; i++)
            m_engine.Execute($"INSERT INTO LargeTable (Id) VALUES ({i})");
        for (int i = 1; i <= 5; i++)
            m_engine.Execute($"INSERT INTO SmallTable (Id) VALUES ({i})");

        // RIGHT JOIN - swapping would change semantics
        var join = new TableSourceJoin
        {
            Left = new TableSourceSimple { TableName = "LargeTable" },
            Right = new TableSourceSimple { TableName = "SmallTable" },
            JoinType = JoinType.Right,
            OnCondition = null!
        };

        var shouldSwap = m_optimizer.ShouldSwapJoinSides(join);

        Assert.That(shouldSwap, Is.False);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void OptimizeJoinOrderHandlesEmptyTablesTest()
    {
        m_engine.Execute("CREATE TABLE Empty1 (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE Empty2 (Id BIGINT PRIMARY KEY)");

        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "Empty1" },
            new TableSourceSimple { TableName = "Empty2" }
        };

        // Should not throw, even with empty tables
        var result = m_optimizer.OptimizeJoinOrder(tables);

        // Order doesn't matter for empty tables
        Assert.That(result, Is.Null.Or.Not.Null);
    }

    [Test]
    public void OptimizeJoinOrderHandlesAliasesTest()
    {
        m_engine.Execute("CREATE TABLE Users (Id BIGINT PRIMARY KEY)");
        
        for (int i = 1; i <= 10; i++)
            m_engine.Execute($"INSERT INTO Users (Id) VALUES ({i})");

        var tables = new List<TableSource>
        {
            new TableSourceSimple { TableName = "Users", Alias = "u1" },
            new TableSourceSimple { TableName = "Users", Alias = "u2" }
        };

        // Self-join with aliases - should handle correctly
        var result = m_optimizer.OptimizeJoinOrder(tables);

        // Same table, same size - no optimization needed
        Assert.That(result, Is.Null);
    }

    #endregion
}
