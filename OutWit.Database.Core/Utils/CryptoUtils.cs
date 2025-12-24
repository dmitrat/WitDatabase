using System.Security.Cryptography;

namespace OutWit.Database.Core.Utils;

/// <summary>
/// Cryptographic utility methods for key derivation.
/// </summary>
public static class CryptoUtils
{
    #region Constants

    /// <summary>
    /// Default number of PBKDF2 iterations for native environments.
    /// </summary>
    public const int DEFAULT_PBKDF2_ITERATIONS = 100_000;

    /// <summary>
    /// Reduced number of PBKDF2 iterations for WASM/browser environments.
    /// WASM crypto is slower, so we use fewer iterations to maintain responsiveness.
    /// Still provides reasonable security for browser-based encryption.
    /// </summary>
    public const int WASM_PBKDF2_ITERATIONS = 10_000;

    #endregion

    #region Key Derivation

    /// <summary>
    /// Derives a 256-bit encryption key from a password using PBKDF2.
    /// </summary>
    /// <param name="password">The password to derive the key from.</param>
    /// <param name="salt">The salt to use for key derivation.</param>
    /// <param name="iterations">Number of PBKDF2 iterations (default: 100,000).</param>
    /// <returns>A 32-byte encryption key.</returns>
    public static byte[] DeriveKey(string password, byte[] salt, int iterations = DEFAULT_PBKDF2_ITERATIONS)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    /// <summary>
    /// Derives a deterministic salt from a password.
    /// </summary>
    /// <param name="password">The password to derive salt from.</param>
    /// <param name="suffix">Optional suffix to make salt unique per use case.</param>
    /// <returns>A 16-byte salt.</returns>
    public static byte[] DerivePasswordSalt(string password, string suffix = "_WitDB_Salt")
    {
        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password + suffix);
        return SHA256.HashData(passwordBytes)[..16];
    }

    /// <summary>
    /// Derives a deterministic salt from a username.
    /// </summary>
    /// <param name="user">The username to derive salt from.</param>
    /// <param name="suffix">Optional suffix to make salt unique per use case.</param>
    /// <returns>A 16-byte salt.</returns>
    public static byte[] DeriveUserSalt(string user, string suffix = "_WitDB_UserSalt")
    {
        var userBytes = System.Text.Encoding.UTF8.GetBytes(user + suffix);
        return SHA256.HashData(userBytes)[..16];
    }

    #endregion
}
