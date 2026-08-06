namespace OutWit.Database.Studio.Models;

/// <summary>
/// What happened to a set of statements sent as one transaction.
///
/// Deliberately not a bool and not a list of errors: the two questions that matter after a failed set
/// of edits are "is anything in the database" and "which statement stopped it", and a list of collected
/// error strings answers neither.
/// </summary>
public sealed class BatchResult
{
    /// <summary>
    /// True when every statement ran and the transaction was committed. False means nothing was
    /// applied - not "some of it".
    /// </summary>
    public bool Committed { get; init; }

    /// <summary>
    /// Rows affected across the whole set. Meaningful only when committed.
    /// </summary>
    public int RowsAffected { get; init; }

    /// <summary>
    /// Zero-based index of the statement that failed, or -1 when the set was committed.
    /// </summary>
    public int FailedIndex { get; init; } = -1;

    /// <summary>
    /// The engine's message for the failure, already formatted for a human.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True when the set was refused because a statement matched no row - which, for a statement that
    /// names its row by primary key AND by the values it was read with, means somebody else changed
    /// that row first (WS-37).
    ///
    /// A separate answer from a failure, because it needs a different question put to the user: not
    /// "it did not work" but "here is your value and here is the one in the database".
    /// </summary>
    public bool IsConflict { get; init; }

    /// <summary>
    /// Nothing was asked for, so nothing failed. Committed on purpose: an empty set of edits is not
    /// an error, and the caller should not have to special-case it.
    /// </summary>
    public static BatchResult Empty { get; } = new() { Committed = true };
}
