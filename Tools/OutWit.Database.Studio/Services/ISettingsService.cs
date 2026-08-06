using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The settings, held live (WS-52).
///
/// <para>
/// <b>There is no Save.</b> <see cref="Current"/> is one object that everything binds to and reads
/// from; changing a property on it applies the change and persists it. A setting that does not act
/// until a button is pressed makes the user guess the result instead of seeing it, and the guessing is
/// worst exactly where the setting is hardest to describe - a date format, a keyword case, a page size.
/// </para>
/// <para>
/// <see cref="LoadAsync"/> is kept because the file is read from disk once and that read is
/// asynchronous. It answers with the same live object.
/// </para>
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// The live settings. Never null; reads the file on first access if <see cref="LoadAsync"/> has not
    /// been called yet.
    /// </summary>
    Settings Current { get; }

    /// <summary>Reads the settings file if it has not been read, and answers with <see cref="Current"/>.</summary>
    Task<Settings> LoadAsync();

    /// <summary>
    /// Writes the settings out now. Nothing needs to call this to make a change stick - a change
    /// persists itself - but leaving the application does, so that a write still in flight is finished
    /// rather than dropped.
    /// </summary>
    Task SaveAsync(Settings settings);

    /// <summary>
    /// Waits for the write a change started. Changing a setting persists it in the background, so
    /// nothing in the application needs this - but anything that wants to READ the file afterwards
    /// does, and that is a test asking whether the change really landed.
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// Raised after any property of <see cref="Current"/> has changed, carrying its name. This is the
    /// mechanism behind "applied immediately": whoever reads a setting listens rather than being told
    /// by a dialog that it has been saved.
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? Changed;

    /// <summary>Adds a database to the recent list, most recent first, trimmed to the configured length.</summary>
    Task AddRecentFileAsync(string filePath);

    /// <summary>Removes a database from the recent list.</summary>
    Task RemoveRecentFileAsync(string filePath);

    /// <summary>Empties the recent list.</summary>
    Task ClearRecentFilesAsync();

    /// <summary>
    /// Puts every setting back to its default. Deliberately does NOT touch the saved connections or
    /// anything in the credential store: "reset settings" is about this window, and a person pressing
    /// it is not asking to lose their list of databases.
    /// </summary>
    Task ResetAsync();
}

/// <summary>Which setting changed.</summary>
public sealed class SettingChangedEventArgs(string propertyName) : EventArgs
{
    /// <summary>The name of the property on <see cref="Settings"/>.</summary>
    public string PropertyName { get; } = propertyName;
}
