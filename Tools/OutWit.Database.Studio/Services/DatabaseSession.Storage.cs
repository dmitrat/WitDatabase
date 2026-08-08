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

    /// <summary>
    /// Reads every row and every value a query returns, and answers how many rows there were.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every VALUE, not every row.</b> A row can be read without its long values being touched, and
    /// a long value is where the overflow pages are - which is exactly the damage a read check exists
    /// to find. So each column is fetched and thrown away.
    /// </para>
    /// <para>
    /// <b>And nothing is materialised.</b> The ordinary query path builds a <c>DataTable</c>, which is
    /// right for a result a person is going to look at and wrong for a scan of a table that does not
    /// fit in memory.
    /// </para>
    /// </remarks>
    public Task<long> ScanAsync(string sql, CancellationToken ct = default) =>
        ScanAsync(Models.SqlStatement.Of(sql), ct);

    /// <inheritdoc cref="ScanAsync(string, CancellationToken)"/>
    public async Task<long> ScanAsync(Models.SqlStatement statement, CancellationToken ct = default)
    {
        _ = Storage();

        await using var command = CreateCommand(statement, transaction: null);

        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = 0L;

        while (await reader.ReadAsync(ct))
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (!await reader.IsDBNullAsync(i, ct))
                    _ = reader.GetValue(i);
            }

            rows++;
        }

        return rows;
    }

    #endregion

    #region Tools

    private WitDbConnection Storage() =>
        m_connection ?? throw new InvalidOperationException(
            "This session has no open connection, so there is no storage to report on or maintain.");

    #endregion
}
