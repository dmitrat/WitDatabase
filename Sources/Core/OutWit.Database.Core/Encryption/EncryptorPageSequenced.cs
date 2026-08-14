using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Encryption;

/// <summary>
/// Page encryptor whose nonce is the page number and a sequence number that survives the file.
/// </summary>
/// <remarks>
/// <para>
/// Replaces <see cref="EncryptorPage"/> for databases carrying a <see cref="CryptoHeader"/>. The
/// difference is entirely in the nonce:
/// </para>
/// <code>
/// EncryptorPage           (salt[0..8] XOR pageNumber) || counter as int32
/// EncryptorPageSequenced   pageNumber as uint32       || sequence as uint64
/// </code>
/// <para>
/// Two things change with it. The salt is out of the nonce, so the first bytes of the file stop
/// being a password verifier - which is what they were, at one SHA-256 per guess. And the counter is
/// out of the encryptor, so it no longer restarts at zero every time the database is opened; the
/// sequence comes from <see cref="INonceSequence"/>, which reserves a block in the file's header
/// before handing any of it out.
/// </para>
/// <para>
/// The page number still occupies the first four bytes and is still verified on decrypt, so a page
/// moved to another offset is still rejected. Four bytes rather than eight bounds a database at
/// 2^32 pages - 17 TB at the default page size - and <see cref="Encrypt"/> refuses beyond that
/// rather than letting two pages share a prefix.
/// </para>
/// <para>
/// The sequence alone makes every nonce unique, so uniqueness does not depend on the page number at
/// all; the prefix is a binding, not a distinguisher.
/// </para>
/// </remarks>
public sealed class EncryptorPageSequenced : IPageEncryptor
{
    #region Constants

    private const int PREFIX_SIZE = 4;

    #endregion

    #region Fields

    private readonly ICryptoProvider m_crypto;

    private readonly INonceSequence m_sequence;

    private bool m_disposed;

    #endregion

    #region Constructors

    public EncryptorPageSequenced(ICryptoProvider crypto, INonceSequence sequence)
    {
        m_crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        m_sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));

        if (m_crypto.NonceSize != PREFIX_SIZE + sizeof(ulong))
        {
            throw new ArgumentException(
                $"This encryptor writes a {PREFIX_SIZE + sizeof(ulong)}-byte nonce and the crypto "
                + $"provider takes {m_crypto.NonceSize}.", nameof(crypto));
        }
    }

    #endregion

    #region Encrypt

    /// <inheritdoc/>
    public int Encrypt(ReadOnlySpan<byte> plaintext, long pageNumber, Span<byte> ciphertext)
    {
        ThrowIfDisposed();

        var totalSize = Overhead + plaintext.Length;

        if (ciphertext.Length < totalSize)
            throw new ArgumentException($"Ciphertext buffer too small: need {totalSize}, got {ciphertext.Length}");

        var nonce = ciphertext[..NonceSize];
        WritePrefix(pageNumber, nonce[..PREFIX_SIZE]);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[PREFIX_SIZE..], m_sequence.Next());

        var ciphertextData = ciphertext.Slice(NonceSize, plaintext.Length);
        var tag = ciphertext.Slice(NonceSize + plaintext.Length, TagSize);

        m_crypto.Encrypt(nonce, plaintext, ciphertextData, tag);

        return totalSize;
    }

    #endregion

    #region Decrypt

    /// <inheritdoc/>
    public int Decrypt(ReadOnlySpan<byte> ciphertext, long pageNumber, Span<byte> plaintext)
    {
        ThrowIfDisposed();

        if (ciphertext.Length < Overhead)
            return -1;

        var plaintextLen = ciphertext.Length - Overhead;

        if (plaintext.Length < plaintextLen)
            throw new ArgumentException($"Plaintext buffer too small: need {plaintextLen}, got {plaintext.Length}");

        var storedNonce = ciphertext[..NonceSize];

        Span<byte> expectedPrefix = stackalloc byte[PREFIX_SIZE];
        WritePrefix(pageNumber, expectedPrefix);

        // The page number is bound into the nonce, so a page lifted to another offset is refused
        // before the tag is even consulted.
        if (!CryptographicOperations.FixedTimeEquals(storedNonce[..PREFIX_SIZE], expectedPrefix))
            return -1;

        var encryptedData = ciphertext.Slice(NonceSize, plaintextLen);
        var tag = ciphertext[^TagSize..];

        if (m_crypto.Decrypt(storedNonce, encryptedData, tag, plaintext[..plaintextLen]))
            return plaintextLen;

        return -1;
    }

    #endregion

    #region Tools

    private static void WritePrefix(long pageNumber, Span<byte> prefix)
    {
        if (pageNumber < 0 || pageNumber > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"An encrypted database holds at most {uint.MaxValue} pages: the page number is "
                + "bound into the nonce as four bytes, and letting it wrap would put two pages "
                + "behind one prefix.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)pageNumber);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_crypto.Dispose();
    }

    #endregion

    #region Properties

    public int Overhead => m_crypto.Overhead;

    private int NonceSize => m_crypto.NonceSize;

    private int TagSize => m_crypto.TagSize;

    #endregion
}
