using System.Transactions;

namespace OutWit.Database.AdoNet;

/// <summary>
/// Ties one connection's work to an ambient <see cref="Transaction"/>, so that a
/// <see cref="TransactionScope"/> that is never completed rolls the work back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Promotable single phase, deliberately.</b> This database is one resource manager on one machine,
/// so the transaction manager can leave the whole transaction to it and skip two-phase commit entirely -
/// that is what <c>EnlistPromotableSinglePhase</c> buys, and it is the only shape this engine can honour
/// honestly. It has no durable prepare record, so it cannot promise "prepared, and I will still be
/// prepared after a crash", which is what a real two-phase participant promises.
/// </para>
/// <para>
/// <b>So promotion is refused rather than faked.</b> If a second durable resource manager joins the
/// transaction, the manager asks this one to promote to a distributed transaction;
/// <see cref="Promote"/> throws instead, and the caller finds out at that moment rather than discovering
/// afterwards that atomicity across the two was never real. That is the same shape as the rest of this
/// provider's contract work: support the ordinary case, refuse the rest by name.
/// </para>
/// <para>
/// Callbacks arrive on the transaction manager's thread, not the connection's, and after the connection
/// may already have been disposed - the ordinary idiom disposes the connection inside the scope and
/// completes the scope afterwards. <see cref="WitDbConnection"/> keeps its engine alive for exactly that
/// window and closes for real when <see cref="WitDbConnection.OnEnlistmentFinished"/> is called.
/// </para>
/// </remarks>
internal sealed class WitDbEnlistment : IPromotableSinglePhaseNotification
{
    #region Fields

    private readonly WitDbConnection m_connection;

    #endregion

    #region Constructors

    public WitDbEnlistment(WitDbConnection connection, Transaction transaction)
    {
        m_connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
    }

    #endregion

    #region Properties

    /// <summary>The ambient transaction this connection is enlisted in.</summary>
    public Transaction Transaction { get; }

    #endregion

    #region IPromotableSinglePhaseNotification

    /// <summary>
    /// Called by the transaction manager when the enlistment is accepted. Opens the engine transaction
    /// that the scope's outcome will decide.
    /// </summary>
    public void Initialize()
    {
        m_connection.Engine!.Execute("BEGIN TRANSACTION");
    }

    /// <inheritdoc/>
    public void SinglePhaseCommit(SinglePhaseEnlistment singlePhaseEnlistment)
    {
        try
        {
            m_connection.Engine?.Execute("COMMIT");
            singlePhaseEnlistment.Committed();
        }
        catch (Exception e)
        {
            // The scope asked to commit and the engine refused: the transaction did not happen, and
            // saying so is the whole point of being enlisted.
            singlePhaseEnlistment.Aborted(e);
        }
        finally
        {
            m_connection.OnEnlistmentFinished();
        }
    }

    /// <inheritdoc/>
    public void Rollback(SinglePhaseEnlistment singlePhaseEnlistment)
    {
        try
        {
            m_connection.Engine?.Execute("ROLLBACK");
        }
        catch
        {
            // Already rolled back, or the engine is gone with it. Either way the work is not committed,
            // which is what the caller is being told.
        }
        finally
        {
            singlePhaseEnlistment.Aborted();
            m_connection.OnEnlistmentFinished();
        }
    }

    /// <inheritdoc/>
    public byte[] Promote()
    {
        throw new TransactionPromotionException(
            "WitDatabase cannot take part in a distributed transaction. It enlisted as the single "
            + "resource manager of this transaction, and something has asked it to promote - which "
            + "happens when a second durable resource manager joins the same TransactionScope. Use one "
            + "database per scope, or coordinate the two yourself.");
    }

    #endregion
}
