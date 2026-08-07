namespace OutWit.Database.AdoNet.Maintenance;

/// <summary>
/// What the open database can say about its own storage, right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only, and a snapshot rather than a live view</b>: every field is read once, together, so a
/// caller cannot show a file count from one moment beside a size from another.
/// </para>
/// <para>
/// <b>It carries facts, not sentences.</b> Nothing here is prose and nothing is localised - a service
/// that composes text fixes the language of whatever shows it, which is a defect this repository has
/// already found in three of its own services. The caller says what these numbers mean.
/// </para>
/// </remarks>
public sealed class WitDbStorageSnapshot
{
    /// <summary>
    /// The store that does the storing: <c>btree</c>, <c>lsm</c>, or whatever a third-party provider
    /// calls itself.
    /// </summary>
    public required string StoreProviderKey { get; init; }

    /// <summary>
    /// The layers between the connection and that store, outermost first, by provider key.
    /// </summary>
    /// <remarks>
    /// The transaction model is visible here rather than as a separate flag, because that is what it
    /// IS - <c>mvcc-transactional</c> or <c>transactional</c> is a layer, and a database built without
    /// one has neither.
    /// </remarks>
    public required IReadOnlyList<string> Chain { get; init; }

    /// <summary>
    /// The size the store reports for itself, or null when it does not answer that question.
    /// </summary>
    public long? ApproximateSizeInBytes { get; init; }

    /// <summary>
    /// Whether the store's own counts are exact or an estimate. Null when it publishes no statistics.
    /// </summary>
    public bool? StatisticsAreExact { get; init; }

    /// <summary>
    /// The LSM-specific half, or null for a store that is not an LSM tree.
    /// </summary>
    public WitDbLsmSnapshot? Lsm { get; init; }
}

/// <summary>
/// What an LSM store can say about itself. Null on any other store, rather than zeroed.
/// </summary>
/// <remarks>
/// <b>There are no levels, and that is measured rather than omitted.</b> This store keeps a flat list
/// of SSTables and compacts all of them into one, so <see cref="CompactionTrigger"/> is "how many
/// files before a full merge" and not the size of a level. A caller drawing levels would be drawing
/// something the engine does not have.
/// </remarks>
public sealed class WitDbLsmSnapshot
{
    /// <summary>How many SSTables exist.</summary>
    public required int SstableCount { get; init; }

    /// <summary>How many SSTables trigger an automatic full merge.</summary>
    public required int CompactionTrigger { get; init; }

    /// <summary>Bytes the memtable is holding.</summary>
    public required long MemTableUsedBytes { get; init; }

    /// <summary>The size at which the memtable flushes itself.</summary>
    public required long MemTableLimitBytes { get; init; }

    /// <summary>Whether a compaction is running at this moment.</summary>
    public required bool IsCompacting { get; init; }

    /// <summary>
    /// Counters SINCE THIS CONNECTION OPENED, not since the database was created.
    /// </summary>
    /// <remarks>
    /// They live in the store object and start at zero when it is built - measured: reopening a store
    /// with 300 keys and a compaction behind it reports puts=0 and compactions=0. Anything showing
    /// them has to say so, or it is describing a file by what one program happened to do to it.
    /// </remarks>
    public required WitDbLsmCounters CountersSinceOpened { get; init; }
}

/// <summary>
/// The LSM store's operation counters, as of the snapshot.
/// </summary>
public sealed class WitDbLsmCounters
{
    public required long Gets { get; init; }

    public required long Puts { get; init; }

    public required long Deletes { get; init; }

    public required long Scans { get; init; }

    /// <summary>Memtables written out as SSTables.</summary>
    public required long Flushes { get; init; }

    /// <summary>Full merges performed.</summary>
    public required long Compactions { get; init; }

    public required long BytesWritten { get; init; }

    public required long BytesRead { get; init; }

    /// <summary>Lookups the bloom filter answered without touching a file.</summary>
    public required long BloomFilterHits { get; init; }

    /// <summary>Lookups that needed a file read after the bloom filter said "maybe".</summary>
    public required long BloomFilterMisses { get; init; }
}
