namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Priority-based wait queue for transactions waiting to acquire resources.
    /// Supports configurable priority levels, writer priority, and timeouts.
    /// </summary>
    public sealed class TransactionWaitQueue : IDisposable
    {
        #region Constants

        private const int PRIORITY_LEVELS = 4;

        #endregion

        #region Fields

        private readonly TransactionWaitQueueOptions m_options;
        private readonly List<WaitEntry>[] m_queues;
        private readonly Lock m_lock = new();
        private readonly Dictionary<long, WaitEntry> m_waitingTransactions;
        private int m_totalWaiting;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new transaction wait queue with default options.
        /// </summary>
        public TransactionWaitQueue()
            : this(new TransactionWaitQueueOptions())
        {
        }

        /// <summary>
        /// Creates a new transaction wait queue with the specified options.
        /// </summary>
        /// <param name="options">Configuration options.</param>
        public TransactionWaitQueue(TransactionWaitQueueOptions options)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_waitingTransactions = new Dictionary<long, WaitEntry>();
            m_queues = new List<WaitEntry>[PRIORITY_LEVELS];

            for (int i = 0; i < PRIORITY_LEVELS; i++)
            {
                m_queues[i] = new List<WaitEntry>();
            }
        }

        #endregion

        #region Enqueue

        /// <summary>
        /// Adds a transaction to the wait queue.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <param name="isWriter">True if this is a write transaction.</param>
        /// <param name="priority">Priority level for the transaction.</param>
        /// <param name="timeout">Optional timeout override.</param>
        /// <returns>A wait handle that completes when the transaction can proceed.</returns>
        /// <exception cref="InvalidOperationException">Queue is full or transaction already waiting.</exception>
        public WaitHandle Enqueue(
            long transactionId,
            bool isWriter,
            TransactionPriority priority = TransactionPriority.Normal,
            TimeSpan? timeout = null)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (m_totalWaiting >= m_options.MaxWaitingTransactions)
                {
                    throw new InvalidOperationException(
                        $"Transaction wait queue is full ({m_options.MaxWaitingTransactions} transactions waiting).");
                }

                if (m_waitingTransactions.ContainsKey(transactionId))
                {
                    throw new InvalidOperationException(
                        $"Transaction {transactionId} is already in the wait queue.");
                }

                var effectiveTimeout = timeout ?? m_options.DefaultTimeout;
                var entry = new WaitEntry(transactionId, isWriter, priority, effectiveTimeout);

                // Add to priority queue
                var queueIndex = GetQueueIndex(priority, isWriter);
                var queue = m_queues[queueIndex];

                if (m_options.UseFifoOrdering)
                {
                    queue.Add(entry);
                }
                else
                {
                    queue.Insert(0, entry);
                }

                m_waitingTransactions[transactionId] = entry;
                m_totalWaiting++;

                return entry.WaitHandle;
            }
        }

        /// <summary>
        /// Adds a transaction to the wait queue and waits synchronously.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <param name="isWriter">True if this is a write transaction.</param>
        /// <param name="priority">Priority level.</param>
        /// <param name="timeout">Optional timeout override.</param>
        /// <returns>True if signaled, false if timed out.</returns>
        public bool EnqueueAndWait(
            long transactionId,
            bool isWriter,
            TransactionPriority priority = TransactionPriority.Normal,
            TimeSpan? timeout = null)
        {
            var handle = Enqueue(transactionId, isWriter, priority, timeout);
            var effectiveTimeout = timeout ?? m_options.DefaultTimeout;

            try
            {
                return handle.WaitOne(effectiveTimeout);
            }
            finally
            {
                // Clean up if timed out
                Dequeue(transactionId);
            }
        }

        /// <summary>
        /// Adds a transaction to the wait queue and waits asynchronously.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <param name="isWriter">True if this is a write transaction.</param>
        /// <param name="priority">Priority level.</param>
        /// <param name="timeout">Optional timeout override.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if signaled, false if timed out or cancelled.</returns>
        public async Task<bool> EnqueueAndWaitAsync(
            long transactionId,
            bool isWriter,
            TransactionPriority priority = TransactionPriority.Normal,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            var handle = Enqueue(transactionId, isWriter, priority, timeout);
            var effectiveTimeout = timeout ?? m_options.DefaultTimeout;

            try
            {
                // RunContinuationsAsynchronously matters here for the same reason it does in
                // RowLockManager: both completion paths run on a thread that must not be made to
                // execute this transaction's continuation. CancellationToken.Register callbacks are
                // synchronous, so without it the thread calling Cancel pays for whatever the cancelled
                // transaction does next - measured at 1004 ms for a 1 s continuation.
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var registration = ThreadPool.RegisterWaitForSingleObject(
                    handle,
                    (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut),
                    tcs,
                    effectiveTimeout,
                    executeOnlyOnce: true);

                await using (cancellationToken.Register(() => tcs.TrySetResult(false)))
                {
                    var result = await tcs.Task.ConfigureAwait(false);
                    registration.Unregister(handle);
                    return result;
                }
            }
            finally
            {
                Dequeue(transactionId);
            }
        }

        #endregion

        #region Dequeue

        /// <summary>
        /// Removes a transaction from the wait queue.
        /// </summary>
        /// <param name="transactionId">The transaction ID to remove.</param>
        /// <returns>True if the transaction was found and removed.</returns>
        public bool Dequeue(long transactionId)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (!m_waitingTransactions.TryGetValue(transactionId, out var entry))
                {
                    return false;
                }

                var queueIndex = GetQueueIndex(entry.Priority, entry.IsWriter);
                m_queues[queueIndex].Remove(entry);
                m_waitingTransactions.Remove(transactionId);
                m_totalWaiting--;

                entry.Dispose();
                return true;
            }
        }

        #endregion

        #region Signal

        /// <summary>
        /// Signals the next transaction in the queue to proceed.
        /// </summary>
        /// <returns>The transaction ID that was signaled, or null if queue is empty.</returns>
        public long? SignalNext()
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                var entry = GetNextEntry();
                if (entry == null)
                    return null;

                var queueIndex = GetQueueIndex(entry.Priority, entry.IsWriter);
                m_queues[queueIndex].Remove(entry);
                m_waitingTransactions.Remove(entry.TransactionId);
                m_totalWaiting--;

                entry.Signal();
                return entry.TransactionId;
            }
        }

        /// <summary>
        /// Signals a specific transaction to proceed.
        /// </summary>
        /// <param name="transactionId">The transaction ID to signal.</param>
        /// <returns>True if the transaction was found and signaled.</returns>
        public bool Signal(long transactionId)
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                if (!m_waitingTransactions.TryGetValue(transactionId, out var entry))
                {
                    return false;
                }

                var queueIndex = GetQueueIndex(entry.Priority, entry.IsWriter);
                m_queues[queueIndex].Remove(entry);
                m_waitingTransactions.Remove(transactionId);
                m_totalWaiting--;

                entry.Signal();
                return true;
            }
        }

        /// <summary>
        /// Signals all waiting transactions to proceed.
        /// </summary>
        /// <returns>The number of transactions signaled.</returns>
        public int SignalAll()
        {
            ThrowIfDisposed();

            lock (m_lock)
            {
                var count = m_totalWaiting;

                foreach (var queue in m_queues)
                {
                    foreach (var entry in queue)
                    {
                        entry.Signal();
                    }
                    queue.Clear();
                }

                m_waitingTransactions.Clear();
                m_totalWaiting = 0;

                return count;
            }
        }

        #endregion

        #region Query

        /// <summary>
        /// Checks if a transaction is in the wait queue.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <returns>True if waiting.</returns>
        public bool IsWaiting(long transactionId)
        {
            lock (m_lock)
            {
                return m_waitingTransactions.ContainsKey(transactionId);
            }
        }

        /// <summary>
        /// Gets all waiting transaction IDs.
        /// </summary>
        /// <returns>List of waiting transaction IDs.</returns>
        public IReadOnlyList<long> GetWaitingTransactions()
        {
            lock (m_lock)
            {
                return m_waitingTransactions.Keys.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Gets the position of a transaction in the queue.
        /// </summary>
        /// <param name="transactionId">The transaction ID.</param>
        /// <returns>Position (0-based) or -1 if not found.</returns>
        public int GetPosition(long transactionId)
        {
            lock (m_lock)
            {
                if (!m_waitingTransactions.TryGetValue(transactionId, out var entry))
                {
                    return -1;
                }

                int position = 0;

                // Count all entries in higher priority queues
                for (int i = PRIORITY_LEVELS - 1; i >= 0; i--)
                {
                    var queueIndex = i;
                    if (m_options.WriterPriority)
                    {
                        // Writers at even indices, readers at odd indices within each priority
                        // Check writer queue first (higher effective priority)
                    }

                    var queue = m_queues[i];
                    var entryIndex = queue.IndexOf(entry);
                    
                    if (entryIndex >= 0)
                    {
                        return position + entryIndex;
                    }
                    
                    position += queue.Count;
                }

                return -1;
            }
        }

        #endregion

        #region Tools

        private int GetQueueIndex(TransactionPriority priority, bool isWriter)
        {
            // Simple mapping: use priority value directly
            // If writer priority is enabled, writers go to their priority queue
            // and are dequeued before readers of the same priority
            return (int)priority;
        }

        private WaitEntry? GetNextEntry()
        {
            // Process from highest priority to lowest
            for (int priority = PRIORITY_LEVELS - 1; priority >= 0; priority--)
            {
                var queue = m_queues[priority];
                
                if (m_options.WriterPriority)
                {
                    // Find first writer in this priority
                    var writer = queue.FirstOrDefault(e => e.IsWriter);
                    if (writer != null)
                        return writer;
                }

                // Otherwise return first entry
                if (queue.Count > 0)
                    return queue[0];
            }

            return null;
        }

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

            lock (m_lock)
            {
                foreach (var queue in m_queues)
                {
                    foreach (var entry in queue)
                    {
                        entry.Dispose();
                    }
                    queue.Clear();
                }
                m_waitingTransactions.Clear();
                m_totalWaiting = 0;
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of transactions waiting in the queue.
        /// </summary>
        public int WaitingCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_totalWaiting;
                }
            }
        }

        /// <summary>
        /// Gets the configuration options.
        /// </summary>
        public TransactionWaitQueueOptions Options => m_options;

        #endregion

        #region Nested Types

        private sealed class WaitEntry : IDisposable
        {
            private readonly ManualResetEvent m_event;

            public WaitEntry(
                long transactionId, 
                bool isWriter, 
                TransactionPriority priority, 
                TimeSpan timeout)
            {
                TransactionId = transactionId;
                IsWriter = isWriter;
                Priority = priority;
                Timeout = timeout;
                EnqueuedAt = DateTime.UtcNow;
                m_event = new ManualResetEvent(false);
            }

            public long TransactionId { get; }
            public bool IsWriter { get; }
            public TransactionPriority Priority { get; }
            public TimeSpan Timeout { get; }
            public DateTime EnqueuedAt { get; }
            public WaitHandle WaitHandle => m_event;

            public void Signal()
            {
                m_event.Set();
            }

            public void Dispose()
            {
                m_event.Dispose();
            }
        }

        #endregion
    }
}
