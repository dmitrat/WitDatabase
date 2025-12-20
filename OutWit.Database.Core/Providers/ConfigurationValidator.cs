using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Providers;

/// <summary>
/// Validates database configuration matches between builder settings and stored metadata.
/// </summary>
public static class ConfigurationValidator
{
    /// <summary>
    /// Validates that the current configuration matches what's stored in the database.
    /// </summary>
    /// <param name="current">Current configuration from builder.</param>
    /// <param name="stored">Stored configuration from database header.</param>
    /// <param name="strict">If true, all settings must match. If false, only critical settings are checked.</param>
    /// <returns>List of mismatches (empty if valid).</returns>
    public static List<string> Validate(ProviderMetadata current, ProviderMetadata stored, bool strict = false)
    {
        var mismatches = new List<string>();

        // Critical: Store provider must match
        if (!string.IsNullOrEmpty(stored.StoreProviderKey) &&
            !string.Equals(current.StoreProviderKey, stored.StoreProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"Store provider mismatch: expected '{stored.StoreProviderKey}', got '{current.StoreProviderKey}'");
        }

        // Critical: Encryption must match
        if (stored.IsEncrypted && !current.IsEncrypted)
        {
            mismatches.Add("Database is encrypted but no encryption provider configured. Use WithEncryption().");
        }
        else if (!stored.IsEncrypted && current.IsEncrypted)
        {
            // Warning, not error - user might be adding encryption
            if (strict)
            {
                mismatches.Add("Database is not encrypted but encryption provider configured.");
            }
        }

        // Check encryption provider type
        if (stored.IsEncrypted && current.IsEncrypted &&
            !string.IsNullOrEmpty(stored.EncryptionProviderKey) &&
            !string.Equals(current.EncryptionProviderKey, stored.EncryptionProviderKey, StringComparison.OrdinalIgnoreCase))
        {
            mismatches.Add($"Encryption provider mismatch: expected '{stored.EncryptionProviderKey}', got '{current.EncryptionProviderKey}'");
        }

        // Optional: Check transaction settings (warning only in strict mode)
        if (strict)
        {
            if (stored.HasTransactions && !current.HasTransactions)
            {
                mismatches.Add("Database was created with transactions but they are now disabled.");
            }
        }

        return mismatches;
    }

    /// <summary>
    /// Validates configuration and throws if there are critical mismatches.
    /// </summary>
    /// <exception cref="ConfigurationMismatchException">Thrown when configuration doesn't match.</exception>
    public static void ValidateOrThrow(ProviderMetadata current, ProviderMetadata stored, bool strict = false)
    {
        var mismatches = Validate(current, stored, strict);
        if (mismatches.Count > 0)
        {
            throw new ConfigurationMismatchException(current, stored, mismatches);
        }
    }

    /// <summary>
    /// Checks if the stored metadata indicates the database was created with specific features.
    /// Useful for determining what configuration to use when opening.
    /// </summary>
    public static OpeningHints GetOpeningHints(ProviderMetadata stored)
    {
        return new OpeningHints
        {
            RequiresEncryption = stored.IsEncrypted,
            EncryptionProvider = stored.EncryptionProviderKey,
            StoreProvider = stored.StoreProviderKey,
            HasTransactions = stored.HasTransactions,
            HasFileLocking = stored.Features.HasFlag(ProviderFeatures.FileLocking)
        };
    }
}