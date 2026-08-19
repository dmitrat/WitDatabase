using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests;

/// <summary>
/// Every line of an <c>EXPLAIN</c> names the line it is really under.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by looking at Studio's plan panel on 2026-08-19.</b> The panel draws the plan as a tree
/// from the <c>id</c> and <c>parent</c> columns, and it drew <c>SCAN TABLE Orders</c> under
/// <c>ALIAS c</c> - the alias of the OTHER table. The panel was faithful; the plan was wrong.
/// </para>
/// <para>
/// The cause: the lines of each child's subtree were re-based by a constant instead of by where
/// that subtree actually starts, so everything below the SECOND child of any node was attributed to
/// the first child's subtree. A join has two children, and so does every set operation.
/// </para>
/// <para>
/// This fixture asserts the SHAPE rather than one row: one root, every parent a real earlier line,
/// and - the part that catches an off-by-anything - each <c>ALIAS</c> standing directly over the
/// scan of the table it aliases.
/// </para>
/// </remarks>
[TestFixture]
public class APlanNodeNamesItsRealParentTests
{
    #region Fields

    private WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

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
        m_engine.Execute("CREATE TABLE Items (Id INT PRIMARY KEY, OrderId INT NOT NULL)");

        for (var i = 1; i <= 60; i++)
            m_engine.Execute($"INSERT INTO Customers (Id, Country) VALUES ({i}, 'C{i % 7}')");

        for (var i = 1; i <= 200; i++)
            m_engine.Execute($"INSERT INTO Orders (Id, CustomerId, Total) VALUES ({i}, {(i % 60) + 1}, {i})");

        for (var i = 1; i <= 200; i++)
            m_engine.Execute($"INSERT INTO Items (Id, OrderId) VALUES ({i}, {(i % 200) + 1})");
    }

    [TearDown]
    public void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region The rule

    [Test]
    public void EachAliasStandsOverTheTableItAliasesTest()
    {
        var plan = Plan("EXPLAIN SELECT c.Country, o.Total FROM Customers c JOIN Orders o "
            + "ON c.Id = o.CustomerId LIMIT 3");

        Assert.Multiple(() =>
        {
            Assert.That(ChildrenOf(plan, "ALIAS c"), Is.EqualTo(new[] { "SCAN TABLE Customers" }));
            Assert.That(ChildrenOf(plan, "ALIAS o"), Is.EqualTo(new[] { "SCAN TABLE Orders" }));

            // CONTROL: a plan that was not read at all has no aliases in it either.
            Assert.That(plan.Select(line => line.Detail), Has.Some.Contains("ALIAS c"),
                "CONTROL: the plan was not read - " + Written(plan));
        });
    }

    /// <summary>
    /// Three tables, which is two joins - so the second join's right input sits under a node that
    /// already has a subtree of its own.
    /// </summary>
    [Test]
    public void AChainOfJoinsNamesEveryParentTest()
    {
        var plan = Plan("EXPLAIN SELECT c.Country FROM Customers c JOIN Orders o ON c.Id = o.CustomerId "
            + "JOIN Items i ON o.Id = i.OrderId");

        Assert.Multiple(() =>
        {
            Assert.That(ChildrenOf(plan, "ALIAS c"), Is.EqualTo(new[] { "SCAN TABLE Customers" }));
            Assert.That(ChildrenOf(plan, "ALIAS o"), Is.EqualTo(new[] { "SCAN TABLE Orders" }));
            Assert.That(ChildrenOf(plan, "ALIAS i"), Is.EqualTo(new[] { "SCAN TABLE Items" }));
        });
    }

    /// <summary>
    /// The other two-child shape in the engine, and it was wrong for the same reason.
    /// </summary>
    [Test]
    public void BothArmsOfAUnionKeepTheirOwnChildrenTest()
    {
        var plan = Plan("EXPLAIN SELECT Id FROM Customers UNION SELECT Id FROM Orders");

        var scans = plan.Where(line => line.Detail.Contains("SCAN TABLE")).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(scans, Has.Count.EqualTo(2), "CONTROL: two arms, two scans - " + Written(plan));

            foreach (var scan in scans)
            {
                Assert.That(ParentOf(plan, scan), Is.Not.Null,
                    $"«{scan.Detail}» is under a line that is not in the plan");
            }

            Assert.That(scans.Select(scan => ParentOf(plan, scan)!.Id).Distinct().Count(), Is.EqualTo(2),
                "the two arms are two branches, not one - " + Written(plan));
        });
    }

    [Test]
    public void EveryLineIsUnderALineThatComesBeforeItTest()
    {
        var offenders = new List<string>();
        var examined = 0;

        foreach (var sql in new[]
                 {
                     "EXPLAIN SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId LIMIT 3",
                     "EXPLAIN SELECT c.Country FROM Customers c JOIN Orders o ON c.Id = o.CustomerId "
                     + "JOIN Items i ON o.Id = i.OrderId",
                     "EXPLAIN SELECT Id FROM Customers UNION SELECT Id FROM Orders",
                     "EXPLAIN SELECT Country, COUNT(*) FROM Customers GROUP BY Country ORDER BY Country"
                 })
        {
            var plan = Plan(sql);

            examined += plan.Count;

            var roots = plan.Count(line => line.Parent < 0);

            if (roots != 1)
                offenders.Add($"{roots} roots in: {Written(plan)}");

            foreach (var line in plan.Where(line => line.Parent >= 0))
            {
                if (line.Parent >= line.Id)
                    offenders.Add($"line {line.Id} is under {line.Parent}, which comes after it: {Written(plan)}");

                else if (plan.All(other => other.Id != line.Parent))
                    offenders.Add($"line {line.Id} is under {line.Parent}, which is not in the plan: {Written(plan)}");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: four plans that were never read would report nothing wrong.
            Assert.That(examined, Is.GreaterThan(12), "CONTROL: too few plan lines were read");

            Assert.That(offenders, Is.Empty, string.Join(Environment.NewLine, offenders));
        });
    }

    #endregion

    #region Tools

    private sealed record PlanLine(int Id, int Parent, string Detail);

    private List<PlanLine> Plan(string sql)
    {
        using var result = m_engine.Execute(sql);

        return result.ReadAll()
            .Select(row => new PlanLine(
                (int)row["id"].AsInt64(),
                (int)row["parent"].AsInt64(),
                row["detail"].AsString().Trim()))
            .ToList();
    }

    private static IReadOnlyList<string> ChildrenOf(List<PlanLine> plan, string detail)
    {
        var parent = plan.FirstOrDefault(line => line.Detail.StartsWith(detail, StringComparison.Ordinal));

        Assert.That(parent, Is.Not.Null, $"«{detail}» is not in the plan - {Written(plan)}");

        // The head of the line only: a plain EXPLAIN also writes the schema after an arrow, and
        // this fixture is about who is under whom.
        return plan.Where(line => line.Parent == parent!.Id)
                   .Select(line => line.Detail.Split(" -> ")[0].Trim())
                   .ToList();
    }

    private static PlanLine? ParentOf(List<PlanLine> plan, PlanLine line)
    {
        return plan.FirstOrDefault(candidate => candidate.Id == line.Parent);
    }

    private static string Written(List<PlanLine> plan)
    {
        return Environment.NewLine
            + string.Join(Environment.NewLine, plan.Select(line => $"{line.Id} <- {line.Parent} | {line.Detail}"));
    }

    #endregion
}
