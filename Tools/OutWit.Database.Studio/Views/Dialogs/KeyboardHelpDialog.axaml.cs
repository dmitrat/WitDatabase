using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>The keyboard reference (9.6, WS-69).</summary>
public partial class KeyboardHelpDialog : Window
{
    public KeyboardHelpDialog()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, KeyboardHelpViewModel viewModel)
    {
        var dialog = new KeyboardHelpDialog { DataContext = viewModel };

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
