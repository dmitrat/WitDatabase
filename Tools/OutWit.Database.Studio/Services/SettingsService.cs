using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;
using System.IO;
using System.Text.Json;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Implementation of <see cref="ISettingsService"/> using JSON file storage.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    #region Constants

    private const string SETTINGS_FILE_NAME = "settings.json";

    #endregion

    #region Fields

    private readonly ILogger<SettingsService> m_logger;
    private readonly string m_settingsFilePath;
    private Settings? m_cachedSettings;

    #endregion

    #region Constructors

    public SettingsService(ILogger<SettingsService> logger)
    {
        m_logger = logger;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "WitDatabase.Studio");
        Directory.CreateDirectory(appFolder);
        
        m_settingsFilePath = Path.Combine(appFolder, SETTINGS_FILE_NAME);
    }

    #endregion

    #region ISettingsService

    public async Task<Settings> LoadAsync()
    {
        if (m_cachedSettings != null)
            return m_cachedSettings;

        try
        {
            if (File.Exists(m_settingsFilePath))
            {
                var json = await File.ReadAllTextAsync(m_settingsFilePath);
                m_cachedSettings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                m_logger.LogInformation("Settings loaded from {Path}", m_settingsFilePath);
            }
            else
            {
                m_cachedSettings = new Settings();
                m_logger.LogInformation("Created default settings");
            }
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to load settings, using defaults");
            m_cachedSettings = new Settings();
        }

        return m_cachedSettings;
    }

    public async Task SaveAsync(Settings settings)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(m_settingsFilePath, json);
            
            m_cachedSettings = settings;
            m_logger.LogInformation("Settings saved to {Path}", m_settingsFilePath);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to save settings");
            throw;
        }
    }

    public async Task AddRecentFileAsync(string filePath)
    {
        var settings = await LoadAsync();
        
        // Remove if already exists
        settings.RecentFiles.Remove(filePath);
        
        // Add to the beginning
        settings.RecentFiles.Insert(0, filePath);
        
        // Trim to max size
        if (settings.RecentFiles.Count > settings.MaxRecentFiles)
        {
            settings.RecentFiles.RemoveRange(settings.MaxRecentFiles, 
                settings.RecentFiles.Count - settings.MaxRecentFiles);
        }
        
        await SaveAsync(settings);
    }

    public async Task RemoveRecentFileAsync(string filePath)
    {
        var settings = await LoadAsync();
        settings.RecentFiles.Remove(filePath);
        await SaveAsync(settings);
    }

    public async Task ClearRecentFilesAsync()
    {
        var settings = await LoadAsync();
        settings.RecentFiles.Clear();
        await SaveAsync(settings);
    }

    #endregion
}
