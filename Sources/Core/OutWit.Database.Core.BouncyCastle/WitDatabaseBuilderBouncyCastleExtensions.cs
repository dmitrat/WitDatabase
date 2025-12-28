using OutWit.Database.Core.Builder;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace OutWit.Database.Core.BouncyCastle;

/// <summary>
/// Extension methods for configuring WitDatabaseBuilder with BouncyCastle encryption.
/// </summary>
public static class WitDatabaseBuilderBouncyCastleExtensions
{
    #region Constants

    private const int DEFAULT_PBKDF2_ITERATIONS = 100_000;
    private const int WASM_PBKDF2_ITERATIONS = 10_000;
    private const int KEY_SIZE_BYTES = 32;
    private const int SALT_SIZE_BYTES = 16;

    #endregion

    #region Encryption - Password Based

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, string password)
    {
        return builder.WithBouncyCastleEncryption(password, DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with password-based key derivation and custom iterations.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, string password, int iterations)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = DerivePasswordSalt(password);
        var key = DeriveKey(password, salt, iterations);
        
        builder.Options.EncryptionParameters.Set("password", password);
        builder.Options.EncryptionParameters.Set("iterations", iterations);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.CustomCryptoProvider = new BouncyCastleCryptoProvider(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption optimized for WASM/browser environments.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryptionFast(this WitDatabaseBuilder builder, string password)
    {
        return builder.WithBouncyCastleEncryption(password, WASM_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with user and password-based key derivation.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, string user, string password)
    {
        return builder.WithBouncyCastleUserEncryption(user, password, DEFAULT_PBKDF2_ITERATIONS);
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with user/password and custom iterations.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleUserEncryption(this WitDatabaseBuilder builder, string user, string password, int iterations)
    {
        if (string.IsNullOrEmpty(user))
            throw new ArgumentException("User cannot be empty", nameof(user));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        var salt = DeriveUserSalt(user);
        var key = DeriveKey(password, salt, iterations);
        
        builder.Options.EncryptionParameters.Set("user", user);
        builder.Options.EncryptionParameters.Set("password", password);
        builder.Options.EncryptionParameters.Set("iterations", iterations);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.CustomCryptoProvider = new BouncyCastleCryptoProvider(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption with user/password optimized for WASM/browser environments.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryptionFast(this WitDatabaseBuilder builder, string user, string password)
    {
        return builder.WithBouncyCastleUserEncryption(user, password, WASM_PBKDF2_ITERATIONS);
    }

    #endregion

    #region Encryption - Key Based

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with the specified 256-bit key.
    /// Salt is derived from the key for deterministic behavior.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, byte[] key)
    {
        if (key.Length != KEY_SIZE_BYTES)
            throw new ArgumentException("ChaCha20-Poly1305 requires a 32-byte key", nameof(key));
        
        var salt = DeriveKeySalt(key);
        
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = new BouncyCastleCryptoProvider(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    /// <summary>
    /// Enable ChaCha20-Poly1305 encryption using BouncyCastle with the specified 256-bit key and salt.
    /// </summary>
    public static WitDatabaseBuilder WithBouncyCastleEncryption(this WitDatabaseBuilder builder, byte[] key, byte[] salt)
    {
        if (key.Length != KEY_SIZE_BYTES)
            throw new ArgumentException("ChaCha20-Poly1305 requires a 32-byte key", nameof(key));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        
        builder.Options.EncryptionParameters.Set("key", key);
        builder.Options.EncryptionParameters.Set("salt", salt);
        builder.Options.CustomCryptoProvider = new BouncyCastleCryptoProvider(key);
        builder.Options.EncryptionProviderKey = null;
        return builder;
    }

    #endregion

    #region Key Derivation (BouncyCastle)

    private static byte[] DerivePasswordSalt(string password)
    {
        var digest = new Sha256Digest();
        var input = System.Text.Encoding.UTF8.GetBytes(password + "_WitDB_BC_Salt");
        digest.BlockUpdate(input, 0, input.Length);
        
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        
        var salt = new byte[SALT_SIZE_BYTES];
        Array.Copy(hash, salt, SALT_SIZE_BYTES);
        return salt;
    }

    private static byte[] DeriveUserSalt(string user)
    {
        var digest = new Sha256Digest();
        var input = System.Text.Encoding.UTF8.GetBytes(user + "_WitDB_BC_UserSalt");
        digest.BlockUpdate(input, 0, input.Length);
        
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        
        var salt = new byte[SALT_SIZE_BYTES];
        Array.Copy(hash, salt, SALT_SIZE_BYTES);
        return salt;
    }

    private static byte[] DeriveKeySalt(byte[] key)
    {
        var digest = new Sha256Digest();
        digest.BlockUpdate(key, 0, key.Length);
        var suffix = "_WitDB_BC_KeySalt"u8.ToArray();
        digest.BlockUpdate(suffix, 0, suffix.Length);
        
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        
        var salt = new byte[SALT_SIZE_BYTES];
        Array.Copy(hash, salt, SALT_SIZE_BYTES);
        return salt;
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        var generator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
        generator.Init(
            Org.BouncyCastle.Crypto.PbeParametersGenerator.Pkcs5PasswordToUtf8Bytes(password.ToCharArray()),
            salt,
            iterations);
        
        var keyParam = (KeyParameter)generator.GenerateDerivedMacParameters(KEY_SIZE_BYTES * 8);
        return keyParam.GetKey();
    }

    #endregion
}
