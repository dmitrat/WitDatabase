namespace OutWit.Database.Studio.Models;

/// <summary>
/// How a copy went.
/// </summary>
public enum CopyOutcome
{
    /// <summary>Everything the database owns is at the destination.</summary>
    Copied,

    /// <summary>Nothing was written, or not all of it - and the message says what happened.</summary>
    Failed,

    /// <summary>
    /// The database is a paged file and this connection is holding it, so nothing can read it.
    /// </summary>
    /// <remarks>
    /// Measured 2026-08-08, twice: <c>File.Copy</c> and a stream opened with
    /// <c>FileShare.ReadWrite | FileShare.Delete</c> both fail with an <c>IOException</c> while the
    /// engine has the file. It is not a risk of an inconsistent copy - it is not possible at all. An
    /// LSM database is a folder of finished files and copies while open; a paged one has to be closed
    /// first, and saying so is better than a failure the user has to interpret.
    /// </remarks>
    SourceIsHeldOpen
}

/// <summary>
/// One part of a database on disk, and what happened to it.
/// </summary>
/// <param name="Name">The file or folder name, as it will appear beside the copy.</param>
/// <param name="Bytes">How much was copied.</param>
public sealed record CopiedPart(string Name, long Bytes);

/// <summary>
/// What the copy did (WS-59).
/// </summary>
/// <param name="Outcome">Whether the whole of it arrived.</param>
/// <param name="Parts">Every file and folder that was taken, so the report can name them.</param>
/// <param name="Verified">
/// Null when verification was not asked for; otherwise whether the copy opened and answered.
/// </param>
/// <param name="ObjectsInCopy">How many schema objects the verification found, when it ran.</param>
/// <param name="EngineMessage">The engine's own words, when it had any.</param>
public sealed record CopyResult(
    CopyOutcome Outcome,
    IReadOnlyList<CopiedPart> Parts,
    bool? Verified = null,
    int? ObjectsInCopy = null,
    string? EngineMessage = null)
{
    public long Bytes => Parts.Sum(part => part.Bytes);
}
