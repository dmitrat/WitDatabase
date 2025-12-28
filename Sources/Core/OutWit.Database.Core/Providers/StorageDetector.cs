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
    /// Detects if the given directory is an LSM database.
    /// </summary>
    private static StorageDetectionResult DetectDirectory(string directory)
    {
        // LSM stores data in .sst files and optionally wal.log
        var sstFiles = Directory.GetFiles(directory, "sst_*.sst");
        var hasWal = File.Exists(Path.Combine(directory, "wal.log"));

        if (sstFiles.Length > 0 || hasWal)
        {
            return new StorageDetectionResult
            {
                Exists = true,
                IsDirectory = true,
                StoreType = "lsm",
                Path = directory,
                RequiresPassword = false // Can't detect encryption without opening
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
