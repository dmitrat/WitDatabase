using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Schema;

namespace OutWit.Database.Tests.Schema;

/// <summary>
/// Routines against the rest of the engine - the combinations, rather than the parts.
/// </summary>
/// <remarks>
/// <para>
/// Written during the pre-release audit of phase 9d. Everything here was probed once, by hand,
/// while looking for gaps, and everything here passed - which is exactly why it needed pinning: a
/// behaviour confirmed by a throwaway probe is confirmed for one afternoon, and this project has
/// been wrong about what it remembered ten times over.
/// </para>
/// <para>
/// The parts are covered by <c>ScalarFunctionTests</c> and <c>ProcedureTests</c>. What is here is
/// where a routine meets something else: a trigger, a view, a group-by, another session.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RoutineIntegrationTests : WitSqlEngineTestsBase
{
    #region Setup

    public override void Setup()
    {
        base.Setup();

        m_engine.Execute("CREATE TABLE T (Id INT PRIMARY KEY, V INT)");
        m_engine.Execute("CREATE TABLE Log (Id INT PRIMARY KEY AUTOINCREMENT, V INT)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (1, 10)");
        m_engine.Execute("INSERT INTO T (Id, V) VALUES (2, 20)");
        m_engine.Execute("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");
    }

    #endregion

    #region A function wherever a query puts an expression

    [TestCase("SELECT Doubled(V) FROM T GROUP BY Doubled(V)", TestName = "in a GROUP BY")]
    [TestCase("SELECT SUM(Doubled(V)) FROM T", TestName = "inside an aggregate")]
    [TestCase("SELECT V FROM T GROUP BY V HAVING SUM(Doubled(V)) > 10", TestName = "inside an aggregate in HAVING")]
    [TestCase("SELECT V FROM T ORDER BY Doubled(V)", TestName = "in an ORDER BY")]
    [TestCase("SELECT V FROM T WHERE Doubled(V) IN (20, 40)", TestName = "in an IN list")]
    [TestCase("SELECT Doubled((SELECT MAX(V) FROM T))", TestName = "over a scalar subquery")]
    [TestCase("UPDATE T SET V = Doubled(V) WHERE Id = 1", TestName = "in an UPDATE assignment")]
    [TestCase("DELETE FROM T WHERE Doubled(V) = 40", TestName = "in a DELETE predicate")]
    [TestCase("INSERT INTO T (Id, V) VALUES (9, Doubled(5))", TestName = "in an INSERT value")]
    public void AFunctionWorksInEveryExpressionPositionTest(string sql)
    {
        Assert.That(() => m_engine.Execute(sql), Throws.Nothing);
    }

    /// <summary>
    /// A NULL argument gives NULL, which is the SQL answer and not a failure.
    /// </summary>
    [Test]
    public void ANullArgumentGivesNullTest()
    {
        Assert.That(m_engine.Query("SELECT Doubled(NULL)")[0][0].IsNull, Is.True);
    }

    #endregion

    #region A function meeting the rest of the engine

    /// <summary>
    /// A trigger body may call a function - it is an expression, not a <c>CALL</c>.
    /// </summary>
    /// <remarks>
    /// The distinction that makes the phase-9d rule coherent: a trigger may not reach a
    /// <b>procedure</b>, because that would put a row loop under a body allowed to run DDL, but a
    /// function is evaluated inside an expression and runs no statements at all.
    /// </remarks>
    [Test]
    public void ATriggerBodyMayUseAFunctionTest()
    {
        m_engine.Execute(
            "CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW "
            + "BEGIN INSERT INTO Log (V) VALUES (Doubled(NEW.V)); END");

        m_engine.Execute("INSERT INTO T (Id, V) VALUES (3, 30)");

        Assert.That(m_engine.Query("SELECT V FROM Log")[0][0].AsInt64(), Is.EqualTo(60));
    }

    [Test]
    public void AViewMayUseAFunctionTest()
    {
        m_engine.Execute("CREATE VIEW VW AS SELECT Doubled(V) AS D FROM T ORDER BY Id");

        Assert.That(m_engine.Query("SELECT D FROM VW").Select(row => row[0].AsInt64()).ToArray(),
            Is.EqualTo(new[] { 20L, 40L }));
    }

    [Test]
    public void ADefaultMayUseAFunctionTest()
    {
        m_engine.Execute("CREATE TABLE D (Id INT PRIMARY KEY, V INT DEFAULT (Doubled(21)))");
        m_engine.Execute("INSERT INTO D (Id) VALUES (1)");

        Assert.That(m_engine.Query("SELECT V FROM D")[0][0].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void AProcedureMayUseAFunctionTest()
    {
        m_engine.Execute("CREATE PROCEDURE P(N INT) AS BEGIN INSERT INTO Log (V) VALUES (Doubled(N)); END");
        m_engine.Execute("CALL P(21)");

        Assert.That(m_engine.Query("SELECT V FROM Log")[0][0].AsInt64(), Is.EqualTo(42));
    }

    #endregion

    #region Two sessions over one catalog

    /// <summary>
    /// A catalog is a property of the database, so a routine one session creates is another's too.
    /// </summary>
    /// <remarks>
    /// Several connections over one database is the supported deployment shape - an ASP.NET Core
    /// host with scoped contexts - and until 5.0.0 every engine built its own catalog, so two
    /// sessions diverged measurably. A routine is a schema object like any other and must not
    /// reintroduce that.
    /// </remarks>
    [Test]
    public void ARoutineCreatedByOneSessionIsVisibleToAnotherTest()
    {
        var database = WitDatabase.CreateInMemory();
        var catalog = new SchemaCatalog(database.Store);

        using var first = new WitSqlEngine(database, catalog);
        using var second = new WitSqlEngine(database, catalog);

        first.Execute("CREATE FUNCTION Tripled(N INT) RETURNS INT AS BEGIN RETURN N * 3; END");

        Assert.That(second.Query("SELECT Tripled(14)")[0][0].AsInt64(), Is.EqualTo(42));
    }

    #endregion

    #region A cycle of functions cannot form

    /// <summary>
    /// The claim that made refusing only direct self-calls sufficient, asserted rather than argued.
    /// </summary>
    /// <remarks>
    /// Recursion in an expression-bodied function is unbounded and process-fatal, and only the
    /// direct case is refused at declaration. That is safe <b>if</b> a cycle cannot form afterwards,
    /// which rests on two other refusals: a function cannot be dropped while another calls it, and a
    /// name cannot be redeclared. Both are checked here, because "it follows" is how a guard ends up
    /// resting on something that quietly changed.
    /// </remarks>
    [Test]
    public void ACycleOfFunctionsCannotBeCreatedTest()
    {
        m_engine.Execute("CREATE FUNCTION Base1(N INT) RETURNS INT AS BEGIN RETURN N + 1; END");
        m_engine.Execute("CREATE FUNCTION Caller1(N INT) RETURNS INT AS BEGIN RETURN Base1(N); END");

        Assert.Multiple(() =>
        {
            Assert.That(() => m_engine.Execute("DROP FUNCTION Base1"),
                Throws.InstanceOf<InvalidOperationException>(),
                "the only way to make Base1 call back would be to replace it, and it cannot be dropped");

            Assert.That(() => m_engine.Execute(
                    "CREATE FUNCTION Base1(N INT) RETURNS INT AS BEGIN RETURN Caller1(N); END"),
                Throws.InstanceOf<InvalidOperationException>(),
                "nor redeclared over the existing one");
        });
    }

    #endregion

    #region A procedure's DDL is undone with its call

    /// <summary>
    /// A procedure body that creates a table and then fails leaves no table.
    /// </summary>
    /// <remarks>
    /// Two things have to hold together for this: schema writes go through the caller's transaction
    /// (phase 9d's audit finding 19), and a <c>CALL</c> is a unit of work (found missing while
    /// procedures were being built). Either one alone leaves the table behind.
    /// </remarks>
    [Test]
    public void DdlInAFailingProcedureIsUndoneTest()
    {
        m_engine.Execute(@"
            CREATE PROCEDURE MakeAndFail AS BEGIN
                CREATE TABLE Z (Id INT PRIMARY KEY);
                INSERT INTO T (Id, V) VALUES (1, 1);
            END");

        Assert.That(() => m_engine.Execute("CALL MakeAndFail()"), Throws.Exception);

        Assert.That(m_engine.GetTable("Z"), Is.Null,
            "the table the body created before it failed must be gone with the call");
    }

    #endregion
}
