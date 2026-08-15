namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// A journal file recovery could not apply, and why.
    /// </summary>
    /// <param name="Path">The file, which is KEPT so that it can be looked at.</param>
    /// <param name="Reason">What went wrong, in the words of whatever refused it.</param>
    /// <remarks>
    /// <para>
    /// Recovery does not stop a database opening - one bad file must not lock somebody out of the
    /// rest of their data - so a failure has to arrive some other way, and this is it.
    /// </para>
    /// <para>
    /// <b>Nothing logs it.</b> <c>OutWit.Database.Core</c> has no logging dependency, so a caller who
    /// wants to know reads <c>ITransactionJournal.RecoveryFailures</c> or
    /// <c>TransactionalStore.RecoveryFailures</c> after opening. A caller who never reads it is no
    /// better off than before, and that is said out loud rather than left to be discovered.
    /// </para>
    /// </remarks>
    public sealed record JournalRecoveryFailure(string Path, string Reason);
}
