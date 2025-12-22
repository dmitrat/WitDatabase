namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Low-level interface for raw AEAD (Authenticated Encryption with Associated Data) operations.
    /// Implementations provide the actual cryptographic algorithm (AES-GCM, ChaCha20-Poly1305, etc).
    /// </summary>
    public interface ICryptoProvider : IProvider, IDisposable
    {
        #region Encryption

        /// <summary>
        /// Encrypts plaintext using AEAD.
        /// </summary>
        /// <param name="nonce">Nonce (must be NonceSize bytes).</param>
        /// <param name="plaintext">Data to encrypt.</param>
        /// <param name="ciphertext">Output buffer for encrypted data (same size as plaintext).</param>
        /// <param name="tag">Output buffer for authentication tag (must be TagSize bytes).</param>
        void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag);

        /// <summary>
        /// Decrypts ciphertext using AEAD.
        /// </summary>
        /// <param name="nonce">Nonce used during encryption.</param>
        /// <param name="ciphertext">Encrypted data.</param>
        /// <param name="tag">Authentication tag.</param>
        /// <param name="plaintext">Output buffer for decrypted data.</param>
        /// <returns>True if authentication succeeded, false otherwise.</returns>
        bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext);

        #endregion

        #region Clone

        /// <summary>
        /// Creates a new instance of this crypto provider with the same key.
        /// Used when multiple independent consumers need their own instance
        /// (e.g., main store and secondary indexes).
        /// </summary>
        /// <returns>A new instance with the same encryption key.</returns>
        ICryptoProvider Clone();

        #endregion

        #region Properties

        /// <summary>
        /// Nonce size in bytes required by this provider.
        /// </summary>
        int NonceSize { get; }

        /// <summary>
        /// Authentication tag size in bytes.
        /// </summary>
        int TagSize { get; }

        /// <summary>
        /// Total overhead (nonce + tag) in bytes.
        /// </summary>
        int Overhead => NonceSize + TagSize;

        /// <summary>
        /// Gets the unique provider key identifying this crypto implementation.
        /// Used for validation when opening existing databases.
        /// </summary>
        /// <example>
        /// "aes-gcm" for AES-GCM, "chacha20-poly1305" for ChaCha20-Poly1305.
        /// </example>
        string ProviderKey { get; }

        #endregion
    }
}