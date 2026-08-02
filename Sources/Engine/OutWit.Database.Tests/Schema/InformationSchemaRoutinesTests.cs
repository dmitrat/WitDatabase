using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Engine;
using OutWit.Database.Parser;
using OutWit.Database.Sql;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Schema;

/// <summary>
/// <c>INFORMATION_SCHEMA.ROUTINES</c> and <c>PARAMETERS</c> - what scaffolding reads.
/// </summary>
/// <remarks>
/// <para>
/// Measured before these were built: both names were refused by the planner - <i>"Unknown
/// INFORMATION_SCHEMA view: ROUTINES"</i>. That was the right failure for something unbuilt, and it
/// is why nothing was quietly reading an empty result and concluding the database had no routines.
/// </para>
/// <para>
/// <c>ROUTINE_DEFINITION</c> is rendered from the stored tree on demand. Nothing here asks that
/// rendering a question about the routine - <c>IS_DETERMINISTIC</c> comes from the definition, which
/// decided it from the tree. Asking a rendering about the schema is what made a partial index report
/// itself as complete, and the optimiser believed it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class InformationSchemaRoutinesTests
{
    #region Setup

    private WitSqlEngine m_engine = null!;

    [SetUp]
    public void Setup()
    {
        m_engine = new WitSqlEngine(WitDatabase.CreateInMemory(), ownsStore: true);
        m_engine.Execute("CREATE TABLE Log (Id INT PRIMARY KEY, Note VARCHAR(100))");
    }

    [TearDown]
    public void TearDown() => m_engine.Dispose();

    #endregion

    #region Empty

    /// <summary>
    /// With no routines the views answer with no rows - not with an error, and not with a refusal.
    /// </summary>
    [TestCase("ROUTINES")]
    [TestCase("PARAMETERS")]
    public void TheViewsExistAndAreEmptyToBeginWithTest(string view)
    {
        Assert.That(m_engine.Query($"SELECT * FROM INFORMATION_SCHEMA.{view}"), Is.Empty);
    }

    #endregion

    #region A function

    [Test]
    public void AFunctionIsReportedTest()
    {
        m_engine.CreateFunction(Doubled());

        var row = m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.ROUTINES").Single();

        Assert.Multiple(() =>
        {
            Assert.That(Text(row, "ROUTINE_NAME"), Is.EqualTo("Doubled"));
            Assert.That(Text(row, "ROUTINE_TYPE"), Is.EqualTo("FUNCTION"));
            Assert.That(Text(row, "ROUTINE_BODY"), Is.EqualTo("SQL"));
            Assert.That(Text(row, "IS_DETERMINISTIC"), Is.EqualTo("YES"));
            Assert.That(Text(row, "DATA_TYPE"), Is.Not.Null, "a function has a return type");
        });
    }

    /// <summary>
    /// The definition column carries the body, rendered from the tree.
    /// </summary>
    [Test]
    public void TheDefinitionIsRenderedFromTheStoredTreeTest()
    {
        m_engine.CreateFunction(Doubled());

        var definition = Text(m_engine.Query(
            "SELECT ROUTINE_DEFINITION FROM INFORMATION_SCHEMA.ROUTINES").Single(), "ROUTINE_DEFINITION");

        Assert.That(definition, Does.Contain("2"),
            "the body must be reported, not an empty column");
    }

    /// <summary>
    /// A non-deterministic function says so, and the answer comes from the definition.
    /// </summary>
    [Test]
    public void DeterminismIsReportedFromTheDefinitionTest()
    {
        m_engine.CreateFunction(new DefinitionFunction
        {
            Name = "Rolled",
            ReturnType = WitDataType.Int32,
            IsDeterministic = false,
            Body = WitSql.ParseExpression("RANDOM()")
        });

        var row = m_engine.Query(
            "SELECT IS_DETERMINISTIC FROM INFORMATION_SCHEMA.ROUTINES").Single();

        Assert.That(Text(row, "IS_DETERMINISTIC"), Is.EqualTo("NO"));
    }

    #endregion

    #region A procedure

    [Test]
    public void AProcedureIsReportedTest()
    {
        m_engine.CreateProcedure(Logged());

        var row = m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.ROUTINES").Single();

        Assert.Multiple(() =>
        {
            Assert.That(Text(row, "ROUTINE_NAME"), Is.EqualTo("Logged"));
            Assert.That(Text(row, "ROUTINE_TYPE"), Is.EqualTo("PROCEDURE"));
            Assert.That(row[Index(row, "DATA_TYPE")].IsNull, Is.True,
                "a procedure has no return type, and NULL is how that is said - an empty string "
                + "would be a value a consumer cannot tell from a type");
        });
    }

    [Test]
    public void BothKindsAppearInTheSameViewTest()
    {
        m_engine.CreateFunction(Doubled());
        m_engine.CreateProcedure(Logged());

        var types = m_engine.Query("SELECT ROUTINE_TYPE FROM INFORMATION_SCHEMA.ROUTINES")
            .Select(row => row[0].AsString())
            .ToArray();

        Assert.That(types, Is.EquivalentTo(new[] { "FUNCTION", "PROCEDURE" }));
    }

    #endregion

    #region Parameters

    [Test]
    public void ParametersAreReportedInDeclarationOrderTest()
    {
        m_engine.CreateFunction(new DefinitionFunction
        {
            Name = "Padded",
            ReturnType = WitDataType.StringVariable,
            IsDeterministic = true,
            // Not named "Text": that is a type keyword, and the lexer takes it as one inside an
            // expression. Worth knowing when the grammar for CREATE FUNCTION is written - a
            // parameter name is an identifier and has to be admitted like any other.
            Parameters =
            [
                new DefinitionRoutineParameter { Name = "Src", Type = WitDataType.StringVariable, MaxLength = 50 },
                new DefinitionRoutineParameter { Name = "Width", Type = WitDataType.Int32 }
            ],
            Body = WitSql.ParseExpression("LPAD(Src, Width, ' ')")
        });

        var rows = m_engine.Query(
            "SELECT ORDINAL_POSITION, PARAMETER_NAME, PARAMETER_MODE, CHARACTER_MAXIMUM_LENGTH "
            + "FROM INFORMATION_SCHEMA.PARAMETERS ORDER BY ORDINAL_POSITION");

        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Count.EqualTo(2));

            Assert.That(rows[0][0].AsInt64(), Is.EqualTo(1), "positions are 1-based, as the standard has them");

            // AsString and equality, not ToString and Does.Contain. ToString renders a value as
            // "Text:Src", so a containment check against "Text" passed while the column held
            // something else entirely - which is what the first version of this assertion did.
            Assert.That(rows[0][1].AsString(), Is.EqualTo("Src"));
            Assert.That(rows[0][2].AsString(), Is.EqualTo("IN"));
            Assert.That(rows[0][3].AsInt64(), Is.EqualTo(50), "the declared length must be reported");

            Assert.That(rows[1][0].AsInt64(), Is.EqualTo(2));
            Assert.That(rows[1][1].AsString(), Is.EqualTo("Width"));
            Assert.That(rows[1][3].IsNull, Is.True, "an unsized type has no length");
        });
    }

    [Test]
    public void ARoutineWithNoParametersReportsNoneTest()
    {
        m_engine.CreateProcedure(Logged());

        Assert.That(m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.PARAMETERS"), Is.Empty);
    }

    /// <summary>
    /// A dropped routine leaves neither view.
    /// </summary>
    /// <remarks>
    /// Its parameters live in the definition rather than in a table of their own, so this cannot
    /// leave orphans - but that is an argument, and this is the check.
    /// </remarks>
    [Test]
    public void ADroppedRoutineDisappearsFromBothViewsTest()
    {
        m_engine.CreateFunction(Doubled());
        m_engine.DropFunction("Doubled");

        Assert.Multiple(() =>
        {
            Assert.That(m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.ROUTINES"), Is.Empty);
            Assert.That(m_engine.Query("SELECT * FROM INFORMATION_SCHEMA.PARAMETERS"), Is.Empty);
        });
    }

    #endregion

    #region Helpers

    private static int Index(WitSqlRow row, string column) =>
        row.ColumnNames.ToList().FindIndex(name => string.Equals(name, column, StringComparison.OrdinalIgnoreCase));

    /// <remarks>
    /// <c>AsString()</c>, not <c>ToString()</c>: the latter renders a <c>WitSqlValue</c> as
    /// <c>Text:Doubled</c>, which compares equal to nothing and would have made every assertion here
    /// fail for a reason that has nothing to do with the subject.
    /// </remarks>
    private static string? Text(WitSqlRow row, string column)
    {
        var index = Index(row, column);
        return index < 0 || row[index].IsNull ? null : row[index].AsString();
    }

    private static DefinitionFunction Doubled() => new()
    {
        Name = "Doubled",
        ReturnType = WitDataType.Int32,
        IsDeterministic = true,
        Parameters = [new DefinitionRoutineParameter { Name = "N", Type = WitDataType.Int32 }],
        Body = WitSql.ParseExpression("N * 2")
    };

    private static DefinitionProcedure Logged() => new()
    {
        Name = "Logged",
        Statements = WitSql.Parse("INSERT INTO Log (Id, Note) VALUES (1, 'x')").ToList()
    };

    #endregion
}
