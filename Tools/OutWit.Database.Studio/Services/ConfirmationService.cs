using Avalonia.Controls;
using Avalonia.Threading;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The Avalonia answer to <see cref="IConfirmationService"/>: a modal over the main window.
///
/// The owner is supplied by a callback rather than held, because the ViewModel graph is built before
/// the window exists and the service is a singleton that outlives any one window.
/// </summary>
/// <remarks>
/// <b>This type is where a question is switched on or off</b>, and it is the reason the settings can
/// be trusted again. Before 2026-08-10 the four "ask before" settings had no reader anywhere; each
/// call site decided for itself whether to ask, and all four decided not to.
/// </remarks>
public sealed class ConfirmationService : IConfirmationService
{
    #region Fields

    private readonly Func<Window?> m_owner;
    private readonly Func<Settings> m_settings;

    #endregion

    #region Constructors

    public ConfirmationService(Func<Window?> owner, Func<Settings> settings)
    {
        m_owner = owner;
        m_settings = settings;
    }

    #endregion

    #region IConfirmationService

    public async Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount)
    {
        // The question the user switched off. Discard rather than apply: closing a tab must never
        // write to the database with nobody asked, and what is lost is a buffer that was never
        // applied. Inverting this to Apply would be a decision about somebody's data taken by a
        // checkbox labelled "ask me".
        if (!m_settings().AskBeforeClosingEditedTab)
            return UnsavedChangesDecision.Discard;

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

    public async Task<bool> AskAboutDestructiveActionAsync(DestructiveAction action)
    {
        if (!IsEnabled(action.Kind))
            return true;

        var owner = m_owner();

        // Nothing to ask over. Refuse rather than proceed: a missing window must not read as
        // permission, which is the same rule KeepUnsavedChangesService follows.
        if (owner == null)
            return false;

        if (Dispatcher.UIThread.CheckAccess())
            return await DestructiveActionDialog.AskAsync(owner, action);

        return await Dispatcher.UIThread.InvokeAsync(
            () => DestructiveActionDialog.AskAsync(owner, action));
    }

    #endregion

    #region Tools

    /// <summary>
    /// The catalogue's one mapping from a question to the setting that governs it. Exhaustive by
    /// construction - a new <see cref="ConfirmationKind"/> that is not listed here fails to compile.
    /// </summary>
    private bool IsEnabled(ConfirmationKind kind)
    {
        var settings = m_settings();

        return kind switch
        {
            ConfirmationKind.DroppingObject => settings.AskBeforeDroppingObject,
            ConfirmationKind.UnfilteredWrite => settings.AskBeforeUnfilteredWrite,
            ConfirmationKind.LongScript => settings.AskBeforeLongScript,
            _ => true
        };
    }

    #endregion
}
