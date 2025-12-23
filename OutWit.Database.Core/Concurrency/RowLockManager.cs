using OutWit.Database.Core.Comparers;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.Concurrency
{
    /// <summary>
    /// Manages row-level locks for fine-grained concurrency control.
    /// Supports shared (FOR SHARE) and exclusive (FOR UPDATE) locks.
    /// </summary>
    public sealed class RowLockManager : IRowLockManager
    {
        #region Constants

        private static readonly TimeSpan DEFAULT_TIMEOUT = TimeSpan.FromSeconds(30);

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents lock state for a single key.
        /// </summary>
        private sealed class LockEntry
        {
            public RowLockMode Mode { get; set; }
            public HashSet<long> HoldingTransactions { get; } = [];
            public Queue<WaitingRequest> WaitQueue { get; } = new();
        }

        /// <summary>
        /// Represents a transaction waiting for a lock.
        /// </summary>
        private sealed class WaitingRequest
        {
            public long TransactionId { get; init; }
            public RowLockMode RequestedMode { get; init; }
            public TaskCompletionSource<bool> Completion { get; init; } = null!;
        }

        #endregion

        #region Fields

        private readonly Dictionary<ByteArrayKey, LockEntry> m_locks = new();
        private readonly Dictionary<long, HashSet<ByteArrayKey>> m_transactionLocks = new();
        private readonly Lock m_syncLock = new();
        private readonly TimeSpan m_defaultTimeout;
        private bool m_disposed;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new row lock manager with default timeout.
        /// </summary>
        public RowLockManager()
            : this(DEFAULT_TIMEOUT)
        {
        }

        /// <summary>
        /// Creates a new row lock manager with specified default timeout.
        /// </summary>
        /// <param name="defaultTimeout">Default timeout for lock acquisition.</param>
        public RowLockManager(TimeSpan defaultTimeout)
        {
            m_defaultTimeout = defaultTimeout;
        }

        #endregion

        #region AcquireLock

        /// <inheritdoc/>
        public RowLockHandle? AcquireLock(RowLockRequest request)
        {
            ThrowIfDisposed();

            var key = new ByteArrayKey(request.Key);
            var timeout = request.Timeout ?? m_defaultTimeout;
            WaitingRequest? waitRequest = null;

            lock (m_syncLock)
            {
                // Try to acquire immediately
                if (TryAcquireLockInternal(key, request.TransactionId, request.Mode))
                {
                    return new RowLockHandle(this, request.Key, request.TransactionId, request.Mode);
                }

                // Handle based on wait mode
                switch (request.WaitMode)
                {
                    case RowLockWaitMode.NoWait:
                        var holdingTx = GetHoldingTransaction(key);
                        throw new RowLockException(request.Key, holdingTx, request.TransactionId);

                    case RowLockWaitMode.SkipLocked:
                        return null; // Caller should skip this row

                    case RowLockWaitMode.Wait:
                        // Add to wait queue
                        var entry = GetOrCreateEntry(key);
                        waitRequest = new WaitingRequest
                        {
                            TransactionId = request.TransactionId,
                            RequestedMode = request.Mode,
                            Completion = new TaskCompletionSource<bool>()
                        };
                        entry.WaitQueue.Enqueue(waitRequest);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(request.WaitMode));
                }
            }

            // Wait outside the lock
            try
            {
                if (!waitRequest!.Completion.Task.Wait(timeout))
                {
                    // Timeout - remove from queue and throw
                    lock (m_syncLock)
                    {
                        RemoveFromWaitQueue(key, request.TransactionId);
                    }
                    throw new TimeoutException($"Timeout waiting for row lock after {timeout.TotalSeconds} seconds.");
                }

                if (waitRequest.Completion.Task.Result)
                {
                    return new RowLockHandle(this, request.Key, request.TransactionId, request.Mode);
                }
                return null;
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                lock (m_syncLock)
                {
                    RemoveFromWaitQueue(key, request.TransactionId);
                }
                throw new TimeoutException($"Timeout waiting for row lock after {timeout.TotalSeconds} seconds.", ex);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<RowLockHandle?> AcquireLockAsync(RowLockRequest request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var key = new ByteArrayKey(request.Key);
            var timeout = request.Timeout ?? m_defaultTimeout;
            WaitingRequest? waitRequest = null;

            lock (m_syncLock)
            {
                // Try to acquire immediately
                if (TryAcquireLockInternal(key, request.TransactionId, request.Mode))
                {
                    return new RowLockHandle(this, request.Key, request.TransactionId, request.Mode);
                }

                // Handle based on wait mode
                switch (request.WaitMode)
                {
                    case RowLockWaitMode.NoWait:
                        var holdingTx = GetHoldingTransaction(key);
                        throw new RowLockException(request.Key, holdingTx, request.TransactionId);

                    case RowLockWaitMode.SkipLocked:
                        return null;

                    case RowLockWaitMode.Wait:
                        // Add to wait queue
                        var entry = GetOrCreateEntry(key);
                        waitRequest = new WaitingRequest
                        {
                            TransactionId = request.TransactionId,
                            RequestedMode = request.Mode,
                            Completion = new TaskCompletionSource<bool>()
                        };
                        entry.WaitQueue.Enqueue(waitRequest);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(request.WaitMode));
                }
            }

            // Wait outside the lock
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await waitRequest!.Completion.Task.WaitAsync(cts.Token).ConfigureAwait(false);

                if (waitRequest.Completion.Task.Result)
                {
                    return new RowLockHandle(this, request.Key, request.TransactionId, request.Mode);
                }
                return null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout
                lock (m_syncLock)
                {
                    RemoveFromWaitQueue(key, request.TransactionId);
                }
                throw new TimeoutException($"Timeout waiting for row lock after {timeout.TotalSeconds} seconds.");
            }
            catch (OperationCanceledException)
            {
                // Cancelled by caller
                lock (m_syncLock)
                {
                    RemoveFromWaitQueue(key, request.TransactionId);
                }
                throw;
            }
        }

        #endregion

        #region ReleaseLock

        /// <inheritdoc/>
        public void ReleaseAllLocks(long transactionId)
        {
            ThrowIfDisposed();

            lock (m_syncLock)
            {
                if (!m_transactionLocks.TryGetValue(transactionId, out var keys))
                    return;

                foreach (var key in keys.ToList())
                {
                    ReleaseLockInternal(key, transactionId);
                }

                m_transactionLocks.Remove(transactionId);
            }
        }

        private void ReleaseLockInternal(ByteArrayKey key, long transactionId)
        {
            if (!m_locks.TryGetValue(key, out var entry))
                return;

            entry.HoldingTransactions.Remove(transactionId);

            // If no more holders, try to grant to waiters
            if (entry.HoldingTransactions.Count == 0)
            {
                GrantToWaiters(key, entry);
            }
            else if (entry.Mode == RowLockMode.Shared)
            {
                // Check if we can grant more shared locks
                GrantSharedToWaiters(key, entry);
            }

            // Clean up empty entries
            if (entry.HoldingTransactions.Count == 0 && entry.WaitQueue.Count == 0)
            {
                m_locks.Remove(key);
            }
        }

        private void GrantToWaiters(ByteArrayKey key, LockEntry entry)
        {
            while (entry.WaitQueue.Count > 0)
            {
                var waiter = entry.WaitQueue.Peek();

                // Grant the lock
                entry.HoldingTransactions.Add(waiter.TransactionId);
                entry.Mode = waiter.RequestedMode;
                AddTransactionLock(waiter.TransactionId, key);

                entry.WaitQueue.Dequeue();
                waiter.Completion.TrySetResult(true);

                // If exclusive, only grant one
                if (waiter.RequestedMode == RowLockMode.Exclusive)
                    break;

                // If shared, grant all consecutive shared requests
                if (entry.WaitQueue.Count > 0 && entry.WaitQueue.Peek().RequestedMode != RowLockMode.Shared)
                    break;
            }
        }

        private void GrantSharedToWaiters(ByteArrayKey key, LockEntry entry)
        {
            while (entry.WaitQueue.Count > 0)
            {
                var waiter = entry.WaitQueue.Peek();

                // Can only grant more shared locks if current mode is shared
                if (waiter.RequestedMode != RowLockMode.Shared)
                    break;

                entry.HoldingTransactions.Add(waiter.TransactionId);
                AddTransactionLock(waiter.TransactionId, key);

                entry.WaitQueue.Dequeue();
                waiter.Completion.TrySetResult(true);
            }
        }

        #endregion

        #region Query Methods

        /// <inheritdoc/>
        public bool IsLocked(ReadOnlySpan<byte> key)
        {
            ThrowIfDisposed();

            var keyObj = new ByteArrayKey(key.ToArray());
            lock (m_syncLock)
            {
                return m_locks.TryGetValue(keyObj, out var entry) && entry.HoldingTransactions.Count > 0;
            }
        }

        /// <inheritdoc/>
        public bool IsLockedByTransaction(ReadOnlySpan<byte> key, long transactionId)
        {
            ThrowIfDisposed();

            var keyObj = new ByteArrayKey(key.ToArray());
            lock (m_syncLock)
            {
                return m_locks.TryGetValue(keyObj, out var entry) && entry.HoldingTransactions.Contains(transactionId);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<byte[]> GetLockedKeys(long transactionId)
        {
            ThrowIfDisposed();

            lock (m_syncLock)
            {
                if (!m_transactionLocks.TryGetValue(transactionId, out var keys))
                    return Array.Empty<byte[]>();

                return keys.Select(k => k.Value).ToList().AsReadOnly();
            }
        }

        #endregion

        #region Internal Methods

        private bool TryAcquireLockInternal(ByteArrayKey key, long transactionId, RowLockMode mode)
        {
            if (!m_locks.TryGetValue(key, out var entry))
            {
                // No existing lock - create new
                entry = new LockEntry { Mode = mode };
                entry.HoldingTransactions.Add(transactionId);
                m_locks[key] = entry;
                AddTransactionLock(transactionId, key);
                return true;
            }

            // Check if this transaction already holds the lock
            if (entry.HoldingTransactions.Contains(transactionId))
            {
                // Upgrade from shared to exclusive?
                if (entry.Mode == RowLockMode.Shared && mode == RowLockMode.Exclusive)
                {
                    // Can only upgrade if we're the only holder
                    if (entry.HoldingTransactions.Count == 1)
                    {
                        entry.Mode = RowLockMode.Exclusive;
                        return true;
                    }
                    return false; // Can't upgrade, others holding shared
                }
                return true; // Already have compatible lock
            }

            // Check compatibility
            if (entry.Mode == RowLockMode.Shared && mode == RowLockMode.Shared)
            {
                // Multiple shared locks allowed
                entry.HoldingTransactions.Add(transactionId);
                AddTransactionLock(transactionId, key);
                return true;
            }

            // Incompatible lock
            return false;
        }

        private void AddTransactionLock(long transactionId, ByteArrayKey key)
        {
            if (!m_transactionLocks.TryGetValue(transactionId, out var keys))
            {
                keys = [];
                m_transactionLocks[transactionId] = keys;
            }
            keys.Add(key);
        }

        private LockEntry GetOrCreateEntry(ByteArrayKey key)
        {
            if (!m_locks.TryGetValue(key, out var entry))
            {
                entry = new LockEntry();
                m_locks[key] = entry;
            }
            return entry;
        }

        private long GetHoldingTransaction(ByteArrayKey key)
        {
            if (m_locks.TryGetValue(key, out var entry) && entry.HoldingTransactions.Count > 0)
            {
                return entry.HoldingTransactions.First();
            }
            return 0;
        }

        private void RemoveFromWaitQueue(ByteArrayKey key, long transactionId)
        {
            if (!m_locks.TryGetValue(key, out var entry))
                return;

            var newQueue = new Queue<WaitingRequest>();
            while (entry.WaitQueue.Count > 0)
            {
                var waiter = entry.WaitQueue.Dequeue();
                if (waiter.TransactionId != transactionId)
                {
                    newQueue.Enqueue(waiter);
                }
                else
                {
                    waiter.Completion.TrySetResult(false);
                }
            }

            while (newQueue.Count > 0)
            {
                entry.WaitQueue.Enqueue(newQueue.Dequeue());
            }
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

            lock (m_syncLock)
            {
                // Cancel all waiting requests
                foreach (var entry in m_locks.Values)
                {
                    while (entry.WaitQueue.Count > 0)
                    {
                        var waiter = entry.WaitQueue.Dequeue();
                        waiter.Completion.TrySetResult(false);
                    }
                }

                m_locks.Clear();
                m_transactionLocks.Clear();
            }
        }

        #endregion

        #region Properties

        /// <inheritdoc/>
        public int LockCount
        {
            get
            {
                lock (m_syncLock)
                {
                    return m_locks.Count(e => e.Value.HoldingTransactions.Count > 0);
                }
            }
        }

        /// <inheritdoc/>
        public int HoldingTransactionCount
        {
            get
            {
                lock (m_syncLock)
                {
                    return m_transactionLocks.Count;
                }
            }
        }

        #endregion

        #region Nested Key Wrapper

        /// <summary>
        /// Wrapper for byte[] to use as dictionary key.
        /// </summary>
        private readonly struct ByteArrayKey : IEquatable<ByteArrayKey>
        {
            public byte[] Value { get; }
            private readonly int m_hashCode;

            public ByteArrayKey(byte[] value)
            {
                Value = value;
                m_hashCode = ComputeHashCode(value);
            }

            private static int ComputeHashCode(byte[] value)
            {
                if (value == null || value.Length == 0)
                    return 0;

                var hash = new HashCode();
                foreach (var b in value)
                {
                    hash.Add(b);
                }
                return hash.ToHashCode();
            }

            public bool Equals(ByteArrayKey other)
            {
                return Value.AsSpan().SequenceEqual(other.Value.AsSpan());
            }

            public override bool Equals(object? obj)
            {
                return obj is ByteArrayKey other && Equals(other);
            }

            public override int GetHashCode() => m_hashCode;
        }

        #endregion
    }
}
