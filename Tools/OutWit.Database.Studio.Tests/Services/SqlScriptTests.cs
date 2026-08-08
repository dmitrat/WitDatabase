using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Cutting a script into statements, and putting an error back where the user wrote it (WS-22).
///
/// None of this needs a database: it is about text and coordinates. What it does need is the parser,
/// and that is the point - Studio used to answer these questions with hand-written scanning that had
/// to know, for itself, that a semicolon can live inside a string and that a keyword can hide behind
/// a block comment.
/// </summary>
[TestFixture]
public class SqlScriptTests
{
    #region Splitting

    /// <summary>
    /// The shapes a splitter gets wrong: a semicolon inside a string, a statement over several lines,
    /// comments between and before statements.
    /// </summary>
    [Test]
    public void AScriptIsCutWhereTheStatementsActuallyStartTest()
    {
        var script = string.Join("\n",
        [
            "-- a leading comment",                                          // 1
            "CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name VARCHAR(50));",// 2
            "",                                                              // 3
            "INSERT INTO Probe (Id, Name) VALUES (1, 'has a ; semicolon');", // 4
            "INSERT INTO Probe (Id, Name)",                                  // 5
            "    VALUES (2, 'two",                                           // 6
            "lines');",                                                      // 7
            "/* block",                                                      // 8
            "   comment */",                                                 // 9
            "SELECT * FROM Probe WHERE Name LIKE '%;%';",                    // 10
            "UPDATE Probe SET Name = 'x' WHERE Id = 1"                       // 11
        ]);

        var split = SqlScript.Split(script);

        Assert.Multiple(() =>
        {
            Assert.That(split.IsSuccess, Is.True, "the script parses");
            Assert.That(split.Statements, Has.Count.EqualTo(5),
                "five statements: the semicolons inside the strings are not cuts");

            Assert.That(split.Statements.Select(s => s.Line), Is.EqualTo(new[] { 2, 4, 5, 10, 11 }).AsCollection,
                "each statement starts on the line it was written on");

            Assert.That(split.Statements[0].Text, Does.StartWith("CREATE TABLE"),
                "the comment above a statement is not part of it");
            Assert.That(split.Statements[2].Text, Does.Contain("lines');"),
                "a statement that spans lines keeps all of them");
            Assert.That(split.Statements[3].Text, Does.StartWith("SELECT"),
                "and the block comment before it is not part of it either");
        });
    }

    /// <summary>
    /// CONTROL for the case above: without it, "five statements" would pass for a splitter that cut
    /// on every semicolon and happened to produce five pieces out of a different script.
    /// </summary>
    [Test]
    public void ControlASemicolonInsideAStringIsNotACutTest()
    {
        var split = SqlScript.Split("INSERT INTO T (V) VALUES ('a;b;c')");

        Assert.Multiple(() =>
        {
            Assert.That(split.Statements, Has.Count.EqualTo(1));
            Assert.That(split.Statements[0].Text, Does.Contain("'a;b;c'"), "the value arrives whole");
        });
    }

    [Test]
    public void AnEmptyScriptHasNoStatementsAndNoErrorsTest()
    {
        var split = SqlScript.Split("   \n\t\n");

        Assert.Multiple(() =>
        {
            Assert.That(split.Statements, Is.Empty);
            Assert.That(split.Errors, Is.Empty, "nothing to run is not a failure");
        });
    }

    /// <summary>
    /// The statements each go to the engine on their own, so each has to be executable on its own.
    /// </summary>
    [Test]
    public void EveryPieceParsesOnItsOwnTest()
    {
        var script = "CREATE TABLE T (Id INTEGER PRIMARY KEY);\nINSERT INTO T (Id) VALUES (1);\nSELECT * FROM T";

        var split = SqlScript.Split(script);

        Assert.Multiple(() =>
        {
            foreach (var statement in split.Statements)
            {
                var alone = SqlScript.Split(statement.Text);

                Assert.That(alone.IsSuccess, Is.True, $"statement {statement.Number()} does not parse alone");
                Assert.That(alone.Statements, Has.Count.EqualTo(1), "and is exactly one statement");
            }
        });
    }

    /// <summary>
    /// A compound <c>BEGIN … END</c> body stays inside its statement, with a statement either side of
    /// it as the control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written to ANSWER A QUESTION rather than to guard a behaviour, and the answer changed where the
    /// repair went. The dump could not rebuild a trigger, and there were two candidates: the definition
    /// might arrive incomplete, or the splitter might cut the body loose at the semicolons inside it.
    /// Measured 2026-08-08: the splitter keeps all of it - three statements here, no errors - and the
    /// engine accepts the middle one. The defect was entirely in what the catalogue handed over, which
    /// is where it was fixed.
    /// </para>
    /// <para>
    /// It is kept because the question will be asked again the next time a trigger comes back wrong,
    /// and because the splitter genuinely could regress here: the semicolons inside the body are the
    /// exact thing a hand-written splitter gets wrong.
    /// </para>
    /// </remarks>
    [Test]
    public void ATriggerBodyIsNotCutLooseFromItsTriggerTest()
    {
        const string script = """
                              CREATE TABLE T (Id INTEGER PRIMARY KEY);
                              CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW
                              BEGIN
                                  INSERT INTO T (Id) VALUES (NEW.Id);
                                  INSERT INTO T (Id) VALUES (NEW.Id + 1);
                              END;
                              SELECT 1;
                              """;

        var split = SqlScript.Split(script);

        Assert.Multiple(() =>
        {
            Assert.That(split.Errors, Is.Empty);
            Assert.That(split.Statements, Has.Count.EqualTo(3),
                "the two semicolons inside the body must not start statements of their own");

            var trigger = split.Statements[1];

            Assert.That(trigger.Text, Does.StartWith("CREATE TRIGGER"));
            Assert.That(trigger.Text, Does.Contain("END"), "the body travels with the trigger");
            Assert.That(trigger.Text.Split("INSERT INTO T"), Has.Length.EqualTo(3),
                "both of the body's statements are in it");
        });
    }

    #endregion

    #region What changes the schema

    /// <summary>
    /// Studio reloads the tree after a statement that changes the schema. The question used to be
    /// answered by looking for a leading keyword, with its own comment-skipping - which is why the
    /// cases with comments in front are here. It is now the parsed statement's type that answers.
    /// </summary>
    [TestCase("CREATE TABLE t (id INT)", true)]
    [TestCase("DROP TABLE t", true)]
    [TestCase("ALTER TABLE t ADD COLUMN x INT", true)]
    [TestCase("TRUNCATE TABLE t", true)]
    [TestCase("CREATE INDEX ix ON t (id)", true)]
    [TestCase("CREATE VIEW v AS SELECT * FROM t", true)]
    [TestCase("SELECT * FROM t", false)]
    [TestCase("INSERT INTO t VALUES (1)", false)]
    [TestCase("UPDATE t SET x = 1", false)]
    [TestCase("DELETE FROM t", false)]
    [TestCase("-- comment\nCREATE TABLE t (id INT)", true)]
    [TestCase("/* block comment */\nDROP TABLE t", true)]
    [TestCase("/* multi\nline\ncomment */CREATE TABLE t (id INT)", true)]
    [TestCase("  \n  \t  create table t (id INT)", true)]
    public void SchemaChangesAreRecognisedByTypeNotByKeywordTest(string sql, bool expected)
    {
        var split = SqlScript.Split(sql);

        Assert.That(split.IsSuccess, Is.True, $"the case has to parse to mean anything: {sql}");
        Assert.That(split.Statements[0].ChangesSchema, Is.EqualTo(expected), sql);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("-- only a comment")]
    public void TextWithNoStatementInItChangesNothingTest(string sql)
    {
        var split = SqlScript.Split(sql);

        Assert.That(split.Statements, Is.Empty);
    }

    #endregion

    #region Coordinates

    /// <summary>
    /// The readiness case of the stage, at the level of text: an error in the sixth statement is
    /// reported on the sixth line.
    /// </summary>
    [Test]
    public void AParseErrorIsReportedWhereItIsTest()
    {
        var script = string.Join("\n",
        [
            "SELECT 1;",
            "SELECT 2;",
            "SELECT 3;",
            "SELECT 4;",
            "SELECT 5;",
            "SELECT FROM WHERE;",
            "SELECT 7;"
        ]);

        var split = SqlScript.Split(script);

        Assert.Multiple(() =>
        {
            Assert.That(split.IsSuccess, Is.False);
            Assert.That(split.Errors[0].Line, Is.EqualTo(6), "the line the mistake is on");
            Assert.That(split.Errors[0].Message, Does.Contain("mismatched input"));
            Assert.That(split.Errors[0].Message, Does.Not.Contain("expecting {"),
                "the expected-token set belongs in the details, not in front of the user");
            Assert.That(split.Errors[0].Detail, Does.Contain("expecting {"),
                "but it is not thrown away either");
        });
    }

    /// <summary>
    /// The engine counts from the start of the text it was given. A statement sent on its own always
    /// reports line 1, and this is what turns that back into the line of the tab.
    /// </summary>
    [Test]
    public void APositionInsideAStatementBecomesAPositionInTheScriptTest()
    {
        // A statement that starts on line 6, indented four spaces.
        var statement = new SqlStatementSpan(5, "SELECT\n  FROM WHERE", 6, 4, false);

        var onFirstLine = SqlScript.ToScriptPosition(statement, 1, 7);
        var onLaterLine = SqlScript.ToScriptPosition(statement, 2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(onFirstLine, Is.EqualTo((6, 11)),
                "the first line of the statement starts at the statement's own column");
            Assert.That(onLaterLine, Is.EqualTo((7, 2)),
                "every line after it starts at the beginning of its own line");
        });
    }

    [Test]
    public void AnEngineMessageWithAPositionIsMovedIntoTheTabTest()
    {
        var statement = new SqlStatementSpan(5, "SELECT FROM WHERE", 6, 0, false);

        var error = SqlScript.ErrorFor(statement,
            "Line 1:7 - mismatched input 'FROM' expecting {ALL, CASE, CAST, DISTINCT}");

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Line, Is.EqualTo(6), "line 1 of the sixth statement is line 6 of the tab");
            Assert.That(error.Column, Is.EqualTo(7));
            Assert.That(error.Message, Is.EqualTo("mismatched input 'FROM'"));
        });
    }

    /// <summary>
    /// Not every failure is about a place in the text - a constraint violation is about a row. Saying
    /// "line 1" about one of those would send the user looking at the wrong thing.
    /// </summary>
    [Test]
    public void AMessageWithNoPositionGetsNoneInventedTest()
    {
        var statement = new SqlStatementSpan(0, "INSERT INTO T (Id) VALUES (1)", 1, 0, false);

        Assert.Multiple(() =>
        {
            Assert.That(SqlScript.ErrorFor(statement, "UNIQUE constraint failed: T.Id"), Is.Null);
            Assert.That(SqlScript.ErrorFor(statement, null), Is.Null);
            Assert.That(SqlScript.ErrorFor(statement, "  "), Is.Null);
        });
    }

    [Test]
    public void TheExpectedTokenSetIsCutOffTheFrontOfAMessageTest()
    {
        const string LONG = "mismatched input 'FROM' expecting {ALL, CASE, CAST, DISTINCT, EXISTS, FALSE}";

        Assert.Multiple(() =>
        {
            Assert.That(SqlScript.Shorten(LONG), Is.EqualTo("mismatched input 'FROM'"));
            Assert.That(SqlScript.Shorten("UNIQUE constraint failed: T.Id"),
                Is.EqualTo("UNIQUE constraint failed: T.Id"),
                "a message with nothing to cut is left alone");
        });
    }

    #endregion
}

/// <summary>
/// Small helper so the assertion messages above can name a statement the way a person counts.
/// </summary>
internal static class SqlStatementSpanExtensions
{
    public static int Number(this SqlStatementSpan span) => span.Index + 1;
}
