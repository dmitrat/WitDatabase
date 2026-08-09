using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>A newer Studio has been published (9.8, WS-70).</summary>
public partial class UpdateDialog : Window
{
    public UpdateDialog()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, UpdateViewModel viewModel)
    {
        var dialog = new UpdateDialog { DataContext = viewModel };

        void OnCloseRequested() => dialog.Close();

        viewModel.ShouldCloseDialog += OnCloseRequested;

        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnCloseRequested;
        }
    }
}
