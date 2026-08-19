using System.Text.RegularExpressions;
using System.Xml.Linq;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A separator divides two groups, so it is drawn only when there is something on both sides of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported from a screenshot, 2026-08-19.</b> The connection's own menu drew <i>Database...</i>,
/// <i>Close database</i>, a rule, <i>Create</i>, then TWO rules with nothing between them,
/// <i>Refresh</i>, and two more rules below it with nothing after them at all.
/// </para>
/// <para>
/// The cause is the other half of the change that gave each node its own menu
/// (<see cref="EachNodeGetsItsOwnMenuTests"/>): every ITEM learned to hide itself where it does not
/// apply, and the five separators - the only elements of that menu with no condition on them - were
/// left drawing the shape of a menu that is no longer there. A folder's menu began with a rule, and a
/// column's menu was one command wrapped in five.
/// </para>
/// <para>
/// The reading here is the real one: the ORDER and the conditions come from
/// <c>Views/DatabaseExplorer.axaml</c>, and the answers come from the real ViewModel. Nothing in this
/// fixture knows what the menu contains except where it says so on purpose.
/// </para>
/// </remarks>
[TestFixture]
public class ARuleHasSomethingOnBothSidesTests
{
    #region Constants

    /// <summary>How a separator is written in the sequences below.</summary>
    private const string RULE = "-----";

    #endregion

    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        await m_studio.Explorer.RefreshAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region The reported menu

    /// <summary>
    /// The connection node, which is the one in the screenshot.
    /// </summary>
    [Test]
    public void TheConnectionsMenuIsTheFourThingsAConnectionHasTest()
    {
        var menu = TheMenuAsTheMarkupDeclaresIt();

        Assert.That(Drawn(DatabaseNodeType.Database, menu), Is.EqualTo(new[]
        {
            "DatabaseExplorerOpenDatabaseTab",
            "DatabaseExplorerCloseConnection",
            RULE,
            "DatabaseExplorerCreate",
            RULE,
            "DatabaseExplorerRefresh2"
        }));
    }

    /// <summary>
    /// A folder's menu, which began with a rule.
    /// </summary>
    [Test]
    public void AFoldersMenuIsTwoCommandsWithOneRuleBetweenThemTest()
    {
        var menu = TheMenuAsTheMarkupDeclaresIt();

        Assert.That(Drawn(DatabaseNodeType.TablesFolder, menu), Is.EqualTo(new[]
        {
            "DatabaseExplorerCreate",
            RULE,
            "DatabaseExplorerRefresh2"
        }));
    }

    /// <summary>
    /// A column has nothing of its own, so its menu is one command and no rules at all.
    /// </summary>
    [Test]
    public void AColumnsMenuIsOneCommandTest()
    {
        var menu = TheMenuAsTheMarkupDeclaresIt();

        Assert.That(Drawn(DatabaseNodeType.Column, menu), Is.EqualTo(new[]
        {
            "DatabaseExplorerRefresh2"
        }));
    }

    #endregion

    #region The rule, over every kind of node

    [Test]
    public void NoMenuBeginsWithARuleEndsWithOneOrDrawsTwoRunningTogetherTest()
    {
        var menu = TheMenuAsTheMarkupDeclaresIt();

        var offenders = new List<string>();

        foreach (var type in Enum.GetValues<DatabaseNodeType>())
        {
            var drawn = Drawn(type, menu);

            if (drawn.Count == 0)
            {
                offenders.Add($"{type}: an empty menu");
                continue;
            }

            if (drawn[0] == RULE)
                offenders.Add($"{type}: begins with a rule - {Join(drawn)}");

            if (drawn[^1] == RULE)
                offenders.Add($"{type}: ends with a rule - {Join(drawn)}");

            for (var i = 1; i < drawn.Count; i++)
            {
                if (drawn[i] == RULE && drawn[i - 1] == RULE)
                    offenders.Add($"{type}: two rules with nothing between them - {Join(drawn)}");
            }
        }

        Assert.That(offenders, Is.Empty, string.Join(Environment.NewLine, offenders));
    }

    #endregion

    #region The control

    /// <summary>
    /// The other direction, and the reason it is here: every rule of this menu is still DRAWN
    /// somewhere. A separator that has quietly become unreachable satisfies the rule above, and a
    /// menu with no divisions in it is not what was asked for.
    /// </summary>
    [Test]
    public void ATableGetsTheWholeMenuWithEveryRuleInItTest()
    {
        var menu = TheMenuAsTheMarkupDeclaresIt();

        var rules = menu.Count(entry => entry.Name == RULE);

        Assert.Multiple(() =>
        {
            // CONTROL: a parse that found nothing would agree with every assertion in this fixture.
            Assert.That(rules, Is.GreaterThanOrEqualTo(4),
                "CONTROL: the markup was not read - no separators were found in it");

            Assert.That(menu.Select(entry => entry.Name), Does.Contain("DatabaseExplorerDrop"),
                "CONTROL: the markup was not read - the menu's last item is missing");

            Assert.That(Drawn(DatabaseNodeType.Table, menu), Is.EqualTo(new[]
            {
                "DatabaseExplorerCreate",
                RULE,
                "DatabaseExplorerSelectData",
                "DatabaseExplorerEditData",
                "DatabaseExplorerViewStructure",
                "DatabaseExplorerViewDefinition",
                RULE,
                "DatabaseExplorerRefresh2",
                RULE,
                "DatabaseExplorerRename",
                "DatabaseExplorerTruncate",
                RULE,
                "DatabaseExplorerDrop"
            }), "a table is the node that has one of everything");
        });
    }

    #endregion

    #region Tools

    private sealed record MenuEntry(string Name, string? Condition);

    /// <summary>
    /// The menu as it is written: the direct children of the context menu, in order, each with the
    /// property that decides whether it is drawn at all.
    /// </summary>
    private static IReadOnlyList<MenuEntry> TheMenuAsTheMarkupDeclaresIt()
    {
        var document = XDocument.Parse(Markup("Views/DatabaseExplorer.axaml"));

        var menus = document.Descendants()
            .Where(element => element.Name.LocalName == "ContextMenu")
            .ToList();

        Assert.That(menus, Has.Count.EqualTo(1), "the tree has one context menu, shared by every node");

        var entries = new List<MenuEntry>();

        foreach (var element in menus[0].Elements())
        {
            var kind = element.Name.LocalName;

            if (kind == "Separator")
                entries.Add(new MenuEntry(RULE, ConditionOf(element)));

            else if (kind == "MenuItem")
                entries.Add(new MenuEntry(IdOf(element), ConditionOf(element)));
        }

        return entries;
    }

    /// <summary>
    /// What the menu draws over a node of this kind, in order.
    /// </summary>
    private IReadOnlyList<string> Drawn(DatabaseNodeType type, IReadOnlyList<MenuEntry> menu)
    {
        m_studio.Explorer.SelectedNode = NodeOf(type);

        return menu.Where(entry => IsDrawn(entry.Condition))
                   .Select(entry => entry.Name)
                   .ToList();
    }

    private bool IsDrawn(string? condition)
    {
        if (condition == null)
            return true;

        var property = typeof(DatabaseExplorerViewModel).GetProperty(condition);

        Assert.That(property, Is.Not.Null,
            $"the markup binds IsVisible to DatabaseExplorerVm.{condition}, which does not exist");

        return (bool)property!.GetValue(m_studio.Explorer)!;
    }

    /// <summary>
    /// A real node of this kind where the fixture's database has one, and a stand-in where it does
    /// not - there are no sequences or routines in this schema, and the menu still has to answer for
    /// them.
    /// </summary>
    private DatabaseNode NodeOf(DatabaseNodeType type)
    {
        var real = Walk(m_studio.Explorer.Nodes).FirstOrDefault(node => node.NodeType == type);

        return real ?? new DatabaseNode { Name = type.ToString(), NodeType = type };
    }

    private static IEnumerable<DatabaseNode> Walk(IEnumerable<DatabaseNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Walk(node.Children))
                yield return child;
        }
    }

    private static string? ConditionOf(XElement element)
    {
        var visible = element.Attribute("IsVisible")?.Value;

        if (visible == null)
            return null;

        var match = Regex.Match(visible, @"^\{Binding\s+DatabaseExplorerVm\.(\w+)\}$");

        Assert.That(match.Success, Is.True,
            $"this fixture reads IsVisible bindings to the explorer, and this one is different: {visible}");

        return match.Groups[1].Value;
    }

    private static string IdOf(XElement element)
    {
        var id = element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.EndsWith("AutomationId"))?.Value;

        Assert.That(id, Is.Not.Null, "every item of this menu carries an automation id");

        return id!;
    }

    private static string Join(IEnumerable<string> drawn)
    {
        return string.Join(" | ", drawn);
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
