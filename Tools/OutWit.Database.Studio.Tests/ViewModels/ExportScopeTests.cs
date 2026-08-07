using System.Data;
using NUnit.Framework;
using OutWit.Database.Studio.Tests.Helpers;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The export begins with the scope (WS-51).
///
/// <para>
/// Twelve selected rows, the page on screen and the whole table are three different exports, and the
/// only thing that distinguishes them is a choice the design puts FIRST. Asking for it last is what
/// makes someone export the wrong thing and then do it again.
/// </para>
/// </summary>
[TestFixture]
public class ExportScopeTests
{
    #region Fields

    private StudioFixture m_studio = null!;
    private ExportViewModel m_export = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        m_export = new ExportViewModel(m_studio.App);
    }

    [TearDown]
    public async Task TearDown()
    {
        m_export.Dispose();

        await m_studio.DisposeAsync();
    }

    #endregion

    #region The scope

    /// <summary>
    /// The scope starts on what the user actually has. Starting on "everything" is how an export of
    /// one row becomes an export of four million.
    /// </summary>
    [Test]
    public void WithRowsSelectedTheScopeStartsOnTheSelectionTest()
    {
        var page = Page(4);

        m_export.SetDataSource(page, "Orders", Selected(page, 0, 2), rowsInSource: 4812);

        Assert.Multiple(() =>
        {
            Assert.That(m_export.SelectedScope, Is.EqualTo(ExportScope.Selection));
            Assert.That(m_export.CanExportSelection, Is.True);
        });
    }

    /// <summary>And with nothing selected it starts on the page, not on everything.</summary>
    [Test]
    public void WithNothingSelectedTheScopeStartsOnThePageTest()
    {
        m_export.SetDataSource(Page(4), "Orders", selection: null, rowsInSource: 4812);

        Assert.Multiple(() =>
        {
            Assert.That(m_export.SelectedScope, Is.EqualTo(ExportScope.Page));
            Assert.That(m_export.CanExportSelection, Is.False,
                "an empty selection offered as a scope is a button that writes an empty file");
        });
    }

    /// <summary>
    /// The three counts are three different numbers, and the third is the one that is easy to get
    /// wrong: the grid pages server-side, so the page is not the table. Passing the page count for
    /// "all" would make the number a person checks before pressing Export a lie.
    /// </summary>
    [Test]
    public void TheThreeCountsAreThreeDifferentNumbersTest()
    {
        var page = Page(1000);

        m_export.SetDataSource(page, "Orders", Selected(page, 0, 12), rowsInSource: 4812);

        Assert.Multiple(() =>
        {
            Assert.That(m_export.SelectionCount, Is.EqualTo(12));
            Assert.That(m_export.PageCount, Is.EqualTo(1000));
            Assert.That(m_export.EverythingCount, Is.EqualTo(4812));
        });
    }

    #endregion

    #region And it writes what the scope says

    /// <summary>
    /// The measurement, not the label: a selection of two out of four writes two rows, and they are
    /// the two that were selected. Counting alone would pass for an export of the WRONG two.
    /// </summary>
    [Test]
    public async Task ExportingASelectionWritesThoseRowsAndNoOthersAsync()
    {
        var page = Page(4);
        var target = Path.Combine(m_studio.Root, "selection.csv");

        m_export.SetDataSource(page, "Orders", Selected(page, 1, 2), rowsInSource: 4);
        m_export.SelectedFormat = ExportFormat.Csv;
        m_export.OutputPath = target;
        m_export.SelectedScope = ExportScope.Selection;

        await StudioFixture.PressAsync(m_export.ExportCommand);

        var lines = await File.ReadAllLinesAsync(target);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(3), "a header and two rows");
            Assert.That(lines[1], Does.Contain("row-1"));
            Assert.That(lines[2], Does.Contain("row-2"));
            Assert.That(string.Join("|", lines), Does.Not.Contain("row-0"),
                "and not the row that was not selected");
        });
    }

    /// <summary>
    /// CONTROL: the page scope writes all four. Without it "two rows" would pass for an export that
    /// always writes two.
    /// </summary>
    [Test]
    public async Task ExportingThePageWritesAllOfItAsync()
    {
        var page = Page(4);
        var target = Path.Combine(m_studio.Root, "page.csv");

        m_export.SetDataSource(page, "Orders", Selected(page, 1, 2), rowsInSource: 4);
        m_export.SelectedFormat = ExportFormat.Csv;
        m_export.OutputPath = target;
        m_export.SelectedScope = ExportScope.Page;

        await StudioFixture.PressAsync(m_export.ExportCommand);

        Assert.That(await File.ReadAllLinesAsync(target), Has.Length.EqualTo(5), "a header and four rows");
    }

    #endregion

    #region Markdown

    /// <summary>
    /// The one format meant to be READ. A pipe inside a value would end the cell, so it is escaped; a
    /// newline would end the row, so it becomes a space. A broken table is worse than a value that
    /// lost its line break, and anyone who needs the value exactly has three other formats.
    /// </summary>
    [Test]
    public async Task MarkdownEscapesWhatWouldBreakTheTableAsync()
    {
        var page = new DataTable();
        page.Columns.Add("Name", typeof(string));
        page.Rows.Add("a | b");
        page.Rows.Add("two\nlines");

        var target = Path.Combine(m_studio.Root, "table.md");

        m_export.SetDataSource(page, "Orders");
        m_export.SelectedFormat = ExportFormat.Markdown;
        m_export.OutputPath = target;
        m_export.SelectedScope = ExportScope.Page;

        await StudioFixture.PressAsync(m_export.ExportCommand);

        var text = await File.ReadAllTextAsync(target);
        var lines = await File.ReadAllLinesAsync(target);

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(4), "a header, the rule, and two rows - not three rows");
            Assert.That(text, Does.Contain(@"a \| b"));
            Assert.That(text, Does.Contain("two lines"));
        });
    }

    #endregion

    #region Tools

    private static DataTable Page(int rows)
    {
        var table = new DataTable();

        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));

        for (var index = 0; index < rows; index++)
            table.Rows.Add(index, "row-" + index);

        return table;
    }

    private static List<DataRowView> Selected(DataTable page, int from, int count)
    {
        var view = new DataView(page);

        return Enumerable.Range(from, count).Select(index => view[index]).ToList();
    }

    #endregion
}
