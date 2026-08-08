using OutWit.Database.AdoNet;
using OutWit.Database.AdoNet.Maintenance;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The storage half of a session: what the store can say about itself, and the two things that can be
/// asked of it (WS-56, WS-57).
/// </summary>
/// <remarks>
/// <para>
/// Thin on purpose. The provider's own surface already answers in the shape the panel needs - an
/// outcome CODE with the SSTable counts on either side, rather than a sentence - so translating it
/// here would only give Studio a second vocabulary to keep in step with the engine's.
/// </para>
/// <para>
/// <b>Off the calling thread</b>, because a compaction of a large store is not instant and the tab
/// runs it from a button.
/// </para>
/// </remarks>
public sealed partial class DatabaseSession
{
    #region Storage

    public Task<WitDbStorageSnapshot> GetStorageSnapshotAsync(CancellationToken ct = default) =>
        Task.Run(() => Storage().GetStorageSnapshot(), ct);

    public Task<WitDbMaintenanceResult> CheckpointAsync(CancellationToken ct = default) =>
        Task.Run(() => Storage().Checkpoint(), ct);

    public Task<WitDbMaintenanceResult> CompactAsync(CancellationToken ct = default) =>
        Task.Run(() => Storage().Compact(), ct);

    #endregion

    #region Tools

    private WitDbConnection Storage() =>
        m_connection ?? throw new InvalidOperationException(
            "This session has no open connection, so there is no storage to report on or maintain.");

    #endregion
}
