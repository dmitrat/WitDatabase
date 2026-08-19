using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests;

/// <summary>
/// A join condition means the same thing whichever way round it is written.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by using Studio on 2026-08-19, and it had shipped in 14.0.0.</b>
/// <c>ON c.Id = o.CustomerId</c> answered three rows; <c>ON o.CustomerId = c.Id</c> - the same
/// condition, the same two tables - failed with <c>Column 'CustomerId' not found</c>. In a chain of
/// joins the trap is easier to fall into, because "the left input" is everything joined so far, so
/// <c>JOIN Items i ON i.OrderId = o.Id</c> is the WRONG way round however natural it reads.
/// </para>
/// <para>
/// The cause was in the planner rather than in the evaluator: the equi-join key pair was built as
/// <c>LeftKey = binary.Left, RightKey = binary.Right</c>, taking the written order of the equality
/// for the order of the join's inputs. It checked that the two column references named DIFFERENT
/// tables and never asked WHICH input each belonged to, so the hash join looked for the right
/// table's column in rows of the left one.
/// </para>
/// <para>
/// <b>Why 106 join cases said nothing:</b> every one of them writes the equality left-hand-side
/// first. That is what this fixture exists to stop - it asserts the two orders agree ROW FOR ROW,
/// over every shape the engine offers, rather than asserting that one case no longer throws.
/// </para>
/// </remarks>
[TestFixture]
public class JoinKeysBelongToTheirOwnSideTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    /// <summary>
    /// Enough rows that the planner reaches for a hash join - which is the path that was broken.
    /// <see cref="TheHashJoinIsTheOneBeingExercisedTest"/> is the control on that.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        var database = new WitDatabaseBuilder()
            .WithMemoryStorage()
            .WithBTree()
            .WithTransactions()
            .Build();

        m_engine = new WitSqlEngine(database, ownsStore: true);

        m_engine.Execute("CREATE TABLE Customers (Id INT PRIMARY KEY, Country VARCHAR(60) NOT NULL)");
        m_engine.Execute("CREATE TABLE Orders (Id INT PRIMARY KEY, CustomerId INT NOT NULL, Total INT NOT NULL)");
        m_engine.Execute("CREATE TABLE Items (Id INT PRIMARY KEY, OrderId INT NOT NULL, Quantity INT NOT NULL)");

        // A composite key needs a pair of columns on each side.
        m_engine.Execute("CREATE TABLE Legs (Region INT NOT NULL, Slot INT NOT NULL, Label VARCHAR(20) NOT NULL)");
        m_engine.Execute("CREATE TABLE Cargo (LegRegion INT NOT NULL, LegSlot INT NOT NULL, Weight INT NOT NULL)");

        for (var i = 1; i <= 60; i++)
            m_engine.Execute($"INSERT INTO Customers (Id, Country) VALUES ({i}, 'C{i % 7}')");

        for (var i = 1; i <= 200; i++)
            m_engine.Execute($"INSERT INTO Orders (Id, CustomerId, Total) VALUES ({i}, {(i % 60) + 1}, {i * 3})");

        for (var i = 1; i <= 200; i++)
            m_engine.Execute($"INSERT INTO Items (Id, OrderId, Quantity) VALUES ({i}, {(i % 200) + 1}, {i % 9})");

        // Big enough for a hash join here too - at forty rows each the planner chose a nested
        // loop, and the two composite cases passed without touching the path they are about.
        for (var i = 1; i <= 120; i++)
            m_engine.Execute($"INSERT INTO Legs (Region, Slot, Label) VALUES ({i % 5}, {i % 8}, 'L{i}')");

        for (var i = 1; i <= 200; i++)
            m_engine.Execute($"INSERT INTO Cargo (LegRegion, LegSlot, Weight) VALUES ({i % 5}, {i % 8}, {i})");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region The rule, over every shape

    [Test]
    public void AnInnerJoinReadsTheSameBothWaysTest()
    {
        SameRows(
            "SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId ORDER BY o.Total",
            "SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON o.CustomerId = c.Id ORDER BY o.Total");
    }

    [Test]
    public void ALeftJoinReadsTheSameBothWaysTest()
    {
        SameRows(
            "SELECT c.Country, o.Total FROM Customers c LEFT JOIN Orders o ON c.Id = o.CustomerId ORDER BY c.Id, o.Total",
            "SELECT c.Country, o.Total FROM Customers c LEFT JOIN Orders o ON o.CustomerId = c.Id ORDER BY c.Id, o.Total");
    }

    /// <summary>
    /// The shape that is hardest to write correctly by accident: the left input of the second join
    /// is not a table at all, it is everything joined before it.
    /// </summary>
    [Test]
    public void AChainOfJoinsReadsTheSameBothWaysTest()
    {
        SameRows(
            "SELECT c.Country, i.Quantity FROM Customers c JOIN Orders o ON c.Id = o.CustomerId "
            + "JOIN Items i ON o.Id = i.OrderId ORDER BY i.Id",
            "SELECT c.Country, i.Quantity FROM Customers c JOIN Orders o ON o.CustomerId = c.Id "
            + "JOIN Items i ON i.OrderId = o.Id ORDER BY i.Id");
    }

    [Test]
    public void AJoinWithoutAliasesReadsTheSameBothWaysTest()
    {
        SameRows(
            "SELECT Customers.Country, Orders.Total FROM Customers JOIN Orders "
            + "ON Customers.Id = Orders.CustomerId ORDER BY Orders.Total",
            "SELECT Customers.Country, Orders.Total FROM Customers JOIN Orders "
            + "ON Orders.CustomerId = Customers.Id ORDER BY Orders.Total");
    }

    /// <summary>
    /// Two keys, and the second one written the other way round - so a fix that swaps the whole
    /// condition rather than each pair is caught here.
    /// </summary>
    [Test]
    public void ACompositeKeyReadsTheSameWithOnePartTurnedRoundTest()
    {
        SameRows(
            "SELECT l.Label, g.Weight FROM Legs l JOIN Cargo g ON l.Region = g.LegRegion AND l.Slot = g.LegSlot "
            + "ORDER BY l.Label, g.Weight",
            "SELECT l.Label, g.Weight FROM Legs l JOIN Cargo g ON l.Region = g.LegRegion AND g.LegSlot = l.Slot "
            + "ORDER BY l.Label, g.Weight");
    }

    [Test]
    public void ACompositeKeyReadsTheSameWithBothPartsTurnedRoundTest()
    {
        SameRows(
            "SELECT l.Label, g.Weight FROM Legs l JOIN Cargo g ON l.Region = g.LegRegion AND l.Slot = g.LegSlot "
            + "ORDER BY l.Label, g.Weight",
            "SELECT l.Label, g.Weight FROM Legs l JOIN Cargo g ON g.LegRegion = l.Region AND g.LegSlot = l.Slot "
            + "ORDER BY l.Label, g.Weight");
    }

    /// <summary>
    /// A residual condition beside the key, because the two are separated by the same walk.
    /// </summary>
    [Test]
    public void AKeyBesideAResidualConditionReadsTheSameBothWaysTest()
    {
        SameRows(
            "SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId AND o.Total > 100 "
            + "ORDER BY o.Total",
            "SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON o.CustomerId = c.Id AND o.Total > 100 "
            + "ORDER BY o.Total");
    }

    /// <summary>
    /// The same condition in WHERE, which never went through the key extraction and was the
    /// workaround while this was open.
    /// </summary>
    [Test]
    public void TheConditionInWhereReadsTheSameAsTheJoinTest()
    {
        SameRows(
            "SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId ORDER BY o.Total",
            "SELECT c.Country, o.Total FROM Customers c, Orders o WHERE o.CustomerId = c.Id ORDER BY o.Total");
    }

    #endregion

    #region The controls

    /// <summary>
    /// CONTROL: the path this fixture is about is the one being taken. A nested loop join evaluates
    /// the ON condition whole and has never cared which way round it is written - so if the planner
    /// stopped choosing a hash join here, every case above would pass for a reason that has nothing
    /// to do with the defect.
    /// </summary>
    [Test]
    public void TheHashJoinIsTheOneBeingExercisedTest()
    {
        var plan = Details("EXPLAIN SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId");

        var composite = Details("EXPLAIN SELECT l.Label FROM Legs l JOIN Cargo g "
            + "ON l.Region = g.LegRegion AND l.Slot = g.LegSlot");

        Assert.Multiple(() =>
        {
            Assert.That(plan.Any(line => line.Contains("HASH", StringComparison.OrdinalIgnoreCase)), Is.True,
                "CONTROL: these tables are meant to be big enough for a hash join - " + string.Join(" | ", plan));

            Assert.That(composite.Any(line => line.Contains("HASH", StringComparison.OrdinalIgnoreCase)), Is.True,
                "CONTROL: the composite key must reach the hash join too - " + string.Join(" | ", composite));
        });
    }

    /// <summary>
    /// CONTROL: the queries return something. Two empty results agree with each other.
    /// </summary>
    [Test]
    public void TheJoinsUsedHereActuallyMatchRowsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Rows("SELECT o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId"),
                Has.Count.EqualTo(200), "every order has a customer");

            Assert.That(Rows("SELECT l.Label FROM Legs l JOIN Cargo g ON l.Region = g.LegRegion AND l.Slot = g.LegSlot"),
                Is.Not.Empty, "the composite key matches something");
        });
    }

    #endregion

    #region Tools

    private void SameRows(string oneWay, string theOther)
    {
        var expected = Rows(oneWay);
        var actual = Rows(theOther);

        Assert.That(actual, Is.EqualTo(expected),
            "the same condition, written the other way round:" + Environment.NewLine
            + oneWay + Environment.NewLine + theOther);
    }

    private List<string> Rows(string sql)
    {
        using var result = m_engine.Execute(sql);

        return result.ReadAll()
            .Select(row => string.Join("|", row.Values.Select(value => value.ToString())))
            .ToList();
    }

    private List<string> Details(string sql)
    {
        using var result = m_engine.Execute(sql);

        return result.ReadAll().Select(row => row["detail"].AsString()).ToList();
    }

    #endregion
}
