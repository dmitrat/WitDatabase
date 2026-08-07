using System.IO;
using OutWit.Database.Core.Providers;

namespace OutWit.Database.Studio.Services;

/// <summary>What is at a path, as far as it can be known without opening it.</summary>
public enum StorageKind
{
    /// <summary>Nothing there. The Open dialog refuses; Create is where a new database comes from.</summary>
    NotFound,

    /// <summary>Something is there and it is not a WitDatabase.</summary>
    NotADatabase,

    /// <summary>A database whose header could be read.</summary>
    Database,

    /// <summary>
    /// A file whose magic bytes are not there. Almost certainly encrypted - and possibly not a
    /// database at all, because the two are indistinguishable from outside. See
    /// <see cref="StorageProbe.CouldAlsoBeSomethingElse"/>.
    /// </summary>
    Encrypted
}

/// <summary>
/// What Studio can say about a path before connecting (WS-47).
///
/// <para>
/// The design shows one line - store, size, encryption, MVCC - as soon as a path is picked. Measured
/// against the engine, that line is only obtainable for an <b>unencrypted</b> database:
/// <c>StorageDetector</c> reads the header out of the first page, and in an encrypted database that
/// page is encrypted. For one of those it answers <c>StoreType = "btree"</c> (an assumption, not a
/// reading) and <c>EncryptionProvider = "unknown"</c>, and it cannot see MVCC, the journal or the page
/// size at all.
/// </para>
/// <para>
/// Worse, and this is the reason for <see cref="CouldAlsoBeSomethingElse"/>: a file that is not a
/// database fails the same magic-byte check, so it comes back looking exactly like an encrypted one.
/// Studio would have asked for the password to a text file and then blamed the password. This type
/// exists so the interface can say what is actually known rather than the most confident-sounding
/// reading of it.
/// </para>
/// </summary>
public sealed class StorageProbe
{
    #region Constructors

    private StorageProbe(StorageKind kind)
    {
        Kind = kind;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Looks at <paramref name="path"/> without opening anything. Never throws: an unreadable path is
    /// a thing to report, not a thing to fail on.
    /// </summary>
    public static StorageProbe Look(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new StorageProbe(StorageKind.NotFound);

        try
        {
            var detected = StorageDetector.Detect(path);

            if (!detected.Exists)
                return new StorageProbe(StorageKind.NotFound);

            // A directory with no SSTable and no manifest: the detector answers with a null store
            // type, which for a folder means "there is no LSM database in here".
            if (detected.IsDirectory && detected.StoreType == null)
                return new StorageProbe(StorageKind.NotADatabase) { SizeInBytes = SizeOf(path) };

            // A file too short to hold a header. The detector answers with a null store type for this
            // too, and it is NOT ambiguous - there is nothing there that could have been encrypted.
            if (!detected.IsDirectory && detected.StoreType == null)
                return new StorageProbe(StorageKind.NotADatabase) { SizeInBytes = SizeOf(path) };

            if (detected.RequiresPassword)
            {
                return new StorageProbe(StorageKind.Encrypted)
                {
                    SizeInBytes = SizeOf(path),
                    IsDirectory = detected.IsDirectory,

                    // Deliberately NOT carried across from the detection result. It answers "btree"
                    // and "unknown" here, and both are guesses; printing them would be Studio
                    // claiming to have read something it did not read.
                    StoreType = null,
                    EncryptionProvider = null,

                    // An LSM database announces itself by its SSTables, so a directory that got this
                    // far really is one. A FILE with no magic bytes could be anything.
                    CouldAlsoBeSomethingElse = !detected.IsDirectory
                };
            }

            var stored = StorageDetector.ReadStoredConfiguration(path);

            return new StorageProbe(StorageKind.Database)
            {
                SizeInBytes = SizeOf(path),
                IsDirectory = detected.IsDirectory,
                StoreType = detected.StoreType,
                EncryptionProvider = detected.EncryptionProvider,
                HasTransactions = detected.HasTransactions,
                HasMvcc = detected.HasMvcc,
                HasFileLocking = detected.HasFileLocking,
                PageSize = stored?.PageSize ?? 0
            };
        }
        catch
        {
            return new StorageProbe(StorageKind.NotADatabase);
        }
    }

    /// <summary>
    /// The size of a file, or of everything in a directory. The one fact the file system knows about
    /// an encrypted database.
    /// </summary>
    private static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path))
                return new FileInfo(path).Length;

            if (!Directory.Exists(path))
                return 0;

            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => file.Length);
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Properties

    public StorageKind Kind { get; }

    /// <summary>Whether a password has to be asked for before the database can be opened.</summary>
    public bool RequiresPassword => Kind == StorageKind.Encrypted;

    /// <summary>
    /// True when "encrypted" is a guess rather than a reading: the magic bytes are absent, which is
    /// what an encrypted database looks like AND what a file that is not a database looks like. The
    /// dialog says both rather than picking one.
    /// </summary>
    public bool CouldAlsoBeSomethingElse { get; private init; }

    public bool IsDirectory { get; private init; }

    public long SizeInBytes { get; private init; }

    /// <summary><c>btree</c> or <c>lsm</c>, and null whenever it could not actually be read.</summary>
    public string? StoreType { get; private init; }

    /// <summary>The encryption provider key, and null when it could not be read.</summary>
    public string? EncryptionProvider { get; private init; }

    public bool HasTransactions { get; private init; }

    public bool HasMvcc { get; private init; }

    public bool HasFileLocking { get; private init; }

    /// <summary>The page size the file was written with; zero for LSM and for anything unreadable.</summary>
    public int PageSize { get; private init; }

    #endregion
}
