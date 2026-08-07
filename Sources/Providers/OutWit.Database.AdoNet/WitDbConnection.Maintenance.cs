using OutWit.Database.AdoNet.Maintenance;
using OutWit.Database.Core.Concurrency;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Storage maintenance and reporting - WS-57, the three things a consumer cannot reach through
/// ADO.NET's own surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>None of this is new capability.</b> It is <c>Checkpoint</c>, <c>Compact</c> and the store's own
/// counters, which the engine has had all along and no consumer could get at, because a database
/// hands out its outermost wrapper. The work was making them reachable; this file is the surface.
/// </para>
/// <para>
/// <b>Every operation says what it did.</b> An administrative call that returns <c>void</c> cannot
/// report "there was nothing to do", and the measurement that started this work was exactly that:
/// <c>Compact()</c> declining silently.
/// </para>
/// </remarks>
public sealed partial class WitDbConnection
{
    #region Reporting

    /// <summary>
    /// What this connection's storage can say about itself, read once and together.
    /// </summary>
    /// <exception cref="InvalidOperationException">The connection is not open.</exception>
    public WitDbStorageSnapshot GetStorageSnapshot()
    {
        EnsureOpen();

        var store = Store();
        var chain = store.Chain();

        var statistics = store.FindCapability<IKeyValueStoreStatistics>();
        var lsm = store.FindCapability<StoreLsm>();

        return new WitDbStorageSnapshot
        {
            StoreProviderKey = chain[^1].ProviderKey,
            Chain = chain.Select(link => link.ProviderKey).ToList(),
            ApproximateSizeInBytes = statistics?.ApproximateSizeInBytes,
            StatisticsAreExact = statistics?.AreStatisticsExact,
            Lsm = lsm == null ? null : Describe(lsm)
        };
    }

    private static WitDbLsmSnapshot Describe(StoreLsm lsm)
    {
        var (used, limit) = lsm.MemTable;
        var counters = lsm.Statistics.GetSnapshot();

        return new WitDbLsmSnapshot
        {
            SstableCount = lsm.SstableCount,
            CompactionTrigger = lsm.CompactionTrigger,
            MemTableUsedBytes = used,
            MemTableLimitBytes = limit,
            IsCompacting = lsm.IsCompacting,
            CountersSinceOpened = new WitDbLsmCounters
            {
                Gets = counters.Gets,
                Puts = counters.Puts,
                Deletes = counters.Deletes,
                Scans = counters.Scans,
                Flushes = counters.Flushes,
                Compactions = counters.Compactions,
                BytesWritten = counters.BytesWritten,
                BytesRead = counters.BytesRead,
                BloomFilterHits = counters.BloomFilterHits,
                BloomFilterMisses = counters.BloomFilterMisses
            }
        };
    }

    #endregion

    #region Maintenance

    /// <summary>
    /// Forces the store's accumulated in-memory state out into its on-disk structure.
    /// </summary>
    /// <remarks>
    /// Distinct from a commit, which makes writes durable. On an LSM store this is what turns the
    /// memtable into an SSTable; on a B+Tree there is no staging area, so it flushes and reports
    /// <see cref="WitDbMaintenanceOutcome.NothingToDo"/> rather than pretending to have reorganised
    /// anything.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The connection is not open.</exception>
    public WitDbMaintenanceResult Checkpoint()
    {
        EnsureOpen();

        var store = Store();
        var lsm = store.FindCapability<StoreLsm>();

        var before = lsm?.SstableCount;

        store.Checkpoint();

        var after = lsm?.SstableCount;

        return new WitDbMaintenanceResult
        {
            Operation = WitDbMaintenanceOperation.Checkpoint,
            SstablesBefore = before,
            SstablesAfter = after,

            // Judged by what changed on disk rather than by whether the call returned, which is the
            // difference the whole of WS-57 turned on.
            Outcome = lsm == null
                ? WitDbMaintenanceOutcome.NothingToDo
                : after > before
                    ? WitDbMaintenanceOutcome.Completed
                    : WitDbMaintenanceOutcome.NothingToDo
        };
    }

    /// <summary>
    /// Merges the store's files.
    /// </summary>
    /// <remarks>
    /// Only an LSM store has files to merge. A B+Tree reports
    /// <see cref="WitDbMaintenanceOutcome.NotSupported"/> - not "done", and not "nothing to do":
    /// WS-55 asks that an operation the engine does not have be absent from the interface rather than
    /// greyed out, and a caller cannot tell the two apart unless this does.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The connection is not open.</exception>
    public WitDbMaintenanceResult Compact()
    {
        EnsureOpen();

        var lsm = Store().FindCapability<StoreLsm>();

        if (lsm == null)
        {
            return new WitDbMaintenanceResult
            {
                Operation = WitDbMaintenanceOperation.Compact,
                Outcome = WitDbMaintenanceOutcome.NotSupported
            };
        }

        var before = lsm.SstableCount;

        lsm.Compact();

        var after = lsm.SstableCount;

        return new WitDbMaintenanceResult
        {
            Operation = WitDbMaintenanceOperation.Compact,
            SstablesBefore = before,
            SstablesAfter = after,
            Outcome = after < before
                ? WitDbMaintenanceOutcome.Completed
                : WitDbMaintenanceOutcome.NothingToDo
        };
    }

    #endregion

    #region Is it in use

    /// <summary>
    /// Whether a database at this path is open in some process - including this one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked by taking the exclusive lock and letting it go again, which is the same guard the engine
    /// uses to refuse a second opener, so the answer is the one an open would get rather than a
    /// separate opinion about it.
    /// </para>
    /// <para>
    /// <b>It is a moment, not a promise.</b> The database can be opened or closed in the time between
    /// this returning and a caller acting on it; nothing here reserves anything. Use it to explain a
    /// refusal, not to decide whether one will happen.
    /// </para>
    /// <para>
    /// <b>The holder is not named</b>, because the operating system does not tell us and the lock file
    /// carries nothing. WS-57 asks for the name "if available"; it is not available, and inventing a
    /// field that is always null would be worse than saying so here.
    /// </para>
    /// </remarks>
    public static bool IsDatabaseInUse(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        try
        {
            using var probe = new FileLock(path);

            return !probe.TryAcquireExclusiveLock();
        }
        catch (IOException)
        {
            // The lock file itself could not be opened - which is what being in use looks like from
            // here, and reporting "free" would be worse than reporting "busy".
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    #endregion

    #region Tools

    private IKeyValueStore Store() =>
        m_database?.Store
        ?? throw new InvalidOperationException(
            "This connection has no database of its own - it was handed an engine directly, so its "
            + "storage cannot be reported on or maintained through it.");

    #endregion
}
