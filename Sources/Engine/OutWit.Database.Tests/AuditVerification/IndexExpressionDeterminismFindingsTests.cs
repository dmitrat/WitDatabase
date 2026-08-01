namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// An index key is computed once and read forever, so what computes it must be deterministic.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-01 at head <c>c23b983</c>, while auditing the area for phase 9d:
/// <c>CREATE INDEX IX ON T ((V + (SELECT N FROM Lookup WHERE Id = 1)))</c> was accepted, and so were
/// rows inserted against it. The key is written from the expression's value at insert time; nothing
/// recomputes it when the row the subquery reads changes. The index and a scan then answer
/// differently for the same query, and which one a caller gets depends on the plan.
/// </para>
/// <para>
/// <b>What was measured is the acceptance, not a wrong answer</b> - the probe changed the looked-up
/// row and the query still came back from a scan. That distinction is kept deliberately: the same
/// gap between "available" and "demonstrated" is what <c>IsFiltered</c> had until the optimiser
/// started believing it, and then it was a silent wrong result. Refusing the declaration closes it
/// before that happens rather than after.
/// </para>
/// <para>
/// The same argument covers a nondeterministic function - <c>RANDOM()</c>, <c>NOW()</c> - which needs
/// no second table to disagree with itself. And it is the rule phase 9d needs in place before a
/// user-defined function can be allowed into an index expression at all: see
/// <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 5.
/// </para>
/// <para>
/// Refused at declaration, which is phase 7's rule: accepted, enforced, or refused - never accepted
/// and quietly wrong.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class IndexExpressionDeterminismFindingsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE Lookup (Id INT PRIMARY KEY, N INT)");
        m_engine.Execute("INSERT INTO Lookup (Id, N) VALUES (1, 10)");
        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT, W VARCHAR(50))");
    }

    #endregion

    #region Refused

    /// <summary>
    /// An index expression that reads another table cannot be kept up to date, so it is refused.
    /// </summary>
    [Test]
    public void IndexExpressionWithASubqueryIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute(
                "CREATE INDEX IX ON T ((V + (SELECT N FROM Lookup WHERE Id = 1)))"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("IX"),
            "an index key computed from another table goes stale the moment that table changes");
    }

    /// <summary>
    /// Every shape a subquery can take in an expression, not only the scalar one.
    /// </summary>
    /// <remarks>
    /// The lesson from the aggregate defect: a check written against the one shape somebody happened
    /// to type covers four of nineteen expression types and silently returns "fine" for the rest.
    /// <c>EXISTS</c>, <c>IN (SELECT …)</c> and a quantified comparison are all subqueries too.
    /// </remarks>
    [TestCase("(V + (SELECT N FROM Lookup WHERE Id = 1))", TestName = "a scalar subquery")]
    [TestCase("(CASE WHEN EXISTS (SELECT 1 FROM Lookup) THEN 1 ELSE 0 END)", TestName = "EXISTS")]
    [TestCase("(CASE WHEN V IN (SELECT N FROM Lookup) THEN 1 ELSE 0 END)", TestName = "IN a subquery")]
    public void EverySubqueryShapeIsRefusedInAnIndexExpressionTest(string expression)
    {
        Assert.That(() => m_engine.Execute($"CREATE INDEX IX ON T ({expression})"),
            Throws.InstanceOf<NotSupportedException>());
    }

    /// <summary>
    /// And a function whose answer changes on its own.
    /// </summary>
    /// <remarks>
    /// <c>RANDOM()</c> needs no second table to make the stored key disagree with the expression, so
    /// this is the same defect without the indirection. <c>NOW()</c> is the one a consumer is
    /// actually likely to write, in an index meant to help a "recent rows" query.
    /// </remarks>
    [TestCase("(RANDOM())", TestName = "RANDOM")]
    [TestCase("(V + RANDOM())", TestName = "RANDOM inside a larger expression")]
    [TestCase("(NOW())", TestName = "NOW")]
    [TestCase("(NEWGUID())", TestName = "NEWGUID")]
    public void NondeterministicFunctionIsRefusedInAnIndexExpressionTest(string expression)
    {
        Assert.That(() => m_engine.Execute($"CREATE INDEX IX ON T ({expression})"),
            Throws.InstanceOf<NotSupportedException>());
    }

    #endregion

    #region Still allowed

    /// <summary>
    /// The refusal must be narrow. An expression index over the row's own values is the point of the
    /// feature and must keep working.
    /// </summary>
    [TestCase("(V * 2)", TestName = "arithmetic")]
    [TestCase("(UPPER(W))", TestName = "a deterministic function")]
    [TestCase("(LENGTH(W) + V)", TestName = "several of them")]
    [TestCase("(CASE WHEN V > 10 THEN 1 ELSE 0 END)", TestName = "a CASE over the row")]
    public void AnExpressionOverTheRowIsStillAllowedTest(string expression)
    {
        Assert.That(() => m_engine.Execute($"CREATE INDEX IX ON T ({expression})"), Throws.Nothing);

        m_engine.Execute("INSERT INTO T (Id, V, W) VALUES (1, 20, 'abc')");

        Assert.That(m_engine.Query("SELECT COUNT(*) FROM T")[0][0].AsInt64(), Is.EqualTo(1));
    }

    /// <summary>
    /// A plain column index is not an expression index and must not be touched by any of this.
    /// </summary>
    [Test]
    public void APlainColumnIndexIsUnaffectedTest()
    {
        Assert.That(() => m_engine.Execute("CREATE INDEX IX ON T (V)"), Throws.Nothing);
        Assert.That(() => m_engine.Execute("CREATE INDEX IX2 ON T (V, W)"), Throws.Nothing);
    }

    /// <summary>
    /// And a partial index's filter is a different clause with a different job.
    /// </summary>
    /// <remarks>
    /// A <c>WHERE</c> decides which rows are in the index, not what key they get. It has the same
    /// staleness question and it is <b>not</b> answered here - narrowing the change to the key
    /// expression is deliberate, and the filter is left as it was rather than swept up untested.
    /// </remarks>
    [Test]
    public void APartialIndexFilterIsNotTouchedTest()
    {
        Assert.That(() => m_engine.Execute("CREATE INDEX IX ON T (V) WHERE V > 5"), Throws.Nothing);
    }

    #endregion
}
