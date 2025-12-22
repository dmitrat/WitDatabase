using OutWit.Database.Core.Interfaces;
using System.Security.Cryptography;

namespace OutWit.Database.Core.Encryption;

/// <summary>
/// AES-GCM crypto provider using built-in .NET cryptography.
/// Fast, hardware-accelerated with AES-NI.
/// Thread-safe - creates new AesGcm instance per operation.
/// </summary>
public sealed class EncryptorProviderAesGcm : ICryptoProvider
{
    #region Constants

    /// <summary>
    /// Default PBKDF2 iteration count for password derivation.
    /// </summary>
    public const int DEFAULT_ITERATIONS = 100_000;

    /// <summary>
    /// Minimum recommended PBKDF2 iterations.
    /// </summary>
    public const int MIN_ITERATIONS = 10_000;

    /// <summary>
    /// Provider key for AES-GCM crypto.
    /// </summary>
    public const string PROVIDER_KEY = "aes-gcm";

    #endregion

    #region Fields

    private readonly byte[] m_key;

    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates AES-GCM provider with specified key.
    /// </summary>
    /// <param name="key">256-bit (32 bytes) encryption key.</param>
    public EncryptorProviderAesGcm(byte[] key)
    {
        if (key.Length != 32)
            throw new ArgumentException("Key must be 256 bits (32 bytes)", nameof(key));

        m_key = new byte[32];
        key.AsSpan().CopyTo(m_key);
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates AES-GCM provider from password using PBKDF2-SHA256.
    /// </summary>
    /// <param name="password">User password.</param>
    /// <param name="salt">Salt for key derivation (at least 8 bytes, 16 recommended).</param>
    /// <param name="iterations">PBKDF2 iteration count (minimum 10000).</param>
    public static EncryptorProviderAesGcm FromPassword(string password, byte[] salt, int iterations = DEFAULT_ITERATIONS)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));
        if (salt.Length < 8)
            throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        if (iterations < MIN_ITERATIONS)
            throw new ArgumentException($"Iterations must be at least {MIN_ITERATIONS}", nameof(iterations));

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return new EncryptorProviderAesGcm(key);
    }

    #endregion

    #region ICryptoProvider

    /// <inheritdoc/>
    public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
    {
        ThrowIfDisposed();
        
        if (nonce.Length != NonceSize)
            throw new ArgumentException($"Nonce must be {NonceSize} bytes", nameof(nonce));
        if (ciphertext.Length < plaintext.Length)
            throw new ArgumentException("Ciphertext buffer too small", nameof(ciphertext));
        if (tag.Length != TagSize)
            throw new ArgumentException($"Tag must be {TagSize} bytes", nameof(tag));

        using var aes = new AesGcm(m_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext[..plaintext.Length], tag);
    }

    /// <inheritdoc/>
    public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
    {
        ThrowIfDisposed();
        
        if (nonce.Length != NonceSize)
            return false;
        if (plaintext.Length < ciphertext.Length)
            return false;
        if (tag.Length != TagSize)
            return false;

        try
        {
            using var aes = new AesGcm(m_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext[..ciphertext.Length]);
            return true;
        }
        catch (CryptographicException)
        {
            // Clear partial output on failure for security
            plaintext[..ciphertext.Length].Clear();
            return false;
        }
    }

    /// <inheritdoc/>
    public ICryptoProvider Clone()
    {
        ThrowIfDisposed();
        return new EncryptorProviderAesGcm(m_key);
    }

    #endregion

    #region Tools

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (m_disposed) return;
        m_disposed = true;
        CryptographicOperations.ZeroMemory(m_key);
    }

    #endregion

    #region Properties

    /// <inheritdoc/>
    public int NonceSize => 12;

    /// <inheritdoc/>
    public int TagSize => 16;

    /// <inheritdoc/>
    public string ProviderKey => PROVIDER_KEY;

    #endregion
}