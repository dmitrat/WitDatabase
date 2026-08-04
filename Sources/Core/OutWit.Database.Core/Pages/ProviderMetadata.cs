using System.Buffers.Binary;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core;

/// <summary>
/// Provider metadata stored in the database header (bytes 48-127).
/// Contains the configuration the database was created with.
/// </summary>
/// <remarks>
/// <para>
/// Layout (80 bytes total):
/// [48]       Features flags (encryption enabled, transactions enabled, etc.)
/// [49-55]    Reserved bytes
/// [56-71]    Store provider key (16 bytes, null-padded)
/// [72-87]    Encryption provider key (16 bytes, null-padded)
/// [88-103]   Cache provider key (16 bytes, null-padded)
/// [104-119]  Journal provider key (16 bytes, null-padded)
/// [120-123]  Cache size in pages (int32, 0 = not recorded)
/// [124-127]  Reserved
/// </para>
/// <para>
/// <b>The cache and journal keys used to be declared here and not written.</b> The struct carried them
/// with the comment "Not persisted - always uses default on reopen", and 12 bytes were reserved for
/// them - which is not enough for two 16-byte keys and a cache size, so the region grew and the header
/// with it, from 100 bytes to 128. Page 0 holds nothing but the header and the smallest page is 512
/// bytes, so the room was already there.
/// </para>
/// <para>
/// <b>Both directions stay readable.</b> A file written before 12.2.0 has zeros from byte 88 on, which
/// reads as "not recorded" and falls back to the defaults it always used. A build older than 12.2.0
/// reads only the first 100 bytes of a new file and sees exactly what it saw before. The format
/// version's minor is bumped to record the change; the major is unchanged, and the major is what an
/// older build refuses on.
/// </para>
/// <para>
/// <b>Keys are stored as text, not as an enumeration.</b> A third party can register a cache or journal
/// provider under any key - <c>ThirdPartyProviderTests</c> drives a real database through one - and an
/// id would quietly make the registry closed.
/// </para>
/// </remarks>
public struct ProviderMetadata
{
    #region Constants

    /// <summary>
    /// Maximum length for a provider key string.
    /// </summary>
    public const int MAX_PROVIDER_KEY_LENGTH = 16;

    /// <summary>
    /// Offset in the database header where metadata starts.
    /// </summary>
    public const int HEADER_OFFSET = 48;

    /// <summary>
    /// Total size of the metadata section.
    /// </summary>
    public const int METADATA_SIZE = 80; // 48 to 127 inclusive

    /// <summary>Offsets within the metadata region.</summary>
    private const int FEATURES = 0;
    private const int STORE_KEY = 8;
    private const int ENCRYPTION_KEY = 24;
    private const int CACHE_KEY = 40;
    private const int JOURNAL_KEY = 56;
    private const int CACHE_SIZE = 72;

    #endregion

    #region Fields

    /// <summary>
    /// Feature flags indicating which features are enabled.
    /// </summary>
    public ProviderFeatures Features;

    /// <summary>
    /// Store provider key (e.g., "btree", "lsm").
    /// </summary>
    public string StoreProviderKey;

    /// <summary>
    /// Encryption provider key (e.g., "aes-gcm", "" for none).
    /// </summary>
    public string EncryptionProviderKey;

    /// <summary>
    /// Cache provider key (e.g., "clock", "lru"). Empty when the file does not record one.
    /// </summary>
    public string CacheProviderKey;

    /// <summary>
    /// Journal provider key (e.g., "wal", "rollback", "" for none).
    /// </summary>
    public string JournalProviderKey;

    /// <summary>
    /// Page cache size in pages. Zero means the file does not record one.
    /// </summary>
    public int CacheSize;

    #endregion

    #region Functions

    /// <summary>
    /// Writes metadata to the header buffer at offset 48.
    /// </summary>
    public readonly void WriteTo(Span<byte> headerBuffer)
    {
        if (headerBuffer.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            throw new ArgumentException($"Buffer must be at least {DatabaseConstants.DATABASE_HEADER_SIZE} bytes");

        WriteBlock(headerBuffer.Slice(HEADER_OFFSET, METADATA_SIZE));
    }

    /// <summary>
    /// Reads metadata from the header buffer.
    /// </summary>
    public static ProviderMetadata ReadFrom(ReadOnlySpan<byte> headerBuffer)
    {
        if (headerBuffer.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            throw new ArgumentException($"Buffer must be at least {DatabaseConstants.DATABASE_HEADER_SIZE} bytes");

        return ReadBlock(headerBuffer.Slice(HEADER_OFFSET, METADATA_SIZE));
    }

    /// <summary>
    /// Writes the metadata region on its own, without a database header around it.
    /// </summary>
    /// <remarks>
    /// The LSM store keeps a directory rather than a paged file, so it has nowhere to put a database
    /// header - and until 12.2.0 it recorded nothing at all, which is why
    /// <c>WitDatabase.Open</c> could build the wrong transaction model over one and report every table
    /// as missing. Its sidecar carries this same block, so there is one encoding of these fields rather
    /// than two that can drift apart.
    /// </remarks>
    public readonly void WriteBlock(Span<byte> block)
    {
        if (block.Length < METADATA_SIZE)
            throw new ArgumentException($"Buffer must be at least {METADATA_SIZE} bytes", nameof(block));

        block[..METADATA_SIZE].Clear();

        block[FEATURES] = (byte)Features;

        WriteProviderKey(block.Slice(STORE_KEY, MAX_PROVIDER_KEY_LENGTH), StoreProviderKey);
        WriteProviderKey(block.Slice(ENCRYPTION_KEY, MAX_PROVIDER_KEY_LENGTH), EncryptionProviderKey);
        WriteProviderKey(block.Slice(CACHE_KEY, MAX_PROVIDER_KEY_LENGTH), CacheProviderKey);
        WriteProviderKey(block.Slice(JOURNAL_KEY, MAX_PROVIDER_KEY_LENGTH), JournalProviderKey);

        BinaryPrimitives.WriteInt32LittleEndian(block[CACHE_SIZE..], Math.Max(0, CacheSize));
    }

    /// <summary>
    /// Reads the metadata region on its own. A region of zeros reads as "nothing recorded".
    /// </summary>
    public static ProviderMetadata ReadBlock(ReadOnlySpan<byte> block)
    {
        if (block.Length < METADATA_SIZE)
            throw new ArgumentException($"Buffer must be at least {METADATA_SIZE} bytes", nameof(block));

        return new ProviderMetadata
        {
            Features = (ProviderFeatures)block[FEATURES],
            StoreProviderKey = ReadProviderKey(block.Slice(STORE_KEY, MAX_PROVIDER_KEY_LENGTH)),
            EncryptionProviderKey = ReadProviderKey(block.Slice(ENCRYPTION_KEY, MAX_PROVIDER_KEY_LENGTH)),
            CacheProviderKey = ReadProviderKey(block.Slice(CACHE_KEY, MAX_PROVIDER_KEY_LENGTH)),
            JournalProviderKey = ReadProviderKey(block.Slice(JOURNAL_KEY, MAX_PROVIDER_KEY_LENGTH)),
            CacheSize = BinaryPrimitives.ReadInt32LittleEndian(block[CACHE_SIZE..])
        };
    }

    /// <summary>
    /// Creates default metadata for a new database.
    /// </summary>
    public static ProviderMetadata CreateDefault()
    {
        return new ProviderMetadata
        {
            Features = ProviderFeatures.None,
            StoreProviderKey = "btree",
            EncryptionProviderKey = "",
            CacheProviderKey = "clock",
            JournalProviderKey = ""
        };
    }

    /// <summary>
    /// Creates a copy with updated features.
    /// </summary>
    public readonly ProviderMetadata WithFeatures(ProviderFeatures features)
    {
        return new ProviderMetadata
        {
            Features = features,
            StoreProviderKey = StoreProviderKey,
            EncryptionProviderKey = EncryptionProviderKey,
            CacheProviderKey = CacheProviderKey,
            JournalProviderKey = JournalProviderKey,
            CacheSize = CacheSize
        };
    }

    #endregion

    #region Tools

    private static void WriteProviderKey(Span<byte> buffer, string? key)
    {
        buffer.Clear();
        if (string.IsNullOrEmpty(key))
            return;

        var keyBytes = TextEncoding.UTF8.GetBytes(key);
        var length = Math.Min(keyBytes.Length, MAX_PROVIDER_KEY_LENGTH);
        keyBytes.AsSpan(0, length).CopyTo(buffer);
    }

    private static string ReadProviderKey(ReadOnlySpan<byte> buffer)
    {
        // Find null terminator or end
        int length = buffer.IndexOf((byte)0);
        if (length < 0)
            length = buffer.Length;
        
        if (length == 0)
            return "";

        return TextEncoding.UTF8.GetString(buffer[..length]);
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether encryption is enabled.
    /// </summary>
    public readonly bool IsEncrypted => Features.HasFlag(ProviderFeatures.Encryption);

    /// <summary>
    /// Gets whether transactions are enabled.
    /// </summary>
    public readonly bool HasTransactions => Features.HasFlag(ProviderFeatures.Transactions);

    /// <summary>
    /// Gets whether file locking is enabled.
    /// </summary>
    public readonly bool HasFileLocking => Features.HasFlag(ProviderFeatures.FileLocking);

    /// <summary>
    /// Gets whether MVCC (Multi-Version Concurrency Control) is enabled.
    /// </summary>
    public readonly bool HasMvcc => Features.HasFlag(ProviderFeatures.Mvcc);

    #endregion

    #region Equality

    public override readonly bool Equals(object? obj)
    {
        return obj is ProviderMetadata other &&
               Features == other.Features &&
               StoreProviderKey == other.StoreProviderKey &&
               EncryptionProviderKey == other.EncryptionProviderKey &&
               CacheProviderKey == other.CacheProviderKey &&
               JournalProviderKey == other.JournalProviderKey &&
               CacheSize == other.CacheSize;
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Features, StoreProviderKey, EncryptionProviderKey,
            CacheProviderKey, JournalProviderKey, CacheSize);
    }

    public override readonly string ToString()
    {
        var parts = new List<string> { $"Store={StoreProviderKey ?? "btree"}" };

        if (IsEncrypted)
            parts.Add($"Encryption={EncryptionProviderKey}");

        if (HasTransactions)
            parts.Add("Transactions");

        if (HasMvcc)
            parts.Add("MVCC");

        if (HasFileLocking)
            parts.Add("FileLocking");

        if (!string.IsNullOrEmpty(CacheProviderKey))
            parts.Add($"Cache={CacheProviderKey}");

        if (!string.IsNullOrEmpty(JournalProviderKey))
            parts.Add($"Journal={JournalProviderKey}");

        if (CacheSize > 0)
            parts.Add($"CacheSize={CacheSize}");

        return $"ProviderMetadata({string.Join(", ", parts)})";
    }

    #endregion
}

/// <summary>
/// Feature flags stored in the database header.
/// </summary>
[Flags]
public enum ProviderFeatures : byte
{
    /// <summary>
    /// No special features.
    /// </summary>
    None = 0,

    /// <summary>
    /// Database is encrypted.
    /// </summary>
    Encryption = 1 << 0,

    /// <summary>
    /// Transactions are enabled.
    /// </summary>
    Transactions = 1 << 1,

    /// <summary>
    /// File locking is enabled.
    /// </summary>
    FileLocking = 1 << 2,

    /// <summary>
    /// MVCC (Multi-Version Concurrency Control) is enabled.
    /// When enabled, keys are stored with version suffixes for snapshot isolation.
    /// </summary>
    Mvcc = 1 << 3,

    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Reserved2 = 1 << 4,
    Reserved3 = 1 << 5,
    Reserved4 = 1 << 6,
    Reserved5 = 1 << 7
}
