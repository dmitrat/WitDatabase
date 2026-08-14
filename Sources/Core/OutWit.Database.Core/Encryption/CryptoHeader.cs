using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OutWit.Database.Core.Encryption;

/// <summary>
/// Which key derivation a file's crypto header describes.
/// </summary>
/// <remarks>
/// The id is in the file so that a second algorithm - Argon2id, most likely - is a new value here
/// rather than a format nobody can tell from the old one. Before this existed, the derivation was
/// whatever the running build happened to do.
/// </remarks>
public enum CryptoKdf : byte
{
    /// <summary>
    /// No derivation: the caller supplied the key material itself, through
    /// <c>WithAesEncryption(key)</c> or its own <c>ICryptoProvider</c>. The header still carries a
    /// random salt and the nonce sequence, which is what the file needs it for.
    /// </summary>
    None = 0,

    /// <summary>
    /// PBKDF2-HMAC-SHA256 over the password, at the iteration count recorded beside this id.
    /// </summary>
    Pbkdf2Sha256 = 1
}

/// <summary>
/// The plaintext preamble of an encrypted database: everything needed to turn a password into the
/// key that opens the file, and nothing that helps anyone who does not have the password.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why there is a preamble at all.</b> Before this, an encrypted database began with page 0's
/// ciphertext and there was no plaintext region anywhere in the file - <c>StorageDetector</c> reports
/// "encrypted" precisely because the magic bytes fail to match, not because it read anything. So the
/// four things a file has to say about its own encryption had nowhere to live, and each of them was
/// instead recomputed from the password by whatever build happened to open it. That produced:
/// </para>
/// <list type="bullet">
/// <item><description>a salt that was <c>SHA256(password + "_WitDB_Salt")[..16]</c>, so one password
/// meant one key across every database ever created with it;</description></item>
/// <item><description>that same salt written in the clear as the first eight bytes of the file, by
/// way of the page nonce - a password verifier costing ONE SHA-256, measured at 2,000,007 candidates
/// in 0.48 s against 5.6 hours through the derivation it was supposed to cost;</description></item>
/// <item><description>an iteration count that lived only in the connection string, so
/// <c>Fast Encryption=true</c> wrote files that could not be opened without remembering a
/// flag;</description></item>
/// <item><description>a nonce counter starting at 0 on every open, so two sessions encrypted the
/// same page under the same nonce - which for AES-GCM hands the second plaintext to anyone holding
/// both ciphertexts.</description></item>
/// </list>
/// <para>
/// <b>The layout</b>, 128 bytes at the start of a physical page that is never encrypted. Everything
/// here is public by design; the only secret is the password.
/// </para>
/// <code>
/// [0-15]    magic "WitDB Crypt 1\0\0\0"
/// [16-17]   format version, major.minor as ushort
/// [18]      KDF id (CryptoKdf)
/// [19]      flags - bit 0: a wrapped data key follows
/// [20-23]   iterations
/// [24-39]   salt, 16 random bytes drawn when the database is created
/// [40-47]   the next unused nonce sequence number
/// [48-59]   nonce the data key was wrapped under
/// [60-91]   the wrapped data key
/// [92-107]  its authentication tag
/// [108-127] reserved, zero
/// </code>
/// <para>
/// <b>The wrapped data key</b> is what makes a password change cheap: the pages are encrypted under
/// a random data key, and the password only ever encrypts that key. Changing the password, or
/// raising the iteration count, rewrites these 60 bytes rather than the database. It also gives a
/// wrong password an honest answer - the wrap tag fails and the file says so, instead of the old
/// "Failed to decrypt page 0 - authentication failed" from somewhere further in.
/// </para>
/// </remarks>
public struct CryptoHeader
{
    #region Constants

    /// <summary>
    /// Marks the file as carrying this preamble. Deliberately unlike
    /// <see cref="DatabaseConstants.MAGIC_BYTES"/>: reading the first sixteen bytes has to
    /// distinguish three cases, and the third one - a database written before this existed - is
    /// recognised by matching NEITHER.
    /// </summary>
    public static ReadOnlySpan<byte> MAGIC_BYTES => "WitDB Crypt 1\0\0\0"u8;

    /// <summary>
    /// Current preamble version, major.minor as a ushort.
    /// </summary>
    public const ushort FORMAT_VERSION = 0x0100;

    /// <summary>
    /// Bytes of the page the preamble occupies. The rest of that page is zero.
    /// </summary>
    public const int SIZE = 128;

    /// <summary>
    /// Bytes of salt drawn at creation.
    /// </summary>
    public const int SALT_SIZE = 16;

    /// <summary>
    /// Bytes of the data key. AES-256 and ChaCha20-Poly1305 both take 32.
    /// </summary>
    public const int DATA_KEY_SIZE = 32;

    private const int WRAP_NONCE_SIZE = 12;

    private const int WRAP_TAG_SIZE = 16;

    private const byte FLAG_WRAPPED_KEY = 0x01;

    /// <summary>
    /// The default iteration count for new databases. The OWASP figure for PBKDF2-HMAC-SHA256, and
    /// only raisable at all because the number now lives in the file rather than in the caller's
    /// configuration.
    /// </summary>
    public const int DEFAULT_ITERATIONS = 600_000;

    /// <summary>
    /// The iteration count for environments where the derivation is slow enough to be felt - WASM,
    /// mainly. Unlike the flag it replaces, a file written with this opens without being told.
    /// </summary>
    public const int FAST_ITERATIONS = 10_000;

    #endregion

    #region Fields

    /// <summary>
    /// Preamble version, major.minor as a ushort.
    /// </summary>
    public ushort FormatVersion;

    /// <summary>
    /// Which derivation turns the password into the key that unwraps the data key.
    /// </summary>
    public CryptoKdf Kdf;

    /// <summary>
    /// Iterations for <see cref="CryptoKdf.Pbkdf2Sha256"/>; zero for <see cref="CryptoKdf.None"/>.
    /// </summary>
    public uint Iterations;

    /// <summary>
    /// Sixteen random bytes drawn when the database was created. Public, and unique per file.
    /// </summary>
    public byte[] Salt;

    /// <summary>
    /// The next nonce sequence number no write has used. Monotonic, and it survives the file - which
    /// is the whole difference from the counter it replaces.
    /// </summary>
    public ulong NonceSequence;

    /// <summary>
    /// The data key, encrypted under the key the password derives. Null when the caller owns the key
    /// material and there is nothing to wrap.
    /// </summary>
    public byte[]? WrappedKey;

    /// <summary>
    /// The nonce <see cref="WrappedKey"/> was produced under.
    /// </summary>
    public byte[]? WrapNonce;

    /// <summary>
    /// The tag that tells a wrong password from a right one before anything else is read.
    /// </summary>
    public byte[]? WrapTag;

    #endregion

    #region Create

    /// <summary>
    /// Draws a new preamble for a database being created: a random salt, a random data key wrapped
    /// under the password, and a nonce sequence starting at one.
    /// </summary>
    /// <param name="password">The password, or the user and password already combined.</param>
    /// <param name="iterations">PBKDF2 iterations to record in the file and use now.</param>
    /// <param name="dataKey">Receives the data key the pages are to be encrypted under.</param>
    public static CryptoHeader CreateWrapping(string password, int iterations, out byte[] dataKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
        dataKey = RandomNumberGenerator.GetBytes(DATA_KEY_SIZE);

        var wrappingKey = DeriveWrappingKey(password, salt, iterations);
        var wrapNonce = RandomNumberGenerator.GetBytes(WRAP_NONCE_SIZE);
        var wrapped = new byte[DATA_KEY_SIZE];
        var tag = new byte[WRAP_TAG_SIZE];

        try
        {
            using var aes = new AesGcm(wrappingKey, WRAP_TAG_SIZE);
            aes.Encrypt(wrapNonce, dataKey, wrapped, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        return new CryptoHeader
        {
            FormatVersion = FORMAT_VERSION,
            Kdf = CryptoKdf.Pbkdf2Sha256,
            Iterations = (uint)iterations,
            Salt = salt,
            NonceSequence = 1,
            WrappedKey = wrapped,
            WrapNonce = wrapNonce,
            WrapTag = tag
        };
    }

    /// <summary>
    /// Draws a new preamble for a database whose key the caller owns. There is nothing to wrap, so
    /// the header carries the salt and the nonce sequence alone - which is still the difference
    /// between a nonce that repeats across sessions and one that does not.
    /// </summary>
    public static CryptoHeader CreateUnwrapped()
    {
        return new CryptoHeader
        {
            FormatVersion = FORMAT_VERSION,
            Kdf = CryptoKdf.None,
            Iterations = 0,
            Salt = RandomNumberGenerator.GetBytes(SALT_SIZE),
            NonceSequence = 1,
            WrappedKey = null,
            WrapNonce = null,
            WrapTag = null
        };
    }

    #endregion

    #region Unwrap

    /// <summary>
    /// Recovers the data key from the password, or says the password is wrong.
    /// </summary>
    /// <remarks>
    /// The wrap tag is the check, and it is the FIRST thing a wrong password meets. Before this, a
    /// wrong password - or a right password missing <c>Fast Encryption=true</c> - surfaced as
    /// "Failed to decrypt page 0", from a layer that knows nothing about passwords.
    /// </remarks>
    public readonly byte[] UnwrapDataKey(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (Kdf != CryptoKdf.Pbkdf2Sha256 || WrappedKey == null || WrapNonce == null || WrapTag == null)
        {
            throw new InvalidOperationException(
                "This database's crypto header carries no wrapped key, so it cannot be opened with a "
                + "password. It was created with a caller-supplied key.");
        }

        var wrappingKey = DeriveWrappingKey(password, Salt, (int)Iterations);
        var dataKey = new byte[DATA_KEY_SIZE];

        try
        {
            using var aes = new AesGcm(wrappingKey, WRAP_TAG_SIZE);
            aes.Decrypt(WrapNonce, WrappedKey, WrapTag, dataKey);
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(dataKey);

            throw new CryptographicException(
                "The password does not open this database. The iteration count and salt come from "
                + "the file itself, so no connection-string setting other than the password can be "
                + "the cause.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        return dataKey;
    }

    /// <summary>
    /// Rewrites the wrapped key under a new password, leaving the data key - and therefore every
    /// page of the database - untouched. This is the password change, and it is 60 bytes.
    /// </summary>
    public void Rewrap(byte[] dataKey, string password, int iterations)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);

        if (dataKey.Length != DATA_KEY_SIZE)
            throw new ArgumentException($"The data key must be {DATA_KEY_SIZE} bytes", nameof(dataKey));

        // A new salt as well as a new wrap. The salt is not secret, but reusing it across two
        // passwords would let one derivation be reused against both.
        var salt = RandomNumberGenerator.GetBytes(SALT_SIZE);
        var wrappingKey = DeriveWrappingKey(password, salt, iterations);
        var wrapNonce = RandomNumberGenerator.GetBytes(WRAP_NONCE_SIZE);
        var wrapped = new byte[DATA_KEY_SIZE];
        var tag = new byte[WRAP_TAG_SIZE];

        try
        {
            using var aes = new AesGcm(wrappingKey, WRAP_TAG_SIZE);
            aes.Encrypt(wrapNonce, dataKey, wrapped, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }

        Kdf = CryptoKdf.Pbkdf2Sha256;
        Iterations = (uint)iterations;
        Salt = salt;
        WrappedKey = wrapped;
        WrapNonce = wrapNonce;
        WrapTag = tag;
    }

    #endregion

    #region Read and write

    /// <summary>
    /// Writes the preamble into a buffer, clearing everything up to <see cref="SIZE"/> first.
    /// </summary>
    public readonly void WriteTo(Span<byte> buffer)
    {
        if (buffer.Length < SIZE)
            throw new ArgumentException($"Buffer must be at least {SIZE} bytes", nameof(buffer));

        buffer[..SIZE].Clear();

        MAGIC_BYTES.CopyTo(buffer);

        BinaryPrimitives.WriteUInt16LittleEndian(buffer[16..], FormatVersion);
        buffer[18] = (byte)Kdf;
        buffer[19] = WrappedKey != null ? FLAG_WRAPPED_KEY : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[20..], Iterations);

        Salt.AsSpan(0, SALT_SIZE).CopyTo(buffer[24..]);

        BinaryPrimitives.WriteUInt64LittleEndian(buffer[40..], NonceSequence);

        if (WrappedKey == null)
            return;

        WrapNonce!.AsSpan(0, WRAP_NONCE_SIZE).CopyTo(buffer[48..]);
        WrappedKey.AsSpan(0, DATA_KEY_SIZE).CopyTo(buffer[60..]);
        WrapTag!.AsSpan(0, WRAP_TAG_SIZE).CopyTo(buffer[92..]);
    }

    /// <summary>
    /// Reads a preamble, or reports that this buffer does not begin with one.
    /// </summary>
    /// <remarks>
    /// False is not a failure. It is how a database written before the preamble existed - and an
    /// unencrypted one, which begins with <see cref="DatabaseConstants.MAGIC_BYTES"/> - is
    /// recognised, so the caller can fall back to the old derivation for exactly those files.
    /// </remarks>
    public static bool TryReadFrom(ReadOnlySpan<byte> buffer, out CryptoHeader header)
    {
        header = default;

        if (buffer.Length < SIZE || !buffer[..16].SequenceEqual(MAGIC_BYTES))
            return false;

        var formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(buffer[16..]);
        var fileMajor = (byte)(formatVersion >> 8);
        var supportedMajor = (byte)(FORMAT_VERSION >> 8);

        // The same rule DatabaseHeader.ReadFrom applies, and for the same reason: a newer major
        // version means a layout this build would misread, and a clear refusal beats reading a salt
        // out of bytes that are something else.
        if (fileMajor > supportedMajor)
        {
            throw new InvalidDataException(
                $"Unsupported encryption header version {fileMajor}.{(byte)formatVersion}: this "
                + $"build reads up to major version {supportedMajor}. The database was encrypted by "
                + "a newer version of WitDatabase.");
        }

        var kdf = (CryptoKdf)buffer[18];

        if (kdf is not (CryptoKdf.None or CryptoKdf.Pbkdf2Sha256))
        {
            throw new InvalidDataException(
                $"Unsupported key derivation id {buffer[18]} in the encryption header. This build "
                + "knows PBKDF2-SHA256 and caller-supplied keys.");
        }

        var hasWrappedKey = (buffer[19] & FLAG_WRAPPED_KEY) != 0;

        header = new CryptoHeader
        {
            FormatVersion = formatVersion,
            Kdf = kdf,
            Iterations = BinaryPrimitives.ReadUInt32LittleEndian(buffer[20..]),
            Salt = buffer.Slice(24, SALT_SIZE).ToArray(),
            NonceSequence = BinaryPrimitives.ReadUInt64LittleEndian(buffer[40..]),
            WrapNonce = hasWrappedKey ? buffer.Slice(48, WRAP_NONCE_SIZE).ToArray() : null,
            WrappedKey = hasWrappedKey ? buffer.Slice(60, DATA_KEY_SIZE).ToArray() : null,
            WrapTag = hasWrappedKey ? buffer.Slice(92, WRAP_TAG_SIZE).ToArray() : null
        };

        return true;
    }

    #endregion

    #region Tools

    /// <summary>
    /// Combines a user with a password when both are configured, so that the user stays part of the
    /// secret.
    /// </summary>
    /// <remarks>
    /// The old user-based route derived the SALT from the user, which is how the user came to matter
    /// at all. The salt is now the file's, so the user has to enter the derivation itself or
    /// <c>WithUserEncryption("admin", p)</c> would quietly become <c>WithEncryption(p)</c> - a
    /// database that used to need two things to open would need one.
    ///
    /// Length-prefixed rather than joined by a separator, so that ("ab", "c") and ("a", "bc") cannot
    /// derive one key.
    /// </remarks>
    public static string CombineUserAndPassword(string? user, string password)
    {
        return string.IsNullOrEmpty(user) ? password : $"{user.Length}:{user}{password}";
    }

    private static byte[] DeriveWrappingKey(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            DATA_KEY_SIZE);
    }

    #endregion
}
