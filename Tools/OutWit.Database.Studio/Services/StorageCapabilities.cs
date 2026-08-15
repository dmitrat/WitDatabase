using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// One row of the matrix in section 7.3: an operation, where it would come from, and whether it can be
/// had at all (WS-55).
/// </summary>
/// <param name="OperationKey">Catalogue key for what a person would call it.</param>
/// <param name="SourceKey">Catalogue key for where it comes from.</param>
/// <param name="Availability">Whether it is here, reachable, or absent.</param>
/// <param name="NoteKey">Catalogue key for the one thing worth adding, or null.</param>
public sealed record StorageCapability(
    string OperationKey,
    string SourceKey,
    StorageAvailability Availability,
    string? NoteKey = null);

/// <summary>
/// What Studio can actually do to the storage, as data.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys and not sentences</b>, which is the difference between this matrix and
/// <see cref="SchemaCapabilities"/>: that one carries its reasons as English and fixes the language of
/// every screen showing them. Written the other way round here because the rule was learnt first.
/// </para>
/// <para>
/// <b>The point of holding it as data is that a test walks it</b>, in the same shape as
/// <c>SchemaMatrixTests</c>. A matrix nobody re-measures starts promising things - and this one has
/// already moved once: three rows the design wrote as "needs access" became available the day WS-57
/// landed, and one it left open turned out to be absent.
/// </para>
/// <para>
/// <b>What changed against the design, measured rather than assumed:</b>
/// </para>
/// <list type="bullet">
/// <item><b>Compaction, checkpoint and the store's statistics are AVAILABLE.</b> The design asked for
/// them as a change order; <c>WitDbConnection.Compact</c>, <c>.Checkpoint</c> and
/// <c>.GetStorageSnapshot</c> are that order, delivered.</item>
/// <item><b>There are no levels to report.</b> The design's LSM panel draws L0/L1/L2; the store keeps a
/// flat list and merges all of it into one file, so the statistics row promises a count and a trigger
/// and nothing shaped like a level.</item>
/// <item><b>The page cache counts no hits.</b> The design left this one open - "может не быть, нужно
/// уточнить". Measured 2026-08-08: neither <c>PageCacheLru</c> nor <c>PageCacheShardedClock</c> counts
/// a hit or a miss, so a hit rate is not "unreachable", it does not exist. Occupancy does - both keep
/// <c>Count</c> and <c>DirtyCount</c> - and that is the row that needs access.</item>
/// </list>
/// </remarks>
public static class StorageCapabilities
{
    #region Matrix

    /// <summary>
    /// The table of 7.3, in the order the design shows it.
    /// </summary>
    public static IReadOnlyList<StorageCapability> Matrix { get; } =
    [
        new("Database.Cap.Facts", "Database.Cap.Source.Detector", StorageAvailability.Available),

        new("Database.Cap.Schema", "Database.Cap.Source.InformationSchema", StorageAvailability.Available),

        new("Database.Cap.ReadCheck", "Database.Cap.Source.Studio", StorageAvailability.Available),

        new("Database.Cap.Copy", "Database.Cap.Source.FileCopy", StorageAvailability.Available,
            "Database.Cap.Note.CopyNeedsQuiet"),

        new("Database.Cap.Dump", "Database.Cap.Source.Script", StorageAvailability.Available),

        new("Database.Cap.Compact", "Database.Cap.Source.Provider", StorageAvailability.Available,
            "Database.Cap.Note.LsmOnly"),

        new("Database.Cap.Checkpoint", "Database.Cap.Source.Provider", StorageAvailability.Available),

        new("Database.Cap.Statistics", "Database.Cap.Source.Provider", StorageAvailability.Available,
            "Database.Cap.Note.NoLevels"),

        // Available since 2026-08-09, and it was the last row in this matrix needing provider access.
        // Both caches had kept Count and DirtyCount from the start and neither handed them out;
        // IPageCacheOccupancySource carries them out through WitDatabase and the connection.
        // (Written without quotation marks on purpose: rule 4 of the localisation lint reads this
        //  declaration as a data table and does not skip comments, so a quoted phrase in here is a
        //  sentence in a table as far as it is concerned. It caught this one.)
        new("Database.Cap.CacheOccupancy", "Database.Cap.Source.Cache", StorageAvailability.Available,
            "Database.Cap.Note.PagedOnly"),

        new("Database.Cap.CacheHitRate", "Database.Cap.Source.Cache", StorageAvailability.NotInEngine,
            "Database.Cap.Note.NothingCounts"),

        new("Database.Cap.Vacuum", "Database.Cap.Source.Specification", StorageAvailability.NotInEngine),

        new("Database.Cap.Analyze", "Database.Cap.Source.Specification", StorageAvailability.NotInEngine),

        new("Database.Cap.IntegrityCheck", "Database.Cap.Source.Specification",
            StorageAvailability.NotInEngine, "Database.Cap.Note.ReadCheckInstead"),

        // Was "not in the engine", with "the key is derived when the database is created" as its
        // reason. Both stopped being true at the format change: the data key is drawn at random and
        // the password only WRAPS it, so replacing a password rewrites 60 bytes.
        //
        // The sharper half of the mistake is that the RIGHT answer already had a name here.
        // NeedsProviderAccess exists for exactly this - a thing the engine can do that nothing above
        // it can ask for - and its own comment says no row had been in that state since 2026-08-09.
        // The rewrap was in that state for a whole release and this row said "not in the engine"
        // instead, which is the matrix failing at its one job: it is the row a reader trusts most,
        // because the honest "not in the engine" rows are what make the rest of it credible.
        new("Database.Cap.ChangePassword", "Database.Cap.Source.Rewrap", StorageAvailability.Available,
            "Database.Cap.Note.RewrapNotMigration")
    ];

    #endregion
}
