using System.Buffers.Binary;

namespace OutWit.Database.Core.Providers;

/// <summary>
/// The configuration an LSM database records when it is created, kept in a sidecar beside its SSTables.
/// </summary>
/// <remarks>
/// <para>
/// <b>Until 12.2.0 an LSM database recorded nothing at all</b>, and the cost of that was not a lost
/// setting. <see cref="WitDatabase.Open(string)"/> asks the detector what the database was made with;
/// for a directory the detector filled in nothing, so <c>HasTransactions</c> came back as the default
/// of a field nobody had set - <c>false</c> - and <c>Open</c> built a store with no transaction layer
/// over a database whose every value sits under a versioned MVCC key. It opened without complaint and
/// reported every table as missing, with the rows intact underneath. That is the exact shape 12.0.0
/// fixed for the B+Tree store, and it survived here because the fix was a comparison against a header
/// this store did not have.
/// </para>
/// <para>
/// The file carries the same <see cref="ProviderMetadata"/> block the database header carries, so there
/// is one encoding of those fields rather than two that can drift, followed by the LSM options - which
/// have nowhere else to live, since the B+Tree header has no place for them and they are meaningless
/// there.
/// </para>
/// <para>
/// <b>Absent means "created before this existed".</b> A directory without the sidecar reads as null and
/// the caller falls back to what it did before, so a database written by an earlier version still
/// opens. Nothing rewrites the sidecar on open, for the same reason the database header is only written
/// when the database is created: reopening with different settings must not edit what the file says it
/// was made with.
/// </para>
/// </remarks>
public static class LsmDirectoryMetadata
{
    #region Constants

    /// <summary>The sidecar's name inside the LSM directory.</summary>
    public const string FILE_NAME = "provider.meta";

    private static ReadOnlySpan<byte> MAGIC => "WitDB LSM Meta 1"u8;

    private const int MAGIC_SIZE = 16;
    private const int VERSION_OFFSET = 16;
    private const int METADATA_OFFSET = 24;
    private const int OPTIONS_OFFSET = METADATA_OFFSET + ProviderMetadata.METADATA_SIZE;
    private const int OPTIONS_SIZE = 32;
    private const int TOTAL_SIZE = OPTIONS_OFFSET + OPTIONS_SIZE;

    private const ushort VERSION = 1;

    #endregion

    #region Functions

    /// <summary>
    /// Writes the sidecar, replacing any that is there.
    /// </summary>
    public static void Write(string directory, ProviderMetadata metadata, LsmStoredOptions options)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        Directory.CreateDirectory(directory);

        var buffer = new byte[TOTAL_SIZE];
        var span = buffer.AsSpan();

        MAGIC.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[VERSION_OFFSET..], VERSION);

        metadata.WriteBlock(span.Slice(METADATA_OFFSET, ProviderMetadata.METADATA_SIZE));

        var tail = span.Slice(OPTIONS_OFFSET, OPTIONS_SIZE);
        BinaryPrimitives.WriteInt64LittleEndian(tail, options.MemTableSizeLimit);
        BinaryPrimitives.WriteInt64LittleEndian(tail[8..], options.BlockCacheSizeBytes);
        BinaryPrimitives.WriteInt32LittleEndian(tail[16..], options.BlockSize);
        BinaryPrimitives.WriteInt32LittleEndian(tail[20..], options.Level0CompactionTrigger);
        tail[24] = (byte)(options.EnableWal ? 1 : 0);
        tail[25] = (byte)(options.SyncWrites ? 1 : 0);
        tail[26] = (byte)(options.EnableBlockCache ? 1 : 0);
        tail[27] = (byte)(options.BackgroundCompaction ? 1 : 0);

        // Written under a temporary name and moved into place, which is atomic on NTFS and POSIX. The
        // same reasoning as the SSTables next to it: a sidecar that was never finished never appears,
        // so a crash mid-write cannot leave a half-written configuration to be read as a real one.
        var path = Path.Combine(directory, FILE_NAME);
        var temporary = path + ".tmp";

        File.WriteAllBytes(temporary, buffer);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Reads the sidecar, or null when the directory has none or it cannot be read.
    /// </summary>
    public static (ProviderMetadata Metadata, LsmStoredOptions Options)? Read(string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return null;

        var path = Path.Combine(directory, FILE_NAME);

        if (!File.Exists(path))
            return null;

        try
        {
            var buffer = File.ReadAllBytes(path);

            if (buffer.Length < TOTAL_SIZE)
                return null;

            var span = buffer.AsSpan();

            if (!span[..MAGIC_SIZE].SequenceEqual(MAGIC))
                return null;

            if (BinaryPrimitives.ReadUInt16LittleEndian(span[VERSION_OFFSET..]) > VERSION)
                return null;

            var metadata = ProviderMetadata.ReadBlock(span.Slice(METADATA_OFFSET, ProviderMetadata.METADATA_SIZE));
            var tail = span.Slice(OPTIONS_OFFSET, OPTIONS_SIZE);

            var options = new LsmStoredOptions
            {
                MemTableSizeLimit = BinaryPrimitives.ReadInt64LittleEndian(tail),
                BlockCacheSizeBytes = BinaryPrimitives.ReadInt64LittleEndian(tail[8..]),
                BlockSize = BinaryPrimitives.ReadInt32LittleEndian(tail[16..]),
                Level0CompactionTrigger = BinaryPrimitives.ReadInt32LittleEndian(tail[20..]),
                EnableWal = tail[24] != 0,
                SyncWrites = tail[25] != 0,
                EnableBlockCache = tail[26] != 0,
                BackgroundCompaction = tail[27] != 0
            };

            return (metadata, options);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

/// <summary>
/// The LSM settings a database records when it is created.
/// </summary>
/// <remarks>
/// A flat copy of the subset of <c>LsmOptions</c> that a connection string can select, so the sidecar
/// does not have to serialize a type carrying an encryptor and a file factory.
/// </remarks>
public sealed record LsmStoredOptions
{
    public long MemTableSizeLimit { get; init; }
    public long BlockCacheSizeBytes { get; init; }
    public int BlockSize { get; init; }
    public int Level0CompactionTrigger { get; init; }
    public bool EnableWal { get; init; }
    public bool SyncWrites { get; init; }
    public bool EnableBlockCache { get; init; }
    public bool BackgroundCompaction { get; init; }
}
