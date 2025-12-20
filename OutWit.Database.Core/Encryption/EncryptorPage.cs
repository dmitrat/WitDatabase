using OutWit.Database.Core.Interfaces;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OutWit.Database.Core.Encryption
{
    /// <summary>
    /// Page encryptor optimized for in-place encryption/decryption.
    /// Zero-copy operations directly into output spans.
    /// Implements IPageEncryptor for BTree page storage.
    /// 
    /// THREAD-SAFETY: This class uses atomic counter for nonce generation,
    /// making it safe for concurrent use from multiple threads.
    /// </summary>
    public sealed class EncryptorPage : IPageEncryptor
    {
        #region Fields

        private readonly ICryptoProvider m_crypto;
        private readonly byte[] m_salt;
        private long m_writeCounter;
        private bool m_disposed;

        #endregion

        #region Constructors

        public EncryptorPage(ICryptoProvider crypto, byte[] salt)
        {
            if (salt.Length < 8)
                throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        
            m_crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            m_salt = (byte[])salt.Clone();
            m_writeCounter = 0;
        }

        #endregion

        #region Encrypt

        /// <summary>
        /// Encrypts plaintext directly into ciphertext span (zero-copy).
        /// Uses unique nonce with monotonic counter to prevent nonce reuse.
        /// </summary>
        /// <returns>Number of bytes written to ciphertext.</returns>
        public int Encrypt(ReadOnlySpan<byte> plaintext, long pageNumber, Span<byte> ciphertext)
        {
            ThrowIfDisposed();

            int totalSize = Overhead + plaintext.Length;
            if (ciphertext.Length < totalSize)
                throw new ArgumentException($"Ciphertext buffer too small: need {totalSize}, got {ciphertext.Length}");

            // Generate unique nonce directly into output
            Span<byte> nonce = ciphertext[..NonceSize];
            GenerateNonce(pageNumber, nonce);

            var ciphertextData = ciphertext.Slice(NonceSize, plaintext.Length);
            var tag = ciphertext.Slice(NonceSize + plaintext.Length, TagSize);

            m_crypto.Encrypt(nonce, plaintext, ciphertextData, tag);

            return totalSize;
        }

        #endregion

        #region Decrypt

        /// <summary>
        /// Decrypts ciphertext directly into plaintext span (zero-copy).
        /// Verifies nonce prefix matches expected page number.
        /// </summary>
        /// <returns>Number of bytes written to plaintext, or -1 on authentication failure.</returns>
        public int Decrypt(ReadOnlySpan<byte> ciphertext, long pageNumber, Span<byte> plaintext)
        {
            ThrowIfDisposed();

            if (ciphertext.Length < Overhead)
                return -1;

            int plaintextLen = ciphertext.Length - Overhead;
            if (plaintext.Length < plaintextLen)
                throw new ArgumentException($"Plaintext buffer too small: need {plaintextLen}, got {plaintext.Length}");

            var storedNonce = ciphertext[..NonceSize];
        
            // Verify nonce prefix matches expected (prevents page swap attacks)
            Span<byte> expectedPrefix = stackalloc byte[8];
            GenerateNoncePrefix(pageNumber, expectedPrefix);
            
            if (!CryptographicOperations.FixedTimeEquals(storedNonce[..8], expectedPrefix))
                return -1;

            var encryptedData = ciphertext.Slice(NonceSize, plaintextLen);
            var tag = ciphertext[^TagSize..];

            if (m_crypto.Decrypt(storedNonce, encryptedData, tag, plaintext[..plaintextLen]))
                return plaintextLen;

            return -1;
        }

        #endregion

        #region Nonce Generation

        /// <summary>
        /// Generates unique nonce for encryption.
        /// Structure (12 bytes):
        /// - Bytes 0-7: salt XOR pageNumber (deterministic, verified on decrypt)
        /// - Bytes 8-11: monotonic counter (ensures uniqueness on rewrite)
        /// </summary>
        private void GenerateNonce(long pageNumber, Span<byte> nonce)
        {
            // Increment counter atomically for thread safety
            long counter = Interlocked.Increment(ref m_writeCounter);
            
            // First 8 bytes: deterministic part (salt XOR pageNumber)
            GenerateNoncePrefix(pageNumber, nonce[..8]);
            
            // Last 4 bytes: counter ensures uniqueness even for same page
            BinaryPrimitives.WriteInt32LittleEndian(nonce[8..], (int)counter);
        }

        /// <summary>
        /// Generates the deterministic prefix of nonce (for verification).
        /// </summary>
        private void GenerateNoncePrefix(long pageNumber, Span<byte> prefix)
        {
            Span<byte> pageBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(pageBytes, pageNumber);
            
            int saltLen = Math.Min(m_salt.Length, 8);
            for (int i = 0; i < saltLen; i++)
            {
                prefix[i] = (byte)(m_salt[i] ^ pageBytes[i]);
            }
            for (int i = saltLen; i < 8; i++)
            {
                prefix[i] = pageBytes[i];
            }
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            CryptographicOperations.ZeroMemory(m_salt);
            m_crypto.Dispose();
        }

        #endregion

        #region Properties

        public int Overhead => m_crypto.Overhead;

        private int NonceSize => m_crypto.NonceSize;

        private int TagSize => m_crypto.TagSize;

        #endregion
    }
}