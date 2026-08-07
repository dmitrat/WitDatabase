using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>
/// The settings window. <b>Not modal</b> (WS-52): people come here to read a setting, go and look at
/// what it did, and come back, and a modal window forbids all three.
/// </summary>
public partial class SettingsDialog : Window
{
    #region Fields

    private static SettingsDialog? s_open;

    #endregion

    #region Constructors

    public SettingsDialog()
    {
        InitializeComponent();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Shows the window, or brings the one already open to the front. A second settings window would be
    /// two views of one live object, which is not wrong so much as pointless and confusing.
    /// </summary>
    public static Task ShowAsync(Window owner, SettingsViewModel viewModel)
    {
        if (s_open != null)
        {
            s_open.DataContext = viewModel;
            s_open.Activate();

            return Task.CompletedTask;
        }

        var dialog = new SettingsDialog { DataContext = viewModel };

        void OnCloseRequested(object? sender, EventArgs e) => dialog.Close();

        viewModel.CloseRequested += OnCloseRequested;

        dialog.Closed += (_, _) =>
        {
            viewModel.CloseRequested -= OnCloseRequested;
            s_open = null;
        };

        s_open = dialog;

        dialog.Show(owner);

        return Task.CompletedTask;
    }

    #endregion
}
