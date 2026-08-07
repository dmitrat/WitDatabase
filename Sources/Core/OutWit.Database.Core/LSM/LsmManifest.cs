using System.Globalization;

namespace OutWit.Database.Core.LSM;

/// <summary>
/// The list of SSTables that are live, stated in a file instead of inferred from the directory.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> The live set used to be
/// <c>Directory.GetFiles(directory, "sst_*.sst")</c> sorted by name. A full compaction merges every
/// SSTable into one and <b>drops the tombstones</b> - legitimate only because the output is meant to
/// replace the whole set - and then deletes the inputs with each failure swallowed. So an input the
/// compaction did not manage to delete, because the process died between the two or a scanner held
/// the handle, came back as live data on the next open with nothing left to mask it, and rows deleted
/// before the crash were readable again. Measured, and pinned as
/// <c>CrashedCompactionResurrectsDeletedRowsTest</c>.
/// </para>
/// <para>
/// <b>What makes it safe is the ORDER, not the file.</b> A compaction writes its output, then
/// publishes a manifest naming only the output, and only then deletes the inputs. A crash before the
/// publish leaves the old manifest and an orphaned output - the inputs are still the truth. A crash
/// after it leaves the new manifest and some undeleted inputs - which are no longer named, so nobody
/// reads them. There is no moment at which a deleted row is reachable.
/// </para>
/// <para>
/// <b>Published by rename.</b> The file is written whole under a temporary name, flushed to the disk,
/// and moved over the old one; a rename within a directory is atomic on both platforms this ships to.
/// A reader therefore sees the previous manifest or the next one, never half of either.
/// </para>
/// <para>
/// <b>Text, and it says how many entries it has.</b> The count is not decoration: a truncated file is
/// then detectable rather than silently short, which is the difference between "the live set is these
/// three" and "the live set is these three because the fourth line was lost". Anything unparseable is
/// refused rather than half-read, and the caller falls back to the directory - which is also what
/// happens for a database written before manifests existed.
/// </para>
/// </remarks>
public sealed class LsmManifest
{
    #region Constants

    /// <summary>The manifest's file name inside the store directory.</summary>
    public const string FILE_NAME = "manifest";

    private const string TEMPORARY_FILE_NAME = "manifest.tmp";

    private const string HEADER = "WitDbLsmManifest 1";

    private const string NEXT_PREFIX = "next ";

    private const string COUNT_PREFIX = "count ";

    #endregion

    #region Constructors

    private LsmManifest(IReadOnlyList<string> sstables, int nextSstableId)
    {
        Sstables = sstables;
        NextSstableId = nextSstableId;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The live SSTables, file names only, <b>oldest first</b> - which is the order a lookup walks
    /// backwards through, so it is part of the meaning rather than a presentation detail.
    /// </summary>
    public IReadOnlyList<string> Sstables { get; }

    /// <summary>
    /// The next SSTable number to hand out.
    /// </summary>
    /// <remarks>
    /// Carried here so that a store which ignores orphaned files still does not reuse their names -
    /// an orphan and a new SSTable sharing a name would make the manifest ambiguous about which file
    /// it meant.
    /// </remarks>
    public int NextSstableId { get; }

    #endregion

    #region Functions

    /// <summary>
    /// Reads the manifest from a store directory, or null when there is none or it cannot be trusted.
    /// </summary>
    /// <remarks>
    /// Null is not an error: a database written before manifests existed has none, and the caller
    /// falls back to reading the directory. It is also what a torn or unreadable file gives, and the
    /// fallback is the same - the directory is a worse answer than a manifest and a better one than a
    /// guess.
    /// </remarks>
    public static LsmManifest? Read(string directory)
    {
        var path = Path.Combine(directory, FILE_NAME);

        if (!File.Exists(path))
            return null;

        try
        {
            var lines = File.ReadAllLines(path);

            if (lines.Length < 3 || lines[0] != HEADER)
                return null;

            if (!TryReadValue(lines[1], NEXT_PREFIX, out var next))
                return null;

            if (!TryReadValue(lines[2], COUNT_PREFIX, out var count))
                return null;

            var names = lines.Skip(3).Where(line => line.Length > 0).ToList();

            // The count is what makes a truncated file detectable. Without it a manifest that lost
            // its last line would read as a shorter live set - which is a silently older database.
            if (names.Count != count)
                return null;

            return new LsmManifest(names, next);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the manifest and makes it the one a reader will see.
    /// </summary>
    /// <param name="directory">The store directory.</param>
    /// <param name="sstables">Live SSTable paths or names, oldest first.</param>
    /// <param name="nextSstableId">The next number to hand out.</param>
    /// <remarks>
    /// Whole file, flushed, then renamed over the old one. The flush is not optional: a rename that
    /// publishes a file whose contents are still in the operating system's cache moves the torn write
    /// from the manifest to the manifest's replacement, which is the same defect one step along.
    /// </remarks>
    public static void Write(string directory, IEnumerable<string> sstables, int nextSstableId)
    {
        var names = sstables.Select(Path.GetFileName).ToList();

        var temporary = Path.Combine(directory, TEMPORARY_FILE_NAME);
        var path = Path.Combine(directory, FILE_NAME);

        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.WriteLine(HEADER);
            writer.WriteLine($"{NEXT_PREFIX}{nextSstableId.ToString(CultureInfo.InvariantCulture)}");
            writer.WriteLine($"{COUNT_PREFIX}{names.Count.ToString(CultureInfo.InvariantCulture)}");

            foreach (var name in names)
                writer.WriteLine(name);

            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    #endregion

    #region Tools

    private static bool TryReadValue(string line, string prefix, out int value)
    {
        value = 0;

        return line.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(line.AsSpan(prefix.Length), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value);
    }

    #endregion
}
