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
    /// A file whose magic bytes are not there: <b>encrypted, or not a database at all</b>.
    ///
    /// <para>
    /// These are one state and not two, and that was measured rather than assumed. The first version
    /// of this type had a separate "definitely encrypted" state with a flag for the ambiguous case -
    /// and the control written to prove the flag meant something went red, because it is true of
    /// EVERY encrypted file. Encryption is what makes the header unreadable, and an unreadable header
    /// is all there is to see. Studio says both, once.
    /// </para>
    /// </summary>
    Unreadable,

    /// <summary>
    /// The file is there and <b>something is holding it</b> - most often this very application, with
    /// the database already open in another connection.
    ///
    /// <para>
    /// A state of its own since 2026-08-09, and it is the fourth rather than a wording of the third
    /// for the reason the third exists: "encrypted, or not a database" is one answer because
    /// encryption is what makes the header unreadable and there is nothing else to see. A held file is
    /// different in kind - the header is perfectly readable and simply out of reach for now - and it
    /// is the one case where the right advice is "close it, or open the connection you already have"
    /// rather than "give me the password" or "this is not a database".
    /// </para>
    /// </summary>
    Locked
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

            // Held by something, so nothing could be read - and that is a different sentence from
            // every other one here. It has to be asked BEFORE the store-type questions below, because
            // a held file answers them exactly the way a text file does.
            if (detected.IsLocked)
                return new StorageProbe(StorageKind.Locked) { SizeInBytes = SizeOf(path) };

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
                return new StorageProbe(StorageKind.Unreadable)
                {
                    SizeInBytes = SizeOf(path),
                    IsDirectory = detected.IsDirectory,

                    // For a FILE these are deliberately NOT carried across: detection answers "btree"
                    // and "unknown" from the absence of the magic bytes, and both are guesses;
                    // printing them would be Studio claiming to have read something it did not read.
                    //
                    // For a DIRECTORY they are not guesses. The sidecar is written in the clear and is
                    // what said the database is encrypted at all, so the store and the provider are
                    // things that WERE read - and the dialog can then say "an encrypted LSM database"
                    // rather than "encrypted, or not a database".
                    StoreType = detected.IsDirectory ? detected.StoreType : null,
                    EncryptionProvider = detected.IsDirectory ? detected.EncryptionProvider : null,
                    HasTransactions = detected.IsDirectory && detected.HasTransactions,
                    HasMvcc = detected.IsDirectory && detected.HasMvcc,
                    HasFileLocking = detected.IsDirectory && detected.HasFileLocking
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
    public bool RequiresPassword => Kind == StorageKind.Unreadable;

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
