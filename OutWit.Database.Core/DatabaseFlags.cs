namespace OutWit.Database.Core;

/// <summary>
/// Database feature flags
/// </summary>
[Flags]
public enum DatabaseFlags : uint
{
    /// <summary>
    /// No special flags
    /// </summary>
    None = 0,

    /// <summary>
    /// Database is using WAL (Write-Ahead Logging) mode
    /// </summary>
    WalMode = 1 << 0,

    /// <summary>
    /// Database is encrypted
    /// </summary>
    Encrypted = 1 << 1,

    /// <summary>
    /// Database is read-only
    /// </summary>
    ReadOnly = 1 << 2,

    /// <summary>
    /// Database has UTF-8 encoding (default is UTF-16)
    /// </summary>
    Utf8Encoding = 1 << 3
}