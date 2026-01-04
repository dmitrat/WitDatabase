using Avalonia.Controls;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    #region Constructors

    public MainWindow()
    {
        DataContext = ViewModels.ApplicationViewModel.Instance.MainWindowVm;
        InitializeComponent();
    }

    #endregion
}