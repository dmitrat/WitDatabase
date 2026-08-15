using System.Xml.Linq;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// Options standing side by side share a baseline, and they only do it if they say so.
/// </summary>
/// <remarks>
/// <para>
/// The Create dialog's three storage options were vertically centred inside a <c>UniformGrid</c>,
/// whose row height comes from the tallest cell - so the option with the longest description defined
/// the row and every shorter sibling sat ~10-11 px below it. <b>Which one is the outlier moves with
/// the language</b>: in English it is LSM, in Russian «В памяти». A screenshot review pinned to one
/// language finds the defect and blames the wrong control, which is why this is a rule about the
/// markup and not a picture.
/// </para>
/// <para>
/// <b>The rule is deliberately narrow, and the wide one is not written.</b> Requiring a declared
/// vertical alignment from all 66 horizontal sibling groups in these views would report legend rows,
/// caption rows and every deliberately staggered layout - and an exemption written for a false
/// positive is a hole in a rule wearing a comment's clothes. What is asserted instead is the shape
/// that actually produces the defect: two or more radio buttons side by side, at least one of them
/// carrying a description that WRAPS, so their heights differ by construction.
/// </para>
/// </remarks>
[TestFixture]
public class AlignmentSurfaceTests
{
    [Test]
    public void OptionsWithWrappingDescriptionsDeclareTheirBaselineTest()
    {
        var groups = 0;
        var options = 0;
        var floating = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ViewsFolder(), "*.axaml", SearchOption.AllDirectories))
        {
            var markup = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var parent in markup.Descendants())
            {
                var radios = parent.Elements()
                    .Where(child => child.Name.LocalName == "RadioButton")
                    .ToList();

                if (radios.Count < 2 || !radios.Any(Wraps))
                    continue;

                groups++;
                options += radios.Count;

                foreach (var radio in radios.Where(radio => !Aligned(radio)))
                {
                    floating.Add($"{Path.GetFileName(file)}:{((System.Xml.IXmlLineInfo)radio).LineNumber} "
                                 + $"<RadioButton {radio.Attribute("AutomationProperties.AutomationId")?.Value}>");
                }
            }
        }

        Assert.Multiple(() =>
        {
            // THE SURFACE. One group today - the Create dialog's storage choice - and saying so is what
            // tells "nothing left to find" apart from "this rule read no markup at all". A second group
            // arriving is not a failure; it is a reason to check it and change this number.
            Assert.That(groups, Is.EqualTo(1),
                "the views hold a different number of side-by-side option groups with wrapping "
                + "descriptions than this rule was measured against");

            Assert.That(options, Is.EqualTo(3), "and the one group has three options in it");

            Assert.That(floating, Is.Empty,
                "these sit wherever the tallest sibling leaves them:"
                + Environment.NewLine + string.Join(Environment.NewLine, floating));
        });
    }

    /// <summary>Whether anything inside this option wraps, which is what makes the heights differ.</summary>
    private static bool Wraps(XElement radio) =>
        radio.Descendants().Any(element => element.Attribute("TextWrapping")?.Value == "Wrap");

    private static bool Aligned(XElement radio) =>
        radio.Attribute("VerticalAlignment") != null || radio.Attribute("VerticalContentAlignment") != null;

    private static string ViewsFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio", "Views");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the Studio views were not found from " + AppContext.BaseDirectory);
    }
}
