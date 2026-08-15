using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Encryption;

/// <summary>
/// Block encryptor for variable-length data - LSM SSTables and the WAL - whose nonce is the block id
/// and a sequence number that survives the file.
/// </summary>
/// <remarks>
/// <para>
/// The same change as <see cref="EncryptorPageSequenced"/>, on the other implementation of the same
/// idea. <see cref="EncryptorBlock"/> carried the identical construction - the salt XORed into the
/// nonce prefix and a counter zeroed in the constructor - and therefore the identical two defects.
/// Checking the other implementation of an interface is the cheapest version of "fix every path with
/// the shape, not the one the finding names".
/// </para>
/// <para>
/// Kept for reading blocks written before the crypto header existed; new databases get this one.
/// </para>
/// </remarks>
public sealed class EncryptorBlockSequenced : IBlockEncryptor, IPasswordRewrapSource
{
    #region Constants

    private const int PREFIX_SIZE = 4;

    #endregion

    #region Fields

    private readonly ICryptoProvider m_crypto;

    private readonly INonceSequence m_sequence;

    private readonly IDisposable? m_owned;

    /// <summary>
    /// The same object as <see cref="m_owned"/> when it is one, kept typed so an LSM database's
    /// password can be rewrapped through the LIVE preamble - the same reason and the same shape as
    /// <c>StorageEncrypted</c>, which does it for the paged store.
    /// </summary>
    private readonly CryptoPreamble? m_rewrappable;

    private bool m_disposed;

    #endregion

    #region Constructors

    /// <param name="owned">
    /// Disposed with this encryptor. An LSM store owns its encryptor and nothing else owns the
    /// preamble the sequence comes from, so it travels with it.
    /// </param>
    public EncryptorBlockSequenced(ICryptoProvider crypto, INonceSequence sequence, IDisposable? owned = null)
    {
        m_crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        m_sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        m_owned = owned;
        m_rewrappable = owned as CryptoPreamble ?? sequence as CryptoPreamble;

        if (m_crypto.NonceSize != PREFIX_SIZE + sizeof(ulong))
        {
            throw new ArgumentException(
                $"This encryptor writes a {PREFIX_SIZE + sizeof(ulong)}-byte nonce and the crypto "
                + $"provider takes {m_crypto.NonceSize}.", nameof(crypto));
        }
    }

    #endregion

    #region IBlockEncryptor

    /// <inheritdoc/>
    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, long blockId)
    {
        ThrowIfDisposed();

        var result = new byte[Overhead + plaintext.Length];

        var nonce = result.AsSpan(0, NonceSize);
        WritePrefix(blockId, nonce[..PREFIX_SIZE]);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[PREFIX_SIZE..], m_sequence.Next());

        var ciphertext = result.AsSpan(NonceSize, plaintext.Length);
        var tag = result.AsSpan(NonceSize + plaintext.Length, TagSize);

        m_crypto.Encrypt(nonce, plaintext, ciphertext, tag);

        return result;
    }

    /// <inheritdoc/>
    public byte[]? Decrypt(ReadOnlySpan<byte> ciphertext, long blockId)
    {
        ThrowIfDisposed();

        if (ciphertext.Length < Overhead)
            return null;

        var storedNonce = ciphertext[..NonceSize];

        Span<byte> expectedPrefix = stackalloc byte[PREFIX_SIZE];
        WritePrefix(blockId, expectedPrefix);

        if (!CryptographicOperations.FixedTimeEquals(storedNonce[..PREFIX_SIZE], expectedPrefix))
            return null;

        var encryptedData = ciphertext[NonceSize..^TagSize];
        var tag = ciphertext[^TagSize..];

        var plaintext = new byte[encryptedData.Length];

        if (m_crypto.Decrypt(storedNonce, encryptedData, tag, plaintext))
            return plaintext;

        return null;
    }

    #endregion

    #region Tools

    private static void WritePrefix(long blockId, Span<byte> prefix)
    {
        if (blockId < 0 || blockId > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(blockId),
                $"An encrypted store addresses at most {uint.MaxValue} blocks: the block id is bound "
                + "into the nonce as four bytes.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(prefix, (uint)blockId);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
    }

    #endregion

    #region IPasswordRewrapSource

    /// <summary>
    /// True when this encryptor's preamble holds a WRAPPED key. False for an index directory, whose
    /// sidecar rides on the database's data key and has no wrapped key of its own - which is also
    /// why rewrapping the database does not disturb its indexes.
    /// </summary>
    public bool CanRewrapPassword => m_rewrappable?.Header.WrappedKey != null;

    /// <inheritdoc />
    public void RewrapPassword(string currentPassword, string newPassword, int? iterations = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(currentPassword);
        ArgumentException.ThrowIfNullOrEmpty(newPassword);
        ObjectDisposedException.ThrowIf(m_disposed, this);

        if (m_rewrappable == null || m_rewrappable.Header.WrappedKey == null)
        {
            throw new NotSupportedException(
                "This store has no wrapped key, so there is no password to rewrap. A store created "
                + "without a password cannot be given one in place - that is a migration.");
        }

        var dataKey = m_rewrappable.Header.UnwrapDataKey(currentPassword);

        try
        {
            m_rewrappable.Rewrap(dataKey, newPassword, iterations ?? (int)m_rewrappable.Header.Iterations);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_owned?.Dispose();
        m_crypto.Dispose();
    }

    #endregion

    #region Properties

    public int Overhead => m_crypto.Overhead;

    private int NonceSize => m_crypto.NonceSize;

    private int TagSize => m_crypto.TagSize;

    #endregion
}
