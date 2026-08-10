using Avalonia.Controls;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>
/// The question asked before something is destroyed. It shows the statement that will run and what
/// breaks, which is the canon's "деструктив называет последствия" - and deliberately does NOT ask for
/// the object's name to be typed, which the canon rejects as a ritual.
/// </summary>
public partial class DestructiveActionDialog : Window
{
    #region Constructors

    public DestructiveActionDialog()
    {
        InitializeComponent();

        ProceedButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Shows the question and returns whether to go ahead. Closing the window by any other means -
    /// Esc, the title bar - counts as "no", because that is the answer that destroys nothing.
    /// </summary>
    public static async Task<bool> AskAsync(Window owner, DestructiveAction action)
    {
        var dialog = new DestructiveActionDialog();
        var localization = ViewModels.ApplicationViewModel.Instance.Localization;

        dialog.HeadlineText.Text = action.Headline;

        // An empty consequence list is an ANSWER and is said out loud. Leaving the section out would
        // make "nothing else refers to this" indistinguishable from "nobody looked".
        dialog.ConsequencesHeader.Text = action.Consequences.Count == 0
            ? localization["Dialog.Destructive.NoConsequences"]
            : localization["Dialog.Destructive.Consequences"];

        dialog.ConsequencesList.ItemsSource = action.Consequences;
        dialog.ConsequencesList.IsVisible = action.Consequences.Count > 0;

        dialog.SqlText.Text = action.Sql ?? string.Empty;

        // A hidden control does not collapse its row: bind the row's own visibility, or the dialog
        // reserves 180 px for a box nobody can see. Same trap as the inspector panel in stage 5.
        dialog.SqlBlock.IsVisible = !string.IsNullOrWhiteSpace(action.Sql);

        return await dialog.ShowDialog<bool>(owner);
    }

    #endregion
}
