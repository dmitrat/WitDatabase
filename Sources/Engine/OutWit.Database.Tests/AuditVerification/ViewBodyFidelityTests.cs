using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A view must answer from the body it was created with.
/// </summary>
/// <remarks>
/// <para>
/// Until 9.0.0 a view body was persisted as text produced by the expression serializer and re-parsed
/// on every query. Anything that serializer could not write was lost <b>at creation time</b>, and
/// the loss came in two grades, of which the second is worse:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Created then broken.</b> A subquery was written as the literal text <c>SELECT ...</c>, so
/// <c>CREATE VIEW</c> succeeded and every query against the view then raised a parse error.
/// </description></item>
/// <item><description>
/// <b>Created then silently wrong.</b> A <c>UNION</c>'s second branch, a <c>WITH</c> clause and an
/// <c>OFFSET</c> without <c>LIMIT</c> were dropped outright, so the view was created, queried
/// without any error at all, and returned the wrong rows for ever. Measured 2026-07-31: a view over
/// <c>SELECT Id FROM A UNION SELECT Id FROM B</c> was stored as <c>SELECT Id FROM A</c> and answered
/// with half its rows.
/// </description></item>
/// </list>
/// <para>
/// The old round-trip harness could not see the second grade: it compared two <i>serializations</i>,
/// and a dropped clause is idempotent, so both passes agreed and the entry was counted clean. These
/// are written as queries against real data rather than as assertions about stored text, because
/// what the stored text says is exactly what was not in dispute.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class ViewBodyFidelityTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE A (Id INT PRIMARY KEY)");
        m_engine.Execute("CREATE TABLE B (Id INT PRIMARY KEY)");

        foreach (var id in new[] { 1, 2 })
            m_engine.Execute($"INSERT INTO A (Id) VALUES ({id})");

        foreach (var id in new[] { 3, 4 })
            m_engine.Execute($"INSERT INTO B (Id) VALUES ({id})");
    }

    #endregion

    #region Silently wrong

    [Test]
    public void ViewKeepsBothBranchesOfAUnionTest()
    {
        m_engine.Execute("CREATE VIEW V AS SELECT Id FROM A UNION SELECT Id FROM B");

        Assert.That(Ids("SELECT Id FROM V"), Is.EqualTo(new long[] { 1, 2, 3, 4 }),
            "the second branch of the UNION was dropped when the body was stored as text");
    }

    [Test]
    public void ViewKeepsBothBranchesOfAUnionAllTest()
    {
        m_engine.Execute("CREATE VIEW V AS SELECT Id FROM A UNION ALL SELECT Id FROM B");

        Assert.That(Ids("SELECT Id FROM V"), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void ViewKeepsItsCommonTableExpressionTest()
    {
        m_engine.Execute("CREATE VIEW V AS WITH C AS (SELECT Id FROM A) SELECT Id FROM C");

        // The WITH clause used to be dropped, leaving a body that referenced an undefined table, so
        // the view raised "Table 'C' not found" on every query.
        Assert.That(Ids("SELECT Id FROM V"), Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public void ViewKeepsAnOffsetWithoutALimitTest()
    {
        m_engine.Execute("CREATE VIEW V AS SELECT Id FROM A ORDER BY Id OFFSET 1");

        Assert.That(Ids("SELECT Id FROM V"), Is.EqualTo(new long[] { 2 }),
            "OFFSET with no LIMIT was dropped, so the view returned the rows it was told to skip");
    }

    #endregion

    #region Created then broken

    [Test]
    public void ViewKeepsAScalarSubqueryInItsWhereClauseTest()
    {
        m_engine.Execute("CREATE VIEW V AS SELECT Id FROM A WHERE Id > (SELECT 1)");

        Assert.That(Ids("SELECT Id FROM V"), Is.EqualTo(new long[] { 2 }));
    }

    [Test]
    public void ViewKeepsAnInSubqueryTest()
    {
        m_engine.Execute("CREATE VIEW V AS SELECT Id FROM A WHERE Id IN (SELECT Id FROM B)");

        Assert.That(Ids("SELECT Id FROM V"), Is.Empty);
    }

    [Test]
    public void ViewKeepsAnExistsSubqueryTest()
    {
        m_engine.Execute("CREATE VIEW V AS SELECT Id FROM A WHERE EXISTS (SELECT Id FROM B)");

        Assert.That(Ids("SELECT Id FROM V"), Is.EqualTo(new long[] { 1, 2 }));
    }

    #endregion

    #region What the catalog reports

    /// <summary>
    /// <c>INFORMATION_SCHEMA</c> reports the view's definition correctly, or reports nothing - never
    /// a definition that is not the view's.
    /// </summary>
    /// <remarks>
    /// The renderer is display-only from 9.0.0, so a gap in it is cosmetic. It stops being cosmetic
    /// the moment it produces <i>plausible but different</i> SQL: <c>SELECT Id FROM A</c> reported as
    /// the definition of a view over <c>A UNION B</c> is a statement about the database that is
    /// simply false, and someone will copy it. Every rendering is therefore read back and compared
    /// to the tree, and an unfaithful one is withheld.
    /// </remarks>
    [TestCase("SELECT Id FROM A")]
    [TestCase("SELECT Id FROM A WHERE Id > 1")]
    [TestCase("SELECT Id FROM A UNION SELECT Id FROM B")]
    [TestCase("WITH C AS (SELECT Id FROM A) SELECT Id FROM C")]
    [TestCase("SELECT Id FROM A WHERE Id > (SELECT 1)")]
    public void ReportedDefinitionIsEitherRightOrAbsentTest(string body)
    {
        m_engine.Execute($"CREATE VIEW V AS {body}");

        var reported = m_engine
            .Query("SELECT VIEW_DEFINITION FROM INFORMATION_SCHEMA.VIEWS WHERE TABLE_NAME = 'V'")
            .Select(row => row[0].AsString())
            .FirstOrDefault();

        if (string.IsNullOrEmpty(reported))
            Assert.Pass("withheld rather than reported wrongly, which is the honest outcome");

        // Reported: then it must mean the same thing as the view, which is testable by running it.
        var throughTheView = Ids("SELECT Id FROM V");
        var throughTheText = m_engine.Query(reported!)
            .Select(row => row[0].AsInt64())
            .OrderBy(id => id)
            .ToArray();

        Assert.That(throughTheText, Is.EqualTo(throughTheView),
            $"the catalog reports <{reported}>, which does not answer the way the view does");
    }

    #endregion

    #region Survives a reopen

    /// <summary>
    /// The body has to survive the file, not just the session.
    /// </summary>
    /// <remarks>
    /// Every test above runs against an in-memory database, where a tree held in a field would pass
    /// all of them while nothing durable had changed. This one closes the database and opens it
    /// again, so what is being read is what MemoryPack actually wrote to disk.
    /// </remarks>
    [Test]
    public void ViewBodySurvivesACloseAndReopenTest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"witdb_view_{Guid.NewGuid():N}");

        try
        {
            using (var engine = new WitSqlEngine(WitDatabase.Create(path), ownsStore: true))
            {
                engine.Execute("CREATE TABLE A (Id INT PRIMARY KEY)");
                engine.Execute("CREATE TABLE B (Id INT PRIMARY KEY)");
                engine.Execute("INSERT INTO A (Id) VALUES (1)");
                engine.Execute("INSERT INTO A (Id) VALUES (2)");
                engine.Execute("INSERT INTO B (Id) VALUES (3)");
                engine.Execute("INSERT INTO B (Id) VALUES (4)");
                engine.Execute("CREATE VIEW V AS SELECT Id FROM A UNION SELECT Id FROM B");
            }

            using (var engine = new WitSqlEngine(WitDatabase.Open(path), ownsStore: true))
            {
                var ids = engine.Query("SELECT Id FROM V")
                    .Select(row => row[0].AsInt64())
                    .OrderBy(id => id)
                    .ToArray();

                Assert.That(ids, Is.EqualTo(new long[] { 1, 2, 3, 4 }),
                    "the view body must come back off the disk whole");
            }
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }

    #endregion

    #region Helpers

    private long[] Ids(string sql) =>
        m_engine.Query(sql).Select(row => row[0].AsInt64()).OrderBy(id => id).ToArray();

    #endregion
}
