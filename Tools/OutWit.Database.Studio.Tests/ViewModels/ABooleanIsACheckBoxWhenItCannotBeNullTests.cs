using OutWit.Database.Studio.Controls;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// A BOOLEAN that cannot be NULL is drawn as a checkbox; one that can is not.
/// </summary>
/// <remarks>
/// <para>
/// Decided 2026-08-15 (WS-34): a checkbox for <c>NOT NULL</c> columns, the existing text for nullable
/// ones. <b>Never NULL as an unchecked box</b> - a checkbox has two states and the column has three,
/// so drawing the third as one of the other two would make an unset value indistinguishable from
/// false, in the one place where a person edits data.
/// </para>
/// <para>
/// The choice is a function rather than a branch inside the column factory, so that the decision can
/// be asked a question without a window. What it cannot answer is whether the box is DRAWN, which is
/// what driving Studio is for.
/// </para>
/// </remarks>
[TestFixture]
public class ABooleanIsACheckBoxWhenItCannotBeNullTests
{
    #region The choice

    [Test]
    public void ABooleanThatCannotBeNullIsACheckBoxTest()
    {
        Assert.That(EditableDataGrid.WantsACheckBox(Column("BOOLEAN", nullable: false)), Is.True);
    }

    [Test]
    public void ANullableBooleanKeepsItsTextTest()
    {
        Assert.That(EditableDataGrid.WantsACheckBox(Column("BOOLEAN", nullable: true)), Is.False,
            "a checkbox has two states and this column has three");
    }

    [TestCase("VARCHAR")]
    [TestCase("INTEGER")]
    [TestCase("BOOLEANISH")]
    public void NothingElseBecomesACheckBoxTest(string type)
    {
        Assert.That(EditableDataGrid.WantsACheckBox(Column(type, nullable: false)), Is.False);
    }

    [Test]
    public void AColumnNobodyDescribedKeepsItsTextTest()
    {
        Assert.That(EditableDataGrid.WantsACheckBox(null), Is.False,
            "a column the catalogue said nothing about is text, like everything else");
    }

    #endregion

    #region The write path

    /// <summary>
    /// Whatever the widget, a boolean still reaches the database as a boolean.
    /// </summary>
    [Test]
    public async Task ABooleanColumnStillCommitsBothValuesTest()
    {
        await using var studio = await StudioFixture.CreateAsync();

        await studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Flags (Id INTEGER PRIMARY KEY, Ready BOOLEAN NOT NULL)");
        await studio.Database.ExecuteNonQueryAsync("INSERT INTO Flags (Id, Ready) VALUES (1, FALSE)");

        var editor = await studio.Workspace.OpenTableEditTabAsync(studio.Database, "Flags");

        await editor.LoadDataAsync();

        var view = editor.CurrentView![0];

        view.Row["Ready"] = true;
        editor.CellEditedCommand.Execute(view);

        await StudioFixture.PressAsync(editor.CommitCommand);

        var result = await studio.Database.ExecuteQueryAsync("SELECT Ready FROM Flags");

        Assert.Multiple(() =>
        {
            Assert.That(editor.HasError, Is.False, editor.StatusMessage);
            Assert.That(result.Data!.Rows[0][0], Is.EqualTo(true));
        });
    }

    #endregion

    #region Tools

    private static ColumnInfo Column(string type, bool nullable)
    {
        return new ColumnInfo
        {
            Name = "Flag",
            DataType = type,
            IsNullable = nullable
        };
    }

    #endregion
}
