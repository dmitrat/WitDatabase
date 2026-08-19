using System.Xml.Linq;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// Text that asks to wrap or to trim is not put where it cannot.
/// </summary>
/// <remarks>
/// <para>
/// <b>A horizontal <c>StackPanel</c> measures its children with INFINITE width.</b> A
/// <c>TextBlock</c> inside one is never told there is an edge, so <c>TextWrapping="Wrap"</c> never
/// wraps and <c>TextTrimming</c> never trims: the text is laid out at its full length and drawn over
/// whatever is beside it, or off the end of the window.
/// </para>
/// <para>
/// <b>Three findings, one shape</b>, and it took a driving pass to see the third:
/// </para>
/// <list type="bullet">
/// <item>26 - the import result painted across the line saying what the file held;</item>
/// <item>31 - the conflict message painted across the hint bar, both at full opacity;</item>
/// <item>A3 - the read-only banner cut off mid-sentence: <i>«ImportStaging» has no primary ke</i>.
/// That one had asked to wrap since it was written.</item>
/// </list>
/// <para>
/// The fix in every case is a <c>Grid</c> with an <c>Auto</c> column for the icon and a <c>*</c>
/// column for the words. This rule is what stops the fourth.
/// </para>
/// </remarks>
[TestFixture]
public class NoWrappingTextInAHorizontalStackTests
{
    [Test]
    public void NoTextThatWrapsOrTrimsIsInAHorizontalStackPanelTest()
    {
        var root = FindStudioProject();

        Assert.That(root, Is.Not.Null,
            "the Studio project was not found from " + AppContext.BaseDirectory);

        var offenders = new List<string>();
        var scanned = 0;
        var considered = 0;

        foreach (var file in Directory.EnumerateFiles(root!, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;

            var document = XDocument.Load(file);

            foreach (var text in document.Descendants().Where(element => element.Name.LocalName == "TextBlock"))
            {
                var wraps = (string?)text.Attribute("TextWrapping") == "Wrap";
                var trims = text.Attribute("TextTrimming") != null;

                if (!wraps && !trims)
                    continue;

                considered++;

                // A width of its own is an edge: the panel offers infinity and the element takes
                // what it was given, so the words wrap inside it. That is how the nine settings
                // labels of phase 6 work, and they are not what this rule is about.
                if (text.Attribute("Width") != null || text.Attribute("MaxWidth") != null)
                    continue;

                var parent = text.Parent;

                if (parent == null || parent.Name.LocalName != "StackPanel")
                    continue;

                // A StackPanel is vertical unless it says otherwise, and a vertical one measures its
                // children with the width it has - which is what wrapping needs.
                if ((string?)parent.Attribute("Orientation") != "Horizontal")
                    continue;

                var what = (string?)text.Attribute("Text") ?? "(bound)";

                offenders.Add($"{Path.GetRelativePath(root!, file)}: {what}");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that read no markup would report no offenders either.
            Assert.That(scanned, Is.GreaterThan(20),
                "CONTROL: too few views scanned - the walk is looking in the wrong place");

            // CONTROL: and one that found no wrapping text at all would pass without looking.
            Assert.That(considered, Is.GreaterThan(10),
                "CONTROL: no text that wraps or trims was found - the attributes stopped matching");

            Assert.That(offenders, Is.Empty,
                "these ask to wrap or to trim inside a horizontal StackPanel, which offers them "
                + "infinite width - so they do neither:" + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
        });
    }

    private static string? FindStudioProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }
}
