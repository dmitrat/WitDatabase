using OutWit.Database.AdoNet.Maintenance;

namespace OutWit.Database.Studio.Models;

/// <summary>
/// Everything the «База» tab shows about one open database, as FACTS (WS-54).
/// </summary>
/// <remarks>
/// <para>
/// <b>No sentences here.</b> Provider keys, numbers and flags; the ViewModel writes the words from the
/// catalogue. That is stage 10's "a model must not render itself", and this record is the first one
/// written after the rule rather than swept into it.
/// </para>
/// <para>
/// <b>Nullable means "not applicable", never "not loaded".</b> An LSM database has no page size, no
/// page count and no format version, because it has no database header; a paged one has no
/// <see cref="Lsm"/>. A reader that filled those with zeros would be inventing facts about the store
/// it is describing.
/// </para>
/// </remarks>
/// <param name="Path">The file or the folder, as the connection names it.</param>
/// <param name="IsDirectory">An LSM database is a folder of SSTables; everything else is a file.</param>
/// <param name="StoreProviderKey">The engine's own key: <c>btree</c>, <c>lsm</c>, <c>inmemory</c>.</param>
/// <param name="SizeInBytes">The file, or everything in the folder.</param>
/// <param name="PageSize">The page size the file was written with, or null for a folder.</param>
/// <param name="PageCount">
/// Derived from the size and the page size rather than read: the header's own count is not published,
/// and dividing is exact for a paged file whose length is a whole number of pages.
/// </param>
/// <param name="FormatVersion">Major in the high byte, minor in the low one. Null for a folder.</param>
/// <param name="EncryptionProviderKey">Null when the database is not encrypted.</param>
/// <param name="CacheProviderKey">The page cache the database was created with.</param>
/// <param name="CacheSizeInPages">Its capacity, in pages.</param>
/// <param name="CachePagesHeld">
/// How many pages the cache holds right now, or null when there is no page cache to ask - an LSM
/// database. A READING: it changes with the next statement, which is why the block says when it was
/// taken.
/// </param>
/// <param name="CacheDirtyPages">Of those, how many are written to and not yet flushed.</param>
/// <param name="JournalProviderKey">The journal provider, empty when there is none.</param>
/// <param name="StoreChain">
/// The layers between the database and the disk, outermost first - the answer to "which store am I
/// actually talking to", which no consumer could get before WS-57.
/// </param>
/// <param name="Lsm">The LSM half, or null for a paged store.</param>
/// <param name="Schema">What the database holds.</param>
/// <param name="ConfigurationIsAvailable">
/// Whether the block that comes from the header was readable at all.
///
/// <para>
/// <b>Measured 2026-08-08: an open database cannot be read.</b> Not the header, not even the store
/// type - <c>StorageDetector</c> answers null and an empty type to both questions while a connection
/// holds the file. So the session reads it a moment BEFORE opening, and the one case left without an
/// answer is a path that did not exist and was created by that very open. For that one the block is
/// absent and says why, rather than showing a page size of zero and "no transactions".
/// </para>
/// </param>
/// <param name="IsInUse">
/// Whether the path is locked against a second opener at the moment it was asked. While this session
/// is connected the holder is Studio itself, which is the useful thing to say rather than a
/// contradiction: it is the reason another application cannot start against the same file.
/// </param>
public sealed record DatabaseOverview(
    string Path,
    bool IsDirectory,
    string StoreProviderKey,
    long SizeInBytes,
    int? PageSize,
    long? PageCount,
    ushort? FormatVersion,
    string? EncryptionProviderKey,
    bool? HasTransactions,
    bool? HasMvcc,
    bool? HasFileLocking,
    string CacheProviderKey,
    int CacheSizeInPages,
    int? CachePagesHeld,
    int? CacheDirtyPages,
    string JournalProviderKey,
    IReadOnlyList<string> StoreChain,
    WitDbLsmSnapshot? Lsm,
    SchemaCounts Schema,
    bool IsInUse,
    bool ConfigurationIsAvailable)
{
    /// <summary>
    /// Whether this connection assembled an MVCC layer, read from the chain it actually built rather
    /// than from what the database was created with.
    /// </summary>
    /// <remarks>
    /// The live answer, and the only one available for a database whose header could not be read. It
    /// is also the more useful of the two: what a statement will do depends on the layers this
    /// connection has, not on the flags in a header.
    /// </remarks>
    public bool ChainHasMvcc => StoreChain.Any(link => link.Contains("mvcc", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether this connection has a transactional layer at all.</summary>
    public bool ChainHasTransactions =>
        StoreChain.Any(link => link.Contains("transactional", StringComparison.OrdinalIgnoreCase));

    /// <summary>The format version as a person writes it, or null when there is none.</summary>
    public string? FormatVersionText =>
        FormatVersion is { } version ? $"{version >> 8}.{(byte)version}" : null;

    /// <summary>Whether the database was created with encryption.</summary>
    public bool IsEncrypted => !string.IsNullOrEmpty(EncryptionProviderKey);
}

/// <summary>
/// How many of each kind of object the database holds.
/// </summary>
public sealed record SchemaCounts(
    int Tables,
    int Views,
    int Indexes,
    int Triggers,
    int Sequences,
    int Routines);
