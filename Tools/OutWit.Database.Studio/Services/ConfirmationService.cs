using Avalonia.Controls;
using Avalonia.Threading;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The Avalonia answer to <see cref="IConfirmationService"/>: a modal over the main window.
///
/// The owner is supplied by a callback rather than held, because the ViewModel graph is built before
/// the window exists and the service is a singleton that outlives any one window.
/// </summary>
public sealed class ConfirmationService : IConfirmationService
{
    #region Fields

    private readonly Func<Window?> m_owner;

    #endregion

    #region Constructors

    public ConfirmationService(Func<Window?> owner)
    {
        m_owner = owner;
    }

    #endregion

    #region IConfirmationService

    public async Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount)
    {
        var owner = m_owner();

        // No window to ask over: keep the edits rather than guess. Same reasoning as
        // KeepUnsavedChangesService - silence must never be the answer that destroys work.
        if (owner == null)
            return UnsavedChangesDecision.Cancel;

        if (Dispatcher.UIThread.CheckAccess())
            return await UnsavedChangesDialog.AskAsync(owner, title, changeCount);

        return await Dispatcher.UIThread.InvokeAsync(
            () => UnsavedChangesDialog.AskAsync(owner, title, changeCount));
    }

    #endregion
}
