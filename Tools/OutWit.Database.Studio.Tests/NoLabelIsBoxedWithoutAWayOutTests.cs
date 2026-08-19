using System.Text.RegularExpressions;

namespace OutWit.Database.Studio.Tests;

/// <summary>
/// A label given a fixed width has to be able to wrap or to trim.
/// </summary>
/// <remarks>
/// <para>
/// <b>The previous plan said clipping could not be guarded</b>, and it was right about the pixels:
/// the string is complete in the ViewModel and complete in the accessibility tree, so nothing in a
/// test can see that it was drawn short. <i>How many to rememb</i> and <i>«ImportStaging» has no
/// primary ke</i> are invisible to every assertion this project can write.
/// </para>
/// <para>
/// <b>What can be seen is the mechanism.</b> A <c>TextBlock</c> with a fixed <c>Width</c> and neither
/// <c>TextWrapping</c> nor <c>TextTrimming</c> has no way to survive text longer than the number
/// somebody picked - and the number was picked while looking at ONE language. «Сколько помнить» fits
/// where <i>How many to remember</i> does not, and the reverse happens just as often.
/// </para>
/// <para>
/// This rule does not say the layout is right. It says a label cannot be silently cut off: it either
/// takes the room it needs, wraps, or ends in an ellipsis that shows something was left out.
/// </para>
/// </remarks>
[TestFixture]
public class NoLabelIsBoxedWithoutAWayOutTests
{
    [Test]
    public void EveryFixedWidthLabelCanWrapOrTrimTest()
    {
        var root = FindStudioProject();

        Assert.That(root, Is.Not.Null,
            "the Studio project was not found from " + AppContext.BaseDirectory);

        var offenders = new List<string>();
        var scanned = 0;
        var boxed = 0;

        foreach (var file in Directory.EnumerateFiles(root!, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;

            var text = File.ReadAllText(file);

            foreach (Match element in Regex.Matches(text, @"<TextBlock\b[\s\S]*?/?>"))
            {
                var markup = element.Value;

                if (!Regex.IsMatch(markup, @"\bWidth=""\d"))
                    continue;

                boxed++;

                if (markup.Contains("TextWrapping=", StringComparison.Ordinal)
                    || markup.Contains("TextTrimming=", StringComparison.Ordinal))
                    continue;

                var name = Regex.Match(markup, @"Text=""([^""]{0,60})""").Groups[1].Value;

                offenders.Add($"{Path.GetRelativePath(root!, file)}: {name}");
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that read no markup would report no offenders either.
            Assert.That(scanned, Is.GreaterThan(20),
                "CONTROL: too few views scanned - the walk is looking in the wrong place");

            // CONTROL: and one that matched no fixed widths would pass without looking at anything.
            Assert.That(boxed, Is.GreaterThan(0),
                "CONTROL: no fixed-width label found at all - the pattern stopped matching");

            Assert.That(offenders, Is.Empty,
                "these labels are given a width and no way to survive text longer than it:"
                + Environment.NewLine + string.Join(Environment.NewLine, offenders));
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
