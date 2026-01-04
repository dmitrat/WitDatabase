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
    }

    #endregion
}
