using OutWit.Database.AdoNet;
using OutWit.Database.Core.Utils;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// A byte copy of a database, which is not the same thing as a dump (WS-59).
/// </summary>
/// <remarks>
/// <para>
/// <b>A dump is a script and this is the bytes.</b> Encryption, the page layout, the format version
/// and the statistics all survive, so the copy opens instantly instead of having to be executed. That
/// is what makes it a backup rather than an export.
/// </para>
/// <para>
/// <b>A database is not one file, and the design's "byte copy of the file" would have lost the
/// indexes.</b> <c>DatabaseFiles</c> says it in its own words - the indexes live in a sibling
/// directory and the journal in a sibling file, both named after the data file. Both are taken. The
/// <c>.lock</c> sidecar is NOT: it is the marker of "somebody has this open", and a backup that
/// carries one is a backup that lies about itself.
/// </para>
/// <para>
/// <b>Writing is flushed, not paused.</b> The design says the writes are "приостановлена на время
/// копирования"; there is nothing in the engine that pauses them and nothing Studio can do about
/// another process. What it can do is <c>Checkpoint</c> first, so that what is staged in memory is on
/// the disk before the bytes are taken, and say plainly that a copy taken while something else is
/// writing may be inconsistent. The optional verification - open the copy and read its schema - is the
/// only assurance available, and it is honest about being one.
/// </para>
/// </remarks>
public static class DatabaseCopier
{
    #region Functions

    /// <summary>
    /// Copies everything the database owns to <paramref name="destination"/>.
    /// </summary>
    /// <param name="session">The connection whose database is being copied.</param>
    /// <param name="destination">The new file, or the new folder for an LSM database.</param>
    /// <param name="verify">Whether to open the copy afterwards and read its schema.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<CopyResult> CopyAsync(IDatabaseSession session, string destination,
        bool verify, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var source = session.Connection.FilePath ?? string.Empty;

        if (string.IsNullOrEmpty(source))
        {
            return new CopyResult(CopyOutcome.Failed, [],
                EngineMessage: "This connection has no path, so there is nothing to copy.");
        }

        // A paged database is one file and the engine holds it with no sharing at all, so this is not
        // "the copy might be inconsistent", it is "nothing can read it". Answered before trying, so
        // that the reason is a sentence rather than an IOException the user has to interpret.
        if (!Directory.Exists(source) && session.IsConnected)
            return new CopyResult(CopyOutcome.SourceIsHeldOpen, []);

        try
        {
            // What is staged goes to the disk first. It does not stop anyone writing afterwards - see
            // the remarks - but it does mean the copy is not missing what this connection had in hand.
            //
            // Only while there IS a connection: a closed session has nothing staged and nothing to ask,
            // and it is the state a paged database has to be in to be copied at all.
            if (session.IsConnected)
                await session.CheckpointAsync(ct);

            var parts = Directory.Exists(source)
                ? CopyDirectory(source, destination, ct)
                : CopyFileAndItsSiblings(source, destination, ct);

            if (!verify)
                return new CopyResult(CopyOutcome.Copied, parts);

            var (opened, objects, message) = await VerifyAsync(destination, session, ct);

            return new CopyResult(CopyOutcome.Copied, parts, opened, objects, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CopyResult(CopyOutcome.Failed, [], EngineMessage: ex.Message);
        }
    }

    /// <summary>
    /// The parts a database at this path owns and that exist right now.
    /// </summary>
    /// <remarks>
    /// Public because the dialog shows them before anything is copied: "what will be taken" is the
    /// question a person asks of a backup, and the answer is longer than they expect.
    /// </remarks>
    public static IReadOnlyList<string> PartsOf(string path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        if (Directory.Exists(path))
            return [path];

        var parts = new List<string>();

        if (File.Exists(path))
            parts.Add(path);

        if (DatabaseFiles.GetIndexDirectory(path) is { } indexes && Directory.Exists(indexes))
            parts.Add(indexes);

        if (DatabaseFiles.GetJournalPath(path) is { } journal && File.Exists(journal))
            parts.Add(journal);

        return parts;
    }

    #endregion

    #region Tools

    private static List<CopiedPart> CopyFileAndItsSiblings(string source, string destination,
        CancellationToken ct)
    {
        var parts = new List<CopiedPart>();

        var folder = Path.GetDirectoryName(destination);

        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);

        ct.ThrowIfCancellationRequested();

        File.Copy(source, destination, overwrite: true);
        parts.Add(new CopiedPart(Path.GetFileName(destination), new FileInfo(destination).Length));

        // The indexes, under the name the destination will be known by - a copy called backup.witdb
        // must find its indexes in backup.witdb_indexes and not in the source's directory.
        if (DatabaseFiles.GetIndexDirectory(source) is { } indexes && Directory.Exists(indexes)
            && DatabaseFiles.GetIndexDirectory(destination) is { } target)
        {
            parts.AddRange(CopyDirectory(indexes, target, ct));
        }

        if (DatabaseFiles.GetJournalPath(source) is { } journal && File.Exists(journal)
            && DatabaseFiles.GetJournalPath(destination) is { } journalTarget)
        {
            ct.ThrowIfCancellationRequested();

            File.Copy(journal, journalTarget, overwrite: true);
            parts.Add(new CopiedPart(Path.GetFileName(journalTarget),
                new FileInfo(journalTarget).Length));
        }

        return parts;
    }

    private static List<CopiedPart> CopyDirectory(string source, string destination,
        CancellationToken ct)
    {
        var parts = new List<CopiedPart>();

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);

            var folder = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.Copy(file, target, overwrite: true);

            parts.Add(new CopiedPart(Path.Combine(Path.GetFileName(destination), relative),
                new FileInfo(target).Length));
        }

        return parts;
    }

    /// <summary>
    /// Opens the copy and reads its schema.
    /// </summary>
    /// <remarks>
    /// With the SOURCE's connection string, minus its data source - a copy of an encrypted database
    /// needs the same password to be opened at all, and a verification that could not open it would
    /// report a failure that is about the check rather than about the copy.
    /// </remarks>
    private static async Task<(bool Opened, int Objects, string? Message)> VerifyAsync(
        string destination, IDatabaseSession session, CancellationToken ct)
    {
        try
        {
            var describes = session.Connection.Clone();
            describes.FilePath = destination;

            await using var connection = new WitDbConnection(describes.BuildConnectionString());

            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES";

            var value = await command.ExecuteScalarAsync(ct);

            return (true, value == null ? 0 : Convert.ToInt32(value), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    #endregion
}
