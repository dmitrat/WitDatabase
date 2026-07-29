namespace OutWit.Database.Core.LSM;

/// <summary>
/// The ordinary implementation: a file on disk, written under a name the store ignores and renamed
/// into place once it is complete and durable.
/// </summary>
public sealed class SstableFile : ISstableFile
{
    #region Constants

    /// <summary>
    /// Prefix for a table that is still being written.
    /// </summary>
    /// <remarks>
    /// A <b>prefix</b>, not an extra extension, and that is deliberate. The store lists its tables
    /// with <c>Directory.GetFiles(directory, "sst_*.sst")</c>, and on Windows a three-character
    /// extension in a search pattern also matches longer extensions beginning with it - so a
    /// <c>sst_000009.sst.building</c> would have been listed as a live table on exactly the platform
    /// this is meant to protect.
    /// </remarks>
    public const string BUILDING_PREFIX = "building_";

    #endregion

    #region Fields

    private readonly string m_finalPath;
    private readonly string m_buildingPath;
    private readonly FileStream m_stream;

    private bool m_published;
    private bool m_disposed;

    #endregion

    #region Constructors

    public SstableFile(string path)
    {
        m_finalPath = path;
        m_buildingPath = BuildingPathFor(path);

        m_stream = new FileStream(m_buildingPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The name a table in progress is written under, for the file at the given final path.
    /// </summary>
    public static string BuildingPathFor(string finalPath)
    {
        var directory = Path.GetDirectoryName(finalPath);
        var name = BUILDING_PREFIX + Path.GetFileName(finalPath);

        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name);
    }

    #endregion

    #region ISstableFile

    public Stream Stream => m_stream;

    /// <summary>
    /// Asks for the bytes to reach the media.
    /// </summary>
    /// <remarks>
    /// A caveat worth stating rather than leaving implicit: on POSIX this makes the file's
    /// <i>contents</i> durable, but the directory entry naming a newly created file is a separate
    /// matter, and .NET exposes no portable way to fsync a directory. A crash in that window can
    /// leave a synced file that the directory does not yet list. Recovery already tolerates a
    /// missing SSTable - the WAL is what it falls back on - and closing this properly needs a
    /// platform-specific call, so it is recorded here rather than half-done.
    /// </remarks>
    public void Sync()
    {
        m_stream.Flush(flushToDisk: true);
    }

    public void Publish()
    {
        if (m_published)
            return;

        // The stream has to be closed before the rename: the file is opened with FileShare.None, so
        // Windows will not move it while a handle is open.
        m_stream.Dispose();

        File.Move(m_buildingPath, m_finalPath, overwrite: true);

        m_published = true;
    }

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;

        if (m_published)
            return;

        // Never finished, so it was never a table. Close it and take the fragment away rather than
        // leaving it to accumulate - a crash leaves one behind, an ordinary abandon should not.
        m_stream.Dispose();

        try
        {
            File.Delete(m_buildingPath);
        }
        catch (IOException)
        {
            // Cleanup only.
        }
    }

    #endregion
}

/// <summary>
/// Creates ordinary files on disk.
/// </summary>
public sealed class SstableFileFactory : ISstableFileFactory
{
    /// <summary>The factory used when nothing else is configured.</summary>
    public static ISstableFileFactory Default { get; } = new SstableFileFactory();

    public ISstableFile Create(string path) => new SstableFile(path);
}
