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

    /// <summary>The engine has it and the provider does not hand it over yet.</summary>
    NeedsProviderAccess,

    /// <summary>The engine does not have it at all.</summary>
    NotInEngine
}
