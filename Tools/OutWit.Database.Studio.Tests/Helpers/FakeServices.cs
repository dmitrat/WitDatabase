using System.Data;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.Tests.Helpers;

/// <summary>
/// Test double for IDatabaseService.
/// </summary>
internal class FakeDatabaseService : IDatabaseService
{
    #region IDatabaseService

    public bool IsConnected => false;
    public ConnectionInfo? CurrentConnection => null;

    public event EventHandler<bool>? ConnectionStatusChanged;

    public Task<bool> ConnectAsync(ConnectionInfo connectionInfo, CancellationToken ct = default) => 
        Task.FromResult(false);
    
    public Task DisconnectAsync() => Task.CompletedTask;
    
    public Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default) => 
        Task.FromResult(new QueryResult { ErrorMessage = "Not connected" });
    
    public Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default) => 
        Task.FromResult(0);
    
    public Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default) =>
        Task.FromResult<object?>(null);

    public Task<BatchResult> ExecuteBatchAsync(IReadOnlyList<string> statements, CancellationToken ct = default) =>
        Task.FromResult(new BatchResult { FailedIndex = 0, ErrorMessage = "Not connected" });
    
    public Task<IReadOnlyList<TableInfo>> GetTablesAsync(CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<TableInfo>>(Array.Empty<TableInfo>());
    
    public Task<IReadOnlyList<string>> GetViewsAsync(CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    
    public Task<IReadOnlyList<string>> GetIndexesAsync(CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    
    public Task<IReadOnlyList<string>> GetTriggersAsync(CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    
    public Task<IReadOnlyList<string>> GetSequencesAsync(CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    
    public Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string tableName, CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<ColumnInfo>>(Array.Empty<ColumnInfo>());
    
    public Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct = default) => 
        Task.FromResult<IReadOnlyList<ColumnInfo>>(Array.Empty<ColumnInfo>());

    public Task<string?> GetViewDefinitionAsync(string viewName, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> GetTriggerDefinitionAsync(string triggerName, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> GetIndexDefinitionAsync(string indexName, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> GetTableDefinitionAsync(string tableName, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public void Dispose() { }

    #endregion
}

/// <summary>
/// Test double for ISettingsService.
/// </summary>
internal class FakeSettingsService : ISettingsService
{
    #region Fields

    /// <summary>
    /// Settings to return from LoadAsync. Set this in tests to configure behavior.
    /// </summary>
    public Settings Settings { get; set; } = new();

    #endregion

    #region ISettingsService

    public List<string> RecentFiles { get; } = [];
    
    public Task<Settings> LoadAsync() => Task.FromResult(Settings);
    
    public Task SaveAsync(Settings settings) 
    { 
        Settings = settings; 
        return Task.CompletedTask; 
    }
    
    public Task AddRecentFileAsync(string filePath) 
    { 
        RecentFiles.Add(filePath); 
        return Task.CompletedTask; 
    }
    
    public Task RemoveRecentFileAsync(string filePath) 
    { 
        RecentFiles.Remove(filePath); 
        return Task.CompletedTask; 
    }
    
    public Task ClearRecentFilesAsync() 
    { 
        RecentFiles.Clear(); 
        return Task.CompletedTask; 
    }

    #endregion
}

/// <summary>
/// Answers the unsaved-changes question with a scripted decision, and records that it was asked.
///
/// "Was the user asked" is half of what B6 is about: a close that silently applies is as wrong as a
/// close that silently discards, and only the count can tell them apart.
/// </summary>
internal sealed class ScriptedConfirmationService : IConfirmationService
{
    public ScriptedConfirmationService(UnsavedChangesDecision decision)
    {
        Decision = decision;
    }

    public UnsavedChangesDecision Decision { get; set; }

    public int TimesAsked { get; private set; }

    public int LastChangeCount { get; private set; }

    public string? LastTitle { get; private set; }

    public Task<UnsavedChangesDecision> AskAboutUnsavedChangesAsync(string title, int changeCount)
    {
        TimesAsked++;
        LastTitle = title;
        LastChangeCount = changeCount;

        return Task.FromResult(Decision);
    }
}

/// <summary>
/// Test double for IExportService.
/// </summary>
internal class FakeExportService : IExportService
{
    #region IExportService

    public Task ExportToCsvAsync(DataTable data, string filePath) => Task.CompletedTask;
    public Task ExportToJsonAsync(DataTable data, string filePath) => Task.CompletedTask;
    public Task ExportToSqlAsync(DataTable data, string tableName, string filePath) => Task.CompletedTask;
    public string ToCsv(DataTable data, bool includeHeaders = true) => string.Empty;
    public string ToInsertStatements(DataTable data, string tableName) => string.Empty;
    public string RowsToCsv(IEnumerable<DataRowView> rows, DataTable schema, bool includeHeaders = true) => string.Empty;
    public string RowsToInsertStatements(IEnumerable<DataRowView> rows, DataTable schema, string tableName) => string.Empty;

    #endregion
}
