namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// A store that is built over another one, and says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists rather than one more forwarded member.</b> A database hands out its OUTERMOST
/// store - MVCC over versioned over concurrent over the store itself - and every capability the
/// layers below it have is invisible from there unless each wrapper forwards it by hand. That is not
/// a hypothetical: <c>Checkpoint</c> was forwarded by one wrapper and lost by the three above it, so
/// a checkpoint asked of an LSM database never moved the memtable; and
/// <c>KeyValueStoreStatisticsExtensions.Count</c> tests <c>store is IKeyValueStoreStatistics</c>,
/// fails on every wrapped store, and silently falls back to scanning the whole database.
/// </para>
/// <para>
/// Forwarding is per-capability and has to be remembered every time a capability or a wrapper is
/// added. Saying "I wrap that one" is per-WRAPPER and is remembered once. A caller then walks down
/// with <c>FindCapability</c> and finds whatever is there, including capabilities that did not exist
/// when the wrapper was written.
/// </para>
/// <para>
/// <b>It is for asking, not for reaching around.</b> A caller that takes the inner store and writes
/// to it has stepped outside the transaction, the version and the lock the wrappers exist to provide.
/// This is how a REPORT is assembled - what the store is, how large, how many files - and how an
/// administrative operation finds the layer that implements it.
/// </para>
/// </remarks>
public interface IStoreWrapper
{
    /// <summary>
    /// The store immediately underneath this one.
    /// </summary>
    IKeyValueStore Inner { get; }
}
