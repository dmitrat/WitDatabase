namespace OutWit.Database.Core;

/// <summary>
/// Database format constants
/// </summary>
public static class DatabaseConstants
{
    /// <summary>
    /// Magic bytes identifying WitDB format (16 bytes)
    /// </summary>
    public static ReadOnlySpan<byte> MAGIC_BYTES => "WitDB Format 1\0\0"u8;

    /// <summary>
    /// Current format version (major.minor as ushort)
    /// </summary>
    public const ushort FORMAT_VERSION = 0x0101; // 1.1 - provider metadata grew to record cache, journal and cache size

    /// <summary>
    /// Default page size in bytes (4KB)
    /// </summary>
    public const int DEFAULT_PAGE_SIZE = 4096;

    /// <summary>
    /// Minimum supported page size
    /// </summary>
    public const int MIN_PAGE_SIZE = 512;

    /// <summary>
    /// Maximum supported page size
    /// </summary>
    public const int MAX_PAGE_SIZE = 65536;

    /// <summary>
    /// Size of the database file header in bytes
    /// </summary>
    public const int DATABASE_HEADER_SIZE = 128;

    /// <summary>
    /// Size of a page header in bytes
    /// </summary>
    public const int PAGE_HEADER_SIZE = 16;

    /// <summary>
    /// File extension for WitDatabase database files
    /// </summary>
    public const string FILE_EXTENSION = ".witdb";

    /// <summary>
    /// File extension for WAL (Write-Ahead Log) files
    /// </summary>
    public const string WAL_EXTENSION = ".witdb-wal";

    /// <summary>
    /// Default number of pages to cache in memory
    /// </summary>
    public const int DEFAULT_CACHE_SIZE = 1000;

    /// <summary>
    /// Page number indicating null/no page reference
    /// </summary>
    public const uint NULL_PAGE_NUMBER = 0;
}
