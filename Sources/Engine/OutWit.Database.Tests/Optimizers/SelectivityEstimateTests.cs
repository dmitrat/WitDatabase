using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Engine;
using OutWit.Database.Optimizers;
using OutWit.Database.Parser;
using OutWit.Database.Parser.Expressions;
using OutWit.Database.Parser.Schema.Types;

namespace OutWit.Database.Tests.Query;

/// <summary>
/// What the query optimizer thinks a predicate will return, against what it does return.
/// </summary>
/// <remarks>
/// <para>
/// The optimizer chooses between an index and a table scan by estimating rows, and its estimate for
/// <b>every</b> range predicate is a constant: 20% of the table, whatever the bound and whatever the
/// data. <c>WHERE Id &gt; 999999</c> and <c>WHERE Id &gt; 0</c> on the same table produce the same
/// number. That was recorded as a handover item from phase 10 and never measured, which is the gap this
/// fixture closes: an estimate is only wrong in a way worth fixing if the size of the error is known.
/// </para>
/// <para>
/// <b>The instrument compares the estimate with the answer.</b> Each case builds a real database with a
/// known distribution, asks the optimizer what it expects, runs the same predicate through the engine
/// and counts what comes back. Nothing here asserts that the constant is wrong - that would be
/// asserting the source - it asserts the <b>consequence</b>, and prints the ratio so the error has a
/// size.
/// </para>
/// <para>
/// <b>Controls, both directions.</b> A unique-index equality must estimate exactly what it returns, or
/// the harness is comparing the wrong estimate with the wrong query; and a range that genuinely matches
/// about a fifth of the table must come out near 1.0, or the harness reports error everywhere and its
/// verdicts mean nothing.
/// </para>
/// <para>
/// <b>Measured, 1,000 rows, values 1..1000:</b> the estimate is 200 for all seven predicates, so the
/// error runs from <b>200x too high</b> (<c>Value &gt; 999</c>, one row) to <b>5x too low</b>
/// (<c>Value &gt; 0</c>, the whole table), and the case the constant was written for is exactly 1.00.
/// </para>
/// <para>
/// <b>Why this is pinned rather than fixed.</b> The obvious repair is to interpolate between the
/// smallest and largest key in the index, and that data is not cheaply available: <c>ISecondaryIndex</c>
/// offers <c>GetFirstEntry</c> and <c>GetLastEntry</c>, and the B+Tree implementation of the second one
/// is <c>Scan(null, null).LastOrDefault()</c> - a <b>full scan</b>. Using it per query would reinstate
/// exactly the defect 11.1.0 removed, when the planner scanned 1,000 rows per execution and a unique
/// index seek was 97x slower for it. So the fix needs a first/last key that descends the tree, which is
/// a storage-layer capability rather than an optimizer change.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SelectivityEstimateTests
{
    #region Types

    /// <param name="Predicate">The WHERE clause, as SQL and as the optimizer's input.</param>
    /// <param name="TrueRows">How many of the 1,000 rows actually satisfy it.</param>
    public sealed record Case(string Label, string Predicate, BinaryOperatorType Operator, long Bound, int TrueRows)
    {
        public override string ToString() => Label;
    }

    #endregion

    #region Constants

    private const int ROWS = 1000;

    #endregion

    #region Fields

    private string m_root = null!;
    private OptimizerQuery m_optimizer = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void SetUp()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witdb_selectivity_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
        m_optimizer = new OptimizerQuery();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(m_root))
                Directory.Delete(m_root, recursive: true);
        }
        catch
        {
            // Cleanup must not fail the run.
        }
    }

    #endregion

    #region The cases

    /// <summary>
    /// Values are 1..1000, so the true count of every predicate below is arithmetic rather than a
    /// measurement of the engine - which is what makes a disagreement attributable.
    /// </summary>
    private static IEnumerable<Case> Cases()
    {
        yield return new Case("> 999 (0.1% of the table)", "Value > 999", BinaryOperatorType.GreaterThan, 999, 1);
        yield return new Case("> 990 (1%)", "Value > 990", BinaryOperatorType.GreaterThan, 990, 10);
        yield return new Case("> 800 (20% - the constant)", "Value > 800", BinaryOperatorType.GreaterThan, 800, 200);
        yield return new Case("> 500 (50%)", "Value > 500", BinaryOperatorType.GreaterThan, 500, 500);
        yield return new Case("> 0 (the whole table)", "Value > 0", BinaryOperatorType.GreaterThan, 0, 1000);
        yield return new Case("< 10 (1%)", "Value < 10", BinaryOperatorType.LessThan, 10, 9);
        yield return new Case("< 900 (90%)", "Value < 900", BinaryOperatorType.LessThan, 900, 899);
    }

    #endregion

    #region Controls

    /// <summary>
    /// Control: an equality on a unique index is estimated exactly. If this fails, the harness is not
    /// asking the optimizer the same question it asks the engine.
    /// </summary>
    [Test]
    public void ControlAUniqueEqualityIsEstimatedExactlyTest()
    {
        var indexes = new List<DefinitionIndex> { Index("IX_Value", unique: true) };
        var where = Where(BinaryOperatorType.Equal, 500);

        var strategy = m_optimizer.FindBestIndex("Numbers", where, indexes, ROWS);

        Assert.That(strategy, Is.Not.Null, "no index strategy for an indexed equality");
        Assert.That(strategy!.EstimatedRowsReturned, Is.EqualTo(1));
        Assert.That(ActualRows("Value = 500"), Is.EqualTo(1), "the data does not match what the case claims");
    }

    #endregion

    #region The probe

    [Test]
    [TestCaseSource(nameof(Cases))]
    public void TheEstimateForARangeIsComparedWithTheAnswerTest(Case testCase)
    {
        var indexes = new List<DefinitionIndex> { Index("IX_Value", unique: false) };
        var where = Where(testCase.Operator, testCase.Bound);

        var strategy = m_optimizer.FindBestIndex("Numbers", where, indexes, ROWS);

        Assert.That(strategy, Is.Not.Null, $"{testCase.Label}: the optimizer found no index strategy");

        var actual = ActualRows(testCase.Predicate);

        Assert.That(actual, Is.EqualTo(testCase.TrueRows),
            $"{testCase.Label}: the engine returned {actual} rows where the case claims {testCase.TrueRows} - " +
            "the case is wrong, not the estimate");

        var estimated = strategy!.EstimatedRowsReturned;
        var ratio = (double)estimated / Math.Max(1, actual);

        TestContext.Out.WriteLine(
            $"SELECTIVITY {testCase.Label,-30} estimated={estimated,5}  actual={actual,5}  ratio={ratio,7:0.00}");

        // PINS A DEFECT, NOT CORRECT BEHAVIOUR - for every case but the one the constant happens to fit.
        // The estimate is the same number for all seven, so the assertion is on the number rather than
        // on its accuracy; when the optimizer learns to look at the data, this goes red and the ratios
        // above become the record of what it used to be.
        Assert.That(estimated, Is.EqualTo(200),
            $"{testCase.Label}: the range estimate is no longer a flat 20% of the table - re-measure, " +
            "and replace these pins with assertions about the error");
    }

    #endregion

    #region Tools

    /// <summary>Runs the predicate through a real database of 1,000 rows and counts what comes back.</summary>
    private int ActualRows(string predicate)
    {
        var path = Path.Combine(m_root, $"selectivity_{Guid.NewGuid():N}.witdb");

        using var engine = new WitSqlEngine(new WitDatabaseBuilder().WithFilePath(path).Build(), ownsStore: true);

        engine.Execute("CREATE TABLE Numbers (Id BIGINT PRIMARY KEY, Value BIGINT)");
        engine.Execute("CREATE INDEX IX_Value ON Numbers (Value)");

        for (var i = 1; i <= ROWS; i++)
            engine.Execute($"INSERT INTO Numbers (Id, Value) VALUES ({i}, {i})");

        // Scanned rather than counted: COUNT(*) is a cached counter on this engine and has disagreed
        // with the rows before.
        return engine.Query($"SELECT Id FROM Numbers WHERE {predicate}").Count;
    }

    private static WitSqlExpressionBinary Where(BinaryOperatorType op, long bound)
    {
        return new WitSqlExpressionBinary
        {
            Left = new WitSqlExpressionColumnRef { ColumnName = "Value" },
            Operator = op,
            Right = new WitSqlExpressionLiteral { Type = LiteralType.Integer, Value = bound }
        };
    }

    private static DefinitionIndex Index(string name, bool unique)
    {
        return new DefinitionIndex
        {
            Name = name,
            TableName = "Numbers",
            Columns = ["Value"],
            IsUnique = unique,
            IsPrimaryKey = false
        };
    }

    #endregion
}
