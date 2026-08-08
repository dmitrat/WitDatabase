using OutWit.Database.AdoNet;
using OutWit.Database.Core.Providers;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Gathers what the «База» tab shows, from the three places it is actually written down (WS-54).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three sources, and the tab names them.</b> The file system knows the size; the database's own
/// header - or, for LSM, its <c>provider.meta</c> sidecar - knows what it was created with; and the
/// open connection knows what its storage is doing now. Nothing here is guessed from anything else.
/// </para>
/// <para>
/// <b>The stored configuration and the live snapshot can disagree, and that is a fact worth having
/// rather than a bug to smooth over.</b> The sidecar says what the database was BUILT with; the
/// snapshot says which layers this connection actually assembled. They differ when a connection string
/// overrides something.
/// </para>
/// </remarks>
public static class DatabaseOverviewReader
{
    #region Functions

    /// <summary>
    /// Reads everything at once, so that the numbers on the tab are of one moment.
    /// </summary>
    public static async Task<DatabaseOverview> ReadAsync(IDatabaseSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var path = session.Connection.FilePath ?? string.Empty;

        var snapshot = await session.GetStorageSnapshotAsync(ct);

        // From the SESSION and not from the file. An open database holds an exclusive lock, so reading
        // its header here answers null - which is not "no configuration", it is "the database is open",
        // and it emptied the whole Configuration block on the one screen that exists to show it. The
        // session reads it a moment before opening, where it is readable and equally true.
        var stored = session.StoredConfiguration;

        var isDirectory = stored?.IsDirectory ?? Directory.Exists(path);
        var size = SizeOf(path);
        var pageSize = isDirectory || stored == null || stored.PageSize <= 0 ? (int?)null : stored.PageSize;

        var schema = await CountAsync(session, ct);

        return new DatabaseOverview(
            Path: path,
            IsDirectory: isDirectory,
            StoreProviderKey: snapshot.StoreProviderKey,
            SizeInBytes: size,
            PageSize: pageSize,
            PageCount: pageSize is { } bytes && bytes > 0 ? size / bytes : null,
            FormatVersion: stored?.FormatVersion,
            EncryptionProviderKey: stored is { Metadata.IsEncrypted: true }
                ? stored.Metadata.EncryptionProviderKey
                : null,
            // Null and not false: "the header could not be read" and "the feature is off" are
            // different answers, and one of them is a lie.
            HasTransactions: stored?.Metadata.HasTransactions,
            HasMvcc: stored?.Metadata.HasMvcc,
            HasFileLocking: stored?.Metadata.HasFileLocking,
            CacheProviderKey: stored?.Metadata.CacheProviderKey ?? string.Empty,
            CacheSizeInPages: stored?.Metadata.CacheSize ?? 0,
            JournalProviderKey: stored?.Metadata.JournalProviderKey ?? string.Empty,
            StoreChain: snapshot.Chain,
            Lsm: snapshot.Lsm,
            Schema: schema,

            // Asked of the path rather than of the connection: it is the same guard the engine uses to
            // refuse a second opener, so the answer is the one another application would get.
            IsInUse: !string.IsNullOrEmpty(path) && WitDbConnection.IsDatabaseInUse(path),

            ConfigurationIsAvailable: stored != null);
    }

    #endregion

    #region Tools

    private static async Task<SchemaCounts> CountAsync(IDatabaseSession session, CancellationToken ct)
    {
        var tables = await session.GetTablesAsync(ct);
        var views = await session.GetViewsAsync(ct);
        var indexes = await session.GetIndexesAsync(ct);
        var triggers = await session.GetTriggersAsync(ct);
        var sequences = await session.GetSequencesAsync(ct);
        var routines = await session.GetRoutinesAsync(ct);

        return new SchemaCounts(tables.Count, views.Count, indexes.Count, triggers.Count,
            sequences.Count, routines.Count);
    }

    /// <summary>
    /// The size of a file, or of everything in a folder - an LSM database is its directory.
    /// </summary>
    private static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;

            if (!Directory.Exists(path))
                return 0;

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    #endregion
}
