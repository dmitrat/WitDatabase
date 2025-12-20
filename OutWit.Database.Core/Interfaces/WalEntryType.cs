namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Entry types for Write-Ahead Log operations.
/// </summary>
public enum WalEntryType : byte
{
    /// <summary>Put operation - insert or update a key-value pair.</summary>
    Put = 1,
    
    /// <summary>Delete operation - remove a key.</summary>
    Delete = 2,
    
    /// <summary>Begin transaction marker.</summary>
    BeginTransaction = 3,
    
    /// <summary>Commit transaction marker.</summary>
    CommitTransaction = 4,
    
    /// <summary>Rollback transaction marker.</summary>
    RollbackTransaction = 5
}