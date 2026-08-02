namespace OutWit.Database.Tests.AuditVerification;

/// <summary>
/// A schema object naming a function the engine does not have is refused when it is declared.
/// </summary>
/// <remarks>
/// <para>
/// Measured 2026-08-01 at head <c>c23b983</c>: a <c>CHECK</c>, a computed column, an index
/// expression and a view were <b>all</b> accepted with <c>NoSuchFunc(V)</c> in them. Two of them
/// then threw at first use, and the computed column did not fail at all - it answered NULL, which is
/// the finding <c>ComputedColumnFailureFindingsTests</c> closes.
/// </para>
/// <para>
/// Phase 7's rule across the DDL surface is accepted, enforced, or refused - never accepted and
/// discovered later. The declaration is the one moment the caller is still holding the statement
/// that is wrong, and the moment the table has no rows depending on it.
/// </para>
/// <para>
/// It also matters ahead of phase 9d. Once a function can be created and dropped, a schema object
/// naming one becomes a dependency, and a dangling dependency here is not a small thing: the
/// recorded <c>RENAME COLUMN</c> and <c>DROP COLUMN</c> defects leave expressions naming a column
/// that no longer exists, after which the table <b>cannot be written to at all</b>. Refusing the
/// declaration is the same guard one step earlier.
/// </para>
/// <para>
/// The list of names the engine knows is a <b>superset</b> of what it implements, deliberately - see
/// <c>ExpressionFunctions</c>. Over-permitting costs an error at first use, which is the old
/// behaviour; under-permitting would refuse a schema that works, which is worse than the defect.
/// <c>KnownFunctionCorpusTests</c> is the net that keeps it a superset.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public sealed class UnknownFunctionInSchemaFindingsTests : WitSqlEngineTestsBase
{
    #region Refused

    /// <summary>
    /// Every place a stored expression can carry a function call.
    /// </summary>
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT CHECK (NoSuchFunc(V) > 0))",
        TestName = "a column CHECK")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT, CHECK (NoSuchFunc(V) > 0))",
        TestName = "a table CHECK")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT, W AS (NoSuchFunc(V)))",
        TestName = "a computed column")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT DEFAULT (NoSuchFunc(1)))",
        TestName = "a DEFAULT")]
    public void SchemaNamingAnUnknownFunctionIsRefusedTest(string ddl)
    {
        Assert.That(() => m_engine.Execute(ddl),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("NoSuchFunc").IgnoreCase,
            "the refusal must name the function, so the caller can see the typo");
    }

    /// <summary>
    /// And on the way in through <c>ALTER</c>, which is the other door to the same catalog.
    /// </summary>
    [Test]
    public void AlterTableNamingAnUnknownFunctionIsRefusedTest()
    {
        m_engine.Execute("CREATE TABLE X (Id INT PRIMARY KEY, V INT)");

        Assert.That(() => m_engine.Execute("ALTER TABLE X ADD COLUMN W INT DEFAULT (NoSuchFunc(1))"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("NoSuchFunc").IgnoreCase);
    }

    /// <summary>
    /// And an index expression, which is the third.
    /// </summary>
    [Test]
    public void IndexExpressionNamingAnUnknownFunctionIsRefusedTest()
    {
        m_engine.Execute("CREATE TABLE X (Id INT PRIMARY KEY, V INT)");

        Assert.That(() => m_engine.Execute("CREATE INDEX IX ON X ((NoSuchFunc(V)))"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("NoSuchFunc").IgnoreCase);
    }

    /// <summary>
    /// Nothing may be left behind by the refusal.
    /// </summary>
    /// <remarks>
    /// The half that made the DDL-in-a-transaction finding worse than a refusal: the catalog was
    /// changed before the failure. A statement that is refused must leave no table.
    /// </remarks>
    [Test]
    public void TheRefusedTableIsNotCreatedTest()
    {
        Assert.That(() => m_engine.Execute(
            "CREATE TABLE X (Id INT PRIMARY KEY, V INT CHECK (NoSuchFunc(V) > 0))"), Throws.Exception);

        Assert.That(m_engine.GetTable("X"), Is.Null);
    }

    #endregion

    #region Still allowed

    /// <summary>
    /// The refusal must be narrow, or it breaks every schema that works.
    /// </summary>
    /// <remarks>
    /// This is the risk that decided the shape of the whole change: a name missing from the known
    /// set refuses a valid <c>CREATE TABLE</c>, which is a worse failure than the one being fixed.
    /// The cases below are ordinary and must never be touched.
    /// </remarks>
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V VARCHAR(50) CHECK (LENGTH(V) > 2))",
        TestName = "a CHECK using a scalar function")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V VARCHAR(50), W AS (UPPER(V)))",
        TestName = "a computed column using one")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT, W AS (COALESCE(V, 0) + ABS(V)))",
        TestName = "several of them")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V TIMESTAMP DEFAULT (NOW()))",
        TestName = "a DEFAULT using a nondeterministic function, which is legitimate there")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT CHECK (V BETWEEN 1 AND ABS(-10)))",
        TestName = "one inside a BETWEEN")]
    [TestCase("CREATE TABLE X (Id INT PRIMARY KEY, V INT, W AS (IIF(V > 0, ABS(V), 0)))",
        TestName = "IIF, which lives in a different router")]
    public void AnOrdinarySchemaIsUnaffectedTest(string ddl)
    {
        Assert.That(() => m_engine.Execute(ddl), Throws.Nothing);
        Assert.That(m_engine.GetTable("X"), Is.Not.Null);
    }

    /// <summary>
    /// <c>TOBOOLEAN</c>, which the corpus found the grammar admitting and the engine lacking.
    /// </summary>
    /// <remarks>
    /// Every other <c>TO…</c> conversion was implemented; this one was not, so the grammar promised
    /// a function that did not exist. Found by <c>KnownFunctionCorpusTests</c> on its first green
    /// run - which is the whole argument for asking the question of the entire vocabulary rather
    /// than of the names somebody thought to try.
    /// </remarks>
    [Test]
    public void ToBooleanNowWorksTest()
    {
        Assert.That(m_engine.Query("SELECT TOBOOLEAN(1)")[0][0].AsBool(), Is.True);
        Assert.That(() => m_engine.Execute(
            "CREATE TABLE X (Id INT PRIMARY KEY, V INT, W AS (TOBOOLEAN(V)))"), Throws.Nothing);
    }

    #endregion
}
