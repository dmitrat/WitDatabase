namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Base interface for all pluggable providers (storage, encryption, cache, etc.).
/// Providers register themselves with ProviderRegistry on instantiation.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Gets the unique key identifying this provider type.
    /// Used for serialization in database header and factory lookup.
    /// </summary>
    /// <remarks>
    /// Keys should be lowercase, use hyphens for multi-word names.
    /// Examples: "file", "memory", "aes-gcm", "chacha20-poly1305", "btree", "lsm"
    /// </remarks>
    string ProviderKey { get; }
}
