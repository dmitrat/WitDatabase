using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// One column in the index being built, with the direction it is sorted in.
/// </summary>
public sealed class IndexColumnViewModel : NotifyPropertyChangedBase
{
    public IndexColumnViewModel(string expression, bool isDescending = false)
    {
        Expression = expression;
        IsDescending = isDescending;
    }

    [Notify]
    public string Expression { get; set; }

    [Notify]
    public bool IsDescending { get; set; }

    public IndexColumn ToModel() => new(Expression, IsDescending);

    public override string ToString() => IsDescending ? $"{Expression} DESC" : Expression;
}

/// <summary>
/// The direction button's label: the word the column is sorted by, not an arrow nobody can read out
/// loud.
/// </summary>
public sealed class DirectionConverter : Avalonia.Data.Converters.IValueConverter
{
    public static DirectionConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "DESC" : "ASC";

    public object ConvertBack(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>
/// The index dialog (WS-43), which offers what this engine actually does - and says which of those
/// things the planner will use.
///
/// Measured 2026-08-06, over 200 rows so the ten-row threshold below which no index is considered is
/// not what is being measured:
///
/// | offered | accepted | used by the planner |
/// |---|---|---|
/// | plain | yes | yes - SEARCH TABLE ... USING INDEX |
/// | UNIQUE | yes | yes, and it enforces uniqueness |
/// | INCLUDE (covering) | yes | yes |
/// | partial, WHERE | yes | <b>no</b> - the plan is a full scan either way |
/// | DESC | yes | <b>no</b> - ORDER BY ... DESC LIMIT still sorts the whole table |
/// | by expression | yes | <b>no</b> - and the catalogue reports the column as $expr0 |
///
/// All six are offered, because all six are stored and a database is not only read by Studio. The two
/// that buy nothing today say so next to the box rather than being hidden: an option that quietly does
/// nothing is worse than one that explains itself.
/// </summary>
public class CreateIndexViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool> ShouldCloseDialog = delegate { };

    #endregion

    #region Constructors

    public CreateIndexViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefault();
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        IndexName = string.Empty;
        TableName = string.Empty;
        IsUnique = false;
        SelectedColumns = [];
        IncludedColumns = [];
        FilterCondition = string.Empty;
        AvailableTables = [];
        AvailableColumns = [];
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        SelectedColumns.CollectionChanged += OnCollectionChanged;
        IncludedColumns.CollectionChanged += OnCollectionChanged;
    }

    private void InitCommands()
    {
        LoadTablesCommand = new RelayCommandAsync(LoadTablesAsync);
        LoadColumnsCommand = new RelayCommandAsync(LoadColumnsAsync);
        GenerateDdlCommand = new RelayCommand(GenerateDdl);
        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
        CancelCommand = new RelayCommand(Cancel);

        AddColumnCommand = new RelayCommand<string>(AddColumn);
        RemoveColumnCommand = new RelayCommand<IndexColumnViewModel>(RemoveColumn);
        ToggleDirectionCommand = new RelayCommand<IndexColumnViewModel>(ToggleDirection);
        AddIncludedCommand = new RelayCommand<string>(AddIncluded);
        RemoveIncludedCommand = new RelayCommand<string>(name => IncludedColumns.Remove(name!));
        SendToEditorCommand = new RelayCommand(SendToEditor);
    }

    #endregion

    #region Functions

    private async Task LoadTablesAsync()
    {
        if (Database?.IsConnected != true)
            return;

        try
        {
            var tables = await Database!.GetTablesAsync();
            AvailableTables = tables.Select(t => t.Name).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load tables");
        }
    }

    private async Task LoadColumnsAsync()
    {
        if (Database?.IsConnected != true || string.IsNullOrWhiteSpace(TableName))
            return;

        try
        {
            var columns = await Database!.GetColumnsAsync(TableName);
            AvailableColumns = columns.Select(c => c.Name).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load columns for table {TableName}", TableName);
        }
    }

    /// <summary>
    /// Opens the dialog on one table, with its columns and the warning about its key already worked
    /// out - which is how the designer's "Create index" reaches it (WS-44).
    /// </summary>
    public async Task LoadForTableAsync(string tableName)
    {
        TableName = tableName;

        await LoadTablesAsync();
        await LoadColumnsAsync();
        await UpdateKeyNoteAsync();

        if (string.IsNullOrWhiteSpace(IndexName))
            IndexName = $"IX_{tableName}_";

        GenerateDdl();
    }

    /// <summary>
    /// The one place on the screen that talks about the key, in three states (5.6): an AUTOINCREMENT
    /// key needs no index, a hand-set key with an index is fine, a hand-set key without one degrades
    /// every insert.
    /// </summary>
    private async Task UpdateKeyNoteAsync()
    {
        if (Database?.IsConnected != true || string.IsNullOrWhiteSpace(TableName))
            return;

        try
        {
            var columns = await Database!.GetColumnsAsync(TableName);
            var indexes = await Database.GetTableIndexesAsync(TableName);

            var keys = columns.Where(c => c.IsPrimaryKey).ToList();

            if (keys.Count == 0)
            {
                KeyNote = Localization.Format("Dialog.CreateIndex.NoKey", TableName);
                KeyNoteIsSevere = false;
                return;
            }

            if (keys.All(k => k.IsAutoIncrement))
            {
                KeyNote = Localization["Dialog.CreateIndex.KeyIsGenerated"];
                KeyNoteIsSevere = false;
                return;
            }

            var covered = keys.All(key => indexes.Any(index =>
                index.Columns.FirstOrDefault()?.Equals(key.Name, StringComparison.OrdinalIgnoreCase) == true));

            KeyNote = covered
                ? Localization["Dialog.CreateIndex.KeyHasIndex"]
                : Localization.Format("Dialog.CreateIndex.KeyUnindexed",
                    TableName, string.Join(", ", keys.Select(k => k.Name)));

            KeyNoteIsSevere = !covered;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not work out the key note for {Table}", TableName);
        }
    }

    private void AddColumn(string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
            return;

        SelectedColumns.Add(new IndexColumnViewModel(column));
        GenerateDdl();
    }

    private void RemoveColumn(IndexColumnViewModel? column)
    {
        if (column == null)
            return;

        SelectedColumns.Remove(column);
        GenerateDdl();
    }

    private void ToggleDirection(IndexColumnViewModel? column)
    {
        if (column == null)
            return;

        column.IsDescending = !column.IsDescending;

        // The direction lives on an item INSIDE the collection, so neither the collection nor a
        // property of this ViewModel has changed - the note about what the planner will do with it has
        // to be asked for by hand, or it stays as it was.
        UpdateStatus();
        GenerateDdl();
    }

    private void AddIncluded(string? column)
    {
        if (string.IsNullOrWhiteSpace(column) || IncludedColumns.Contains(column))
            return;

        IncludedColumns.Add(column);
        GenerateDdl();
    }

    private void GenerateDdl()
    {
        GeneratedDdl = BuildCreateIndexSql();
    }

    /// <summary>
    /// Puts the statement in a query tab instead of running it - "В редактор" in the mock-up. The
    /// dialog is a way to write DDL, not the only way to have it.
    /// </summary>
    private void SendToEditor()
    {
        var sql = BuildCreateIndexSql();

        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(sql, $"{IndexName} - DDL", Database);

        ShouldCloseDialog(false);
    }

    private async Task CreateIndexAsync()
    {
        if (!CanCreateIndex)
            return;

        IsCreating = true;
        ErrorMessage = null;

        try
        {
            var sql = BuildCreateIndexSql();
            await Database!.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = Localization.Format("Dialog.CreateIndex.Created", IndexName);
            Logger.LogInformation("Created index: {IndexName}", IndexName);

            await Database.Catalog.RefreshAsync();
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync();

            ShouldCloseDialog(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Dialog.CreateIndex.Failed", ex.Message.Split('\n')[0]);
            ApplicationVm.MainWindowVm.StatusText = Localization["Dialog.CreateIndex.FailedShort"];
            Logger.LogError(ex, "Failed to create index {IndexName}", IndexName);
        }
        finally
        {
            IsCreating = false;
        }
    }

    private void Cancel()
    {
        ShouldCloseDialog(false);
    }

    public string BuildCreateIndexSql() => DdlWriter.CreateIndex(ToDraft());

    public IndexDraft ToDraft() => new()
    {
        Name = IndexName,
        Table = TableName,
        Columns = SelectedColumns.Select(c => c.ToModel()).ToList(),
        IsUnique = IsUnique,
        FilterCondition = string.IsNullOrWhiteSpace(FilterCondition) ? null : FilterCondition,
        IncludedColumns = IncludedColumns.ToList()
    };

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        var hasIndexName = !string.IsNullOrWhiteSpace(IndexName);
        var hasTableName = !string.IsNullOrWhiteSpace(TableName);
        var hasColumns = SelectedColumns.Count > 0;

        CanCreateIndex = hasIndexName && hasTableName && hasColumns && !IsCreating && Database?.IsConnected == true;
        CanGenerateDdl = hasIndexName && hasTableName && hasColumns;
        CanLoadColumns = hasTableName;

        // The two options the engine stores and the planner does not use. Said here rather than
        // hidden, and only when they are actually switched on.
        PlannerNote = BuildPlannerNote();
    }

    private string? BuildPlannerNote()
    {
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(FilterCondition))
            notes.Add("a partial index is stored but this planner does not use it yet");

        if (SelectedColumns.Any(c => c.IsDescending))
            notes.Add("a sort direction is stored but this planner does not read an index in order");

        if (SelectedColumns.Any(c => c.Expression.Contains('(')))
            notes.Add("an index by expression is stored, but the planner does not match it and the " +
                      "catalogue reports its column as $expr0");

        return notes.Count == 0 ? null : string.Join("; ", notes) + ".";
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((CreateIndexViewModel vm) => vm.IndexName) ||
            e.IsProperty((CreateIndexViewModel vm) => vm.TableName) ||
            e.IsProperty((CreateIndexViewModel vm) => vm.IsCreating) ||
            e.IsProperty((CreateIndexViewModel vm) => vm.IsUnique) ||
            e.IsProperty((CreateIndexViewModel vm) => vm.FilterCondition))
        {
            UpdateStatus();

            if (CanGenerateDdl)
                GenerateDdl();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateStatus();

        if (CanGenerateDdl)
            GenerateDdl();
    }

    #endregion

    #region Properties

    [Notify]
    public string IndexName { get; set; } = null!;

    [Notify]
    public string TableName { get; set; } = null!;

    [Notify]
    public bool IsUnique { get; set; }

    /// <summary>
    /// The key columns, in order, each with its direction.
    /// </summary>
    [Notify]
    public ObservableCollection<IndexColumnViewModel> SelectedColumns { get; private set; } = null!;

    /// <summary>
    /// Columns carried in the index without being part of its key - the covering index of WS-43, and
    /// the one advanced option this planner does use.
    /// </summary>
    [Notify]
    public ObservableCollection<string> IncludedColumns { get; private set; } = null!;

    [Notify]
    public string FilterCondition { get; set; } = null!;

    [Notify]
    public List<string> AvailableTables { get; set; } = null!;

    [Notify]
    public List<string> AvailableColumns { get; set; } = null!;

    [Notify]
    public string? GeneratedDdl { get; set; }

    /// <summary>
    /// What the key of this table costs, in three states (WS-44).
    /// </summary>
    [Notify]
    public string? KeyNote { get; private set; }

    [Notify]
    public bool KeyNoteIsSevere { get; private set; }

    /// <summary>
    /// What the chosen options will and will not buy from the planner.
    /// </summary>
    [Notify]
    public string? PlannerNote { get; private set; }

    [Notify]
    public bool IsCreating { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanGenerateDdl { get; private set; }

    [Notify]
    public bool CanCreateIndex { get; private set; }

    [Notify]
    public bool CanLoadColumns { get; private set; }

    #endregion

    #region Commands

    public ICommand LoadTablesCommand { get; private set; } = null!;

    public ICommand LoadColumnsCommand { get; private set; } = null!;

    public ICommand GenerateDdlCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand AddColumnCommand { get; private set; } = null!;

    public ICommand RemoveColumnCommand { get; private set; } = null!;

    public ICommand ToggleDirectionCommand { get; private set; } = null!;

    public ICommand AddIncludedCommand { get; private set; } = null!;

    public ICommand RemoveIncludedCommand { get; private set; } = null!;

    public ICommand SendToEditorCommand { get; private set; } = null!;

    #endregion

    #region Services

    /// <summary>
    /// The active connection - the one selected in the tree. These dialogs act on what the user is
    /// looking at; an open tab does not (WS-3). Null when nothing is open, which every caller here
    /// already had to handle as "not connected".
    /// </summary>
    public IDatabaseSession? Database => ApplicationVm.ActiveSession;

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
