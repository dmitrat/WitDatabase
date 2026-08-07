namespace OutWit.Database.AdoNet.Maintenance;

/// <summary>
/// What a maintenance operation actually did.
/// </summary>
/// <remarks>
/// <para>
/// <b>A maintenance call returns a result because the alternative was measured and it lies.</b>
/// <c>StoreLsm.Compact()</c> was a <c>void</c> that applied the automatic compaction trigger to an
/// explicit call, so it did nothing and said nothing - a button wired to it would have reported
/// success every time and changed the file only occasionally. An operation offered to a person has to
/// be able to say "there was nothing to do" out loud.
/// </para>
/// <para>
/// <b>An outcome CODE, not a sentence.</b> Whoever shows this writes the words, in whatever language
/// the interface is in. A service that composes prose fixes the language of every screen that
/// displays it, which is a defect this repository has already found in three of its own services.
/// </para>
/// </remarks>
public sealed class WitDbMaintenanceResult
{
    /// <summary>
    /// Which operation this is about.
    /// </summary>
    public required WitDbMaintenanceOperation Operation { get; init; }

    /// <summary>
    /// What became of it.
    /// </summary>
    public required WitDbMaintenanceOutcome Outcome { get; init; }

    /// <summary>
    /// SSTables before the call, and after it. Null for a store that has none.
    /// </summary>
    /// <remarks>
    /// The pair is the evidence for <see cref="Outcome"/> rather than a decoration: a caller can show
    /// what changed, and a test can tell "it ran" from "it said it ran".
    /// </remarks>
    public int? SstablesBefore { get; init; }

    /// <inheritdoc cref="SstablesBefore"/>
    public int? SstablesAfter { get; init; }

    /// <summary>
    /// True when the operation changed something on disk.
    /// </summary>
    public bool Ran => Outcome == WitDbMaintenanceOutcome.Completed;
}

/// <summary>
/// The maintenance operations a connection offers.
/// </summary>
public enum WitDbMaintenanceOperation
{
    /// <summary>Force the accumulated in-memory state out into the store's on-disk structure.</summary>
    Checkpoint,

    /// <summary>Merge the store's files.</summary>
    Compact
}

/// <summary>
/// What became of a maintenance operation.
/// </summary>
public enum WitDbMaintenanceOutcome
{
    /// <summary>It ran and something changed.</summary>
    Completed,

    /// <summary>
    /// The store supports it and there was nothing to do - one file cannot be merged with itself, and
    /// an empty memtable has nothing to write out.
    /// </summary>
    NothingToDo,

    /// <summary>
    /// This store does not have the operation at all. A B+Tree is not compacted: there are no files to
    /// merge, and offering the button anyway is what WS-55 refuses.
    /// </summary>
    NotSupported
}
