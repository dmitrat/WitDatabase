namespace OutWit.Database.Studio.Models;

/// <summary>
/// How a migration went.
/// </summary>
public enum MigrationOutcome
{
    /// <summary>Everything ran and every table's rows are on both sides.</summary>
    Transferred,

    /// <summary>
    /// It ran, and the verification found a table whose row counts do not match.
    /// </summary>
    /// <remarks>
    /// A state of its own rather than a failure: the new database exists and holds most of the data,
    /// and whoever started the migration has to decide what that is worth. Saying "done" would be
    /// worse and saying "failed" would be untrue.
    /// </remarks>
    RowsDoNotMatch,

    /// <summary>Nothing usable was produced, and the message says why.</summary>
    Failed
}

/// <summary>
/// One step of the migration, as it happens.
/// </summary>
/// <param name="Key">Catalogue key for what the step is.</param>
/// <param name="Detail">A value to put in it, or null.</param>
public sealed record MigrationStep(string Key, string? Detail = null);

/// <summary>
/// One table, counted on both sides.
/// </summary>
/// <remarks>
/// <b>Counted by SCANNING, not with <c>COUNT(*)</c>.</b> On this engine the count is a number kept
/// beside the rows rather than derived from them, so a verification that used it could agree with
/// itself while the rows were missing. The design asks for "one COUNT(*) per table"; this is the same
/// check done in the only way that can fail for the right reason.
/// </remarks>
public sealed record TableRowCheck(string Table, long InSource, long InTarget)
{
    public bool Matches => InSource == InTarget;
}

/// <summary>
/// What the migration did (WS-58).
/// </summary>
public sealed record MigrationResult(
    MigrationOutcome Outcome,
    string TargetPath,
    IReadOnlyList<TableRowCheck> Verification,
    string? EngineMessage = null)
{
    public long RowsTransferred => Verification.Sum(check => check.InTarget);

    public IReadOnlyList<TableRowCheck> Mismatches =>
        Verification.Where(check => !check.Matches).ToList();
}
