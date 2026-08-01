namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// What this engine does not do, measured rather than remembered.
/// </summary>
/// <remarks>
/// <para>
/// Phase 9 is a decision pass: for each capability the engine lacks, is it worth building? That
/// question can only be asked of a list that is <b>true</b>, and the list in the plan was assembled
/// during phase 3 and had gone stale in three places by the time it was read - which is the tenth
/// time in this project that a record about the past turned out false when re-run.
/// </para>
/// <para>
/// So the status of every item is pinned here and checked on every build. An item that starts working
/// fails this test, which is the point: the list stops drifting the moment it is measured instead of
/// recalled. A pin says nothing about whether the capability <i>should</i> exist - that is the
/// decision, and it is recorded in <c>Docs/PHASE9-UNBUILT-CAPABILITY-PLAN.md</c>.
/// </para>
/// <para>
/// The instrument this phase still owes is the other half: the same corpus run against PostgreSQL and
/// SQL Server, so the list is measured against the drop-in target rather than against SQLite, which
/// lacks most of it too.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class UnbuiltCapabilityCorpusTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(50), Age INT)");
        m_engine.Execute("CREATE TABLE S (Id INT PRIMARY KEY, TId INT, Score INT)");

        m_engine.Execute("INSERT INTO T (Id, Name, Age) VALUES (1, 'a', 10)");
        m_engine.Execute("INSERT INTO T (Id, Name, Age) VALUES (2, 'b', 20)");
        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (1, 1, 100)");
        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (2, 1, 200)");
    }

    #endregion

    #region Absent - the grammar does not admit these at all

    /// <summary>
    /// Each of these is refused by the parser. Refusal is the honest failure for something unbuilt,
    /// and it is what this test pins: none of them may start silently half-working.
    /// </summary>
    [TestCase("SELECT T.Id, X.Score FROM T, LATERAL (SELECT Score FROM S WHERE S.TId = T.Id) AS X",
        TestName = "lateral join")]
    [TestCase("SELECT T.Id, X.Score FROM T CROSS APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X",
        TestName = "CROSS APPLY")]
    [TestCase("SELECT T.Id, X.Score FROM T OUTER APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X",
        TestName = "OUTER APPLY")]
    [TestCase("CREATE FUNCTION Double(N INT) RETURNS INT AS BEGIN RETURN N * 2; END",
        TestName = "user-defined function")]
    [TestCase("CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T; END", TestName = "stored procedure")]
    public void CapabilityIsAbsentAndSaysSoTest(string sql)
    {
        Assert.That(() => m_engine.Execute(sql),
            Throws.InstanceOf<Parser.Exceptions.WitSqlParsingException>(),
            "an unbuilt capability must be refused by the parser. If this now throws something else, " +
            "or succeeds, the capability's status has changed and the phase-9 decision for it needs " +
            "revisiting");
    }

    #endregion

    #region Present - recorded in the plan as unbuilt, and not

    /// <summary>
    /// JSON columns were on the plan's unbuilt list. They work, end to end.
    /// </summary>
    /// <remarks>
    /// Measured 2026-08-01: the column type is accepted and reported as <c>JSON</c> by
    /// <c>INFORMATION_SCHEMA</c>, a document survives being stored and read back, and
    /// <c>JSON_EXTRACT</c> reads it - including a nested array index - both in the select list and in
    /// a <c>WHERE</c> clause. Nothing about it is unbuilt.
    /// </remarks>
    [Test]
    public void JsonColumnsWorkEndToEndTest()
    {
        m_engine.Execute("CREATE TABLE J (Id INT PRIMARY KEY, Doc JSON)");
        m_engine.Execute("INSERT INTO J (Id, Doc) VALUES (1, '{\"a\":1,\"b\":[2,3]}')");

        Assert.Multiple(() =>
        {
            Assert.That(Scalar("SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
                               "WHERE TABLE_NAME = 'J' AND COLUMN_NAME = 'Doc'"),
                Is.EqualTo("JSON"), "the declared type reaches the catalog");

            Assert.That(Scalar("SELECT Doc FROM J WHERE Id = 1"),
                Does.Contain("\"b\""), "the document survives storage");

            Assert.That(Long("SELECT JSON_EXTRACT(Doc, '$.a') FROM J WHERE Id = 1"), Is.EqualTo(1));
            Assert.That(Long("SELECT JSON_EXTRACT(Doc, '$.b[1]') FROM J WHERE Id = 1"), Is.EqualTo(3));
            Assert.That(Long("SELECT Id FROM J WHERE JSON_EXTRACT(Doc, '$.a') = 1"), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// Built by phase 9b, 2026-08-01: all three were on the absent list and all three are supported
    /// by both drop-in targets.
    /// </summary>
    /// <remarks>
    /// They are pinned here rather than only in their own fixture because this corpus is the list
    /// the phase reads. An item that moves has to move here too, or the list starts drifting again -
    /// which is the whole reason this file exists.
    /// </remarks>
    [TestCase("SELECT TOP 1 Id FROM T", TestName = "TOP n")]
    [TestCase("SELECT * FROM (VALUES (1), (2)) AS V", TestName = "VALUES as a table source")]
    [TestCase("SELECT * FROM (SELECT Id FROM T) AS V (Alias)", TestName = "derived column list")]
    public void CapabilityBuiltByPhase9bWorksTest(string sql)
    {
        Assert.That(() => m_engine.Query(sql), Throws.Nothing);
    }

    /// <summary>
    /// Database-first scaffolding was on the plan's unbuilt list too. Phase 7 rewrote the model
    /// factory onto <c>INFORMATION_SCHEMA</c>; the catalog it reads answers.
    /// </summary>
    [Test]
    public void InformationSchemaAnswersForScaffoldingTest()
    {
        var tables = m_engine.Query("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES")
            .Select(row => row[0].AsString())
            .ToArray();

        Assert.That(tables, Does.Contain("T"));
    }

    #endregion

    #region Not a capability gap - a defect, and already recorded

    /// <summary>
    /// The plan filed <c>HAVING COUNT(*) BETWEEN 1 AND 5</c> as unbuilt capability. It is a defect,
    /// and it was **already recorded** on 2026-07-28 in <c>HavingAggregateFindingsTests</c> - with a
    /// better diagnosis than a re-measure produced: that fixture also covers <c>IN</c>, which is what
    /// shows the fault is not <c>BETWEEN</c>'s, and it carries the control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is pinned here, deliberately: a second marker for one finding splits it across two
    /// files and inflates the ledger. This region exists so the next person reading the phase-9 list
    /// is sent to the record rather than re-discovering it.
    /// </para>
    /// <para>
    /// One detail the re-measure did add: <c>SUM</c> and <c>MIN</c> inside <c>BETWEEN</c> fail with
    /// <c>KeyNotFoundException: Column not found</c> rather than the <c>InvalidOperationException</c>
    /// <c>COUNT(*)</c> raises. Same cause, two different internal invariants reaching the caller.
    /// </para>
    /// </remarks>
    [Test]
    public void AggregateInHavingWorksOutsideBetweenTest()
    {
        var groups = m_engine.Query("SELECT TId FROM S GROUP BY TId HAVING COUNT(*) > 1")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(groups, Is.EqualTo(new long[] { 1 }),
            "the control for HavingAggregateFindingsTests: an aggregate beside a plain comparison " +
            "resolves, which is why the failure inside BETWEEN and IN is about the operands");
    }

    #endregion

    #region Helpers

    private string? Scalar(string sql) =>
        m_engine.Query(sql).Select(row => row[0].IsNull ? null : row[0].AsString()).FirstOrDefault();

    private long Long(string sql) => m_engine.Query(sql)[0][0].AsInt64();

    #endregion
}
