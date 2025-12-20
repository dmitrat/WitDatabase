using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Storage
{
    /// <summary>
    /// Storage wrapper that encrypts/decrypts pages transparently.
    /// </summary>
    public sealed class StorageEncrypted : IStorage
    {
        #region Constants

        /// <summary>
        /// Maximum page size for stack allocation. Larger sizes use heap allocation.
        /// </summary>
        private const int MAX_STACK_ALLOC_SIZE = 8192;

        /// <summary>
        /// Provider key for encrypted storage.
        /// </summary>
        public const string PROVIDER_KEY = "encrypted";

        #endregion

        #region Fields

        private readonly IStorage m_innerStorage;

        private readonly IPageEncryptor m_encryptor;

        private readonly int m_overhead;

        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an encrypted storage wrapper.
        /// </summary>
        /// <param name="innerStorage">The underlying storage.</param>
        /// <param name="encryptor">The page encryptor.</param>
        public StorageEncrypted(IStorage innerStorage, IPageEncryptor encryptor)
        {
            ArgumentNullException.ThrowIfNull(innerStorage);
            ArgumentNullException.ThrowIfNull(encryptor);

            m_innerStorage = innerStorage;
            m_encryptor = encryptor;
            m_overhead = encryptor.Overhead;
        }

        #endregion

        #region Read

        /// <inheritdoc/>
        public void ReadPage(long pageNumber, Span<byte> buffer)
        {
            ThrowIfDisposed();
        
            int innerPageSize = m_innerStorage.PageSize;
            byte[]? rentedBuffer = null;
            
            try
            {
                Span<byte> encrypted = innerPageSize <= MAX_STACK_ALLOC_SIZE
                    ? stackalloc byte[innerPageSize]
                    : (rentedBuffer = ArrayPool<byte>.Shared.Rent(innerPageSize)).AsSpan(0, innerPageSize);
                
                m_innerStorage.ReadPage(pageNumber, encrypted);
        
                // Check if page is uninitialized (all zeros) - return zeros
                if (IsAllZeros(encrypted))
                {
                    buffer[..PageSize].Clear();
                    return;
                }
        
                // Decrypt
                int decryptedLen = m_encryptor.Decrypt(encrypted, pageNumber, buffer);
                if (decryptedLen < 0)
                {
                    throw new CryptographicException(
                        $"Failed to decrypt page {pageNumber} - authentication failed");
                }
            }
            finally
            {
                if (rentedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: true);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask ReadPageAsync(long pageNumber, Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
        
            int innerPageSize = m_innerStorage.PageSize;
            byte[] encrypted = ArrayPool<byte>.Shared.Rent(innerPageSize);
            
            try
            {
                await m_innerStorage.ReadPageAsync(pageNumber, encrypted.AsMemory(0, innerPageSize), cancellationToken);
        
                // Check if page is uninitialized (all zeros) - return zeros
                if (IsAllZeros(encrypted.AsSpan(0, innerPageSize)))
                {
                    buffer.Span[..PageSize].Clear();
                    return;
                }
        
                int decryptedLen = m_encryptor.Decrypt(encrypted.AsSpan(0, innerPageSize), pageNumber, buffer.Span);
                if (decryptedLen < 0)
                {
                    throw new CryptographicException(
                        $"Failed to decrypt page {pageNumber} - authentication failed");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(encrypted, clearArray: true);
            }
        }

        #endregion

        #region Write

        /// <inheritdoc/>
        public void WritePage(long pageNumber, ReadOnlySpan<byte> buffer)
        {
            ThrowIfDisposed();
        
            int innerPageSize = m_innerStorage.PageSize;
            byte[]? rentedBuffer = null;
            
            try
            {
                Span<byte> encrypted = innerPageSize <= MAX_STACK_ALLOC_SIZE
                    ? stackalloc byte[innerPageSize]
                    : (rentedBuffer = ArrayPool<byte>.Shared.Rent(innerPageSize)).AsSpan(0, innerPageSize);
                
                m_encryptor.Encrypt(buffer, pageNumber, encrypted);
                m_innerStorage.WritePage(pageNumber, encrypted);
            }
            finally
            {
                if (rentedBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: true);
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask WritePageAsync(long pageNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
        
            int innerPageSize = m_innerStorage.PageSize;
            byte[] encrypted = ArrayPool<byte>.Shared.Rent(innerPageSize);
            
            try
            {
                m_encryptor.Encrypt(buffer.Span, pageNumber, encrypted.AsSpan(0, innerPageSize));
                await m_innerStorage.WritePageAsync(pageNumber, encrypted.AsMemory(0, innerPageSize), cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(encrypted, clearArray: true);
            }
        }

        #endregion

        #region Flush

        /// <inheritdoc/>
        public void Flush()
        {
            ThrowIfDisposed();
            m_innerStorage.Flush();
        }

        /// <inheritdoc/>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_innerStorage.FlushAsync(cancellationToken);
        }

        #endregion

        #region SetSize

        /// <inheritdoc/>
        public void SetSize(long pageCount)
        {
            ThrowIfDisposed();
            m_innerStorage.SetSize(pageCount);
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        /// <summary>
        /// Checks if all bytes in the span are zero using SIMD when available.
        /// </summary>
        private static bool IsAllZeros(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
                return true;

            // Use Vector<byte> for SIMD acceleration when data is large enough
            if (Vector.IsHardwareAccelerated && data.Length >= Vector<byte>.Count)
            {
                var zero = Vector<byte>.Zero;
                
                // Process full vectors
                int vectorCount = data.Length / Vector<byte>.Count;
                var vectors = MemoryMarshal.Cast<byte, Vector<byte>>(data[..(vectorCount * Vector<byte>.Count)]);
                
                foreach (var v in vectors)
                {
                    if (v != zero)
                        return false;
                }
                
                // Process remaining bytes
                for (int i = vectorCount * Vector<byte>.Count; i < data.Length; i++)
                {
                    if (data[i] != 0)
                        return false;
                }
                
                return true;
            }
            
            // Fallback: check in 8-byte chunks for better performance than byte-by-byte
            if (data.Length >= sizeof(long))
            {
                var longs = MemoryMarshal.Cast<byte, long>(data[..(data.Length / sizeof(long) * sizeof(long))]);
                foreach (long l in longs)
                {
                    if (l != 0)
                        return false;
                }
                
                // Check remaining bytes
                for (int i = longs.Length * sizeof(long); i < data.Length; i++)
                {
                    if (data[i] != 0)
                        return false;
                }
                
                return true;
            }
            
            // Small data: byte-by-byte
            foreach (byte b in data)
            {
                if (b != 0)
                    return false;
            }
            return true;
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!m_disposed)
            {
                m_encryptor.Dispose();
                m_innerStorage.Dispose();
                m_disposed = true;
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public int PageSize => m_innerStorage.PageSize - m_overhead;

        /// <inheritdoc/>
        public long PageCount => m_innerStorage.PageCount;

        /// <inheritdoc/>
        public bool IsReadOnly => m_innerStorage.IsReadOnly;

        /// <inheritdoc/>
        public string ProviderKey => PROVIDER_KEY;

        #endregion
    }
}
