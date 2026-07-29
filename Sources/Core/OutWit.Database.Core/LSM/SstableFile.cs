namespace OutWit.Database.Core.LSM;

/// <summary>
/// The ordinary implementation: a file on disk, synced with <c>Flush(flushToDisk: true)</c>.
/// </summary>
public sealed class SstableFile : ISstableFile
{
    #region Fields

    private readonly FileStream m_stream;

    #endregion

    #region Constructors

    public SstableFile(string path)
    {
        m_stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
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

    public void Dispose()
    {
        m_stream.Dispose();
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
