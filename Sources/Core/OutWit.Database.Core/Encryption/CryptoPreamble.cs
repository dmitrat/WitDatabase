using System.Buffers;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Encryption;

/// <summary>
/// The plaintext first page of an encrypted database: owns the <see cref="CryptoHeader"/> and hands
/// out nonce sequence numbers that no other session will ever use.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it lives.</b> An encrypted database has no plaintext region to put a header in - page 0
/// is ciphertext and so are its magic bytes, which is why <c>StorageDetector</c> reports "encrypted"
/// by failing to recognise anything. So one physical page is reserved in front of everything else
/// and never encrypted, and <see cref="Storage.StorageEncrypted"/> shifts every logical page one
/// physical page along. An unencrypted database is untouched by any of this.
/// </para>
/// <para>
/// <b>How the sequence survives a kill.</b> Opening the database RESERVES a block of numbers: the
/// header on disk is advanced by <see cref="RESERVE"/> and flushed before a single one is handed
/// out. A process that is killed loses the unused remainder of its block and nothing else - the next
/// session starts above every number the last one could possibly have used. That is the whole
/// difference from a counter held in a field, which restarted at zero and made two sessions
/// encrypt page 0 under one nonce.
/// </para>
/// <para>
/// The block is never given back on close. Handing back would mean writing a SMALLER number than the
/// one on disk, and a torn write of that is the one failure that could cause reuse. At 2^64 numbers
/// and 65,536 per open, throwing the remainder away costs nothing worth the risk.
/// </para>
/// </remarks>
public sealed class CryptoPreamble : INonceSequence, IDisposable
{
    #region Constants

    /// <summary>
    /// The physical page the preamble occupies. Everything else in the file is shifted past it.
    /// </summary>
    public const long PREAMBLE_PAGE = 0;

    /// <summary>
    /// How many sequence numbers an open reserves at a time. Each reservation costs one page write
    /// and one flush, and each is good for that many page encryptions.
    /// </summary>
    private const ulong RESERVE = 1UL << 16;

    #endregion

    #region Fields

    private readonly IStorage m_storage;

    private readonly Lock m_lock = new();

    private CryptoHeader m_header;

    private ulong m_next;

    private ulong m_reservedTo;

    private bool m_disposed;

    #endregion

    #region Constructors

    private CryptoPreamble(IStorage storage, CryptoHeader header)
    {
        m_storage = storage;
        m_header = header;
        m_next = header.NonceSequence;
        m_reservedTo = header.NonceSequence;
    }

    #endregion

    #region Open

    /// <summary>
    /// What the first physical page of a storage turns out to be.
    /// </summary>
    public enum Shape
    {
        /// <summary>
        /// Nothing has been written: a database being created.
        /// </summary>
        Empty,

        /// <summary>
        /// A crypto preamble, so the file carries its own salt, iteration count and sequence.
        /// </summary>
        Preamble,

        /// <summary>
        /// Something else - an unencrypted database, or one encrypted before the preamble existed.
        /// Either way there is nothing here to read, and the caller falls back.
        /// </summary>
        Other
    }

    /// <summary>
    /// Reads the first physical page and says what it is, without needing a password to do it.
    /// </summary>
    public static Shape Inspect(IStorage storage, out CryptoHeader header)
    {
        ArgumentNullException.ThrowIfNull(storage);

        header = default;

        if (storage.PageCount <= PREAMBLE_PAGE)
            return Shape.Empty;

        var buffer = ArrayPool<byte>.Shared.Rent(storage.PageSize);

        try
        {
            var page = buffer.AsSpan(0, storage.PageSize);
            storage.ReadPage(PREAMBLE_PAGE, page);

            if (CryptoHeader.TryReadFrom(page, out header))
                return Shape.Preamble;

            return IsAllZeros(page[..CryptoHeader.SIZE]) ? Shape.Empty : Shape.Other;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    /// <summary>
    /// Writes a freshly drawn preamble into a database being created.
    /// </summary>
    public static CryptoPreamble Create(IStorage storage, CryptoHeader header)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (storage.PageCount <= PREAMBLE_PAGE)
            storage.SetSize(PREAMBLE_PAGE + 1);

        var preamble = new CryptoPreamble(storage, header);
        preamble.Write(header.NonceSequence);

        return preamble;
    }

    /// <summary>
    /// Takes ownership of a preamble already in the file, as read by <see cref="Inspect"/>.
    /// </summary>
    public static CryptoPreamble Open(IStorage storage, CryptoHeader header)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new CryptoPreamble(storage, header);
    }

    #endregion

    #region INonceSequence

    /// <inheritdoc/>
    public ulong Next()
    {
        lock (m_lock)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            if (m_next >= m_reservedTo)
                Reserve();

            return m_next++;
        }
    }

    #endregion

    #region Rewrap

    /// <summary>
    /// Replaces the wrapped key with one wrapped under a new password, and writes it. The pages are
    /// untouched, which is what the wrapped key exists for.
    /// </summary>
    public void Rewrap(byte[] dataKey, string password, int iterations)
    {
        lock (m_lock)
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);

            m_header.Rewrap(dataKey, password, iterations);

            // Whatever this session has already reserved stays reserved: the sequence is about the
            // pages, and the pages did not move.
            Write(m_reservedTo);
        }
    }

    #endregion

    #region Tools

    /// <summary>
    /// Advances the number on disk past everything this session may hand out, and only then hands
    /// any of it out. The order is the guarantee.
    /// </summary>
    private void Reserve()
    {
        if (m_storage.IsReadOnly)
        {
            throw new InvalidOperationException(
                "This database is open read-only, so no nonce sequence can be reserved - and without "
                + "a reservation nothing may be encrypted.");
        }

        checked
        {
            m_reservedTo = m_next + RESERVE;
        }

        Write(m_reservedTo);
    }

    private void Write(ulong sequence)
    {
        m_header.NonceSequence = sequence;

        var buffer = ArrayPool<byte>.Shared.Rent(m_storage.PageSize);

        try
        {
            var page = buffer.AsSpan(0, m_storage.PageSize);
            page.Clear();
            m_header.WriteTo(page);

            m_storage.WritePage(PREAMBLE_PAGE, page);
            m_storage.Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static bool IsAllZeros(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
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
        lock (m_lock)
        {
            m_disposed = true;
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// The header as it stands, including any rewrapping done since it was opened.
    /// </summary>
    public CryptoHeader Header
    {
        get
        {
            lock (m_lock)
                return m_header;
        }
    }

    #endregion
}
