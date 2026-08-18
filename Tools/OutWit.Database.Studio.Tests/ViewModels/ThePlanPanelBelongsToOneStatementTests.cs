using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The plan is produced for one statement, shown when it is produced, and taken away when it stops
/// being about the text on screen.
/// </summary>
/// <remarks>
/// <para>
/// Two findings from the screenshot pass, and they are the same panel. <b>Pressing Plan built the
/// plan and left the Result tab in front</b>, so the answer arrived behind the question. And
/// <b>a failed run left the previous statement's plan standing</b> - the error bar under the editor,
/// the new text in it, and the analysis of the old statement beside them with nothing saying which
/// it belonged to. On a screenshot that is fatal; at the keyboard it is a wrong answer to "why is
/// this slow".
/// </para>
/// <para>
/// The panel is not cleared by every run: a script that still contains the statement the plan was
/// built for keeps it, which is the case where the plan is exactly what a person wants to look at
/// while reading the result. The control case asserts that.
/// </para>
/// </remarks>
[TestFixture]
public class ThePlanPanelBelongsToOneStatementTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private QueryTabViewModel m_tab = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        // The tab the workspace opens for a connection, which is the one a person types into.
        m_tab = m_studio.FirstQueryTab;
        m_tab.SqlText = "SELECT * FROM Customers";
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region It comes forward

    [Test]
    public async Task PressingPlanBringsThePlanPanelForwardTest()
    {
        Assume.That(m_tab.IsPlanSelected, Is.False, "the Result panel is in front to begin with");

        await StudioFixture.PressAsync(m_tab.ShowPlanCommand);

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.Plan.IsEmpty, Is.False, "the plan was built");

            Assert.That(m_tab.IsPlanSelected, Is.True,
                "and the panel holding it is the one in front");
        });
    }

    /// <summary>
    /// The property has to reach the window, the way the History tab's already does.
    /// </summary>
    [Test]
    public void ThePlanTabFollowsTheSelectionPropertyTest()
    {
        var markup = Markup("Views/Query/QueryEditor.axaml");

        Assert.That(markup, Does.Contain("IsSelected=\"{Binding IsPlanSelected, Mode=TwoWay}\""),
            "a property nothing binds would leave the panel behind exactly as before");
    }

    #endregion

    #region It does not outlive its statement

    [Test]
    public async Task AFailedRunTakesTheOldPlanAwayTest()
    {
        await StudioFixture.PressAsync(m_tab.ShowPlanCommand);

        Assume.That(m_tab.Plan.IsEmpty, Is.False, "there is a plan to go stale");

        m_tab.SqlText = "SELECT * FROM Customers WHER Name = 'x'";

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.ErrorMessage, Is.Not.Null, "the run failed, which is the reported case");

            Assert.That(m_tab.Plan.IsEmpty, Is.True,
                "the plan of the statement before it does not stand beside the new text");

            Assert.That(m_tab.PlanStatement, Is.Null);
        });
    }

    /// <summary>
    /// The control: running the statement the plan was built for keeps it.
    /// </summary>
    [Test]
    public async Task RunningTheSameStatementKeepsThePlanTest()
    {
        await StudioFixture.PressAsync(m_tab.ShowPlanCommand);

        Assume.That(m_tab.Plan.IsEmpty, Is.False);

        await m_tab.ExecuteSqlAsync(m_tab.SqlText);

        Assert.Multiple(() =>
        {
            Assert.That(m_tab.ErrorMessage, Is.Null, "the run succeeded");

            Assert.That(m_tab.Plan.IsEmpty, Is.False,
                "and the plan is still about the statement that was run");
        });
    }

    #endregion

    #region Tools

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
