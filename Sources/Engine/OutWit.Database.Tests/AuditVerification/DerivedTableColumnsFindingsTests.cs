namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// <c>SELECT *</c> over a derived table returns every column twice.
/// </summary>
/// <remarks>
/// <para>
/// Found 2026-08-01 while measuring how much of <c>LATERAL</c> the engine already has, and
/// <b>measured against <c>v9.0.0</c> as pre-existing</b> - the tag behaves identically.
/// </para>
/// <para>
/// The derived table's columns arrive both qualified and bare:
/// </para>
/// <code>
/// SELECT * FROM (SELECT Id, TId FROM S) AS X    ->  X.Id, X.TId, Id, TId
/// SELECT * FROM (SELECT TId FROM S) AS X        ->  X.TId, TId
/// SELECT * FROM S                               ->  Id, TId, Score        (correct)
/// SELECT X.TId FROM (…) AS X                    ->  TId                   (correct)
/// </code>
/// <para>
/// So the fault is the star expansion over an aliased subquery, not the subquery and not the alias.
/// It matters more than it looks: EF Core generates derived tables constantly, and a consumer doing
/// <c>SELECT *</c> gets a row twice as wide as the one it asked for, with duplicate names - which an
/// ordinal reader silently misreads rather than fails on.
/// </para>
/// <para>
/// Recorded here rather than fixed in place because phase 9 builds a derived <b>column list</b>
/// (<c>AS V (Id)</c>) on this same code, and building a feature on top of a broken star expansion
/// would bake the duplication into the new path too.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class DerivedTableColumnsFindingsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE S (Id INT PRIMARY KEY, TId INT, Score INT)");
        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (1, 1, 100)");
        m_engine.Execute("INSERT INTO S (Id, TId, Score) VALUES (2, 1, 200)");
    }

    #endregion

    #region Tests

    [Test]
    [Ignore("CONFIRMED 2026-08-01 by execution, and pre-existing - v9.0.0 behaves identically. " +
            "SELECT * over an aliased subquery expands to every column TWICE, once qualified and " +
            "once bare: (SELECT Id, TId FROM S) AS X yields X.Id, X.TId, Id, TId. SELECT * over a " +
            "plain table is correct, and naming the columns explicitly is correct, so the fault is " +
            "the star expansion over a derived table. engine, Query/QueryPlanner + IteratorAlias")]
    public void StarOverADerivedTableExpandsEachColumnOnceTest()
    {
        var row = m_engine.Query("SELECT * FROM (SELECT Id, TId FROM S) AS X")[0];

        Assert.That(row.ColumnNames, Is.EqualTo(new[] { "Id", "TId" }),
            $"a derived table's star must expand to its own columns once; it gave " +
            $"[{string.Join(", ", row.ColumnNames)}]");
    }

    /// <summary>
    /// The controls: the same star over a plain table, and the same derived table with its columns
    /// named. Both are correct, which is what localises the fault.
    /// </summary>
    [Test]
    public void StarOverAPlainTableAndNamedColumnsAreCorrectTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(m_engine.Query("SELECT * FROM S")[0].ColumnNames,
                Is.EqualTo(new[] { "Id", "TId", "Score" }));

            Assert.That(m_engine.Query("SELECT X.Id, X.TId FROM (SELECT Id, TId FROM S) AS X")[0].ColumnNames,
                Is.EqualTo(new[] { "Id", "TId" }));
        });
    }

    #endregion
}
