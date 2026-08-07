using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>The saved connections (WS-68).</summary>
public interface IConnectionProfileStore
{
    /// <summary>Every saved connection, in the order the user put them in.</summary>
    IReadOnlyList<ConnectionProfile> Profiles { get; }

    /// <summary>Reads the file if it has not been read.</summary>
    Task<IReadOnlyList<ConnectionProfile>> LoadAsync();

    /// <summary>
    /// Adds a connection, or updates the one already saved for the same path. Opening the same
    /// database twice must not make two entries.
    /// </summary>
    Task SaveAsync(ConnectionProfile profile);

    /// <summary>
    /// Takes a connection OUT OF THE LIST. It does not delete the database, and the button that calls
    /// it says "Remove" for that reason: deleting a database from the interface that manages databases
    /// is a function that will one day be pressed without looking, so it is not here at all.
    /// </summary>
    Task RemoveAsync(string path);

    /// <summary>Writes the list out now.</summary>
    Task FlushAsync();
}

/// <summary>
/// The saved connections, in <c>%AppData%\WitDatabase.Studio\connections.json</c>.
///
/// <para>
/// Beside the settings rather than inside them, because the two are cleared for different reasons:
/// "reset settings" must not read as "forget my databases".
/// </para>
/// </summary>
public sealed class ConnectionProfileStore : IConnectionProfileStore
{
    #region Constants

    private const string FILE_NAME = "connections.json";

    #endregion

    #region Fields

    private readonly ILogger<ConnectionProfileStore> m_logger;
    private readonly string m_path;
    private readonly Lock m_lock = new();

    private List<ConnectionProfile>? m_profiles;

    #endregion

    #region Constructors

    public ConnectionProfileStore(ILogger<ConnectionProfileStore> logger, string? path = null)
    {
        m_logger = logger;
        m_path = path ?? DefaultPath();

        var folder = Path.GetDirectoryName(m_path);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
    }

    #endregion

    #region Functions

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Combine(appData, "WitDatabase.Studio", FILE_NAME);
    }

    private List<ConnectionProfile> Current
    {
        get
        {
            if (m_profiles != null)
                return m_profiles;

            lock (m_lock)
            {
                m_profiles ??= Read();
            }

            return m_profiles;
        }
    }

    private List<ConnectionProfile> Read()
    {
        try
        {
            if (!File.Exists(m_path))
                return [];

            return JsonSerializer.Deserialize<List<ConnectionProfile>>(File.ReadAllText(m_path)) ?? [];
        }
        catch (Exception ex)
        {
            // A list that will not parse is a file a person may have edited. Starting empty is right;
            // overwriting theirs before they have seen the problem is not, so nothing is written until
            // something actually changes.
            m_logger.LogError(ex, "Failed to read the saved connections");
            return [];
        }
    }

    private async Task WriteAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(m_path, json);
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "Failed to write the saved connections");
        }
    }

    #endregion

    #region IConnectionProfileStore

    public IReadOnlyList<ConnectionProfile> Profiles => Current;

    public Task<IReadOnlyList<ConnectionProfile>> LoadAsync()
    {
        return Task.FromResult<IReadOnlyList<ConnectionProfile>>(Current);
    }

    public async Task SaveAsync(ConnectionProfile profile)
    {
        lock (m_lock)
        {
            var existing = Current.FindIndex(saved =>
                string.Equals(saved.Path, profile.Path, StringComparison.OrdinalIgnoreCase));

            if (existing >= 0)
                Current[existing] = profile;
            else
                Current.Add(profile);
        }

        await WriteAsync();
    }

    public async Task RemoveAsync(string path)
    {
        lock (m_lock)
        {
            Current.RemoveAll(saved => string.Equals(saved.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        await WriteAsync();
    }

    public Task FlushAsync()
    {
        return WriteAsync();
    }

    #endregion
}
