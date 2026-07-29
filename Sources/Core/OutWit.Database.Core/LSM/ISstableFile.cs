namespace OutWit.Database.Core.LSM;

/// <summary>
/// The file an SSTable is written into, and the one operation that makes it durable.
/// </summary>
/// <remarks>
/// <see cref="SSTableBuilder"/> used to open its own <c>FileStream</c>, which left nothing to
/// substitute: the LSM path could not be told to fail part-way, and there was no way to observe
/// whether it ever asked for its writes to reach the media. It did not - the WAL holding the same
/// data was truncated the moment the SSTable was written, while the SSTable was still only in the
/// operating system's cache.
///
/// <see cref="Sync"/> is separate from a stream flush on purpose. Flushing a <c>BinaryWriter</c> or a
/// <c>FileStream</c> pushes bytes into the operating system and no further; only
/// <c>Flush(flushToDisk: true)</c> asks for the media, and the difference between the two is exactly
/// what a power failure sees.
/// </remarks>
public interface ISstableFile : IDisposable
{
    /// <summary>The stream the SSTable is written to.</summary>
    Stream Stream { get; }

    /// <summary>
    /// Makes everything written so far durable - not merely handed to the operating system.
    /// </summary>
    void Sync();
}

/// <summary>
/// Creates the files an SSTable is written into.
/// </summary>
public interface ISstableFileFactory
{
    /// <summary>
    /// Creates (or truncates) the file at the given path and opens it for writing.
    /// </summary>
    ISstableFile Create(string path);
}
