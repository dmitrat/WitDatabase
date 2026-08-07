using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>The saved connections (WS-68).</summary>
public partial class ConnectionsDialog : Window
{
    public ConnectionsDialog()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, ConnectionsViewModel viewModel)
    {
        var dialog = new ConnectionsDialog { DataContext = viewModel };

        void OnCloseRequested(object? sender, EventArgs e) => dialog.Close();

        viewModel.CloseRequested += OnCloseRequested;

        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }
    }
}
