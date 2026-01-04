using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Table structure view control.
/// </summary>
public partial class TableStructure : UserControl
{
    #region Constructors

    public TableStructure()
    {
        InitializeComponent();
        DataContext = ApplicationViewModel.Instance;
    }

    #endregion
}
