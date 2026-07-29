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

    /// <summary>
    /// Closes the file and makes it visible under the name the store looks for.
    /// </summary>
    /// <remarks>
    /// Until this is called the table is not part of the store, and that is the point. Both the
    /// memtable flush and the compactor used to write straight to the final name, so a crash
    /// part-way through left a truncated file already carrying the name recovery looks for - with the
    /// highest id, which made it the newest table in the store. Measured: the next open failed
    /// outright with <c>InvalidDataException: Invalid SSTable magic</c>. One crash at the wrong
    /// moment and the database could not be opened at all.
    ///
    /// A table that was never finished must never appear. Publishing is a rename within one
    /// directory, which is atomic on NTFS and on POSIX.
    /// </remarks>
    void Publish();
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
