using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services.Localization;
using OutWit.Database.Studio.Ui.Icons;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The measured remainder of the phase-17 audit: four small things, each wrong on screen.
/// </summary>
/// <remarks>
/// They are together because they share a shape rather than a subject - every one of them was a
/// plausible-looking default nobody had a reason to look at twice. A node type that falls through a
/// switch, a count in the nominative where the sentence wants another case, a status line nobody
/// clears, and two labels with only whitespace between them.
/// </remarks>
[TestFixture]
public class SmallSignsTests
{
    #region The tree draws what a node IS

    /// <summary>
    /// Routines were added to the tree and never added to the icon converter, so the folder and
    /// every routine under it fell through to the database glyph.
    /// </summary>
    /// <remarks>
    /// Asserted over the WHOLE enum rather than over the two values that were missing. A rule naming
    /// only what was wrong is satisfied the moment it is fixed and says nothing about the next node
    /// type somebody adds - which is exactly how these two got here.
    /// </remarks>
    [Test]
    public void EveryNodeTypeHasAnIconOfItsOwnTest()
    {
        var converter = new NodeTypeToIconConverter();

        var fallenThrough = Enum.GetValues<DatabaseNodeType>()
            .Where(type => type != DatabaseNodeType.Database)
            .Where(type => Icon(converter, type) == StudioIcons.PATH_DB_DATABASE)
            .ToList();

        Assert.That(fallenThrough, Is.Empty,
            "only a database node may draw the database glyph; these fell through the switch: "
            + string.Join(", ", fallenThrough));
    }

    [Test]
    public void TheRoutinesFolderAndItsRoutinesAreDrawnAsSuchTest()
    {
        var converter = new NodeTypeToIconConverter();

        Assert.Multiple(() =>
        {
            Assert.That(Icon(converter, DatabaseNodeType.RoutinesFolder), Is.EqualTo(StudioIcons.PATH_COMMON_FOLDER),
                "a folder is drawn as a folder, like every other folder in this tree");
            Assert.That(Icon(converter, DatabaseNodeType.Routine), Is.EqualTo(StudioIcons.PATH_DB_ROUTINE),
                "and a routine gets the fx glyph, which was drawn all along and referenced nowhere");
        });
    }

    /// <summary>
    /// Control: the converter can still return the database glyph, so the rule above is not passing
    /// because nothing ever matches it.
    /// </summary>
    [Test]
    public void ControlADatabaseNodeStillDrawsTheDatabaseGlyphTest()
    {
        Assert.That(Icon(new NodeTypeToIconConverter(), DatabaseNodeType.Database),
            Is.EqualTo(StudioIcons.PATH_DB_DATABASE));
    }

    private static string? Icon(NodeTypeToIconConverter converter, DatabaseNodeType type) =>
        converter.Convert(type, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) as string;

    #endregion

    #region A count takes the case its sentence asks for

    /// <summary>
    /// «11 совпадений в 1 подключение» - the count was right and the case was not, because the
    /// plural table held nominative forms only and the phrase reads «в …», which is prepositional.
    /// </summary>
    [Test]
    public void TheFilterSummaryPutsConnectionsInThePrepositionalTest()
    {
        var localization = new LocalizationService();
        var texts = localization.Plurals("ru");

        Assert.That(texts.ContainsKey("Count.ConnectionsIn"), Is.True,
            "the prepositional form has to exist as its own key - one count, two sentences, two cases");

        var forms = texts["Count.ConnectionsIn"];

        Assert.Multiple(() =>
        {
            Assert.That(forms["one"], Does.Contain("подключении"));
            Assert.That(forms["few"], Does.Contain("подключениях"));
            Assert.That(forms["many"], Does.Contain("подключениях"));
        });
    }

    /// <summary>
    /// Control: the nominative set is still there and still nominative, so the two keys are two
    /// sentences rather than one of them having been edited into the other.
    /// </summary>
    [Test]
    public void ControlTheNominativeFormsAreUnchangedTest()
    {
        var forms = new LocalizationService().Plurals("ru")["Count.Connections"];

        Assert.Multiple(() =>
        {
            Assert.That(forms["one"], Does.Contain("подключение"));
            Assert.That(forms["many"], Does.Contain("подключений"));
        });
    }

    #endregion

    #region Every language says all of it

    /// <summary>
    /// The three strings this work added exist in both languages. The wider lint covers the whole
    /// catalogue; this one names them, so a half-added string is caught by the case that added it.
    /// </summary>
    [Test]
    public void TheNewStringsExistInEveryLanguageTest()
    {
        var localization = new LocalizationService();

        string[] added = ["Tabs.ReadOnly", "Query.ReadOnly.Banner", "Query.ReadOnly.Short", "Query.Running"];

        Assert.Multiple(() =>
        {
            foreach (var language in localization.Available)
            {
                var texts = localization.Texts(language.Code);

                foreach (var key in added)
                    Assert.That(texts.ContainsKey(key), Is.True, $"{language.Code}: {key}");
            }
        });
    }

    #endregion
}
