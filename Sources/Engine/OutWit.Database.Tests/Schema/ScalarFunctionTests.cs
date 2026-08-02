namespace OutWit.Database.Tests.Schema;

/// <summary>
/// User-defined scalar functions, end to end.
/// </summary>
/// <remarks>
/// <para>
/// A function's body is one expression, so invoking one is substitution inside
/// <c>ExpressionEvaluator</c> - the body is evaluated against a row built from the arguments. It
/// never enters <c>StatementExecutor</c>, which is what keeps it off the execution-nesting path and
/// makes it safe to reach from a <c>CHECK</c> or an index key. See
/// <c>Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md</c> § 1.
/// </para>
/// <para>
/// Everything a body can be wrong about is decided when it is declared. The reason is the row path:
/// a body that fails per row fails inside a <c>CHECK</c> or a computed column, where no caller is
/// left holding the statement that was wrong.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ScalarFunctionTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT, Name VARCHAR(50))");
        m_engine.Execute("INSERT INTO T (Id, V, Name) VALUES (1, 10, 'alice')");
        m_engine.Execute("INSERT INTO T (Id, V, Name) VALUES (2, 20, 'bob')");
    }

    #endregion

    #region It works

    [Test]
    public void AFunctionIsCallableInASelectTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");

        Assert.That(m_engine.Query("SELECT Doubled(21)")[0][0].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void AFunctionSeesTheRowThroughItsArgumentsTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");

        var values = m_engine.Query("SELECT Doubled(V) FROM T ORDER BY Id")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.That(values, Is.EqualTo(new[] { 20L, 40L }));
    }

    [TestCase("SELECT Id FROM T WHERE Doubled(V) = 40", TestName = "in a WHERE")]
    [TestCase("SELECT Id FROM T ORDER BY Doubled(V) DESC", TestName = "in an ORDER BY")]
    [TestCase("SELECT Doubled(V) + 1 FROM T", TestName = "inside a larger expression")]
    [TestCase("SELECT Doubled(Doubled(V)) FROM T", TestName = "nested in itself as an argument")]
    public void AFunctionWorksWhereverAnExpressionMayAppearTest(string sql)
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");

        Assert.That(() => m_engine.Query(sql), Throws.Nothing);
    }

    [Test]
    public void AFunctionMayTakeSeveralParametersTest()
    {
        m_engine.Execute("CREATE FUNCTION Area(W INT, H INT) RETURNS INT AS BEGIN RETURN W * H; END");

        Assert.That(m_engine.Query("SELECT Area(6, 7)")[0][0].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void AFunctionMayCallBuiltInsAndOtherFunctionsTest()
    {
        m_engine.Execute("CREATE FUNCTION Shout(S VARCHAR(50)) RETURNS VARCHAR(50) AS BEGIN RETURN UPPER(S); END");
        m_engine.Execute("CREATE FUNCTION Loud(S VARCHAR(50)) RETURNS VARCHAR(50) AS BEGIN RETURN Shout(S); END");

        Assert.That(m_engine.Query("SELECT Loud('alice')")[0][0].AsString(), Is.EqualTo("ALICE"));
    }

    /// <summary>
    /// The parameter row is the whole scope: a body cannot reach the caller's row.
    /// </summary>
    /// <remarks>
    /// A parameter deliberately named after a column of the table being read. If the body could see
    /// the caller's row, this would return the column's value instead of the argument - and a
    /// function whose meaning depends on where it is called from is not a function.
    /// </remarks>
    [Test]
    public void TheParameterRowShadowsTheCallersRowTest()
    {
        m_engine.Execute("CREATE FUNCTION Echo(V INT) RETURNS INT AS BEGIN RETURN V; END");

        var values = m_engine.Query("SELECT Echo(99) FROM T")
            .Select(row => row[0].AsInt64())
            .Distinct()
            .ToArray();

        Assert.That(values, Is.EqualTo(new[] { 99L }),
            "the body must see its argument, not the column of the same name");
    }

    #endregion

    #region On the row path

    [Test]
    public void AFunctionWorksInACheckTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE TABLE C (Id INT PRIMARY KEY, V INT CHECK (Doubled(V) < 100))");

        Assert.Multiple(() =>
        {
            Assert.That(() => m_engine.Execute("INSERT INTO C (Id, V) VALUES (1, 10)"), Throws.Nothing);
            Assert.That(() => m_engine.Execute("INSERT INTO C (Id, V) VALUES (2, 60)"), Throws.Exception,
                "the CHECK must be enforced through the function");
        });
    }

    [Test]
    public void AFunctionWorksInAComputedColumnTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE TABLE C (Id INT PRIMARY KEY, V INT, W AS (Doubled(V)))");
        m_engine.Execute("INSERT INTO C (Id, V) VALUES (1, 21)");

        Assert.That(m_engine.Query("SELECT W FROM C")[0][0].AsInt64(), Is.EqualTo(42));
    }

    /// <summary>
    /// A deterministic function may key an index; a non-deterministic one may not.
    /// </summary>
    /// <remarks>
    /// An index key is computed once at write time and never recomputed, so a function whose answer
    /// can move would leave the index describing a value the row no longer has. Determinism is
    /// decided from the body when the function is declared, and this is the rule the design note
    /// named as the precondition for letting a function into an index key at all.
    /// </remarks>
    [Test]
    public void OnlyADeterministicFunctionMayKeyAnIndexTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE FUNCTION Jitter(N INT) RETURNS INT AS BEGIN RETURN N + RANDOM(); END");

        Assert.Multiple(() =>
        {
            Assert.That(() => m_engine.Execute("CREATE INDEX IXD ON T ((Doubled(V)))"), Throws.Nothing);

            Assert.That(() => m_engine.Execute("CREATE INDEX IXJ ON T ((Jitter(V)))"),
                Throws.InstanceOf<NotSupportedException>(),
                "a function that does not give the same answer twice cannot key an index");
        });
    }

    /// <summary>
    /// And an index keyed on a function must give the right rows, not merely be accepted.
    /// </summary>
    /// <remarks>
    /// Acceptance and correctness are different claims, and confusing them is the mistake this phase
    /// already corrected once: the subquery-in-an-index finding was measured as <i>accepted</i> and
    /// recorded as such, precisely because a wrong answer from it had <b>not</b> been demonstrated.
    /// So this asserts the rows.
    /// </remarks>
    [Test]
    public void AnIndexKeyedOnAFunctionAnswersCorrectlyTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE INDEX IX ON T ((Doubled(V)))");

        // Written after the index exists, so the index is maintained by the write path rather than
        // built once from rows that were already there.
        m_engine.Execute("INSERT INTO T (Id, V, Name) VALUES (3, 30, 'carol')");

        var byExpression = m_engine.Query("SELECT Id FROM T WHERE Doubled(V) = 60")
            .Select(row => row[0].AsInt64())
            .ToArray();

        var byPlainScan = m_engine.Query("SELECT Id FROM T WHERE V = 30")
            .Select(row => row[0].AsInt64())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(byExpression, Is.EqualTo(new[] { 3L }));
            Assert.That(byExpression, Is.EqualTo(byPlainScan),
                "the indexed path and the plain one must agree - if they ever do not, the index is "
                + "answering from a key the row no longer has");
        });
    }

    /// <summary>
    /// Determinism composes: a clean function calling a dirty one is dirty.
    /// </summary>
    [Test]
    public void DeterminismFoldsInTheFunctionsItCallsTest()
    {
        m_engine.Execute("CREATE FUNCTION Jitter(N INT) RETURNS INT AS BEGIN RETURN N + RANDOM(); END");
        m_engine.Execute("CREATE FUNCTION Wrapper(N INT) RETURNS INT AS BEGIN RETURN Jitter(N); END");

        var deterministic = m_engine.Query(
                "SELECT IS_DETERMINISTIC FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_NAME = 'Wrapper'")
            [0][0].AsString();

        Assert.That(deterministic, Is.EqualTo("NO"),
            "a function that calls a non-deterministic one cannot itself be deterministic");
    }

    #endregion

    #region Refused at declaration

    [Test]
    public void ABodyNamingSomethingThatIsNotAParameterIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute(
                "CREATE FUNCTION Bad(N INT) RETURNS INT AS BEGIN RETURN N + M; END"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("M"),
            "a function body has no row to read a column from, and this must be said when it is "
            + "declared rather than when a row is written");
    }

    [Test]
    public void ABodyCallingAFunctionThatDoesNotExistIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute(
                "CREATE FUNCTION Bad(N INT) RETURNS INT AS BEGIN RETURN NoSuchFunc(N); END"),
            Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void AFunctionThatCallsItselfIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute(
                "CREATE FUNCTION Loop(N INT) RETURNS INT AS BEGIN RETURN Loop(N); END"),
            Throws.InstanceOf<NotSupportedException>().With.Message.Contains("itself"),
            "recursion in an expression body has nothing to stop it, and a stack overflow cannot be "
            + "caught");
    }

    [Test]
    public void AForeignLanguageIsRefusedTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => m_engine.Execute(
                    "CREATE FUNCTION F(N INT) RETURNS INT LANGUAGE plpgsql AS BEGIN RETURN N; END"),
                Throws.InstanceOf<NotSupportedException>().With.Message.Contains("plpgsql"));

            Assert.That(() => m_engine.Execute(
                    "CREATE FUNCTION G(N INT) RETURNS INT LANGUAGE SQL AS BEGIN RETURN N; END"),
                Throws.Nothing, "LANGUAGE SQL is the one this engine runs");
        });
    }

    [Test]
    public void ADuplicateParameterNameIsRefusedTest()
    {
        Assert.That(() => m_engine.Execute(
                "CREATE FUNCTION F(N INT, N INT) RETURNS INT AS BEGIN RETURN N; END"),
            Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void TheWrongNumberOfArgumentsIsRefusedTest()
    {
        m_engine.Execute("CREATE FUNCTION Area(W INT, H INT) RETURNS INT AS BEGIN RETURN W * H; END");

        Assert.That(() => m_engine.Query("SELECT Area(1)"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("2"));
    }

    #endregion

    #region Dropping

    [Test]
    public void AFunctionCanBeDroppedAndIsThenUnknownTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("DROP FUNCTION Doubled");

        Assert.That(() => m_engine.Query("SELECT Doubled(1)"),
            Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public void DroppingSomethingThatIsNotThereSaysSoTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => m_engine.Execute("DROP FUNCTION NeverExisted"), Throws.Exception);
            Assert.That(() => m_engine.Execute("DROP FUNCTION IF EXISTS NeverExisted"), Throws.Nothing);
        });
    }

    /// <summary>
    /// A function a stored expression still names cannot be dropped.
    /// </summary>
    /// <remarks>
    /// <c>RESTRICT</c>, with no <c>CASCADE</c>. The alternative is already recorded in its worst
    /// form: <c>RENAME COLUMN</c> and <c>DROP COLUMN</c> leave expressions naming something that no
    /// longer exists, and the table cannot be written to at all afterwards.
    /// </remarks>
    [TestCase("CREATE TABLE D (Id INT PRIMARY KEY, V INT CHECK (Doubled(V) < 100))",
        TestName = "a CHECK depends on it")]
    [TestCase("CREATE TABLE D (Id INT PRIMARY KEY, V INT, W AS (Doubled(V)))",
        TestName = "a computed column depends on it")]
    public void AFunctionInUseCannotBeDroppedTest(string ddl)
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute(ddl);

        Assert.That(() => m_engine.Execute("DROP FUNCTION Doubled"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Doubled"));
    }

    [Test]
    public void AFunctionAnIndexIsBuiltOnCannotBeDroppedTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE INDEX IX ON T ((Doubled(V)))");

        Assert.That(() => m_engine.Execute("DROP FUNCTION Doubled"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("IX"));
    }

    [Test]
    public void AFunctionAnotherFunctionCallsCannotBeDroppedTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE FUNCTION Quad(N INT) RETURNS INT AS BEGIN RETURN Doubled(Doubled(N)); END");

        Assert.That(() => m_engine.Execute("DROP FUNCTION Doubled"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("Quad"),
            "dropping it would leave Quad calling something that does not exist");
    }

    /// <summary>
    /// A view, a procedure - every object kind that can name a function holds the drop.
    /// </summary>
    /// <remarks>
    /// <b>Views were missing, and the pre-release audit found it by execution.</b> A view's body is a
    /// query rather than a stored row expression, so it was not among the definitions the refusal
    /// walked - and a view could be left selecting a function that no longer exists, which is the
    /// exact state the refusal was written to prevent. It prevented it for every object kind that
    /// was on the list.
    /// </remarks>
    [Test]
    public void EveryObjectKindThatNamesAFunctionHoldsTheDropTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE VIEW VW AS SELECT Doubled(V) AS D FROM T");

        Assert.That(() => m_engine.Execute("DROP FUNCTION Doubled"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("VW"),
            "a view selecting it must hold the drop");

        m_engine.Execute("DROP VIEW VW");
        m_engine.Execute("CREATE PROCEDURE P AS BEGIN SELECT Doubled(1); END");

        Assert.That(() => m_engine.Execute("DROP FUNCTION Doubled"),
            Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("P"),
            "and so must a procedure using it");
    }

    /// <summary>
    /// The declared return type is applied to what the body produces.
    /// </summary>
    /// <remarks>
    /// <b>Found by the pre-release audit.</b> A function declared <c>RETURNS INT</c> whose body
    /// returned <c>'not a number'</c> handed the text straight through, while
    /// <c>INFORMATION_SCHEMA.ROUTINES</c> reported its type as <c>INTEGER</c> - the catalog
    /// describing something the engine was not doing. A declared type that nothing checks is worse
    /// than no declared type, because a consumer reads the catalog and builds against it. The same
    /// converter a column write uses is applied, so a function and a column agree about what a
    /// declared type means.
    /// </remarks>
    [Test]
    public void TheDeclaredReturnTypeIsAppliedTest()
    {
        m_engine.Execute("CREATE FUNCTION AsText(N INT) RETURNS VARCHAR(20) AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE FUNCTION AsNumber(S VARCHAR(20)) RETURNS INT AS BEGIN RETURN S; END");

        Assert.Multiple(() =>
        {
            Assert.That(m_engine.Query("SELECT AsText(21)")[0][0].AsString(), Is.EqualTo("42"),
                "an INT body under a VARCHAR return type comes back as text");

            Assert.That(m_engine.Query("SELECT AsNumber('42')")[0][0].AsInt64(), Is.EqualTo(42),
                "and a text body under an INT return type comes back as a number");
        });
    }

    /// <summary>
    /// And NULL stays NULL rather than being converted into a zero of the declared type.
    /// </summary>
    [Test]
    public void ANullResultStaysNullTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");

        Assert.That(m_engine.Query("SELECT Doubled(NULL)")[0][0].IsNull, Is.True,
            "NULL is the absence of a value, and coercing it to a typed zero would be an answer "
            + "where there was none");
    }

    /// <summary>
    /// And the refusal must be narrow: an unrelated function drops cleanly.
    /// </summary>
    [Test]
    public void AnUnusedFunctionStillDropsTest()
    {
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
        m_engine.Execute("CREATE FUNCTION Unused(N INT) RETURNS INT AS BEGIN RETURN N; END");
        m_engine.Execute("CREATE TABLE D (Id INT PRIMARY KEY, V INT CHECK (Doubled(V) < 100))");

        Assert.That(() => m_engine.Execute("DROP FUNCTION Unused"), Throws.Nothing);
    }

    #endregion
}
