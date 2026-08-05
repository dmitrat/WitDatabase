namespace OutWit.Database.Studio.Services;

/// <summary>
/// What the user chose when told a tab has edits that were never applied.
/// </summary>
public enum UnsavedChangesDecision
{
    /// <summary>Apply the pending edits, then close.</summary>
    Apply,

    /// <summary>Throw the pending edits away and close.</summary>
    Discard,

    /// <summary>Do not close; leave the edits alone.</summary>
    Cancel
}

/// <summary>
/// The one question Studio has to ask before losing something. Behind an interface because the
/// ViewModel layer must not know about windows, and because a test has to be able to answer it.
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// Asks what to do about edits that were never applied. <paramref name="changeCount"/> is the size
    /// of the edit buffer, so the question can name it rather than say "there are unsaved changes".
    /// </summary>
    Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount);
}

/// <summary>
/// The answer used when nothing has supplied a real one - a headless test, a design-time instance, a
/// host that forgot to register the Avalonia implementation.
///
/// It answers <see cref="UnsavedChangesDecision.Cancel"/>, so the absence of a confirmation service
/// keeps the tab open with its edits intact. The defect being fixed here is data disappearing without
/// a question; a default that discarded silently would reintroduce it through the back door, and a
/// default that applied silently would write to the database with nobody asked.
/// </summary>
public sealed class KeepUnsavedChangesService : IConfirmationService
{
    public Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount)
    {
        return Task.FromResult(UnsavedChangesDecision.Cancel);
    }
}
