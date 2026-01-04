using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Service for managing application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads settings from disk.
    /// </summary>
    Task<Settings> LoadAsync();

    /// <summary>
    /// Saves settings to disk.
    /// </summary>
    Task SaveAsync(Settings settings);

    /// <summary>
    /// Adds a file to the recent files list.
    /// </summary>
    Task AddRecentFileAsync(string filePath);

    /// <summary>
    /// Removes a file from the recent files list.
    /// </summary>
    Task RemoveRecentFileAsync(string filePath);

    /// <summary>
    /// Clears the recent files list.
    /// </summary>
    Task ClearRecentFilesAsync();
}
