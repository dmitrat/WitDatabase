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
/// The product's modal questions, as a closed set.
/// </summary>
/// <remarks>
/// <para>
/// <b>This enum is the catalogue `WS-67` describes</b> - "каждый модальный вопрос продукта перечислен
/// в «Данных» и отключается; список - это и есть полный перечень вопросов". Adding a question means
/// adding a member here and a setting beside it, and a test asserts the two lists match, so the
/// settings page cannot drift from what the application actually asks.
/// </para>
/// <para>
/// <b>Before 2026-08-10 the catalogue was inverted:</b> the settings page offered four ticked
/// checkboxes and the application asked none of the questions. Dropping a table ran
/// <c>DROP TABLE</c> straight from a context-menu click.
/// </para>
/// </remarks>
public enum ConfirmationKind
{
    /// <summary>An object is about to be dropped. <c>Settings.AskBeforeDroppingObject</c>.</summary>
    DroppingObject,

    /// <summary>An UPDATE or DELETE with no WHERE. <c>Settings.AskBeforeUnfilteredWrite</c>.</summary>
    UnfilteredWrite,

    /// <summary>A script with many statements. <c>Settings.AskBeforeLongScript</c>.</summary>
    LongScript
}

/// <summary>
/// A destructive action, described well enough for someone to decide about it.
/// </summary>
/// <param name="Kind">Which question this is, and therefore which setting governs it.</param>
/// <param name="Headline">What is about to happen, in one sentence, already localised.</param>
/// <param name="Sql">
/// The statement that will run. <b>Shown, always</b> - the canon's "клики собирают SQL, и он
/// показывается", and it is the difference between a warning and an explanation.
/// </param>
/// <param name="Consequences">
/// What breaks. Empty is a legitimate answer and is displayed as such: "nothing else refers to it" is
/// information, and leaving the section out would make an empty list indistinguishable from a list
/// nobody looked for.
/// </param>
public sealed record DestructiveAction(
    ConfirmationKind Kind,
    string Headline,
    string? Sql,
    IReadOnlyList<string> Consequences);

/// <summary>
/// Every question Studio asks before losing something. Behind an interface because the ViewModel layer
/// must not know about windows, and because a test has to be able to answer it.
/// </summary>
/// <remarks>
/// <b>The setting is consulted HERE, not at the call site.</b> That is deliberate and it is the fix
/// for the defect this interface grew out of: when each caller decides whether to ask, a caller that
/// forgets is invisible - which is exactly what happened, in all four places at once. One place
/// decides, so "is this question switched off" cannot be forgotten by anybody.
/// </remarks>
public interface IConfirmationService
{
    /// <summary>
    /// Asks what to do about edits that were never applied. <paramref name="changeCount"/> is the size
    /// of the edit buffer, so the question can name it rather than say "there are unsaved changes".
    /// </summary>
    /// <remarks>
    /// Governed by <c>Settings.AskBeforeClosingEditedTab</c>. With the question switched off the
    /// answer is <see cref="UnsavedChangesDecision.Discard"/> - the user asked not to be interrupted
    /// when closing, and the alternative reading, applying silently, would write to the database with
    /// nobody asked. Discarding loses only a buffer that was never applied.
    /// </remarks>
    Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount);

    /// <summary>
    /// Asks whether a destructive action should go ahead. Returns true to proceed.
    /// </summary>
    /// <remarks>
    /// With the governing setting switched off this returns true without showing anything - the user
    /// turned the question off, which is a decision to proceed, not a decision to be protected
    /// silently.
    /// </remarks>
    Task<bool> AskAboutDestructiveActionAsync(DestructiveAction action);
}

/// <summary>
/// The answer used when nothing has supplied a real one - a headless test, a design-time instance, a
/// host that forgot to register the Avalonia implementation.
///
/// It answers <see cref="UnsavedChangesDecision.Cancel"/> and <b>refuses</b> every destructive action,
/// so the absence of a confirmation service keeps the tab open with its edits intact and drops
/// nothing. The defect being fixed here is data disappearing without a question; a default that
/// discarded silently would reintroduce it through the back door, and a default that proceeded would
/// make a missing service indistinguishable from a granted permission.
/// </summary>
public sealed class KeepUnsavedChangesService : IConfirmationService
{
    public Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount)
    {
        return Task.FromResult(UnsavedChangesDecision.Cancel);
    }

    public Task<bool> AskAboutDestructiveActionAsync(DestructiveAction action)
    {
        return Task.FromResult(false);
    }
}
