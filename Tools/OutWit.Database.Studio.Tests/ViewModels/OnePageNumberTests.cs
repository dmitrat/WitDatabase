using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The table editor counts its pages once.
/// </summary>
/// <remarks>
/// <para>
/// Reported from the screenshot pass: on the second page the footer read
/// <i>page 1 · 1000 rows shown of 12437</i> while the status line directly under it read
/// <i>Loaded 1000 rows (page 2), more to come</i>. One counted from zero and the other from one, in
/// the same window, about the same page.
/// </para>
/// <para>
/// <c>PageIndex</c> is an index and stays one - it addresses the anchor list and the query. What a
/// person reads is a NUMBER, and there is now one property for it, so the two cannot drift again.
/// The second case is the one that keeps it that way: a view that binds the index for display is
/// what produced the defect, and the rule counts the places rather than trusting the fix.
/// </para>
/// </remarks>
[TestFixture]
public class OnePageNumberTests
{
    #region The number

    [Test]
    public async Task TheFooterAndTheStatusLineNameTheSamePageTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Customers (Name, Email) VALUES ('First', 'first@example.com')");
        await studio.Database.ExecuteNonQueryAsync(
            "INSERT INTO Customers (Name, Email) VALUES ('Second', 'second@example.com')");

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Customers");

        editor.PageSize = 1;
        await editor.LoadDataAsync();

        Assert.That(editor.PageNumber, Is.EqualTo(1), "the first page is page one");

        await StudioFixture.PressAsync(editor.NextPageCommand);

        Assert.Multiple(() =>
        {
            Assert.That(editor.PageIndex, Is.EqualTo(1), "the index still counts from zero");

            Assert.That(editor.PageNumber, Is.EqualTo(2),
                "and the number a person reads counts from one");

            Assert.That(editor.StatusMessage, Does.Contain("page 2"),
                "the status line says the same page the footer does");
        });
    }

    #endregion

    #region The rule

    /// <summary>
    /// No view displays the index. Both places that show a page number read the number.
    /// </summary>
    [Test]
    public void NoViewShowsThePageIndexTest()
    {
        var root = FindStudioProject();

        Assert.That(root, Is.Not.Null,
            "the Studio project was not found from " + AppContext.BaseDirectory);

        var offenders = new List<string>();
        var numbers = 0;
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(root!, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            scanned++;

            foreach (var line in File.ReadAllLines(file))
            {
                if (line.Contains("PageNumber", StringComparison.Ordinal))
                    numbers++;

                if (line.Contains("Binding PageIndex", StringComparison.Ordinal)
                    || line.Contains("Path=\"PageIndex\"", StringComparison.Ordinal)
                    || line.Contains("Path=\"PageIndex\"".Replace("\"", "'"), StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(root!, file)}: {line.Trim()}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            // CONTROL: a walk that found no markup would report no offenders either.
            Assert.That(scanned, Is.GreaterThan(20),
                "CONTROL: too few views scanned - the walk is looking in the wrong place");

            // SURFACE: the toolbar and the footer. A third place to show a page has to be seen here.
            Assert.That(numbers, Is.EqualTo(2),
                "two places show the page number - the editor's toolbar and its footer");

            Assert.That(offenders, Is.Empty,
                "these show the page INDEX, which counts from zero:" + Environment.NewLine
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

    #endregion
}
