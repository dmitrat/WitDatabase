namespace OutWit.Database.Core.LSM
{
    /// <summary>
    /// Statistics for LSM-Tree operations.
    /// Thread-safe counters for monitoring performance.
    /// </summary>
    public sealed class LsmStatistics
    {
        #region Fields

        private long m_gets;
        private long m_puts;
        private long m_deletes;
        private long m_scans;
        private long m_flushes;
        private long m_compactions;
        private long m_bytesWritten;
        private long m_bytesRead;
        private long m_bloomFilterHits;
        private long m_bloomFilterMisses;

        #endregion

        #region Internal Methods

        internal void RecordGet() => Interlocked.Increment(ref m_gets);
        internal void RecordPut() => Interlocked.Increment(ref m_puts);
        internal void RecordDelete() => Interlocked.Increment(ref m_deletes);
        internal void RecordScan() => Interlocked.Increment(ref m_scans);
        internal void RecordFlush() => Interlocked.Increment(ref m_flushes);
        internal void RecordCompaction() => Interlocked.Increment(ref m_compactions);
        internal void RecordBytesWritten(long bytes) => Interlocked.Add(ref m_bytesWritten, bytes);
        internal void RecordBytesRead(long bytes) => Interlocked.Add(ref m_bytesRead, bytes);
        internal void RecordBloomFilterHit() => Interlocked.Increment(ref m_bloomFilterHits);
        internal void RecordBloomFilterMiss() => Interlocked.Increment(ref m_bloomFilterMisses);

        #endregion

        #region Public Methods

        /// <summary>
        /// Resets all statistics to zero.
        /// </summary>
        public void Reset()
        {
            Interlocked.Exchange(ref m_gets, 0);
            Interlocked.Exchange(ref m_puts, 0);
            Interlocked.Exchange(ref m_deletes, 0);
            Interlocked.Exchange(ref m_scans, 0);
            Interlocked.Exchange(ref m_flushes, 0);
            Interlocked.Exchange(ref m_compactions, 0);
            Interlocked.Exchange(ref m_bytesWritten, 0);
            Interlocked.Exchange(ref m_bytesRead, 0);
            Interlocked.Exchange(ref m_bloomFilterHits, 0);
            Interlocked.Exchange(ref m_bloomFilterMisses, 0);
        }

        /// <summary>
        /// Creates a snapshot of current statistics.
        /// </summary>
        public LsmStatisticsSnapshot GetSnapshot() => new()
        {
            Gets = Interlocked.Read(ref m_gets),
            Puts = Interlocked.Read(ref m_puts),
            Deletes = Interlocked.Read(ref m_deletes),
            Scans = Interlocked.Read(ref m_scans),
            Flushes = Interlocked.Read(ref m_flushes),
            Compactions = Interlocked.Read(ref m_compactions),
            BytesWritten = Interlocked.Read(ref m_bytesWritten),
            BytesRead = Interlocked.Read(ref m_bytesRead),
            BloomFilterHits = Interlocked.Read(ref m_bloomFilterHits),
            BloomFilterMisses = Interlocked.Read(ref m_bloomFilterMisses)
        };

        #endregion

        #region Properties

        /// <summary>Gets total Get operations.</summary>
        public long Gets => Interlocked.Read(ref m_gets);

        /// <summary>Gets total Put operations.</summary>
        public long Puts => Interlocked.Read(ref m_puts);

        /// <summary>Gets total Delete operations.</summary>
        public long Deletes => Interlocked.Read(ref m_deletes);

        /// <summary>Gets total Scan operations.</summary>
        public long Scans => Interlocked.Read(ref m_scans);

        /// <summary>Gets total MemTable flush operations.</summary>
        public long Flushes => Interlocked.Read(ref m_flushes);

        /// <summary>Gets total compaction operations.</summary>
        public long Compactions => Interlocked.Read(ref m_compactions);

        /// <summary>Gets total bytes written to storage.</summary>
        public long BytesWritten => Interlocked.Read(ref m_bytesWritten);

        /// <summary>Gets total bytes read from storage.</summary>
        public long BytesRead => Interlocked.Read(ref m_bytesRead);

        /// <summary>Gets Bloom filter hits (key not found without disk read).</summary>
        public long BloomFilterHits => Interlocked.Read(ref m_bloomFilterHits);

        /// <summary>Gets Bloom filter misses (required disk read).</summary>
        public long BloomFilterMisses => Interlocked.Read(ref m_bloomFilterMisses);

        /// <summary>Gets Bloom filter efficiency ratio.</summary>
        public double BloomFilterEfficiency
        {
            get
            {
                var total = BloomFilterHits + BloomFilterMisses;
                return total == 0 ? 0.0 : (double)BloomFilterHits / total;
            }
        }

        #endregion
    }

    /// <summary>
    /// Immutable snapshot of LSM statistics at a point in time.
    /// </summary>
    public sealed record LsmStatisticsSnapshot
    {
        public long Gets { get; init; }
        public long Puts { get; init; }
        public long Deletes { get; init; }
        public long Scans { get; init; }
        public long Flushes { get; init; }
        public long Compactions { get; init; }
        public long BytesWritten { get; init; }
        public long BytesRead { get; init; }
        public long BloomFilterHits { get; init; }
        public long BloomFilterMisses { get; init; }

        public double BloomFilterEfficiency =>
            BloomFilterHits + BloomFilterMisses == 0 ? 0.0 :
            (double)BloomFilterHits / (BloomFilterHits + BloomFilterMisses);
    }
}
