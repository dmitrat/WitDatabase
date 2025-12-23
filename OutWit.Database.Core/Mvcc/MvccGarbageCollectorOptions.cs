namespace OutWit.Database.Core.Mvcc
{
    /// <summary>
    /// Configuration options for background garbage collection.
    /// </summary>
    public sealed class MvccGarbageCollectorOptions
    {
        /// <summary>
        /// Gets or sets the interval between garbage collection runs.
        /// Default is 30 seconds.
        /// </summary>
        public TimeSpan CollectionInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets whether to run garbage collection immediately on start.
        /// Default is false.
        /// </summary>
        public bool RunOnStart { get; set; } = false;

        /// <summary>
        /// Gets or sets the minimum number of versions a key must have before GC considers it.
        /// This helps avoid GC overhead for keys with few versions.
        /// Default is 2.
        /// </summary>
        public int MinVersionsForCollection { get; set; } = 2;

        /// <summary>
        /// Gets or sets whether to enable logging of GC statistics.
        /// Default is false.
        /// </summary>
        public bool EnableStatistics { get; set; } = false;

        /// <summary>
        /// Gets or sets the callback for GC statistics.
        /// Called after each GC run with the number of versions removed.
        /// </summary>
        public Action<GarbageCollectionStatistics>? OnCollectionComplete { get; set; }
    }

    /// <summary>
    /// Statistics from a garbage collection run.
    /// </summary>
    public sealed class GarbageCollectionStatistics
    {
        /// <summary>
        /// Gets the number of old versions removed.
        /// </summary>
        public int VersionsRemoved { get; init; }

        /// <summary>
        /// Gets the duration of the GC run.
        /// </summary>
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Gets the minimum active snapshot timestamp used for GC.
        /// </summary>
        public long MinActiveSnapshotTimestamp { get; init; }

        /// <summary>
        /// Gets when the GC run completed.
        /// </summary>
        public DateTime CompletedAt { get; init; }
    }
}
