namespace OutWit.Database.Studio.Models;

/// <summary>
/// Where an operation on the storage stands (WS-55).
/// </summary>
/// <remarks>
/// Three states because the engine allows three, and the third is the reason this enum exists: an
/// operation the engine does not have is ABSENT from the interface rather than greyed out. A grey
/// button promises that it will one day be pressable and sends the user looking for a condition that
/// does not exist.
/// </remarks>
public enum StorageAvailability
{
    /// <summary>Studio can do it today.</summary>
    Available,

    /// <summary>
    /// The engine has it and the provider does not hand it over yet.
    /// </summary>
    /// <remarks>
    /// <b>No row is in this state since 2026-08-09</b>, and the last one that was is the reason it
    /// exists: page cache occupancy, which both caches had counted all along and neither published.
    /// The state is kept because it is the honest answer for the next capability found that way - a
    /// thing the engine can do that nothing above it can ask for - and the matrix's own test says the
    /// list is empty rather than pretending the state was never needed.
    /// </remarks>
    NeedsProviderAccess,

    /// <summary>The engine does not have it at all.</summary>
    NotInEngine
}
