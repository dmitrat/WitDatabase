using NUnit.Framework;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// Finding and replacing in the editor's text (9.7), asked of the text rather than of a window.
/// </summary>
/// <remarks>
/// The band is a strip of controls; what it CLAIMS - "2 of 5", which one is current, what "only in the
/// selection" covers, what Replace All writes - is all a function of the text, and that is what is
/// measured here. The ViewModel cases in <c>EditorSearchTests</c> then only have to show that the band
/// asks these questions at the right moments.
/// </remarks>
[TestFixture]
public class SqlSearchTests
{
    #region Constants

    private const string SCRIPT = """
                                  SELECT Id, Total, Status
                                  FROM Orders
                                  WHERE Status = 'new'
                                    AND Total > 100;
                                  """;

    #endregion

    #region Finding

    [Test]
    public void EveryOccurrenceIsFoundInOrderTest()
    {
        var found = SqlSearch.Find(SCRIPT, "Status").Matches;

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(2));
            Assert.That(found[0].Offset, Is.LessThan(found[1].Offset), "in the order they appear");

            foreach (var match in found)
                Assert.That(SCRIPT.Substring(match.Offset, match.Length), Is.EqualTo("Status"),
                    "an offset that does not point at the term is worse than no match at all");
        });
    }

    /// <summary>
    /// The default is case-INSENSITIVE, and <c>Aa</c> turns that off.
    /// </summary>
    /// <remarks>
    /// Measured against the engine's own habits in stage 7: <c>=</c> is case-sensitive and <c>LIKE</c>
    /// is not. This is a search over TEXT and has nothing to do with either - which is exactly why it
    /// gets its own case, so nobody later "makes it consistent" with the filter row.
    /// </remarks>
    [Test]
    public void CaseIsIgnoredUntilItIsAskedForTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.Find(SCRIPT, "status").Matches, Has.Count.EqualTo(2));
            Assert.That(SqlSearch.Find(SCRIPT, "status", new SearchOptions(MatchCase: true)).Matches,
                Is.Empty);
        });
    }

    [Test]
    public void AWholeWordIsAWholeWordTest()
    {
        const string text = "Total, Totals, SubTotal, Total";

        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.Find(text, "Total").Matches, Has.Count.EqualTo(4),
                "without the toggle, Totals and SubTotal contain it");
            Assert.That(SqlSearch.Find(text, "Total", new SearchOptions(WholeWord: true)).Matches,
                Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// A term with regex characters in it is TEXT until the <c>.*</c> toggle says otherwise.
    /// </summary>
    /// <remarks>
    /// This is the case that keeps the band usable for SQL, which is full of <c>(</c>, <c>*</c> and
    /// <c>.</c>. Without the escaping, searching for <c>COUNT(*)</c> is a pattern error rather than a
    /// search.
    /// </remarks>
    [Test]
    public void APlainTermIsNotAPatternTest()
    {
        const string text = "SELECT COUNT(*) FROM T WHERE X = 'a.b'";

        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.Find(text, "COUNT(*)").Matches, Has.Count.EqualTo(1));
            Assert.That(SqlSearch.Find(text, "a.b").Matches, Has.Count.EqualTo(1),
                "the dot is a dot");

            var asPattern = SqlSearch.Find(text, "a.b", new SearchOptions(UseRegex: true)).Matches;

            Assert.That(asPattern, Has.Count.EqualTo(1), "and as a pattern it matches a.b too");
            Assert.That(text.Substring(asPattern[0].Offset, asPattern[0].Length), Is.EqualTo("a.b"));
        });
    }

    /// <summary>
    /// A half-typed pattern is answered, not thrown - and not reported as "no matches".
    /// </summary>
    [Test]
    public void AnUnfinishedPatternIsAnAnswerTest()
    {
        var outcome = SqlSearch.Find(SCRIPT, "Stat(", new SearchOptions(UseRegex: true));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Matches, Is.Empty);
            Assert.That(outcome.IsPattern, Is.False);
            Assert.That(outcome.PatternError, Is.Not.Null.And.Not.Empty,
                "the band has to be able to say what is wrong with it");
        });
    }

    /// <summary>
    /// "Only in the selection" is about where it LOOKS, and a term hanging out of the far edge of the
    /// selection is not a match.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The far edge is the whole point, and the first version of this case did not test it.</b> It
    /// used a selection whose START cut through an occurrence, and measured nothing: the obvious wrong
    /// implementation - find everywhere, then keep the matches whose offset is inside the range -
    /// throws that one away as well, so the case passed against both. Found by writing that
    /// implementation and watching this stay green.
    /// </para>
    /// <para>
    /// A match that BEGINS inside the selection and ends past it is what tells them apart: searching
    /// the substring cannot see it, filtering by offset keeps it.
    /// </para>
    /// </remarks>
    [Test]
    public void OnlyInTheSelectionLooksOnlyThereTest()
    {
        const string text = "one Status two";

        var status = text.IndexOf("Status", StringComparison.Ordinal);

        // The selection ends in the MIDDLE of the occurrence: "one Sta".
        var cutShort = new SearchOptions(RangeStart: 0, RangeLength: status + 3);

        // And the whole of it, as the control.
        var whole = new SearchOptions(RangeStart: 0, RangeLength: text.Length);

        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.Find(text, "Status", cutShort).Matches, Is.Empty,
                "half of the word is inside the selection, and half a word is not a match");
            Assert.That(SqlSearch.Find(text, "Status", whole).Matches, Has.Count.EqualTo(1),
                "CONTROL: the same term IS found when the selection covers it");
        });
    }

    /// <summary>
    /// And the near edge: an occurrence that starts before the selection is not in it either.
    /// </summary>
    [Test]
    public void AnOccurrenceStartingBeforeTheSelectionIsNotInItTest()
    {
        const string text = "Status one Status";

        var second = text.LastIndexOf("Status", StringComparison.Ordinal);

        var options = new SearchOptions(RangeStart: second + 2, RangeLength: text.Length - second - 2);

        Assert.That(SqlSearch.Find(text, "Status", options).Matches, Is.Empty);
    }

    [Test]
    public void NothingToLookForFindsNothingTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.Find(SCRIPT, "").Matches, Is.Empty);
            Assert.That(SqlSearch.Find(SCRIPT, null).Matches, Is.Empty);
            Assert.That(SqlSearch.Find("", "Status").Matches, Is.Empty);
            Assert.That(SqlSearch.Find(SCRIPT, "").IsPattern, Is.True, "and it is not an error either");
        });
    }

    #endregion

    #region Which one is current

    [Test]
    public void TheMatchAtOrAfterTheCaretIsTheCurrentOneTest()
    {
        var found = SqlSearch.Find(SCRIPT, "Status").Matches;

        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.IndexAtOrAfter(found, 0), Is.EqualTo(0));
            Assert.That(SqlSearch.IndexAtOrAfter(found, found[0].Offset), Is.EqualTo(0),
                "a caret sitting exactly on a match means that match");
            Assert.That(SqlSearch.IndexAtOrAfter(found, found[0].Offset + 1), Is.EqualTo(1));
            Assert.That(SqlSearch.IndexAtOrAfter(found, SCRIPT.Length), Is.EqualTo(0),
                "past the last one it wraps, the way every editor does");
            Assert.That(SqlSearch.IndexAtOrAfter([], 0), Is.EqualTo(-1));
        });
    }

    #endregion

    #region Replacing

    [Test]
    public void ReplacingOneChangesOneTest()
    {
        var found = SqlSearch.Find(SCRIPT, "Status").Matches;

        var written = SqlSearch.ReplaceOne(SCRIPT, found[1], "State", new SearchOptions());

        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.Find(written, "Status").Matches, Has.Count.EqualTo(1),
                "the first one is untouched");
            Assert.That(SqlSearch.Find(written, "State").Matches, Has.Count.EqualTo(1));
        });
    }

    /// <summary>
    /// Replace All with a replacement of a DIFFERENT length, which is where the offsets go wrong.
    /// </summary>
    /// <remarks>
    /// The replacement here is longer than the term on purpose. Walked forwards, every replacement
    /// after the first lands at an offset that has moved - the classic way a replace-all corrupts the
    /// tail of a file, and a same-length replacement cannot see it.
    /// </remarks>
    [Test]
    public void ReplaceAllSurvivesADifferentLengthTest()
    {
        const string text = "a X b X c X d";

        var (written, count) = SqlSearch.ReplaceAll(text, "X", "LONGER");

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(written, Is.EqualTo("a LONGER b LONGER c LONGER d"),
                "every letter around the replacements has to be where it was");
        });
    }

    [Test]
    public void ReplaceAllHonoursTheTogglesTest()
    {
        const string text = "Total, Totals, total";

        Assert.Multiple(() =>
        {
            Assert.That(SqlSearch.ReplaceAll(text, "Total", "T").Count, Is.EqualTo(3));
            Assert.That(SqlSearch.ReplaceAll(text, "Total", "T", new SearchOptions(MatchCase: true)).Count,
                Is.EqualTo(2));
            Assert.That(SqlSearch.ReplaceAll(text, "Total", "T", new SearchOptions(WholeWord: true)).Count,
                Is.EqualTo(2), "Total and total, but not Totals");
        });
    }

    /// <summary>
    /// In pattern mode the replacement can use what the pattern captured; in ordinary mode it cannot.
    /// </summary>
    [Test]
    public void ASubstitutionIsOnlyASubstitutionInPatternModeTest()
    {
        const string text = "Total = 100";

        var pattern = SqlSearch.ReplaceAll(text, @"(\w+) = (\d+)", "$2 = $1", new SearchOptions(UseRegex: true));
        var literal = SqlSearch.ReplaceAll(text, "Total", "$1");

        Assert.Multiple(() =>
        {
            Assert.That(pattern.Text, Is.EqualTo("100 = Total"));
            Assert.That(literal.Text, Is.EqualTo("$1 = 100"),
                "a person replacing a word with $1 means those two characters");
        });
    }

    [Test]
    public void ReplaceAllInTheSelectionLeavesTheRestAloneTest()
    {
        const string text = "X and X and X";

        var (written, count) = SqlSearch.ReplaceAll(text, "X", "Y", new SearchOptions(RangeStart: 6, RangeLength: 7));

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(written, Is.EqualTo("X and Y and Y"));
        });
    }

    [Test]
    public void ReplacingWithNothingDeletesTheMatchTest()
    {
        var (written, count) = SqlSearch.ReplaceAll("a X b", "X", null);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(written, Is.EqualTo("a  b"));
        });
    }

    #endregion
}
