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
        var appVm = ViewModels.ApplicationViewModel.Instance;
        DataContext = appVm.MainWindowVm;
        
        // Set MainWindow reference for dialogs
        appVm.MainWindow = this;
        
        InitializeComponent();
    }

    #endregion
}