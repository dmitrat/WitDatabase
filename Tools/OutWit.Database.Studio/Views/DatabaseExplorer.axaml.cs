using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Database explorer tree view control.
/// </summary>
public partial class DatabaseExplorer : UserControl
{
    #region Constructors

    public DatabaseExplorer()
    {
        InitializeComponent();
        DataContext = ApplicationViewModel.Instance;

        DoubleTapped += OnDoubleTapped;
        KeyDown += OnKeyDown;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// A double click opens the DATA of a table, not its structure (WS-19).
    ///
    /// It used to open the structure, with the data hidden in the context menu - while looking at the
    /// data is what people come to a database tool to do, by an order of magnitude. The structure
    /// stays on Ctrl+Enter and in the menu.
    /// </summary>
    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;
        var node = explorer.SelectedNode;

        if (node == null)
            return;

        switch (node.NodeType)
        {
            case Models.DatabaseNodeType.Table when explorer.CanEditData:
                explorer.EditDataCommand.Execute(null);
                break;

            case Models.DatabaseNodeType.View when explorer.CanBrowseData:
                explorer.SelectTop1000Command.Execute(null);
                break;
        }
    }

    /// <summary>
    /// F2 opens the rename box, deferred here from stage 5. Stage 4's lesson applies: a KeyBinding on
    /// the window needs the event to bubble from a focused element, and the tree is one - so this
    /// handler sits on the control that has the focus rather than on the shell.
    /// </summary>
    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.F2)
            return;

        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;

        if (!explorer.CanRename)
            return;

        explorer.BeginRenameCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// Enter renames, Escape puts the old name back. Both close the box.
    /// </summary>
    private void RenameBox_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var explorer = ApplicationViewModel.Instance.DatabaseExplorerVm;

        switch (e.Key)
        {
            case Avalonia.Input.Key.Enter:
                explorer.CommitRenameCommand.Execute(null);
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Escape:
                explorer.CancelRenameCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// The caret goes into the box as it appears. Stage 4 found this the hard way with the command
    /// palette: a box that is visible and not focused is a box the user has to click.
    /// </summary>
    private void RenameBox_Attached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        box.Focus();
        box.SelectAll();
    }

    #endregion
}
