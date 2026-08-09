namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// How full the page cache of an open database is, at the moment it was asked.
    /// </summary>
    /// <param name="ProviderKey">Which cache is answering - <c>lru</c> or <c>sharded-clock</c>.</param>
    /// <param name="Pages">Pages held.</param>
    /// <param name="DirtyPages">Of those, pages written to and not yet flushed.</param>
    public readonly record struct PageCacheOccupancy(string ProviderKey, int Pages, int DirtyPages);

    /// <summary>
    /// A store that can say how full its page cache is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this is NOT.</b> It is occupancy, not a hit rate. Neither <c>PageCacheLru</c> nor
    /// <c>PageCacheShardedClock</c> counts a hit or a miss - only the LSM <c>BlockCache</c> does - so a
    /// hit rate is absent from the ENGINE rather than merely unexposed, and no amount of plumbing here
    /// would produce one. Measured 2026-08-02 and again on 2026-08-09.
    /// </para>
    /// <para>
    /// Unlike <see cref="IStoredConfigurationSource"/>, this answer DOES go stale: it is a reading
    /// taken at the moment of the call and the next page read can change it. A consumer that shows it
    /// has to say when it was taken, or offer to take it again.
    /// </para>
    /// </remarks>
    public interface IPageCacheOccupancySource
    {
        /// <summary>
        /// The occupancy of this store's page cache, or <c>null</c> when there is no page cache to ask
        /// - an LSM store, which is not paged at all.
        /// </summary>
        PageCacheOccupancy? CacheOccupancy { get; }
    }
}
