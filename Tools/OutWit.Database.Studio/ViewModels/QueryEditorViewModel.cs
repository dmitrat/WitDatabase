using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for SQL query editor.
/// </summary>
public class QueryEditorViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseService m_databaseService;
    private readonly ILogger<QueryEditorViewModel> m_logger;

    #endregion

    #region Constructors

    public QueryEditorViewModel(
        ApplicationViewModel applicationVm,
        IDatabaseService databaseService)
        : base(applicationVm)
    {
        m_databaseService = databaseService;
        m_logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<QueryEditorViewModel>.Instance;

        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        SqlText = string.Empty;
    }

    private void InitCommands()
    {
        ExecuteCommand = new DelegateCommand<object>(async _ => await ExecuteAsync(), _ => CanExecute());
        ClearCommand = new DelegateCommand<object>(_ => Clear());
    }

    #endregion

    #region Commands

    public DelegateCommand<object> ExecuteCommand { get; private set; } = null!;
    public DelegateCommand<object> ClearCommand { get; private set; } = null!;

    private async Task ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlText))
            return;

        IsExecuting = true;
        ErrorMessage = null;
        Result = null;

        try
        {
            var result = await m_databaseService.ExecuteQueryAsync(SqlText);
            
            Result = result;
            
            if (result.IsSuccess)
            {
                ApplicationVm.MainWindowVm.StatusText = 
                    $"Query executed in {result.ExecutionTimeMs:F2}ms. {result.RowsAffected} rows affected.";
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
                ApplicationVm.MainWindowVm.StatusText = "Query execution failed";
            }

            m_logger.LogInformation("Query executed: {Time}ms, {Rows} rows", 
                result.ExecutionTimeMs, result.RowsAffected);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Execution error: {ex.Message}";
            ApplicationVm.MainWindowVm.StatusText = "Query execution error";
            m_logger.LogError(ex, "Query execution failed");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private bool CanExecute()
    {
        return !string.IsNullOrWhiteSpace(SqlText) 
            && !IsExecuting 
            && m_databaseService.IsConnected;
    }

    private void Clear()
    {
        SqlText = string.Empty;
        Result = null;
        ErrorMessage = null;
    }

    #endregion

    #region Properties

    [Notify(NotifyAlso = nameof(CanExecuteChanged))]
    public string SqlText { get; set; } = string.Empty;

    [Notify]
    public bool IsExecuting { get; set; }

    [Notify]
    public QueryResult? Result { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    public bool CanExecuteChanged => CanExecute();

    #endregion
}
