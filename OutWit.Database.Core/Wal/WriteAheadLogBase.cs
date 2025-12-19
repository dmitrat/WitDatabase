using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Utils;

namespace OutWit.Database.Core.Wal;

/// <summary>
/// Base class for Write-Ahead Log implementations.
/// Provides common functionality for WAL operations including:
/// - File management and header handling
/// - CRC32 integrity checking
/// - Optional encryption via IBlockEncryptor
/// - ArrayPool for reduced allocations
/// - Thread-safe write operations
/// </summary>
public abstract class WriteAheadLogBase : IDisposable
{
    #region Constants
    
    /// <summary>Maximum key size (1MB).</summary>
    protected const int MAX_KEY_SIZE = 1024 * 1024;
    
    /// <summary>Maximum value size (100MB).</summary>
    protected const int MAX_VALUE_SIZE = 100 * 1024 * 1024;

    #endregion

    #region Fields

    private readonly FileStream m_stream;
    private readonly BinaryWriter m_writer;
    private readonly IBlockEncryptor? m_encryptor;
    private readonly bool m_isEncrypted;
    private readonly object m_writeLock = new();
    private readonly uint m_magic;
    private readonly uint m_magicEncrypted;
    private readonly int m_headerSize;
    private readonly bool m_hasVersion;
    private long m_entryCounter;
    private bool m_disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates or opens a WAL file.
    /// </summary>
    /// <param name="filePath">Path to the WAL file.</param>
    /// <param name="magic">Magic number for unencrypted WAL.</param>
    /// <param name="magicEncrypted">Magic number for encrypted WAL.</param>
    /// <param name="headerSize">Size of header in bytes (12 or 16).</param>
    /// <param name="hasVersion">Whether header includes version field.</param>
    /// <param name="encryptor">Optional encryptor for encrypting entries.</param>
    /// <param name="createNew">If true, creates a new WAL (overwrites existing).</param>
    protected WriteAheadLogBase(
        string filePath, 
        uint magic,
        uint magicEncrypted,
        int headerSize,
        bool hasVersion,
        IBlockEncryptor? encryptor = null, 
        bool createNew = false)
    {
        FilePath = filePath;
        m_magic = magic;
        m_magicEncrypted = magicEncrypted;
        m_headerSize = headerSize;
        m_hasVersion = hasVersion;
        m_encryptor = encryptor;
        m_isEncrypted = encryptor != null;

        // Ensure directory exists
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var mode = createNew ? FileMode.Create : FileMode.OpenOrCreate;
        m_stream = new FileStream(filePath, mode, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.WriteThrough);
        m_writer = new BinaryWriter(m_stream);

        if (createNew || m_stream.Length == 0)
        {
            WriteHeader();
        }
        else
        {
            ValidateHeader();
        }
    }

    #endregion

    #region Properties

    /// <summary>Gets the file path of this WAL.</summary>
    public string FilePath { get; }

    /// <summary>Gets the current size of the WAL file in bytes.</summary>
    public long Size => m_stream.Length;

    /// <summary>Gets whether this WAL is encrypted.</summary>
    public bool IsEncrypted => m_isEncrypted;

    /// <summary>Gets the number of entries written to this WAL.</summary>
    public long EntryCount => Volatile.Read(ref m_entryCounter);

    /// <summary>Gets the encryptor used for this WAL.</summary>
    protected IBlockEncryptor? Encryptor => m_encryptor;

    /// <summary>Gets the write lock object for thread synchronization.</summary>
    protected object WriteLock => m_writeLock;

    /// <summary>Gets the underlying file stream.</summary>
    protected FileStream Stream => m_stream;

    /// <summary>Gets the header size in bytes.</summary>
    protected int HeaderSize => m_headerSize;

    #endregion

    #region Public Methods

    /// <summary>
    /// Ensures all pending writes are flushed to disk.
    /// </summary>
    public void Sync()
    {
        ThrowIfDisposed();
        lock (m_writeLock)
        {
            UpdateHeader();
            m_writer.Flush();
            m_stream.Flush(flushToDisk: true);
        }
    }

    /// <summary>
    /// Truncates the WAL, removing all entries.
    /// </summary>
    public void Truncate()
    {
        ThrowIfDisposed();
        lock (m_writeLock)
        {
            m_stream.SetLength(0);
            m_stream.Position = 0;
            m_entryCounter = 0;
            WriteHeader();
        }
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// Writes raw entry data to the WAL.
    /// Handles encryption if enabled.
    /// </summary>
    protected void WriteEntryData(ReadOnlySpan<byte> entryDataWithCrc)
    {
        if (m_isEncrypted)
        {
            var encrypted = m_encryptor!.Encrypt(entryDataWithCrc.ToArray(), m_entryCounter);
            m_writer.Write(encrypted.Length);
            m_writer.Write(encrypted);
        }
        else
        {
            m_stream.Write(entryDataWithCrc);
        }
        m_entryCounter++;
        m_writer.Flush();
    }

    /// <summary>
    /// Reads raw entry data from the WAL.
    /// Handles decryption if enabled.
    /// </summary>
    /// <returns>Decrypted entry data or null if read failed or not encrypted.</returns>
    protected byte[]? ReadEntryData(BinaryReader reader, long entryId)
    {
        if (m_isEncrypted)
        {
            var encLen = reader.ReadInt32();
            if (encLen < 0 || encLen > MAX_VALUE_SIZE) return null;
            
            var rentedBuffer = ArrayPool<byte>.Shared.Rent(encLen);
            try
            {
                m_stream.ReadExactly(rentedBuffer.AsSpan(0, encLen));
                return m_encryptor!.Decrypt(rentedBuffer.AsSpan(0, encLen).ToArray(), entryId);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
        
        return null; // Caller should handle unencrypted reads directly
    }

    /// <summary>
    /// Positions stream at end for appending.
    /// Must be called within write lock.
    /// </summary>
    protected void SeekToEnd()
    {
        m_stream.Position = m_stream.Length;
    }

    /// <summary>
    /// Gets current stream position.
    /// </summary>
    protected long GetPosition() => m_stream.Position;

    /// <summary>
    /// Gets stream length.
    /// </summary>
    protected long GetLength() => m_stream.Length;

    /// <summary>
    /// Sets stream position for reading.
    /// Must be called within write lock.
    /// </summary>
    protected void SeekTo(long position)
    {
        m_stream.Position = position;
    }

    /// <summary>
    /// Creates a BinaryReader for the stream.
    /// </summary>
    protected BinaryReader CreateReader()
    {
        return new BinaryReader(m_stream, System.Text.Encoding.UTF8, leaveOpen: true);
    }

    /// <summary>
    /// Updates the header with current entry count.
    /// Must be called within write lock.
    /// </summary>
    protected void UpdateHeader()
    {
        var pos = m_stream.Position;
        // Entry counter is after magic (4 bytes) and optional version (4 bytes)
        m_stream.Position = m_hasVersion ? 8 : 4;
        m_writer.Write(m_entryCounter);
        m_stream.Position = pos;
    }

    /// <summary>
    /// Flushes and syncs to disk.
    /// Must be called within write lock.
    /// </summary>
    protected void FlushToDisk()
    {
        UpdateHeader();
        m_writer.Flush();
        m_stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Throws if this WAL has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region Private Methods

    private void WriteHeader()
    {
        var magic = m_isEncrypted ? m_magicEncrypted : m_magic;
        m_writer.Write(magic);
        if (m_hasVersion)
        {
            m_writer.Write(2); // Version 2
        }
        m_writer.Write(m_entryCounter);
        m_writer.Flush();
    }

    private void ValidateHeader()
    {
        if (m_stream.Length < m_headerSize)
            throw new InvalidDataException("WAL file is too small");

        m_stream.Position = 0;
        Span<byte> header = stackalloc byte[m_headerSize];
        m_stream.ReadExactly(header);

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        
        if (magic != m_magic && magic != m_magicEncrypted)
            throw new InvalidDataException($"Invalid WAL magic: got 0x{magic:X8}");

        if (m_isEncrypted && magic != m_magicEncrypted)
            throw new InvalidDataException("WAL is not encrypted but encryptor was provided");

        if (!m_isEncrypted && magic == m_magicEncrypted)
            throw new InvalidDataException("WAL is encrypted but no encryptor was provided");

        if (m_hasVersion)
        {
            var version = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));
            if (version > 2)
                throw new InvalidDataException($"WAL version {version} is not supported (max: 2)");
            m_entryCounter = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(8));
        }
        else
        {
            m_entryCounter = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(4));
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes the WAL, flushing any pending data.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed and unmanaged resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (m_disposed) return;
        m_disposed = true;

        if (disposing)
        {
            lock (m_writeLock)
            {
                try { UpdateHeader(); } catch { }
            }
            m_writer.Dispose();
            m_stream.Dispose();
        }
    }

    #endregion
}
