using OutWit.Database.Core.Interfaces;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using ChaCha20Poly1305 = Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305;

namespace OutWit.Database.Core.BouncyCastle
{
    /// <summary>
    /// ChaCha20-Poly1305 crypto provider using BouncyCastle.
    /// Good alternative when AES-NI is not available.
    /// </summary>
    public sealed class BouncyCastleCryptoProvider : ICryptoProvider
    {
        #region Fields

        private readonly byte[] m_key;

        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates ChaCha20-Poly1305 provider with specified key.
        /// </summary>
        /// <param name="key">256-bit (32 bytes) encryption key.</param>
        public BouncyCastleCryptoProvider(byte[] key)
        {
            if (key.Length != 32)
                throw new ArgumentException("Key must be 256 bits (32 bytes)", nameof(key));
            m_key = (byte[])key.Clone();
        }

        #endregion

        #region Functions

        /// <summary>
        /// Creates ChaCha20-Poly1305 provider from password.
        /// </summary>
        public static BouncyCastleCryptoProvider FromPassword(string password, byte[] salt, int iterations = 100_000)
        {
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return new BouncyCastleCryptoProvider(key);
        }

        /// <inheritdoc/>
        public void Encrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext, Span<byte> tag)
        {
            ThrowIfDisposed();

            var cipher = new ChaCha20Poly1305();
            var keyParam = new KeyParameter(m_key);

            // Copy nonce to array once (BouncyCastle requires array)
            var nonceArray = new byte[nonce.Length];
            nonce.CopyTo(nonceArray);

            var parameters = new AeadParameters(keyParam, TagSize * 8, nonceArray);
            cipher.Init(true, parameters);

            // Use shared buffer for output
            var output = new byte[plaintext.Length + TagSize];

            // Process directly from span (requires copy for BouncyCastle API)
            var plaintextArray = new byte[plaintext.Length];
            plaintext.CopyTo(plaintextArray);

            var len = cipher.ProcessBytes(plaintextArray, 0, plaintextArray.Length, output, 0);
            len += cipher.DoFinal(output, len);

            // Copy results to output spans
            output.AsSpan(0, plaintext.Length).CopyTo(ciphertext);
            output.AsSpan(plaintext.Length, TagSize).CopyTo(tag);
        }

        /// <inheritdoc/>
        public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag, Span<byte> plaintext)
        {
            ThrowIfDisposed();

            try
            {
                var cipher = new ChaCha20Poly1305();
                var keyParam = new KeyParameter(m_key);

                // Copy nonce to array once
                var nonceArray = new byte[nonce.Length];
                nonce.CopyTo(nonceArray);

                var parameters = new AeadParameters(keyParam, TagSize * 8, nonceArray);
                cipher.Init(false, parameters);

                // Combine ciphertext + tag for BouncyCastle (requires single array)
                var input = new byte[ciphertext.Length + TagSize];
                ciphertext.CopyTo(input);
                tag.CopyTo(input.AsSpan(ciphertext.Length));

                var output = new byte[ciphertext.Length];
                var len = cipher.ProcessBytes(input, 0, input.Length, output, 0);
                len += cipher.DoFinal(output, len);

                output.AsSpan(0, len).CopyTo(plaintext);
                return true;
            }
            catch (InvalidCipherTextException)
            {
                return false;
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

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            CryptographicOperations.ZeroMemory(m_key);
        }

        #endregion

        #region Properties

        public int NonceSize => 12;
        public int TagSize => 16;

        #endregion
    }
}
