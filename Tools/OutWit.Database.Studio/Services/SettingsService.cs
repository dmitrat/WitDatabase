using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The settings, in <c>%AppData%\WitDatabase.Studio\settings.json</c>, held live (WS-52).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    #region Constants

    private const string SETTINGS_FILE_NAME = "settings.json";

    #endregion

    #region Fields

    private readonly ILogger<SettingsService> m_logger;
    private readonly string m_settingsFilePath;
    private readonly Lock m_lock = new();

    private Settings? m_settings;

    /// <summary>
    /// Writes are chained rather than fired off in parallel. Two settings changed in the same second -
    /// which is what dragging a font size does - would otherwise race for the same file, and the
    /// surviving write is whichever finished last rather than whichever happened last.
    /// </summary>
    private Task m_writing = Task.CompletedTask;

    #endregion

    #region Constructors

    /// <summary>
    /// The settings file is <c>%AppData%\WitDatabase.Studio\settings.json</c> unless a path is given.
    ///
    /// The parameter exists so that a test can use the REAL service instead of a double: the path was
    /// baked into the constructor, so anything exercising settings honestly would have read and
    /// written the settings of whoever ran the suite.
    /// </summary>
    public SettingsService(ILogger<SettingsService> logger, string? settingsFilePath = null)
    {
        m_logger = logger;

        m_settingsFilePath = settingsFilePath ?? DefaultPath();

        var folder = Path.GetDirectoryName(m_settingsFilePath);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Where settings live when nothing says otherwise. Public so that the diagnostics section can
    /// show it rather than describe it.
    /// </summary>
    public static string DefaultPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Combine(appDataPath, "WitDatabase.Studio", SETTINGS_FILE_NAME);
    }

    private Settings Read()
    {
        try
        {
            if (!File.Exists(m_settingsFilePath))
            {
                m_logger.LogInformation("Created default settings");
                return new Settings();
            }

            var json = File.ReadAllText(m_settingsFilePath);
            var settings = JsonSerializer.Deserialize<Settings>(json) ?? new Settings();

            m_logger.LogInformation("Settings loaded from {Path}", m_settingsFilePath);

            return settings;
        }
        catch (Exception ex)
        {
            // A settings file that will not parse is a file a person may have edited. Starting with
            // defaults is right; overwriting theirs before they have seen the problem is not, so
            // nothing is written until something actually changes.
            m_logger.LogError(ex, "Failed to load settings, using defaults");
            return new Settings();
        }
    }

    private void Attach(Settings settings)
    {
        settings.PropertyChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed?.Invoke(this, new SettingChangedEventArgs(e.PropertyName ?? string.Empty));

        Persist();
    }

    /// <summary>
    /// Persists in the background, one write at a time. Failures are logged and swallowed: a settings
    /// file that cannot be written is not a reason to take a window down in the middle of a session.
    /// </summary>
    private void Persist()
    {
        var settings = Current;

        lock (m_lock)
        {
            m_writing = m_writing.ContinueWith(_ => WriteAsync(settings)).Unwrap();
        }
    }

    private async Task WriteAsync(Settings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(m_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to save settings");
        }
    }

    #endregion

    #region ISettingsService

    public Settings Current
    {
        get
        {
            if (m_settings != null)
                return m_settings;

            lock (m_lock)
            {
                if (m_settings == null)
                {
                    m_settings = Read();
                    Attach(m_settings);
                }
            }

            return m_settings;
        }
    }

    public Task<Settings> LoadAsync()
    {
        return Task.FromResult(Current);
    }

    /// <summary>
    /// Writes now and waits.
    ///
    /// <para>
    /// A caller that hands over a DIFFERENT object has its values copied onto the live one rather than
    /// replacing it. The live instance is what the whole application is bound to; swapping it would
    /// leave every open window reading an object nobody writes to any more, which on screen is
    /// indistinguishable from a setting that stopped applying.
    /// </para>
    /// </summary>
    public async Task SaveAsync(Settings settings)
    {
        if (!ReferenceEquals(settings, Current))
            Current.CopyFrom(settings);

        Task pending;

        lock (m_lock)
        {
            m_writing = m_writing.ContinueWith(_ => WriteAsync(Current)).Unwrap();
            pending = m_writing;
        }

        await pending;

        m_logger.LogInformation("Settings saved to {Path}", m_settingsFilePath);
    }

    public Task FlushAsync()
    {
        lock (m_lock)
        {
            return m_writing;
        }
    }

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public async Task AddRecentFileAsync(string filePath)
    {
        var settings = Current;

        settings.RecentFiles.Remove(filePath);
        settings.RecentFiles.Insert(0, filePath);

        if (settings.RecentFiles.Count > settings.MaxRecentFiles)
        {
            settings.RecentFiles.RemoveRange(settings.MaxRecentFiles,
                settings.RecentFiles.Count - settings.MaxRecentFiles);
        }

        // The list is mutated in place, so no property has changed and nothing has been raised. This is
        // the one collection setting, and it says so rather than being quietly different.
        await SaveAsync(settings);
    }

    public async Task RemoveRecentFileAsync(string filePath)
    {
        var settings = Current;

        settings.RecentFiles.Remove(filePath);

        await SaveAsync(settings);
    }

    public async Task ClearRecentFilesAsync()
    {
        var settings = Current;

        settings.RecentFiles.Clear();

        await SaveAsync(settings);
    }

    public async Task ResetAsync()
    {
        var fresh = new Settings
        {
            // The recent list is not a setting: it is what the user has been doing. "Reset settings"
            // must not read as "forget my databases", which is why it is carried across.
            RecentFiles = Current.RecentFiles.ToList()
        };

        Current.CopyFrom(fresh);

        await SaveAsync(Current);
    }

    #endregion
}
