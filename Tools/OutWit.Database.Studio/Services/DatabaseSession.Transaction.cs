using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// Manual transaction control for a session (WS-26).
///
/// Autocommit is what Studio has always done: every statement its own transaction, and a script of
/// seven that fails on the sixth leaves five applied. A manual transaction is the answer to that, and
/// it is the one thing a database client is expected to have that Studio did not.
///
/// The transaction lives on the SESSION and not on the tab the button is drawn in, because a
/// connection can hold exactly one - <c>WitDbConnection.BeginDbTransaction</c> throws "A transaction is
/// already in progress" on the second. Two query tabs of the same database therefore share one
/// transaction, and both are told so; a per-tab transaction would be a lie the second tab pays for.
/// </summary>
public sealed partial class DatabaseSession
{
    #region Events

    public event EventHandler? TransactionChanged;

    #endregion

    #region Fields

    private DbTransaction? m_transaction;

    #endregion

    #region Properties

    public bool HasOpenTransaction => m_transaction != null;

    public IsolationLevel Isolation { get; set; } = IsolationLevel.ReadCommitted;

    public int TransactionStatementCount { get; private set; }

    #endregion

    #region Functions

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        if (m_transaction != null)
            throw new InvalidOperationException("A transaction is already open on this connection.");

        m_transaction = await m_connection!.BeginTransactionAsync(Isolation, ct);
        TransactionStatementCount = 0;

        m_logger.LogInformation("Transaction opened at {Isolation} on {Database}",
            Isolation, Connection.FilePath);

        TransactionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        // Silent when nothing is open: the toolbar button and the keyboard both reach this, and the
        // second press of a pair is not a mistake worth an exception.
        if (m_transaction == null)
            return;

        var transaction = m_transaction;

        try
        {
            await transaction.CommitAsync(ct);

            m_logger.LogInformation("Transaction committed after {Count} statements",
                TransactionStatementCount);
        }
        finally
        {
            // Cleared even when the commit throws. A transaction that failed to commit is over either
            // way, and leaving the field set would refuse every later Begin on this connection.
            await ClearTransactionAsync(transaction);
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (m_transaction == null)
            return;

        var transaction = m_transaction;

        try
        {
            await transaction.RollbackAsync(ct);

            m_logger.LogInformation("Transaction rolled back after {Count} statements",
                TransactionStatementCount);
        }
        finally
        {
            await ClearTransactionAsync(transaction);
        }
    }

    private async Task ClearTransactionAsync(DbTransaction transaction)
    {
        m_transaction = null;
        TransactionStatementCount = 0;

        await transaction.DisposeAsync();

        TransactionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rolls back an open transaction when the connection is going away. Closing with one open would
    /// otherwise leave the decision to the engine, and the user's last statement decided by nobody.
    /// </summary>
    private async Task DiscardTransactionAsync()
    {
        if (m_transaction == null)
            return;

        m_logger.LogWarning("Connection to {Database} closed with a transaction open after {Count} " +
            "statements - rolling it back", Connection.FilePath, TransactionStatementCount);

        try
        {
            await RollbackTransactionAsync();
        }
        catch (Exception ex)
        {
            m_logger.LogError(ex, "The open transaction could not be rolled back on close");
            m_transaction = null;
            TransactionStatementCount = 0;
        }
    }

    /// <summary>
    /// Counts a statement against the open transaction, so the indicator can say what is at stake.
    /// </summary>
    private void CountInTransaction()
    {
        if (m_transaction == null)
            return;

        TransactionStatementCount++;
        TransactionChanged?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
