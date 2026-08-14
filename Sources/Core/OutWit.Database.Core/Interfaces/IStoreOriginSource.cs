namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// A store that can say whether it was opened over content that was already there, or created from
/// nothing at this open.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the question is worth asking.</b> A secondary index that holds nothing is ambiguous, and
/// the two readings need opposite treatment. An index over a column that is NULL in every row is
/// <b>legitimately</b> empty - <c>FillIndexFromExistingData</c> skips NULLs and rows outside a
/// partial index's condition - and rebuilding it would rescan the whole table on every open, for
/// ever. An index whose file was never copied, or was deleted, is empty because its content is
/// <b>gone</b>, and answering from it is a wrong answer with no error.
/// </para>
/// <para>
/// Emptiness cannot tell those apart. This can: the store underneath the index knows whether it
/// initialised a new, empty store or loaded one that already existed.
/// </para>
/// <para>
/// <b>Found because a control went red.</b> The record said the engine already rebuilds a missing
/// index; measured, it does not - <c>RestoreIndexesFromMetadata</c> calls <c>CreateIndex</c> at
/// open, which MAKES the file, so by the time <c>EnsurePhysicalIndexesExist</c> looks there is
/// always an index there. The fact it needed had been created and thrown away one line earlier.
/// </para>
/// <para>
/// Asked through <c>FindCapability</c> rather than by each wrapper forwarding it - see
/// <see cref="IStoreWrapper"/>.
/// </para>
/// </remarks>
public interface IStoreOriginSource
{
    /// <summary>
    /// True when this store began this session with nothing in it because there was nothing to
    /// load - a file that did not exist, a directory with no data in it, a store that does not
    /// persist at all. False when content was found and loaded, however little of it there is.
    /// </summary>
    bool WasCreatedEmpty { get; }
}
