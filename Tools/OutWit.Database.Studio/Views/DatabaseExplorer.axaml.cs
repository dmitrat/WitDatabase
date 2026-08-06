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

    #endregion
}
