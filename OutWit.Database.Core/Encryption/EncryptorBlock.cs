using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Encryption
{
    /// <summary>
    /// Block encryptor for variable-length data (LSM SSTables, WAL).
    /// Implements IBlockEncryptor with unique nonce per encryption.
    /// 
    /// THREAD-SAFETY: Uses atomic counter for nonce generation.
    /// </summary>
    public sealed class EncryptorBlock : IBlockEncryptor
    {
        #region Fields

        private readonly ICryptoProvider m_crypto;
        private readonly byte[] m_salt;
        private long m_writeCounter;
        private bool m_disposed;

        #endregion

        #region Constructors

        public EncryptorBlock(ICryptoProvider crypto, byte[] salt)
        {
            if (salt.Length < 8)
                throw new ArgumentException("Salt must be at least 8 bytes", nameof(salt));
        
            m_crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            m_salt = (byte[])salt.Clone();
            m_writeCounter = 0;
        }

        #endregion

        #region IBlockEncryptor

        /// <summary>
        /// Encrypts data with unique nonce.
        /// Returns newly allocated result array: [nonce][ciphertext][tag]
        /// </summary>
        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, long blockId)
        {
            ThrowIfDisposed();

            // Allocate result: [nonce][ciphertext][tag]
            var result = new byte[Overhead + plaintext.Length];
            
            // Generate unique nonce directly into result
            GenerateNonce(blockId, result.AsSpan(0, NonceSize));

            var nonce = result.AsSpan(0, NonceSize);
            var ciphertextSpan = result.AsSpan(NonceSize, plaintext.Length);
            var tagSpan = result.AsSpan(NonceSize + plaintext.Length, TagSize);

            m_crypto.Encrypt(nonce, plaintext, ciphertextSpan, tagSpan);

            return result;
        }

        /// <summary>
        /// Decrypts data, returning newly allocated plaintext or null on failure.
        /// Verifies nonce prefix matches expected blockId.
        /// </summary>
        public byte[]? Decrypt(ReadOnlySpan<byte> ciphertext, long blockId)
        {
            ThrowIfDisposed();

            if (ciphertext.Length < Overhead)
                return null;

            var storedNonce = ciphertext[..NonceSize];
        
            // Verify nonce prefix matches expected (prevents block swap attacks)
            Span<byte> expectedPrefix = stackalloc byte[8];
            GenerateNoncePrefix(blockId, expectedPrefix);
            
            if (!CryptographicOperations.FixedTimeEquals(storedNonce[..8], expectedPrefix))
                return null;

            var encryptedData = ciphertext[NonceSize..^TagSize];
            var tag = ciphertext[^TagSize..];

            var plaintext = new byte[encryptedData.Length];

            if (m_crypto.Decrypt(storedNonce, encryptedData, tag, plaintext))
                return plaintext;

            return null;
        }

        #endregion

        #region Nonce Generation

        /// <summary>
        /// Generates unique nonce for encryption.
        /// Structure (12 bytes):
        /// - Bytes 0-7: salt XOR blockId (deterministic, verified on decrypt)
        /// - Bytes 8-11: monotonic counter (ensures uniqueness on rewrite)
        /// </summary>
        private void GenerateNonce(long blockId, Span<byte> nonce)
        {
            long counter = Interlocked.Increment(ref m_writeCounter);
            
            GenerateNoncePrefix(blockId, nonce[..8]);
            BinaryPrimitives.WriteInt32LittleEndian(nonce[8..], (int)counter);
        }

        /// <summary>
        /// Generates the deterministic prefix of nonce (for verification).
        /// </summary>
        private void GenerateNoncePrefix(long blockId, Span<byte> prefix)
        {
            Span<byte> blockBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(blockBytes, blockId);
            
            int saltLen = Math.Min(m_salt.Length, 8);
            for (int i = 0; i < saltLen; i++)
            {
                prefix[i] = (byte)(m_salt[i] ^ blockBytes[i]);
            }
            for (int i = saltLen; i < 8; i++)
            {
                prefix[i] = blockBytes[i];
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