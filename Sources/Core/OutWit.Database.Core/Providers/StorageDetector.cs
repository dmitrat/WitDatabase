namespace OutWit.Database.Core.Providers;

/// <summary>
/// Detects the storage type of existing database files/directories.
/// </summary>
public static class StorageDetector
{
    /// <summary>
    /// Detects the storage type at the given path.
    /// </summary>
    /// <param name="path">File or directory path.</param>
    /// <returns>Detection result with store type and hints.</returns>
    public static StorageDetectionResult Detect(string path)
    {
        if (string.IsNullOrEmpty(path))
            return StorageDetectionResult.NotFound();

        // Check if it's a directory (LSM)
        if (Directory.Exists(path))
        {
            return DetectDirectory(path);
        }

        // Check if it's a file (BTree)
        if (File.Exists(path))
        {
            return DetectFile(path);
        }

        // Path doesn't exist - could be new database
        return StorageDetectionResult.NotFound();
    }

    /// <summary>
    /// Reads the configuration a database recorded when it was created, without building anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns null when there is nothing to read: the path does not exist, it is not a WitDatabase, or
    /// it is <b>encrypted</b> - the header is inside the encrypted page, so a caller who has not
    /// supplied the password cannot be told what the file was configured with. That is a real limit and
    /// it is measured in <c>ConfigurationRestoreTests</c> rather than assumed away: an encrypted
    /// database still needs its non-default settings spelled out.
    /// </para>
    /// <para>
    /// The B+Tree store keeps its configuration in the database header and the LSM store in a sidecar
    /// beside its SSTables, so both are read here and answered in the same shape.
    /// </para>
    /// </remarks>
    public static StoredConfiguration? ReadStoredConfiguration(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (Directory.Exists(path))
            return LsmDirectoryMetadata.Read(path) is { } lsm
                ? new StoredConfiguration(lsm.Metadata, PageSize: 0, IsDirectory: true, lsm.Options,
                    FormatVersion: null)
                : null;

        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            if (stream.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
                return null;

            var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
            stream.ReadExactly(buffer);

            // Encrypted files fail here, and so do files that are not databases. Both mean "nothing to
            // restore from", which is the same answer.
            if (!buffer.AsSpan(0, 16).SequenceEqual(DatabaseConstants.MAGIC_BYTES))
                return null;

            var header = DatabaseHeader.ReadFrom(buffer);

            return new StoredConfiguration(header.Providers, header.PageSize, IsDirectory: false, Lsm: null,
                header.FormatVersion);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Detects if the given directory is an LSM database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The sidecar is read, and it is the same fact the file branch gets from the header.</b> This
    /// used to answer the store type and leave every other field at its default, so
    /// <c>HasTransactions</c>, <c>HasMvcc</c> and <c>HasFileLocking</c> were <c>false</c> for every LSM
    /// database - not "unknown" but wrong, and a consumer prints it. Measured 2026-08-08: an LSM
    /// database built WITH MVCC and one built without were indistinguishable here, while
    /// <see cref="ReadStoredConfiguration"/> - two methods away, and called by the same consumers -
    /// told them apart correctly.
    /// </para>
    /// <para>
    /// <b>Encryption too.</b> "Can't detect encryption without opening" was true of the SSTables and
    /// not of the directory: the sidecar has to be readable in the clear, because it is what says
    /// which encryption provider to build. An encrypted LSM database therefore used to be reported as
    /// needing no password, and the failure arrived later as a wrong-password error.
    /// </para>
    /// <para>
    /// A directory holding SSTables but no sidecar is still an LSM database - one written before the
    /// sidecar existed - and keeps the answer it had.
    /// </para>
    /// </remarks>
    private static StorageDetectionResult DetectDirectory(string directory)
    {
        // LSM stores data in .sst files and optionally wal.log
        var sstFiles = Directory.GetFiles(directory, "sst_*.sst");
        var hasWal = File.Exists(Path.Combine(directory, "wal.log"));

        if (sstFiles.Length > 0 || hasWal)
        {
            var hints = LsmDirectoryMetadata.Read(directory) is { } sidecar
                ? ConfigurationValidator.GetOpeningHints(sidecar.Metadata)
                : null;

            return new StorageDetectionResult
            {
                Exists = true,
                IsDirectory = true,
                StoreType = "lsm",
                Path = directory,
                RequiresPassword = hints?.RequiresEncryption ?? false,
                EncryptionProvider = hints?.EncryptionProvider,
                HasTransactions = hints?.HasTransactions ?? false,
                HasMvcc = hints?.HasMvcc ?? false,
                HasFileLocking = hints?.HasFileLocking ?? false
            };
        }

        // Empty directory - could be new LSM or something else
        return new StorageDetectionResult
        {
            Exists = true,
            IsDirectory = true,
            StoreType = null, // Unknown
            Path = directory,
            RequiresPassword = false
        };
    }

    /// <summary>
    /// Detects if the given file is a BTree database.
    /// </summary>
    private static StorageDetectionResult DetectFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            if (stream.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            {
                return new StorageDetectionResult
                {
                    Exists = true,
                    IsDirectory = false,
                    StoreType = null, // Too small to detect
                    Path = path,
                    RequiresPassword = false
                };
            }

            var buffer = new byte[DatabaseConstants.DATABASE_HEADER_SIZE];
            stream.ReadExactly(buffer);

            // Check magic bytes
            if (buffer.AsSpan(0, 16).SequenceEqual(DatabaseConstants.MAGIC_BYTES))
            {
                // Valid unencrypted BTree file - read metadata
                var header = DatabaseHeader.ReadFrom(buffer);
                var hints = ConfigurationValidator.GetOpeningHints(header.Providers);
                
                return new StorageDetectionResult
                {
                    Exists = true,
                    IsDirectory = false,
                    StoreType = hints.StoreProvider,
                    Path = path,
                    RequiresPassword = hints.RequiresEncryption,
                    EncryptionProvider = hints.EncryptionProvider,
                    HasTransactions = hints.HasTransactions,
                    HasMvcc = hints.HasMvcc,
                    HasFileLocking = hints.HasFileLocking
                };
            }
            else
            {
                // Could be encrypted - magic bytes are encrypted too
                return new StorageDetectionResult
                {
                    Exists = true,
                    IsDirectory = false,
                    StoreType = "btree", // Assume BTree for file
                    Path = path,
                    RequiresPassword = true,
                    EncryptionProvider = "unknown"
                };
            }
        }
        catch
        {
            return new StorageDetectionResult
            {
                Exists = true,
                IsDirectory = false,
                StoreType = null,
                Path = path,
                RequiresPassword = false
            };
        }
    }
}

/// <summary>
/// The configuration a database recorded when it was created.
/// </summary>
/// <param name="Metadata">Store, encryption, cache and journal keys, plus the feature flags.</param>
/// <param name="PageSize">The page size the file was written with; zero for an LSM directory.</param>
/// <param name="IsDirectory">Whether the database is an LSM directory rather than a paged file.</param>
/// <param name="Lsm">The LSM options recorded in the sidecar, or null for a paged file.</param>
/// <param name="FormatVersion">
/// The on-disk format the file was written with, major in the high byte and minor in the low one.
/// <b>Null for an LSM directory</b>, which has no database header - and that is the answer rather than
/// a gap: the sidecar carries a version of its own, but it versions the sidecar and not the store, so
/// reporting it here would put a number in front of a reader that means something else.
/// </param>
public sealed record StoredConfiguration(
    ProviderMetadata Metadata,
    int PageSize,
    bool IsDirectory,
    LsmStoredOptions? Lsm,
    ushort? FormatVersion);

/// <summary>
/// Result of storage type detection.
/// </summary>
public sealed class StorageDetectionResult
{
    /// <summary>
    /// Whether the path exists.
    /// </summary>
    public bool Exists { get; init; }

    /// <summary>
    /// Whether the path is a directory (LSM) or file (BTree).
    /// </summary>
    public bool IsDirectory { get; init; }

    /// <summary>
    /// Detected store type: "btree", "lsm", or null if unknown.
    /// </summary>
    public string? StoreType { get; init; }

    /// <summary>
    /// The path that was checked.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Whether a password is required to open.
    /// </summary>
    public bool RequiresPassword { get; init; }

    /// <summary>
    /// The encryption provider, if known.
    /// </summary>
    public string? EncryptionProvider { get; init; }

    /// <summary>
    /// Whether transactions were enabled (BTree only).
    /// </summary>
    public bool HasTransactions { get; init; }

    /// <summary>
    /// Whether MVCC (Multi-Version Concurrency Control) was enabled (BTree only).
    /// </summary>
    public bool HasMvcc { get; init; }

    /// <summary>
    /// Whether file locking was enabled (BTree only).
    /// </summary>
    public bool HasFileLocking { get; init; }

    /// <summary>
    /// Creates a result for non-existent path.
    /// </summary>
    public static StorageDetectionResult NotFound() => new()
    {
        Exists = false,
        IsDirectory = false,
        StoreType = null,
        Path = ""
    };
}
