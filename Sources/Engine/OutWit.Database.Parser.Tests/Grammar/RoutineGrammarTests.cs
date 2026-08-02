using OutWit.Database.Parser;
using OutWit.Database.Parser.Exceptions;
using OutWit.Database.Parser.Serializers;
using OutWit.Database.Parser.Statements;

namespace OutWit.Database.Parser.Tests.Grammar;

/// <summary>
/// The grammar for functions, procedures and <c>CALL</c>.
/// </summary>
/// <remarks>
/// <para>
/// The spellings are not chosen here. They are fixed by the dialect oracle's corpus, which measured
/// what PostgreSQL 17 and SQL Server 2022 accept and recorded the shape WitDatabase must take:
/// </para>
/// <code>
/// CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END
/// CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T; END
/// </code>
/// <para>
/// So the first two tests here are the corpus entries themselves, and their passing is what will
/// invert <c>UnbuiltCapabilityCorpusTests</c> - which pins both as absent and is written to fail the
/// moment they are not.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RoutineGrammarTests
{
    #region The shapes the oracle pinned

    [Test]
    public void TheOraclesFunctionSpellingParsesTest()
    {
        var statement = Single("CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END");

        Assert.That(statement, Is.InstanceOf<WitSqlStatementCreateFunction>());

        var create = (WitSqlStatementCreateFunction)statement;

        Assert.Multiple(() =>
        {
            Assert.That(create.FunctionName, Is.EqualTo("Doubled"));
            Assert.That(create.Parameters, Has.Count.EqualTo(1));
            Assert.That(create.Parameters![0].Name, Is.EqualTo("N"));
            Assert.That(create.ReturnType, Is.Not.Null);
            Assert.That(create.Body, Is.Not.Null, "the body is the expression after RETURN");
        });
    }

    [Test]
    public void TheOraclesProcedureSpellingParsesTest()
    {
        var statement = Single("CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T; END");

        Assert.That(statement, Is.InstanceOf<WitSqlStatementCreateProcedure>());

        var create = (WitSqlStatementCreateProcedure)statement;

        Assert.Multiple(() =>
        {
            Assert.That(create.ProcedureName, Is.EqualTo("GetAll"));
            Assert.That(create.Parameters, Is.Null, "a procedure may be declared with no parameter list at all");
            Assert.That(create.Body, Has.Count.EqualTo(1));
        });
    }

    #endregion

    #region Shapes

    [Test]
    public void AFunctionWithSeveralParametersParsesTest()
    {
        var create = (WitSqlStatementCreateFunction)Single(
            "CREATE FUNCTION Area(W INT, H INT) RETURNS INT AS BEGIN RETURN W * H; END");

        Assert.That(create.Parameters, Has.Count.EqualTo(2));
        Assert.That(create.Parameters![1].Name, Is.EqualTo("H"));
    }

    [Test]
    public void AFunctionWithNoParametersParsesTest()
    {
        var create = (WitSqlStatementCreateFunction)Single(
            "CREATE FUNCTION Answer() RETURNS INT AS BEGIN RETURN 42; END");

        Assert.That(create.Parameters, Is.Null,
            "no parameters and no parameter list must mean the same thing downstream");
    }

    [Test]
    public void AProcedureWithParametersAndSeveralStatementsParsesTest()
    {
        var create = (WitSqlStatementCreateProcedure)Single(
            "CREATE PROCEDURE Log2(Note VARCHAR(50)) AS BEGIN "
            + "INSERT INTO L (Note) VALUES (Note); "
            + "UPDATE L SET Seen = 1; END");

        Assert.Multiple(() =>
        {
            Assert.That(create.Parameters, Has.Count.EqualTo(1));
            Assert.That(create.Body, Has.Count.EqualTo(2));
            Assert.That(create.Body[0], Is.InstanceOf<WitSqlStatementInsert>());
            Assert.That(create.Body[1], Is.InstanceOf<WitSqlStatementUpdate>());
        });
    }

    [TestCase("CREATE FUNCTION F() RETURNS INT LANGUAGE SQL AS BEGIN RETURN 1; END", "SQL")]
    [TestCase("CREATE FUNCTION F() RETURNS INT LANGUAGE plpgsql AS BEGIN RETURN 1; END", "plpgsql")]
    public void TheLanguageClauseIsCarriedAsWrittenTest(string sql, string expected)
    {
        var create = (WitSqlStatementCreateFunction)Single(sql);

        Assert.That(create.Language, Is.EqualTo(expected).IgnoreCase,
            "the grammar admits any language so the executor can refuse a foreign one with a "
            + "sentence, rather than the parser refusing it with a token position");
    }

    [Test]
    public void IfNotExistsAndIfExistsParseTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(((WitSqlStatementCreateFunction)Single(
                "CREATE FUNCTION IF NOT EXISTS F() RETURNS INT AS BEGIN RETURN 1; END")).IfNotExists, Is.True);
            Assert.That(((WitSqlStatementDropFunction)Single("DROP FUNCTION IF EXISTS F")).IfExists, Is.True);
            Assert.That(((WitSqlStatementDropProcedure)Single("DROP PROCEDURE IF EXISTS P")).IfExists, Is.True);
            Assert.That(((WitSqlStatementDropFunction)Single("DROP FUNCTION F")).IfExists, Is.False);
        });
    }

    [Test]
    public void DropStatementsParseTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(((WitSqlStatementDropFunction)Single("DROP FUNCTION Doubled")).FunctionName,
                Is.EqualTo("Doubled"));
            Assert.That(((WitSqlStatementDropProcedure)Single("DROP PROCEDURE GetAll")).ProcedureName,
                Is.EqualTo("GetAll"));
        });
    }

    #endregion

    #region CALL

    [Test]
    public void CallWithNoArgumentsParsesTest()
    {
        var call = (WitSqlStatementCall)Single("CALL GetAll()");

        Assert.Multiple(() =>
        {
            Assert.That(call.ProcedureName, Is.EqualTo("GetAll"));
            Assert.That(call.Arguments, Is.Null);
        });
    }

    [Test]
    public void CallWithArgumentsParsesTest()
    {
        var call = (WitSqlStatementCall)Single("CALL Log2('hello', 1 + 2)");

        Assert.That(call.Arguments, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// A <c>CALL</c> renders, unlike its DDL neighbours, because a procedure body can contain one.
    /// </summary>
    /// <remarks>
    /// <c>INFORMATION_SCHEMA.ROUTINES.ROUTINE_DEFINITION</c> reports a body, and a body holding a
    /// call that rendered to nothing would report as a body that does not have it. Phase 8's lesson:
    /// a description that quietly drops part of what it describes is worse than no description.
    /// </remarks>
    [Test]
    public void ACallRendersBackToSqlTest()
    {
        var rendered = SchemaText.Render(new[] { Single("CALL Log2('hello')") });

        Assert.That(rendered, Does.Contain("CALL").And.Contain("Log2"));
    }

    #endregion

    #region The keywords stay usable as names

    /// <summary>
    /// Six new tokens, and none of them may take a name away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured before the grammar changed: all of these worked as column names. Adding <c>TOP</c>
    /// in phase 9b took <c>Top</c> away and the keyword corpus is what caught it - that corpus asks
    /// the question of the whole lexer vocabulary and covers these automatically. This fixture asks
    /// it again in the shapes a consumer actually writes, because a name is used as more than a
    /// column declaration.
    /// </para>
    /// </remarks>
    [TestCase("Function")]
    [TestCase("Procedure")]
    [TestCase("Call")]
    [TestCase("Language")]
    [TestCase("Returns")]
    [TestCase("Return")]
    public void ARoutineKeywordIsStillUsableAsAnIdentifierTest(string name)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => WitSql.Parse($"CREATE TABLE T ({name} INT)"), Throws.Nothing,
                "as a column name");
            Assert.That(() => WitSql.Parse($"SELECT {name} FROM T"), Throws.Nothing,
                "as a column reference");
            Assert.That(() => WitSql.Parse($"SELECT * FROM {name}"), Throws.Nothing,
                "as a table name");
            Assert.That(() => WitSql.Parse($"SELECT Id AS {name} FROM T"), Throws.Nothing,
                "as an alias");
        });
    }

    /// <summary>
    /// And a routine may be named after one of them.
    /// </summary>
    [Test]
    public void ARoutineMayBeNamedAfterAKeywordTest()
    {
        Assert.That(((WitSqlStatementDropFunction)Single("DROP FUNCTION Language")).FunctionName,
            Is.EqualTo("Language"));
    }

    #endregion

    #region Refused

    /// <summary>
    /// A function body is one expression, so a statement in it is a parse error.
    /// </summary>
    /// <remarks>
    /// This is the § 1 decision showing through the grammar rather than being enforced later: there
    /// is no rule that would admit a statement there, so the refusal costs nothing to maintain.
    /// </remarks>
    [TestCase("CREATE FUNCTION F() RETURNS INT AS BEGIN SELECT 1; END", TestName = "a SELECT in a function body")]
    [TestCase("CREATE FUNCTION F() RETURNS INT AS BEGIN RETURN 1; RETURN 2; END", TestName = "two RETURNs")]
    [TestCase("CREATE FUNCTION F() RETURNS INT AS $$ SELECT 1 $$", TestName = "a dollar-quoted body")]
    [TestCase("CREATE FUNCTION F RETURNS INT AS BEGIN RETURN 1; END", TestName = "no parameter list")]
    public void AFunctionBodyThatIsNotOneExpressionIsRefusedTest(string sql)
    {
        Assert.That(() => WitSql.Parse(sql), Throws.InstanceOf<WitSqlParsingException>());
    }

    #endregion

    #region Helpers

    private static WitSqlStatement Single(string sql)
    {
        var statements = WitSql.Parse(sql);

        Assert.That(statements, Has.Count.EqualTo(1), $"expected one statement from: {sql}");

        return statements[0];
    }

    #endregion
}
