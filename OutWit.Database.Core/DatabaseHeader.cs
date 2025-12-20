using System.Buffers.Binary;

namespace OutWit.Database.Core;

/// <summary>
/// Database file header structure (100 bytes at the beginning of the file)
/// </summary>
/// <remarks>
/// Layout:
/// [0-15]   Magic bytes: "WitDB Format 1\0\0"
/// [16-17]  Format version (major.minor as ushort)
/// [18-19]  Page size in bytes
/// [20-23]  Total page count in the file
/// [24-27]  First free page number (0 = none)
/// [28-31]  Count of free pages
/// [32-35]  Schema root page number
/// [36-39]  Transaction counter (for WAL)
/// [40-43]  Database flags
/// [44-47]  Checkpoint counter
/// [48-99]  Provider metadata (store, encryption, features)
/// </remarks>
public struct DatabaseHeader
{
    #region Fields

    /// <summary>
    /// Format version (major.minor encoded as ushort)
    /// </summary>
    public ushort FormatVersion;

    /// <summary>
    /// Page size in bytes (must be power of 2, 512-65536)
    /// </summary>
    public ushort PageSize;

    /// <summary>
    /// Total number of pages in the database file
    /// </summary>
    public uint TotalPageCount;

    /// <summary>
    /// Page number of first free page (0 = no free pages)
    /// </summary>
    public uint FirstFreePage;

    /// <summary>
    /// Total count of free pages
    /// </summary>
    public uint FreePageCount;

    /// <summary>
    /// Page number of the schema (master catalog) root
    /// </summary>
    public uint SchemaRootPage;

    /// <summary>
    /// Transaction counter for WAL (incremented on each commit)
    /// </summary>
    public uint TransactionCounter;

    /// <summary>
    /// Database flags
    /// </summary>
    public DatabaseFlags Flags;

    /// <summary>
    /// Checkpoint counter (number of checkpoints performed)
    /// </summary>
    public uint CheckpointCounter;

    /// <summary>
    /// Provider metadata (store type, encryption, features).
    /// </summary>
    public ProviderMetadata Providers;

    #endregion

    #region Functions

    /// <summary>
    /// Writes this header to a byte span
    /// </summary>
    public readonly void WriteTo(Span<byte> buffer)
    {
        if (buffer.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            throw new ArgumentException($"Buffer must be at least {DatabaseConstants.DATABASE_HEADER_SIZE} bytes", nameof(buffer));

        // Clear the buffer first
        buffer[..DatabaseConstants.DATABASE_HEADER_SIZE].Clear();

        // Magic bytes
        DatabaseConstants.MAGIC_BYTES.CopyTo(buffer);

        // Format version and page size
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[16..], FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[18..], PageSize);

        // Page management
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[20..], TotalPageCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[24..], FirstFreePage);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], FreePageCount);

        // Schema and transactions
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[32..], SchemaRootPage);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[36..], TransactionCounter);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[40..], (uint)Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[44..], CheckpointCounter);

        // Provider metadata (bytes 48-99)
        Providers.WriteTo(buffer);
    }

    /// <summary>
    /// Reads a header from a byte span
    /// </summary>
    public static DatabaseHeader ReadFrom(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < DatabaseConstants.DATABASE_HEADER_SIZE)
            throw new ArgumentException($"Buffer must be at least {DatabaseConstants.DATABASE_HEADER_SIZE} bytes", nameof(buffer));

        // Verify magic bytes
        if (!buffer[..16].SequenceEqual(DatabaseConstants.MAGIC_BYTES))
            throw new InvalidDataException("Invalid database file: magic bytes do not match");

        return new DatabaseHeader
        {
            FormatVersion = BinaryPrimitives.ReadUInt16LittleEndian(buffer[16..]),
            PageSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer[18..]),
            TotalPageCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer[20..]),
            FirstFreePage = BinaryPrimitives.ReadUInt32LittleEndian(buffer[24..]),
            FreePageCount = BinaryPrimitives.ReadUInt32LittleEndian(buffer[28..]),
            SchemaRootPage = BinaryPrimitives.ReadUInt32LittleEndian(buffer[32..]),
            TransactionCounter = BinaryPrimitives.ReadUInt32LittleEndian(buffer[36..]),
            Flags = (DatabaseFlags)BinaryPrimitives.ReadUInt32LittleEndian(buffer[40..]),
            CheckpointCounter = BinaryPrimitives.ReadUInt32LittleEndian(buffer[44..]),
            Providers = ProviderMetadata.ReadFrom(buffer)
        };
    }

    /// <summary>
    /// Creates a new database header with default values
    /// </summary>
    public static DatabaseHeader CreateNew(ushort pageSize = DatabaseConstants.DEFAULT_PAGE_SIZE)
    {
        if (!IsPowerOfTwo(pageSize) || pageSize < DatabaseConstants.MIN_PAGE_SIZE || pageSize > DatabaseConstants.MAX_PAGE_SIZE)
            throw new ArgumentOutOfRangeException(nameof(pageSize),
                $"Page size must be a power of 2 between {DatabaseConstants.MIN_PAGE_SIZE} and {DatabaseConstants.MAX_PAGE_SIZE}");

        return new DatabaseHeader
        {
            FormatVersion = DatabaseConstants.FORMAT_VERSION,
            PageSize = pageSize,
            TotalPageCount = 1, // At least one page (header page)
            FirstFreePage = 0,
            FreePageCount = 0,
            SchemaRootPage = 0,
            TransactionCounter = 0,
            Flags = DatabaseFlags.None,
            CheckpointCounter = 0,
            Providers = ProviderMetadata.CreateDefault()
        };
    }

    /// <summary>
    /// Creates a new database header with specified provider metadata.
    /// </summary>
    public static DatabaseHeader CreateNew(ushort pageSize, ProviderMetadata providers)
    {
        var header = CreateNew(pageSize);
        header.Providers = providers;
        return header;
    }

    #endregion

    #region Tools

    private static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

    #endregion
}