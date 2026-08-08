namespace OutWit.Database.Studio.Models;

/// <summary>
/// What was read.
/// </summary>
public enum ReadCheckSubject
{
    /// <summary>A table, scanned row by row.</summary>
    Table,

    /// <summary>An index, walked by a query the planner was asked to answer with it.</summary>
    Index,

    /// <summary>The schema catalogue itself.</summary>
    Catalog
}

/// <summary>
/// How the reading went.
/// </summary>
public enum ReadCheckOutcome
{
    /// <summary>Everything came back.</summary>
    Ok,

    /// <summary>The engine refused or failed partway, and said why.</summary>
    Failed,

    /// <summary>
    /// It was read, and the reading did not prove what it was meant to prove.
    /// </summary>
    /// <remarks>
    /// An index whose query the planner answered with a table scan is the case this exists for. The
    /// rows came back and nothing is wrong - but the index was not touched, so calling it "ok" would
    /// be a green tick for a structure nobody read. Same family as "acceptance is not behaviour".
    /// </remarks>
    Inconclusive
}

/// <summary>
/// One line of the report: an object, and what reading it produced.
/// </summary>
/// <param name="Subject">What kind of thing it is.</param>
/// <param name="Name">Its name in the database.</param>
/// <param name="Outcome">How the reading went.</param>
/// <param name="RowsRead">How many rows actually came back.</param>
/// <param name="CounterSays">
/// What <c>COUNT(*)</c> answers for the same table, which on this engine is a cached counter kept
/// BESIDE the rows rather than derived from them - so the two disagreeing is a real finding and one of
/// the few kinds of damage a read check can see (KnownIssues, the crash-count family).
/// </param>
/// <param name="EngineMessage">The engine's own words when it refused. Never translated.</param>
/// <param name="NoteKey">A catalogue key for what Studio has to add, or null.</param>
public sealed record ReadCheckItem(
    ReadCheckSubject Subject,
    string Name,
    ReadCheckOutcome Outcome,
    long RowsRead,
    long? CounterSays = null,
    string? EngineMessage = null,
    string? NoteKey = null);

/// <summary>
/// The whole of one read check.
/// </summary>
/// <remarks>
/// Counts rather than sentences, and the ViewModel writes the summary. It also carries
/// <see cref="WasCancelled"/>, because a check stopped halfway is not a check that passed.
/// </remarks>
public sealed record ReadCheckReport(IReadOnlyList<ReadCheckItem> Items, bool WasCancelled)
{
    public int Ok => Items.Count(item => item.Outcome == ReadCheckOutcome.Ok);

    public int Failed => Items.Count(item => item.Outcome == ReadCheckOutcome.Failed);

    public int Inconclusive => Items.Count(item => item.Outcome == ReadCheckOutcome.Inconclusive);

    public int Tables => Items.Count(item => item.Subject == ReadCheckSubject.Table);

    public int Indexes => Items.Count(item => item.Subject == ReadCheckSubject.Index);

    /// <summary>
    /// Rows whose table's cached counter disagrees with what the scan actually returned.
    /// </summary>
    public IReadOnlyList<ReadCheckItem> Disagreements =>
        Items.Where(item => item.CounterSays is { } counter && counter != item.RowsRead).ToList();
}
