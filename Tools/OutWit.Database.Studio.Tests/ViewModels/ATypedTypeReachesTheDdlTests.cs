using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// What is typed into the type cell is what the statement carries.
/// </summary>
/// <remarks>
/// <para>
/// Finding 23: typing <c>VARCHAR(40)</c> into a column's type and pressing <b>Generate DDL</b> produced
/// <c>Carrier</c> - the column with no type at all. The combo box takes free text and shows it, and
/// only its <c>SelectedItem</c> was bound, so text that matched no item in the list reached nothing.
/// And the other way round: with <c>VARCHAR(40)</c> typed, picking <c>VARCHAR(255)</c> from the list
/// changed the statement while the cell went on reading <c>VARCHAR(40)</c>, so the two disagreed in
/// the same window.
/// </para>
/// <para>
/// <b>A length is clearly meant to be typed</b> - the list itself offers <c>VARCHAR(255)</c> and
/// <c>CHAR(50)</c> - so the fix is to read the text, not to forbid it.
/// </para>
/// <para>
/// <b>And it has to be refused when it is nonsense</b>, because this engine REFUSES an unknown type
/// name rather than mapping it to TEXT: <c>INTEGERR</c>, <c>VARCHAR2</c> and <c>MEDIUMINT</c> are all
/// parse errors. The dialog is where that can still be corrected. It is checked by PARSING the
/// statement the dialog would run, rather than by keeping a second list of type names beside the
/// engine's - a copy of a list is a thing that goes out of date quietly.
/// </para>
/// </remarks>
[TestFixture]
public class ATypedTypeReachesTheDdlTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private CreateTableViewModel m_dialog = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_dialog = new CreateTableViewModel(m_studio.App)
        {
            TableName = "Shipments"
        };
    }

    [TearDown]
    public async Task TearDown()
    {
        m_dialog.Dispose();

        await m_studio.DisposeAsync();
    }

    #endregion

    #region Tests

    [Test]
    public void ATypedLengthIsInTheStatementTest()
    {
        Column("Carrier", "VARCHAR(40)");

        StudioFixture.PressAsync(m_dialog.GenerateDdlCommand).Wait();

        Assert.That(m_dialog.GeneratedDdl, Does.Contain("VARCHAR(40)"),
            "the type a person typed is the type the statement carries");
    }

    [Test]
    public void ATypeThatTheEngineWouldRefuseIsRefusedHereTest()
    {
        Column("Carrier", "INTEGERR");

        StudioFixture.PressAsync(m_dialog.GenerateDdlCommand).Wait();

        Assert.Multiple(() =>
        {
            Assert.That(m_dialog.TypeProblem, Is.Not.Null.And.Not.Empty,
                "the dialog says which type it cannot use");

            Assert.That(m_dialog.TypeProblem, Does.Contain("Carrier"),
                "and names the column, because a table being created has several");

            Assert.That(m_dialog.CanCreateTable, Is.False,
                "and will not run a statement the engine is going to refuse");
        });
    }

    /// <summary>
    /// The control: the ordinary types, and the ones the list offers with a length in them, are not
    /// refused by the check that refuses nonsense.
    /// </summary>
    [TestCase("TEXT")]
    [TestCase("INTEGER")]
    [TestCase("VARCHAR(255)")]
    [TestCase("DECIMAL(18,2)")]
    [TestCase("BOOLEAN")]
    public void TheTypesTheListOffersAreAcceptedTest(string type)
    {
        Column("Carrier", type);

        StudioFixture.PressAsync(m_dialog.GenerateDdlCommand).Wait();

        Assert.Multiple(() =>
        {
            Assert.That(m_dialog.TypeProblem, Is.Null.Or.Empty, $"{type} is a type this engine takes");
            Assert.That(m_dialog.GeneratedDdl, Does.Contain(type));
        });
    }

    /// <summary>
    /// The cell and the statement cannot disagree, because there is one property behind both.
    /// </summary>
    [Test]
    public void TheCellAndTheStatementReadTheSameValueTest()
    {
        var markup = Markup("Views/Dialogs/CreateTableDialog.axaml");

        Assert.That(markup, Does.Contain("Text=\"{Binding DataType"),
            "the text of the combo is bound, so what is typed is not thrown away");
    }

    #endregion

    #region Tools

    private void Column(string name, string type)
    {
        var column = m_dialog.Columns[0];

        column.Name = name;
        column.DataType = type;
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
