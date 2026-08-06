using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.ViewModels;

/// <summary>
/// The schema-changing actions of the tree, deferred here from stage 5: F2 and TRUNCATE.
///
/// The interesting part is what is NOT offered. Measured 2026-08-06: only a table can be renamed -
/// ALTER VIEW, ALTER INDEX and ALTER TRIGGER do not exist in this language at all - so F2 on a view or
/// an index would be a key that cannot work, and the tree does not pretend otherwise.
/// </summary>
[TestFixture]
public class ExplorerSchemaActionsTests
{
    private StudioFixture m_fixture = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        m_fixture = await StudioFixture.CreateAsync();

        await m_fixture.Explorer.RefreshAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        await m_fixture.DisposeAsync();
    }

    private DatabaseNode Find(string name, DatabaseNodeType type)
    {
        var node = Walk(m_fixture.Explorer.Nodes)
            .FirstOrDefault(n => n.NodeType == type && n.Name == name);

        Assert.That(node, Is.Not.Null, $"{type} {name} is not in the tree.");

        return node!;
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

    #region Rename

    [Test]
    public void RenameIsOfferedForATableAndForNothingElse()
    {
        var explorer = m_fixture.Explorer;

        explorer.SelectedNode = Find("Orders", DatabaseNodeType.Table);
        Assert.That(explorer.CanRename, Is.True);

        explorer.SelectedNode = Find("ActiveOrders", DatabaseNodeType.View);
        Assert.That(explorer.CanRename, Is.False, "There is no ALTER VIEW on this engine.");

        explorer.SelectedNode = Find("IX_Orders_CustomerId", DatabaseNodeType.Index);
        Assert.That(explorer.CanRename, Is.False, "and no ALTER INDEX.");

        explorer.SelectedNode = Find("TR_Orders_Audit", DatabaseNodeType.Trigger);
        Assert.That(explorer.CanRename, Is.False, "and no ALTER TRIGGER.");
    }

    [Test]
    public async Task F2RenamesTheTableAndTheRowsComeWithItAsync()
    {
        var explorer = m_fixture.Explorer;
        var node = Find("Logs", DatabaseNodeType.Table);

        explorer.SelectedNode = node;
        explorer.BeginRenameCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(node.IsRenaming, Is.True);
            Assert.That(node.RenameText, Is.EqualTo("Logs"), "The box opens on the name it has.");
        });

        node.RenameText = "LogLines";

        await StudioFixture.PressAsync(explorer.CommitRenameCommand);

        var tables = await m_fixture.Database.GetTablesAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(tables.Select(t => t.Name), Does.Contain("LogLines"));
            Assert.That(tables.Select(t => t.Name), Does.Not.Contain("Logs"));
            Assert.That(await m_fixture.CountRowsAsync("LogLines"), Is.EqualTo(2),
                "and the rows are still in it.");
        });
    }

    [Test]
    public async Task EscapePutsTheOldNameBackAsync()
    {
        var explorer = m_fixture.Explorer;
        var node = Find("Logs", DatabaseNodeType.Table);

        explorer.SelectedNode = node;
        explorer.BeginRenameCommand.Execute(null);

        node.RenameText = "SomethingElse";
        explorer.CancelRenameCommand.Execute(null);

        Assert.That(node.IsRenaming, Is.False);
        Assert.That(node.RenameText, Is.EqualTo("Logs"));

        var tables = await m_fixture.Database.GetTablesAsync();

        Assert.That(tables.Select(t => t.Name), Does.Contain("Logs"), "and nothing was renamed.");
    }

    [Test]
    public async Task RenamingToTheSameNameDoesNothingAsync()
    {
        var explorer = m_fixture.Explorer;
        var node = Find("Logs", DatabaseNodeType.Table);

        explorer.SelectedNode = node;
        explorer.BeginRenameCommand.Execute(null);

        await StudioFixture.PressAsync(explorer.CommitRenameCommand);

        Assert.That(explorer.ErrorMessage, Is.Null.Or.Empty,
            "Pressing Enter without typing is not an error.");

        Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.EqualTo(2));
    }

    #endregion

    #region Truncate

    [Test]
    public async Task TruncateEmptiesTheTableAndLeavesItThereAsync()
    {
        var explorer = m_fixture.Explorer;

        explorer.SelectedNode = Find("Logs", DatabaseNodeType.Table);

        Assert.That(explorer.CanTruncate, Is.True);
        Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.EqualTo(2));

        await StudioFixture.PressAsync(explorer.TruncateTableCommand);

        Assert.Multiple(async () =>
        {
            Assert.That(await m_fixture.CountRowsAsync("Logs"), Is.Zero);

            var tables = await m_fixture.Database.GetTablesAsync();

            Assert.That(tables.Select(t => t.Name), Does.Contain("Logs"),
                "Emptied, not dropped.");
        });
    }

    [Test]
    public void TruncateIsOfferedForATableOnly()
    {
        var explorer = m_fixture.Explorer;

        explorer.SelectedNode = Find("ActiveOrders", DatabaseNodeType.View);

        Assert.That(explorer.CanTruncate, Is.False);
    }

    #endregion
}
