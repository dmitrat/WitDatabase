using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Aspects;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Dynamic;
using System.Windows.Input;
using OutWit.Common.Utils;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for SQL query editor.
/// </summary>
public class QueryEditorViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields
    private string m_selectedText = string.Empty;

    #endregion

    #region Constructors

    public QueryEditorViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefault();
        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        SqlText = string.Empty;
        StatusText = "Ready";
    }

    private void InitCommands()
    {
        ExecuteCommand = new RelayCommandAsync(ExecuteAsync);
        ExecuteSelectionCommand = new RelayCommandAsync(ExecuteSelectionAsync);
        ClearCommand = new RelayCommand(Clear);
    }

    private void InitEvents()
    {
        this.PropertyChanged += OnPropertyChanged;
    }


    #endregion

    #region Commands
    
    private async Task ExecuteAsync()
    {
        await ExecuteQueryAsync(SqlText);
    }

    private async Task ExecuteSelectionAsync()
    {
        var textToExecute = string.IsNullOrWhiteSpace(m_selectedText) ? SqlText : m_selectedText;
        await ExecuteQueryAsync(textToExecute);
    }

    private async Task ExecuteQueryAsync(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;

        IsExecuting = true;
        ErrorMessage = null;
        Result = null;
        ResultDataView = null;

        try
        {
            var result = await Database.ExecuteQueryAsync(sql);
            
            Result = result;
            
            if (result.IsSuccess)
            {
                // Convert DataTable to DataView for binding
                if (result.ResultTable != null)
                {
                    ResultDataView = result.ResultTable.DefaultView;
                }

                RowsAffected = result.RowsAffected;
                ExecutionTimeMs = result.ExecutionTimeMs;
                StatusText = $"Query executed successfully in {result.ExecutionTimeMs:F2}ms";
                
                ApplicationVm.MainWindowVm.StatusText = 
                    $"Query executed in {result.ExecutionTimeMs:F2}ms. {result.RowsAffected} rows affected.";
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
                StatusText = "Query execution failed";
                ApplicationVm.MainWindowVm.StatusText = "Query execution failed";
            }

            Logger.LogInformation("Query executed: {Time}ms, {Rows} rows", 
                result.ExecutionTimeMs, result.RowsAffected);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Execution error: {ex.Message}";
            StatusText = "Query execution error";
            ApplicationVm.MainWindowVm.StatusText = "Query execution error";
            Logger.LogError(ex, "Query execution failed");
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void Clear()
    {
        SqlText = string.Empty;
        Result = null;
        ResultDataView = null;
        ErrorMessage = null;
        StatusText = "Ready";
        RowsAffected = 0;
        ExecutionTimeMs = 0;
    }

    private void UpdateStatus()
    {
        HasResults = ResultDataView != null && ResultDataView.Count > 0;
        IsSuccess = Result != null && Result.IsSuccess;
        HasMessages = IsSuccess || ErrorMessage != null;

        CanExecute = !string.IsNullOrWhiteSpace(SqlText)
                          && !IsExecuting
                          && Database.IsConnected;

        CanExecuteSelection = !IsExecuting && Database.IsConnected;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Sets the selected text from the editor.
    /// </summary>
    public void SetSelectedText(string selectedText)
    {
        m_selectedText = selectedText;
        //ExecuteSelectionCommand.RaiseCanExecuteChanged();
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.IsProperty((QueryEditorViewModel vm)=>vm.ResultDataView))
            UpdateStatus();

        if (e.IsProperty((QueryEditorViewModel vm) => vm.Result))
            UpdateStatus();

        if (e.IsProperty((QueryEditorViewModel vm) => vm.ErrorMessage))
            UpdateStatus();

        if (e.IsProperty((QueryEditorViewModel vm) => vm.IsExecuting))
            UpdateStatus();

        if (e.IsProperty((QueryEditorViewModel vm) => vm.SqlText))
            UpdateStatus();
    }

    #endregion

    #region Properties

    [Notify]
    public string SqlText { get; set; } = null!;

    [Notify]
    public string StatusText { get; set; } = null!;

    [Notify]
    public bool IsExecuting { get; set; }

    [Notify]
    public QueryResult? Result { get; set; }

    [Notify]
    public DataView? ResultDataView { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public int RowsAffected { get; set; }

    [Notify]
    public double ExecutionTimeMs { get; set; }

    [Notify]
    public bool HasResults { get; private set; }

    [Notify]
    public bool IsSuccess { get; private set; }

    [Notify]
    public bool HasMessages { get; private set; }

    [Notify]
    public bool CanExecute { get; private set; }

    [Notify]
    public bool CanExecuteSelection { get; private set; }

    #endregion

    #region Commands

    public ICommand ExecuteCommand { get; private set; } = null!;

    public ICommand ExecuteSelectionCommand { get; private set; } = null!;

    public ICommand ClearCommand { get; private set; } = null!;

    #endregion

    #region Services

    public IDatabaseService Database => ApplicationVm.Database;

    public ISettingsService Settings => ApplicationVm.Settings;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
