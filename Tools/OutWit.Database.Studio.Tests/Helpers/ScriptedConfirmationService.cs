using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Helpers;

/// <summary>
/// Answers the unsaved-changes question with a scripted decision, and records that it was asked.
///
/// The last stand-in left in this project, and it stands in for a PERSON rather than for a service:
/// there is no honest way for a headless test to press a button in a modal. "Was the user asked at
/// all" is half of what the question is for - a close that silently applies is as wrong as one that
/// silently discards, and only the count can tell those apart.
/// </summary>
public sealed class ScriptedConfirmationService : IConfirmationService
{
    #region Constructors

    public ScriptedConfirmationService(UnsavedChangesDecision decision)
    {
        Decision = decision;
    }

    #endregion

    #region IConfirmationService

    public Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount)
    {
        TimesAsked++;
        LastTitle = title;
        LastChangeCount = changeCount;

        return Task.FromResult(Decision);
    }

    public Task<bool> AskAboutDestructiveActionAsync(DestructiveAction action)
    {
        DestructiveQuestions.Add(action);

        return Task.FromResult(AllowDestructive);
    }

    #endregion

    #region Destructive actions

    /// <summary>
    /// The answer every destructive question gets. <b>Defaults to false</b>, so a case that forgets to
    /// set it cannot destroy anything by accident - and a case that expects the drop to go through has
    /// to say so, which makes the permission visible in the test rather than assumed.
    /// </summary>
    public bool AllowDestructive { get; set; }

    /// <summary>
    /// Every destructive question asked, in order. The QUESTION is the artifact worth asserting on -
    /// "was the user told what breaks" is a different claim from "did the drop happen", and only this
    /// list can answer the first one.
    /// </summary>
    public List<DestructiveAction> DestructiveQuestions { get; } = [];

    #endregion

    #region Properties

    /// <summary>The answer every question gets. Set it per case.</summary>
    public UnsavedChangesDecision Decision { get; set; }

    public int TimesAsked { get; private set; }

    public int LastChangeCount { get; private set; }

    public string? LastTitle { get; private set; }

    #endregion
}
