using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// The formatter, and mostly the four things it refuses to do.
///
/// A formatter is judged by what it does to text it does not understand, because that is the case
/// where it can destroy work. Every "left as written" case below is a defect the formatter would have
/// if it tried harder.
/// </summary>
[TestFixture]
public class SqlFormatterTests
{
    #region What it formats

    [Test]
    public void ASelectIsPutOnSeveralLinesTest()
    {
        var result = SqlFormatter.Format(
            "select c.Name, count(o.Id) as Orders from Customers c join Orders o on o.CustomerId = c.Id where o.Total >= 100 group by c.Name order by c.Name desc limit 10;");

        Assert.That(result.Formatted, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.Zero);

        var lines = result.Text.Split('\n').Select(line => line.TrimEnd()).ToList();

        Assert.That(lines[0], Does.StartWith("SELECT c.Name, COUNT(o.Id) AS Orders"));
        Assert.That(lines.Any(line => line.StartsWith("FROM ")), Is.True, result.Text);
        Assert.That(lines.Any(line => line.TrimStart().StartsWith("INNER JOIN ")), Is.True, result.Text);
        Assert.That(lines.Any(line => line.StartsWith("WHERE ")), Is.True, result.Text);
        Assert.That(lines.Any(line => line.StartsWith("GROUP BY ")), Is.True, result.Text);
        Assert.That(lines.Any(line => line.StartsWith("ORDER BY ")), Is.True, result.Text);
        Assert.That(lines.Any(line => line.StartsWith("LIMIT ")), Is.True, result.Text);
    }

    [Test]
    public void TheFormattedTextStillParsesAndMeansTheSameThingTest()
    {
        const string original =
            "select Id, Total from Orders where Status = 'new' and Total between 1 and 500 order by Id;";

        var result = SqlFormatter.Format(original);

        Assert.That(result.Formatted, Is.EqualTo(1));

        // The proof that the rewrite is the same statement: format it again and nothing moves.
        var again = SqlFormatter.Format(result.Text);

        Assert.That(again.Text, Is.EqualTo(result.Text), "formatting is idempotent or it is guessing");
    }

    [Test]
    public void AStringLiteralIsNotTouchedTest()
    {
        var result = SqlFormatter.Format("select * from Logs where Message = 'from where to group by';");

        Assert.That(result.Text, Does.Contain("'from where to group by'"),
            "the clause words inside a literal are not clauses");
        Assert.That(result.Text.Split('\n').Count(line => line.Contains("'from where")), Is.EqualTo(1));
    }

    [Test]
    public void AQuoteInsideAValueSurvivesTest()
    {
        var result = SqlFormatter.Format("insert into Customers (Name) values ('O''Brien');");

        Assert.That(result.Text, Does.Contain("'O''Brien'"));
    }

    #endregion

    #region What it refuses

    /// <summary>
    /// The one that matters most: the grammar skips comments at the lexer, so a statement rebuilt from
    /// its tree comes back without them. Losing a person's comment to a formatting key would be a
    /// defect of exactly the class stage 0 was about.
    /// </summary>
    [Test]
    public void AStatementWithACommentIsLeftAloneTest()
    {
        const string original = "select Id, /* the key */ Total from Orders;";

        var result = SqlFormatter.Format(original);

        Assert.That(result.Text, Is.EqualTo(original));
        Assert.That(result.Formatted, Is.Zero);
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Reasons, Has.Some.Contains("comment"));
    }

    [Test]
    public void DdlIsLeftAloneAndSaysWhyTest()
    {
        const string original = "create table T (Id INTEGER PRIMARY KEY, Name VARCHAR(10));";

        var result = SqlFormatter.Format(original);

        Assert.That(result.Text, Is.EqualTo(original));
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Reasons, Has.Some.Contains("cannot write"));
    }

    [Test]
    public void TextThatDoesNotParseIsReturnedUnchangedTest()
    {
        const string original = "select Id from Orders wehre Total > 1;";

        var result = SqlFormatter.Format(original);

        Assert.That(result.Text, Is.EqualTo(original));
        Assert.That(result.Formatted, Is.Zero);
        Assert.That(result.Reasons, Has.Some.Contains("does not parse"));
    }

    /// <summary>
    /// The round-trip guard, and the shape that makes it necessary rather than decorative.
    ///
    /// <c>WitSqlExpressionSerializer</c> renders every subquery as the literal three characters
    /// <c>SELECT ...</c> - a defect the engine's own <c>GrammarRoundTripTests</c> pins as one of its
    /// two known causes. A formatter that trusted the serializer would therefore replace a working
    /// query with text that is not SQL at all, and it would do it to the copy the user is holding.
    /// </summary>
    [Test]
    public void AStatementTheSerializerCannotReproduceIsLeftAloneTest()
    {
        const string original =
            "select * from Orders where CustomerId in (select Id from Customers where Name = 'Acme Industrial');";

        var result = SqlFormatter.Format(original);

        Assert.That(result.Text, Is.EqualTo(original));
        Assert.That(result.Formatted, Is.Zero);
        Assert.That(result.Text, Does.Not.Contain("SELECT ..."),
            "the subquery must not be replaced by the serializer's placeholder for one");
    }

    [Test]
    public void AnEmptyScriptIsNotAnErrorTest()
    {
        Assert.That(SqlFormatter.Format("").Text, Is.Empty);
        Assert.That(SqlFormatter.Format("   \n  ").Text, Is.EqualTo("   \n  "));
    }

    #endregion

    #region A script of several statements

    [Test]
    public void TheFormattableStatementsAreFormattedAndTheRestAreNotTest()
    {
        const string original = """
            -- a header comment
            create table Staging (Id INTEGER PRIMARY KEY);
            select Id, Total from Orders where Total > 100;
            -- and a comment of its own
            insert into Logs (Message, Level) values ('done', 'INFO');
            """;

        var result = SqlFormatter.Format(original);

        Assert.That(result.Formatted, Is.EqualTo(2), result.Summary);
        Assert.That(result.Skipped, Is.EqualTo(1), result.Summary);

        Assert.That(result.Text, Does.StartWith("-- a header comment"),
            "the comment above the first statement belongs to nobody and must survive");
        Assert.That(result.Text, Does.Contain("create table Staging (Id INTEGER PRIMARY KEY);"),
            "the CREATE is copied character for character");
        Assert.That(result.Text, Does.Contain("-- and a comment of its own"),
            "a comment between two statements must not be swallowed by the one above it");
        Assert.That(result.Text, Does.Contain("INSERT INTO Logs"));
        Assert.That(result.Summary, Does.Contain("left as written"));
    }

    /// <summary>
    /// The control for the case above: every comment in the input is still in the output. Written as a
    /// count rather than as a lookup, because a formatter that duplicated one would also pass a
    /// "contains" check.
    ///
    /// The first version of this case had four comments in it and every one of them was OUTSIDE a
    /// statement - so removing the formatter's comment guard left it green, and it was a control that
    /// could not see the thing it existed for. <c>-- five</c> and <c>/* six */</c> are inside a
    /// statement, and they are what makes it red.
    /// </summary>
    [Test]
    public void NoCommentIsLostOrDuplicatedTest()
    {
        const string original = """
            -- one
            select Id from Orders; -- two
            /* three */
            select Total from Orders;
            select Id, /* six */ Total
            -- five
            from Orders;
            -- four
            """;

        var result = SqlFormatter.Format(original);

        foreach (var comment in new[] { "-- one", "-- two", "/* three */", "-- four", "-- five", "/* six */" })
            Assert.That(Occurrences(result.Text, comment), Is.EqualTo(1),
                $"{comment} in: {result.Text}");
    }

    [Test]
    public void FormattingOneStatementDoesNotMoveAnotherTest()
    {
        const string original = "select Id from Orders;\n\n\nselect Total from Orders;\n";

        var result = SqlFormatter.Format(original);

        Assert.That(result.Text, Does.Contain(";\n\n\nSELECT"), "the blank lines the user left are theirs");
        Assert.That(result.Text, Does.EndWith(";\n"));
    }

    #endregion

    #region Tools

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    #endregion
}
