using System.Text;

namespace OutWit.Database.AdoNet.Engines;

/// <summary>
/// Works out which database a connection string addresses, and which of its settings shape the engine.
/// </summary>
/// <remarks>
/// Two questions, deliberately separate:
///
/// <list type="bullet">
/// <item><b>Identity</b> - do two connection strings name the same database? Decided by the data source
/// alone, resolved to a full path, so <c>db.witdb</c> and <c>./db.witdb</c> share one engine as they
/// must: they are one file, and one file admits one engine.</item>
/// <item><b>Signature</b> - do they ask for the same <i>kind</i> of engine? Decided by everything except
/// the data source and the purely connection-level settings. Sharing an engine built with a different
/// store, password or journal would hand the second caller somebody else's database, so that is refused
/// with an explanation rather than silently allowed.</item>
/// </list>
/// </remarks>
internal static class SharedDatabaseKey
{
    #region Constants

    /// <summary>
    /// Settings that belong to a connection rather than to the engine, and so must not make two
    /// connections stop sharing.
    /// </summary>
    private static readonly HashSet<string> s_connectionOnlyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data Source",
        "Pooling",
        "Min Pool Size",
        "Max Pool Size",
        "Default Timeout"
    };

    #endregion

    #region Identity

    /// <summary>
    /// The canonical identity of the database a connection string addresses, or null when there is
    /// nothing to share.
    /// </summary>
    /// <remarks>
    /// Null for an in-memory database. Each <c>Data Source=:memory:</c> connection gets its own private
    /// database, which is what it did before 5.0.0 and what SQLite does until asked for
    /// <c>Cache=Shared</c>. Making them share would be a separate, opt-in feature; quietly turning
    /// isolated in-memory databases into one shared database would break every test that relies on
    /// getting a clean one.
    /// </remarks>
    public static string? TryResolve(WitDbConnectionStringBuilder options)
    {
        var dataSource = options.DataSource;

        if (string.IsNullOrEmpty(dataSource))
            return null;

        if (options.Mode == WitDbConnectionMode.Memory ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(dataSource);

            // Case-insensitive on Windows and macOS, where two spellings of one path are one file, and
            // case-sensitive on Linux, where they are not.
            return OperatingSystem.IsLinux() ? full : full.ToLowerInvariant();
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unusable path is not this class's problem to report: let the builder fail with its own
            // message rather than pre-empting it with a worse one.
            return null;
        }
    }

    #endregion

    #region Signature

    /// <summary>
    /// The canonical form of the settings that shape the engine, for comparing two connection strings
    /// that address the same database.
    /// </summary>
    public static string BuildSignature(WitDbConnectionStringBuilder options)
    {
        var parts = new List<string>();

        foreach (string key in options.Keys)
        {
            if (s_connectionOnlyKeys.Contains(key))
                continue;

            parts.Add($"{key.ToLowerInvariant()}={options[key]}");
        }

        // Sorted, so that the same settings written in a different order are the same signature.
        parts.Sort(StringComparer.Ordinal);

        return parts.Count == 0
            ? "<defaults>"
            : string.Join(";", parts);
    }

    #endregion
}
