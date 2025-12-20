namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Visitor interface for WAL replay operations.
/// </summary>
public interface IWalReplayVisitor
{
    /// <summary>Called for each Put entry during replay.</summary>
    void OnPut(long transactionId, byte[] key, byte[] value);
    
    /// <summary>Called for each Delete entry during replay.</summary>
    void OnDelete(long transactionId, byte[] key);
    
    /// <summary>Called for each BeginTransaction entry during replay.</summary>
    void OnBeginTransaction(long transactionId);
    
    /// <summary>Called for each CommitTransaction entry during replay.</summary>
    void OnCommitTransaction(long transactionId);
    
    /// <summary>Called for each RollbackTransaction entry during replay.</summary>
    void OnRollbackTransaction(long transactionId);
}