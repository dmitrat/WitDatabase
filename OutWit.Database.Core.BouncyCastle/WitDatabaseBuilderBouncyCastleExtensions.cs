using OutWit.Database.Core.Builder;

namespace OutWit.Database.Core.BouncyCastle;

/// <summary>
/// Extension methods for configuring WitDatabaseBuilder with BouncyCastle encryption.
/// </summary>
public static class WitDatabaseBuilderBouncyCastleExtensions
{
    #region Encryption

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with the specified 256-bit key.
    /// Good alternative when AES-NI hardware acceleration is not available.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="key">256-bit (32 bytes) encryption key.</param>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("ChaCha20-Poly1305 requires a 32-byte key", nameof(key));
        
        builder.Options.CryptoProvider = new BouncyCastleCryptoProvider(key);
        return builder;
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with the specified 256-bit key and salt.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="key">256-bit (32 bytes) encryption key.</param>
    /// <param name="salt">Salt for key derivation (at least 8 bytes).</param>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, byte[] key, byte[] salt)
    {
        if (key.Length != 32)
            throw new ArgumentException("ChaCha20-Poly1305 requires a 32-byte key", nameof(key));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        
        builder.Options.CryptoProvider = new BouncyCastleCryptoProvider(key);
        builder.Options.EncryptionSalt = salt;
        return builder;
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with password-based key derivation.
    /// </summary>
    /// <param name="builder">The database builder.</param>
    /// <param name="password">Password to derive encryption key from.</param>
    /// <param name="salt">Salt for key derivation (at least 8 bytes).</param>
    /// <param name="iterations">Number of PBKDF2 iterations (default: 100,000).</param>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(
        this WitDatabaseBuilder builder, 
        string password, 
        byte[] salt, 
        int iterations = 100_000)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        if (iterations < 1000)
            throw new ArgumentException("Iterations must be at least 1000", nameof(iterations));
        
        builder.Options.CryptoProvider = BouncyCastleCryptoProvider.FromPassword(password, salt, iterations);
        builder.Options.EncryptionSalt = salt;
        return builder;
    }

    #endregion
}
