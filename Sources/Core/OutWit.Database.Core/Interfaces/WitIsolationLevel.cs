namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Transaction isolation levels.
    /// Defines the degree to which transactions are isolated from each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every level above ReadCommitted is OPTIMISTIC here, and this matters to a caller.</b> A
    /// transaction reads from its snapshot and records what it read; the conflict is found at
    /// COMMIT and raised as an exception. Nothing blocks, so a caller who expected to wait gets an
    /// error instead - and has to retry the whole transaction, which a caller who expected a lock
    /// would never have written code for.
    /// </para>
    /// <para>
    /// <b>What each level prevents was measured on 2026-08-15</b> rather than taken from the
    /// standard's table, and one of the answers is worth reading before choosing:
    /// <c>WhatEachIsolationLevelPreventsTests</c> in the engine's test suite pins all four outcomes,
    /// including the WRITE SKEW that Serializable permits.
    /// </para>
    /// </remarks>
    public enum WitIsolationLevel
    {
        /// <summary>
        /// Allows dirty reads. Transaction can see uncommitted changes from other transactions.
        /// Provides highest concurrency but lowest consistency.
        /// Phenomena allowed: dirty reads, non-repeatable reads, phantom reads.
        /// </summary>
        ReadUncommitted = 0,

        /// <summary>
        /// Only committed data is visible. Prevents dirty reads but allows non-repeatable reads.
        /// Most common default isolation level in databases.
        /// Phenomena allowed: non-repeatable reads, phantom reads.
        /// </summary>
        ReadCommitted = 1,

        /// <summary>
        /// Reads come from the transaction's snapshot and the keys read are validated at commit.
        /// Prevents dirty and non-repeatable reads.
        /// </summary>
        /// <remarks>
        /// <b>This is optimistic concurrency, not locking.</b> The comment here used to say "read
        /// locks are held for the duration of the transaction", which is what the standard describes
        /// and is not what happens: nothing is locked, nothing blocks, and a transaction whose read
        /// set was modified by another one <b>fails at commit</b> and must be retried. A caller who
        /// believed the old sentence would have written no retry at all.
        /// </remarks>
        RepeatableRead = 2,

        /// <summary>
        /// The strictest level this engine offers: reads come from the snapshot, and a transaction
        /// that acted on what it read is refused at commit if another transaction changed it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>It is not serializability in the textbook sense, and the difference has a name:
        /// WRITE SKEW.</b> Measured 2026-08-15: two transactions read the same rows, each writes a
        /// DIFFERENT row, both commit, and an invariant that held for each of them separately is
        /// gone. Nothing detects it, because neither transaction touched what the other wrote. The
        /// standard example holds here exactly - two doctors both on call, each transaction takes one
        /// off, and the ward ends with none.
        /// </para>
        /// <para>
        /// What it DOES prevent, also measured: a transaction that reads a range, another that
        /// inserts into that range and commits, and then a write from the first - the first is
        /// refused at commit. And two transactions writing the same row: the second is refused.
        /// </para>
        /// <para>
        /// An application whose correctness depends on an invariant ACROSS rows has to enforce it
        /// itself - by writing a common row that makes the conflict visible, or by serialising the
        /// operation outside the database.
        /// </para>
        /// </remarks>
        Serializable = 3,

        /// <summary>
        /// Snapshot isolation using multi-version concurrency control (MVCC).
        /// Each transaction sees a consistent snapshot of the database as of its start time.
        /// Provides good balance between isolation and concurrency.
        /// </summary>
        /// <remarks>
        /// Permits write skew, which is the standard caveat on snapshot isolation and is measured
        /// here - see <see cref="Serializable"/>, which permits it too.
        /// </remarks>
        Snapshot = 4
    }
}
