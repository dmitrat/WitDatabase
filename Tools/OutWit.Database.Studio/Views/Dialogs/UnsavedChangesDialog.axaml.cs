using Avalonia.Controls;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>
/// The question asked before edits that were never applied would be lost. Three answers, named by what
/// they do rather than Yes / No / Cancel.
/// </summary>
public partial class UnsavedChangesDialog : Window
{
    #region Constructors

    public UnsavedChangesDialog()
    {
        InitializeComponent();

        ApplyButton.Click += (_, _) => Close(UnsavedChangesDecision.Apply);
        DiscardButton.Click += (_, _) => Close(UnsavedChangesDecision.Discard);
        CancelButton.Click += (_, _) => Close(UnsavedChangesDecision.Cancel);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Shows the question and returns the answer. Closing the window by any other means - Esc, the
    /// title bar - counts as Cancel, because that is the answer that loses nothing.
    /// </summary>
    public static async Task<UnsavedChangesDecision> AskAsync(Window owner, string title, int changeCount)
    {
        var dialog = new UnsavedChangesDialog();

        dialog.HeaderText.Text = changeCount == 1
            ? $"{title} - 1 unsaved change"
            : $"{title} - {changeCount} unsaved changes";

        return await dialog.ShowDialog<UnsavedChangesDecision>(owner);
    }

    #endregion
}
