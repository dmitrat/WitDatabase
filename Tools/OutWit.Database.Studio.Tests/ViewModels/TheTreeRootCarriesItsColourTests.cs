using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// Two databases open side by side have two different roots.
/// </summary>
/// <remarks>
/// <para>
/// Finding 16: the brief for S-15 says <i>each root carries its connection colour</i> and the Open
/// dialog says <i>the colour marks the tabs of this connection</i> - and with Sales on green and
/// Events on amber, the two roots in the tree looked identical. The colour was on the tabs and in the
/// Connections window; the tree, which is where a person looks to answer "which database am I in", had
/// none of it.
/// </para>
/// <para>
/// The colour belongs to the connection, so it is on the root and on nothing under it: a folder or a
/// table wearing it would be repeating what the row above already says.
/// </para>
/// </remarks>
[TestFixture]
public class TheTreeRootCarriesItsColourTests
{
    [Test]
    public async Task EachRootCarriesTheColourOfItsConnectionTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        var second = await studio.OpenAnotherAsync("beside", withSchema: false);

        await studio.Explorer.RefreshAsync();

        var roots = studio.Explorer.Nodes;

        Assert.That(roots, Has.Count.EqualTo(2), "two connections, two roots");

        Assert.Multiple(() =>
        {
            foreach (var root in roots)
            {
                var session = studio.Connections.Find(root.ConnectionId);

                Assert.That(session, Is.Not.Null);
                Assert.That(root.ColorIndex, Is.EqualTo(session!.ColorIndex),
                    "the root wears the colour its connection was given");
            }

            Assert.That(roots[0].ColorIndex, Is.Not.EqualTo(roots[1].ColorIndex),
                "and the two are told apart by it - the manager hands out a colour per connection");
        });

        Assert.That(second, Is.Not.Null);
    }

    [Test]
    public async Task NothingUnderTheRootWearsItTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Explorer.RefreshAsync();

        var under = Walk(studio.Explorer.Nodes.SelectMany(root => root.Children)).ToList();

        Assert.That(under, Is.Not.Empty, "there is something under the root to check");

        Assert.That(under.All(node => node.ColorIndex < 0), Is.True,
            "the colour is the connection's, and the row above already says it");
    }

    [Test]
    public void TheTreeDrawsIt()
    {
        var markup = Markup("Views/DatabaseExplorer.axaml");

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("ConnectionColors.Brush"),
                "the same brush the tabs use, so the two agree about what green means");

            Assert.That(markup, Does.Contain("NodeConverters.HasColour"),
                "and it is drawn only where there is a colour to draw");
        });
    }

    #region Tools

    private static IEnumerable<DatabaseNode> Walk(IEnumerable<DatabaseNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Walk(node.Children))
                yield return child;
        }
    }

    private static string Markup(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
            {
                var path = Path.Combine(candidate, relative.Replace('/', Path.DirectorySeparatorChar));

                Assert.That(File.Exists(path), Is.True, $"{relative} must be where this fixture says");

                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new AssertionException("the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
