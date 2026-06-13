using System.Diagnostics;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Mvcc
{
    /// <summary>
    /// Background garbage collector for MVCC stores.
    /// Periodically removes old versions that are no longer visible to any active transaction.
    /// </summary>
    public sealed class MvccGarbageCollector : IDisposable
    {
        #region Fields

        private readonly MvccKeyValueStore m_store;
        private readonly ITransactionTimestampManager m_timestampManager;
        private readonly MvccGarbageCollectorOptions m_options;
        private readonly Timer m_timer;
        private readonly Lock m_runLock = new();
        private readonly CancellationTokenSource m_cts;
        private bool m_disposed;
        private bool m_running;
        private long m_totalVersionsRemoved;
        private int m_runCount;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a background garbage collector for the specified MVCC store.
        /// </summary>
        /// <param name="store">The MVCC key-value store.</param>
        /// <param name="timestampManager">The transaction timestamp manager.</param>
        /// <param name="options">Configuration options.</param>
        public MvccGarbageCollector(
            MvccKeyValueStore store,
            ITransactionTimestampManager timestampManager,
            MvccGarbageCollectorOptions? options = null)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_timestampManager = timestampManager ?? throw new ArgumentNullException(nameof(timestampManager));
            m_options = options ?? new MvccGarbageCollectorOptions();
            m_cts = new CancellationTokenSource();

            var dueTime = m_options.RunOnStart 
                ? TimeSpan.Zero 
                : m_options.CollectionInterval;

            m_timer = new Timer(
                OnTimerCallback,
                null,
                dueTime,
                m_options.CollectionInterval);
        }

        #endregion

        #region Collection

        /// <summary>
        /// Runs garbage collection immediately.
        /// </summary>
        /// <returns>Statistics from the collection run.</returns>
        public GarbageCollectionStatistics RunNow()
        {
            ThrowIfDisposed();
            return RunCollectionInternal();
        }

        /// <summary>
        /// Runs garbage collection asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Statistics from the collection run.</returns>
        public Task<GarbageCollectionStatistics> RunAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return Task.Run(() => RunCollectionInternal(), cancellationToken);
        }

        private void OnTimerCallback(object? state)
        {
            if (m_disposed || m_cts.Token.IsCancellationRequested)
                return;

            try
            {
                RunCollectionInternal();
            }
            catch
            {
                // Ignore exceptions in background collection
            }
        }

        private GarbageCollectionStatistics RunCollectionInternal()
        {
            lock (m_runLock)
            {
                if (m_running)
                {
                    return new GarbageCollectionStatistics
                    {
                        VersionsRemoved = 0,
                        Duration = TimeSpan.Zero,
                        MinActiveSnapshotTimestamp = 0,
                        CompletedAt = DateTime.UtcNow
                    };
                }

                m_running = true;
            }

            var sw = Stopwatch.StartNew();
            int versionsRemoved = 0;
            long minSnapshot = 0;

            try
            {
                // Get minimum active snapshot timestamp
                minSnapshot = m_timestampManager.GetMinimumActiveSnapshotTimestamp();

                // If no active transactions, use current timestamp
                // This means all old versions can be collected
                if (minSnapshot == long.MaxValue)
                {
                    minSnapshot = m_timestampManager.CurrentTimestamp;
                }

                // Run garbage collection
                versionsRemoved = m_store.GarbageCollect(minSnapshot);

                Interlocked.Add(ref m_totalVersionsRemoved, versionsRemoved);
                Interlocked.Increment(ref m_runCount);
            }
            finally
            {
                sw.Stop();
                lock (m_runLock)
                {
                    m_running = false;
                }
            }

            var stats = new GarbageCollectionStatistics
            {
                VersionsRemoved = versionsRemoved,
                Duration = sw.Elapsed,
                MinActiveSnapshotTimestamp = minSnapshot,
                CompletedAt = DateTime.UtcNow
            };

            // Invoke callback if configured
            if (m_options.EnableStatistics && m_options.OnCollectionComplete != null)
            {
                try
                {
                    m_options.OnCollectionComplete(stats);
                }
                catch
                {
                    // Ignore callback exceptions
                }
            }

            return stats;
        }

        #endregion

        #region Control

        /// <summary>
        /// Pauses background garbage collection.
        /// </summary>
        public void Pause()
        {
            ThrowIfDisposed();
            m_timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Resumes background garbage collection.
        /// </summary>
        public void Resume()
        {
            ThrowIfDisposed();
            m_timer.Change(m_options.CollectionInterval, m_options.CollectionInterval);
        }

        /// <summary>
        /// Changes the collection interval.
        /// </summary>
        /// <param name="interval">New interval.</param>
        public void SetInterval(TimeSpan interval)
        {
            ThrowIfDisposed();

            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");

            m_timer.Change(interval, interval);
        }

        #endregion

        #region Tools

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(m_disposed, this);
        }

        #endregion

        #region IDisposable

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;

            m_cts.Cancel();

            // Wait for an already-running timer callback to finish before returning. Timer.Dispose()
            // alone does not wait for an in-flight callback, so a collection that started just before
            // Dispose could still complete (and bump RunCount) afterwards. The WaitHandle overload
            // signals once all callbacks have drained, guaranteeing no background collection runs
            // after Dispose returns.
            using (var timerCallbacksDrained = new ManualResetEvent(false))
            {
                if (m_timer.Dispose(timerCallbacksDrained))
                    timerCallbacksDrained.WaitOne();
            }

            m_cts.Dispose();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the total number of versions removed by this collector.
        /// </summary>
        public long TotalVersionsRemoved => Interlocked.Read(ref m_totalVersionsRemoved);

        /// <summary>
        /// Gets the number of collection runs performed.
        /// </summary>
        public int RunCount => Interlocked.CompareExchange(ref m_runCount, 0, 0);

        /// <summary>
        /// Gets whether a collection is currently running.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (m_runLock)
                {
                    return m_running;
                }
            }
        }

        /// <summary>
        /// Gets the configured options.
        /// </summary>
        public MvccGarbageCollectorOptions Options => m_options;

        #endregion
    }
}
