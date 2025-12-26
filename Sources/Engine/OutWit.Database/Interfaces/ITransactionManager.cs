using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Interfaces;

/// <summary>
/// Interface for managing transactions.
/// </summary>
public interface ITransactionManager
{
    /// <summary>
    /// Gets the current active transaction, if any.
    /// </summary>
    ITransaction? Transaction { get; }

    /// <summary>
    /// Rollback the current transaction.
    /// </summary>
    void Rollback();
}
