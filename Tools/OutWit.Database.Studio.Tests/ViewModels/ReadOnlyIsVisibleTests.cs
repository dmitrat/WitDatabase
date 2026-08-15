using System.Xml.Linq;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// WS-10: read-only is a mode, and the surface where writing happens has to say so.
/// </summary>
/// <remarks>
/// <para>
/// The refusal is real and the engine's - «This connection is read-only, so INSERT is not allowed on
/// it». Of the canon's three signs, only one was built: four words in the status bar, at the bottom
/// of the window, in English. The other two are the ones on the surface a person is actually looking
/// at - the tab they are about to run a query from, and the editor they are typing it into - and a
/// person learns the mode from neither. They learn it by being refused.
/// </para>
/// <para>
/// <b>Asserted in two places on purpose.</b> A ViewModel property nothing draws is the exact shape
/// this application has spent three stages removing - phase 13 found a dialog no command opened,
/// stage 10 found captions carried in code that nothing showed, and the phase-17 census found 23
/// computed properties bound in markup that announce nothing. So the property is asserted here AND
/// the markup is read, because either alone passes for work that is half done.
/// </para>
/// </remarks>
[TestFixture]
public class ReadOnlyIsVisibleTests
{
    #region Constants

    private const string TAB_STRIP = "Views/Workspace/WorkspaceTabStrip.axaml";

    private const string QUERY_EDITOR = "Views/Query/QueryEditor.axaml";

    #endregion

    #region The tab says it

    [Test]
    public async Task ATabBoundToAReadOnlyConnectionSaysSoAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var session = await OpenReadOnlyAsync(fixture);
        var tab = fixture.Workspace.OpenQueryTab("SELECT 1", session: session);

        Assert.Multiple(() =>
        {
            Assert.That(session.IsReadOnly, Is.True,
                "the fixture must actually have opened a read-only connection, or this case is "
                + "about nothing");

            Assert.That(tab.IsReadOnly, Is.True,
                "a tab bound to a read-only connection has to carry the mode, or nothing on the tab "
                + "strip can draw it");
        });
    }

    /// <summary>
    /// Control: a writable connection does not claim the mode, so the property is reading the
    /// connection rather than returning a constant.
    /// </summary>
    [Test]
    public async Task ControlATabBoundToAWritableConnectionDoesNotAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        var tab = fixture.Workspace.OpenQueryTab("SELECT 1", session: fixture.Database);

        Assert.That(tab.IsReadOnly, Is.False);
    }

    /// <summary>
    /// A second database, made on disk and then opened read-only - the engine refuses to CREATE one
    /// in that mode, so the file has to exist first.
    /// </summary>
    private static async Task<IDatabaseSession> OpenReadOnlyAsync(StudioFixture fixture)
    {
        var path = Path.Combine(fixture.Root, "read-only.witdb");

        StudioFixture.CreateDatabaseOnDisk(path);

        var session = await fixture.Connections.OpenAsync(new ConnectionInfo
        {
            FilePath = path,
            StorageEngine = "btree",
            IsReadOnly = true
        });

        Assert.That(session, Is.Not.Null, "the read-only connection must open");

        return session!;
    }

    #endregion

    #region And the surface draws it

    [Test]
    public void TheTabStripDrawsTheReadOnlySignTest()
    {
        var markup = Markup(TAB_STRIP);

        Assert.That(markup, Does.Contain("IsReadOnly"),
            "the tab strip must bind the mode; a tab that does not show it leaves the connection's "
            + "colour as the only thing distinguishing one database from another");
    }

    [Test]
    public void TheQueryEditorCarriesAReadOnlyBannerTest()
    {
        var markup = Markup(QUERY_EDITOR);

        Assert.That(markup, Does.Contain("IsReadOnly"),
            "the editor is where writing is typed, so it is where the refusal has to be announced "
            + "BEFORE the statement is run");
    }

    /// <summary>
    /// Control: the lint reads the files it names and can see a binding that is there. Without it,
    /// a wrong path or a renamed view would make both rules above pass on an empty read - which is
    /// exactly how stage 9's localisation lint came to be counting nothing.
    /// </summary>
    [Test]
    public void ControlTheMarkupLintReadsTheRightFilesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Markup(TAB_STRIP), Does.Contain("IsPinned"),
                "the tab strip binds IsPinned and always has");
            Assert.That(Markup(QUERY_EDITOR), Does.Contain("SqlEditor"),
                "the query editor holds the editor control");
        });
    }

    #endregion

    #region A table with no primary key (WS-35), and WS-11's rule over it

    /// <summary>
    /// The chrome agrees with the buttons: a table that cannot be edited is not announced as being
    /// edited, and the footer does not advertise the two keys that will do nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven on rc.1: <c>NoKey</c> had every editing control <c>[disabled]</c> - correct, and read
    /// from the accessibility tree - while the status bar said <c>Editing table: NoKey</c> and the
    /// footer said <c>Ctrl+S: Commit</c> and <c>Del: Delete row</c>. Three places agreed and two did
    /// not.
    /// </para>
    /// <para>
    /// <b>The flag matters more than the fix.</b> <c>TableEditTabViewModel.IsReadOnly</c> means "this
    /// table has no primary key" and it is what <c>CanAddRow</c>, <c>CanDeleteRow</c> and
    /// <c>CanCommit</c> are computed from; <c>WorkspaceTabViewModel.IsReadOnly</c>, which it hides,
    /// means "the CONNECTION was opened read-only". Two different concepts under one name, so the
    /// buttons are asserted here beside the words - gating on the other one would look right and be
    /// wrong on both tables.
    /// </para>
    /// </remarks>
    [Test]
    public async Task ATableWithNoPrimaryKeyIsNotAnnouncedAsBeingEditedAsync()
    {
        await using var fixture = await StudioFixture.CreateAsync();

        await fixture.Explorer.RefreshAsync(fixture.Database);

        var withKey = await EditAsync(fixture, "Customers");
        var editing = fixture.MainWindow.StatusText;

        var withoutKey = await EditAsync(fixture, "Logs");
        var viewing = fixture.MainWindow.StatusText;

        TestContext.Out.WriteLine(editing);
        TestContext.Out.WriteLine(viewing);

        Assert.Multiple(() =>
        {
            // The buttons first, because they are the thing the words have to agree WITH.
            Assert.That(withoutKey.IsReadOnly, Is.True, "Logs has no primary key");
            Assert.That(withoutKey.CanAddRow, Is.False);
            Assert.That(withoutKey.CanDeleteRow, Is.False);
            Assert.That(withoutKey.CanCommit, Is.False);

            Assert.That(viewing, Does.Not.Contain("Editing"),
                "the status bar announced editing over five disabled controls");
            Assert.That(viewing, Does.Contain("Logs"),
                "and it still has to say which table is open");

            // CONTROL, and it is the half that makes the other one mean something: a table that CAN be
            // edited is still announced as being edited. Without this, "never say Editing" passes.
            Assert.That(withKey.IsReadOnly, Is.False, "Customers has a primary key");
            Assert.That(withKey.CanAddRow, Is.True);
            Assert.That(editing, Does.Contain("Editing").And.Contain("Customers"));
        });
    }

    /// <summary>
    /// And the footer, which is markup: every hint it prints is gated on the same flag.
    /// </summary>
    /// <remarks>
    /// Structural rather than textual - the element or one of its ancestors carries the gate - because
    /// the three status states each carry their own copy of the two hints, and a rule counting
    /// occurrences would pass with one of the three left open. The surface is asserted for the same
    /// reason it is everywhere else here: seven hints matched and zero ungated is a result, and zero
    /// matched with zero ungated is a rule reading the wrong file.
    /// </remarks>
    [Test]
    public void EveryFooterHintIsGatedOnTheFlagTheButtonsReadTest()
    {
        var view = XDocument.Load(Path.Combine(StudioFolder(),
            "Views", "Workspace", "TableEditView.axaml"));

        var hints = view.Descendants()
            .Where(element => element.Attributes()
                .Any(attribute => attribute.Value.Contains("S.Grid.Hint.", StringComparison.Ordinal)))
            .ToList();

        var ungated = hints
            .Where(hint => !hint.AncestorsAndSelf().Any(IsGatedOnEditable))
            .Select(hint => hint.Attributes()
                .First(attribute => attribute.Value.Contains("S.Grid.Hint.", StringComparison.Ordinal)).Value)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(hints, Has.Count.EqualTo(7),
                "CONTROL: the footer prints two hints in each of the three status states and one "
                + "instruction in the default one - a different number means this rule is reading "
                + "something else, or a hint was added without being gated");

            Assert.That(ungated, Is.Empty,
                "these are shown over disabled buttons:"
                + Environment.NewLine + string.Join(Environment.NewLine, ungated));
        });
    }

    private static bool IsGatedOnEditable(XElement element)
    {
        var visible = element.Attribute("IsVisible")?.Value;

        return visible != null
               && visible.Replace(" ", string.Empty) == "{Binding!IsReadOnly}";
    }

    private static async Task<TableEditTabViewModel> EditAsync(StudioFixture fixture, string table)
    {
        fixture.Explorer.SelectedNode = Walk(fixture.Explorer.Nodes)
            .First(node => node.NodeType == DatabaseNodeType.Table && node.Name == table);

        await StudioFixture.PressAsync(fixture.Explorer.EditDataCommand);

        return fixture.Workspace.Tabs.OfType<TableEditTabViewModel>().First(tab => tab.TableName == table);
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

    #endregion

    #region Tools

    private static string Markup(string relative)
    {
        var path = Path.Combine(StudioFolder(), relative.Replace('/', Path.DirectorySeparatorChar));

        Assert.That(File.Exists(path), Is.True, $"{relative} must be where this fixture says it is");

        return File.ReadAllText(path);
    }

    private static string StudioFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Tools", "OutWit.Database.Studio");

            if (Directory.Exists(Path.Combine(candidate, "Views")))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "the Studio project was not found from " + AppContext.BaseDirectory);
    }

    #endregion
}
