namespace OutWit.Database.Core.Wal;

/// <summary>
/// Raised when replaying a write-ahead log stops before the end and records the log knows it
/// contains are therefore missing from the recovered database.
/// </summary>
/// <remarks>
/// <b>Why this is an exception and not a return value.</b> Recovery used to stop at the first record
/// that failed verification, apply the prefix, report the count it managed and then truncate the log
/// - so a single damaged record silently destroyed every committed transaction behind it. A database
/// may lose data to corruption; it must not do so quietly, and it must not delete the evidence.
///
/// The transactions that <i>were</i> replayed have been applied and flushed by the time this is
/// thrown, and the journal is left intact rather than checkpointed, so nothing behind the damage is
/// destroyed by the act of noticing it.
///
/// A torn tail - the half-written record an ordinary crash leaves - does not raise this. It is told
/// apart from mid-log damage by the entry count in the log's own header: a tail that was never
/// counted is not a record that went missing.
/// </remarks>
public sealed class WalReplayException : Exception
{
    #region Constructors

    public WalReplayException(long replayed, long expected, long position)
        : base($"Write-ahead log replay stopped after {replayed} of {expected} entries at byte "
               + $"{position}. The entries behind that point could not be verified and have not been "
               + "recovered; the log has been left intact rather than checkpointed.")
    {
        Replayed = replayed;
        Expected = expected;
        Position = position;
    }

    #endregion

    #region Properties

    /// <summary>How many entries were replayed and applied before the log stopped verifying.</summary>
    public long Replayed { get; }

    /// <summary>How many entries the log's header says it holds.</summary>
    public long Expected { get; }

    /// <summary>The byte offset at which verification failed.</summary>
    public long Position { get; }

    #endregion
}
