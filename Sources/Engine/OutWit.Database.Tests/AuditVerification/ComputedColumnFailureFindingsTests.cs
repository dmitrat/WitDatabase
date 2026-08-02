namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A computed column that cannot be evaluated must say so, not answer NULL.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-01 at head <c>c23b983</c>, while auditing the area for phase 9d. Three iterators
/// - <c>IteratorTableScan</c>, <c>IteratorIndexSeek</c> and <c>IteratorIndexRangeScan</c> - each
/// carried their own copy of the per-row evaluation, and each copy ended in
/// <c>catch { values[i] = WitSqlValue.Null; }</c>. A bare catch turning <b>every</b> failure into a
/// legal value: NULL is the one answer a caller cannot tell from a computed one, so this was a wrong
/// result rather than a missing one, on every read path, with nothing raised anywhere.
/// </para>
/// <para>
/// <b>How a column gets into that state.</b> <c>DROP COLUMN</c> and <c>RENAME COLUMN</c> leave every
/// expression that named the old column dangling - a defect recorded in
/// <c>ColumnRenameAndDropFindingsTests</c> and still open. For a <c>CHECK</c> that makes the table
/// unwritable, loudly. For a computed column it made the table answer NULL for that column on every
/// row, quietly, which is how it stayed unnoticed. Same broken schema, two different stories.
/// </para>
/// <para>
/// The unknown-function route into the same state is now closed one step earlier -
/// <c>UnknownFunctionInSchemaFindingsTests</c> refuses the declaration - so what this fixture uses
/// is the route that remains, and it is the one consumers actually take.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class ComputedColumnFailureFindingsTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE D (Id INT PRIMARY KEY, V INT, U INT, W AS (V + U))");
        m_engine.Execute("INSERT INTO D (Id, V, U) VALUES (1, 10, 5)");
        m_engine.Execute("INSERT INTO D (Id, V, U) VALUES (2, 20, 5)");
    }

    #endregion

    #region Tests

    /// <summary>
    /// It works before the column it depends on is taken away.
    /// </summary>
    /// <remarks>
    /// The baseline the rest of the fixture rests on: if this were broken, every assertion below
    /// would pass for the wrong reason.
    /// </remarks>
    [Test]
    public void TheComputedColumnWorksToBeginWithTest()
    {
        Assert.That(m_engine.Query("SELECT W FROM D WHERE Id = 1")[0][0].AsInt64(), Is.EqualTo(15));
    }

    /// <summary>
    /// After <c>DROP COLUMN</c> the read must report the broken column, not answer NULL.
    /// </summary>
    [Test]
    public void ADanglingComputedColumnIsReportedOnAScanTest()
    {
        m_engine.Execute("ALTER TABLE D DROP COLUMN U");

        Assert.That(() => m_engine.Query("SELECT W FROM D"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("D.W"),
            "the column that could not be computed must be named, not answered as NULL");
    }

    /// <summary>
    /// And <c>SELECT *</c> must not be a way round it.
    /// </summary>
    [Test]
    public void ADanglingComputedColumnIsReportedOnSelectStarTest()
    {
        m_engine.Execute("ALTER TABLE D DROP COLUMN U");

        Assert.That(() => m_engine.Query("SELECT * FROM D"),
            Throws.InstanceOf<InvalidOperationException>());
    }

    /// <summary>
    /// <c>RENAME COLUMN</c> reaches the same state by the other door.
    /// </summary>
    [Test]
    public void ARenamedColumnUnderneathIsReportedTest()
    {
        m_engine.Execute("ALTER TABLE D RENAME COLUMN U TO Q");

        Assert.That(() => m_engine.Query("SELECT W FROM D"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("D.W"));
    }

    /// <summary>
    /// The index paths carry their own copy of the same evaluation, and had their own copy of the
    /// same catch.
    /// </summary>
    /// <remarks>
    /// Worth its own test rather than trusting that one fix covers three files: the duplication is
    /// exactly how a fix goes half-applied. A seek and a range scan are different iterators, and
    /// each had its own <c>catch</c>.
    /// </remarks>
    [Test]
    public void ADanglingComputedColumnIsReportedThroughAnIndexTest()
    {
        m_engine.Execute("CREATE INDEX IX ON D (V)");
        m_engine.Execute("ALTER TABLE D DROP COLUMN U");

        Assert.That(() => m_engine.Query("SELECT W FROM D WHERE V = 10"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("D.W"),
            "an index seek evaluates the computed column too");

        Assert.That(() => m_engine.Query("SELECT W FROM D WHERE V > 5"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("D.W"),
            "and so does an index range scan");
    }

    /// <summary>
    /// The message must say what actually went wrong, not only that something did.
    /// </summary>
    [Test]
    public void TheReportNamesTheUnderlyingCauseTest()
    {
        m_engine.Execute("ALTER TABLE D DROP COLUMN U");

        var message = Assert.Catch<InvalidOperationException>(
            () => m_engine.Query("SELECT W FROM D"))!.Message;

        Assert.That(message, Does.Contain("U"),
            "the caller needs to know which column the expression is still looking for");
    }

    /// <summary>
    /// And a computed column that is legitimately NULL must stay an answer.
    /// </summary>
    /// <remarks>
    /// The obvious way to get this fix wrong is to make a NULL result raise. A scalar function of
    /// NULL is NULL by the SQL standard, and NULL arithmetic is NULL - both are answers, not
    /// failures, and neither may be turned into an error by a change aimed at the ones that are.
    /// </remarks>
    [Test]
    public void AComputedColumnThatIsLegitimatelyNullStillAnswersTest()
    {
        m_engine.Execute("CREATE TABLE E (Id INT PRIMARY KEY, V INT, U VARCHAR(10), "
                         + "W AS (V + 1), X AS (UPPER(U)))");
        m_engine.Execute("INSERT INTO E (Id, V, U) VALUES (1, NULL, NULL)");

        var row = m_engine.Query("SELECT W, X FROM E")[0];

        Assert.Multiple(() =>
        {
            Assert.That(row[0].IsNull, Is.True, "NULL + 1 is NULL, which is an answer");
            Assert.That(row[1].IsNull, Is.True, "UPPER(NULL) is NULL, which is an answer");
        });
    }

    #endregion
}
