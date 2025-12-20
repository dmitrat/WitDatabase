using System.Buffers.Binary;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core;

/// <summary>
/// Provider metadata stored in the database header (bytes 48-99).
/// Contains information about the providers used when creating the database.
/// </summary>
/// <remarks>
/// Layout (52 bytes total):
/// [48]      Features flags (encryption enabled, transactions enabled, etc.)
/// [49-55]   Reserved bytes
/// [56-71]   Store provider key (16 bytes, null-padded)
/// [72-87]   Encryption provider key (16 bytes, null-padded)
/// [88-99]   Reserved (12 bytes for future: cache, journal keys)
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
    public const int METADATA_SIZE = 52; // 48 to 99 inclusive

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
    /// Cache provider key (e.g., "clock", "lru").
    /// Not persisted - always uses default on reopen.
    /// </summary>
    public string CacheProviderKey;

    /// <summary>
    /// Journal provider key (e.g., "wal", "rollback", "" for none).
    /// Not persisted - always uses default on reopen.
    /// </summary>
    public string JournalProviderKey;

    #endregion

    #region Functions

    /// <summary>
    /// Writes metadata to the header buffer at offset 48.
    /// </summary>
    public readonly void WriteTo(Span<byte> headerBuffer)
    {
        if (headerBuffer.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            throw new ArgumentException($"Buffer must be at least {DatabaseConstants.DATABASE_HEADER_SIZE} bytes");

        var span = headerBuffer[HEADER_OFFSET..];

        // Features byte
        span[0] = (byte)Features;

        // Reserved bytes 1-7
        span[1..8].Clear();

        // Store provider key (16 bytes at offset 8)
        WriteProviderKey(span[8..24], StoreProviderKey);

        // Encryption provider key (16 bytes at offset 24)
        WriteProviderKey(span[24..40], EncryptionProviderKey);

        // Reserved for future (cache, journal keys)
        span[40..METADATA_SIZE].Clear();
    }

    /// <summary>
    /// Reads metadata from the header buffer.
    /// </summary>
    public static ProviderMetadata ReadFrom(ReadOnlySpan<byte> headerBuffer)
    {
        if (headerBuffer.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            throw new ArgumentException($"Buffer must be at least {DatabaseConstants.DATABASE_HEADER_SIZE} bytes");

        var span = headerBuffer[HEADER_OFFSET..];

        return new ProviderMetadata
        {
            Features = (ProviderFeatures)span[0],
            StoreProviderKey = ReadProviderKey(span[8..24]),
            EncryptionProviderKey = ReadProviderKey(span[24..40]),
            CacheProviderKey = "",  // Not stored, use default
            JournalProviderKey = "" // Not stored, use default
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
            JournalProviderKey = JournalProviderKey
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

    #endregion

    #region Equality

    public override readonly bool Equals(object? obj)
    {
        return obj is ProviderMetadata other &&
               Features == other.Features &&
               StoreProviderKey == other.StoreProviderKey &&
               EncryptionProviderKey == other.EncryptionProviderKey;
    }

    public override readonly int GetHashCode()
    {
        return HashCode.Combine(Features, StoreProviderKey, EncryptionProviderKey);
    }

    public override readonly string ToString()
    {
        var parts = new List<string> { $"Store={StoreProviderKey ?? "btree"}" };
        
        if (IsEncrypted)
            parts.Add($"Encryption={EncryptionProviderKey}");
        
        if (HasTransactions)
            parts.Add("Transactions");
        
        if (HasFileLocking)
            parts.Add("FileLocking");

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
    /// Reserved for future use.
    /// </summary>
    Reserved1 = 1 << 3,
    Reserved2 = 1 << 4,
    Reserved3 = 1 << 5,
    Reserved4 = 1 << 6,
    Reserved5 = 1 << 7
}
