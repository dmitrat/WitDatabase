using System.Collections.ObjectModel;
using System.Windows.Input;
// Avalonia 12 moved SetTextAsync off IClipboard and onto ClipboardExtensions.
using Avalonia.Input.Platform;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// The right-hand panel the frame reserved in stage 4 (WS-12), filled in here (WS-18).
///
/// It follows the selection in the tree and answers "what is this" without opening a tab. Not a
/// properties dialog: a dialog is modal, and comparing two objects one after the other is something
/// people do constantly.
///
/// The part that makes it more than a listing is <see cref="DataAccess"/>. This engine does NOT
/// create an index for a PRIMARY KEY, so a table can have a key and no index on it - and an insert
/// with an explicit key then degrades quadratically. The catalogue knows; the inspector says so
/// before anyone notices the slowdown.
/// </summary>
public class ObjectInspectorViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private CancellationTokenSource? m_loadCts;

    #endregion

    #region Constructors

    public ObjectInspectorViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        Columns = [];
        Indexes = [];
        References = [];
        ReferencedBy = [];
        DataAccess = [];

        InitCommands();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(() => LoadAsync(ApplicationVm.DatabaseExplorerVm.SelectedNode));
        CopyDefinitionCommand = new RelayCommandAsync(CopyDefinitionAsync);
        OpenDefinitionInEditorCommand = new RelayCommand(OpenDefinitionInEditor);
    }

    private void InitEvents()
    {
        ApplicationVm.DatabaseExplorerVm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(DatabaseExplorerViewModel.SelectedNode))
                return;

            await LoadAsync(ApplicationVm.DatabaseExplorerVm.SelectedNode);
        };
    }

    #endregion

    #region Functions

    /// <summary>
    /// Describes whatever is selected. Each call cancels the one before it: moving down a tree with
    /// the arrow keys must not queue a schema read per row.
    /// </summary>
    public async Task LoadAsync(DatabaseNode? node)
    {
        m_loadCts?.Cancel();
        m_loadCts?.Dispose();
        m_loadCts = new CancellationTokenSource();

        var ct = m_loadCts.Token;

        Clear();

        Node = node;

        if (node == null)
            return;

        var session = ApplicationVm.DatabaseExplorerVm.SessionFor(node);

        if (session?.IsConnected != true)
        {
            Summary = "The connection this object belongs to is closed.";
            return;
        }

        Title = node.Name;
        Subtitle = $"{session.DisplayName} - {Describe(node.NodeType)}";
        IsLoading = true;

        try
        {
            switch (node.NodeType)
            {
                case DatabaseNodeType.Table:
                    await LoadTableAsync(session, node.Name, ct);
                    break;

                case DatabaseNodeType.View:
                    await LoadViewAsync(session, node.Name, ct);
                    break;

                case DatabaseNodeType.Column:
                    await LoadColumnAsync(session, node, ct);
                    break;

                case DatabaseNodeType.Index:
                    Definition = await session.GetIndexDefinitionAsync(node.Name, ct);
                    Summary = Definition == null ? "No definition recorded for this index." : null;
                    break;

                case DatabaseNodeType.Trigger:
                    Definition = await session.GetTriggerDefinitionAsync(node.Name, ct);
                    Summary = Definition == null ? "No definition recorded for this trigger." : null;
                    break;

                case DatabaseNodeType.Routine:
                    await LoadRoutineAsync(session, node.Name, ct);
                    break;

                default:
                    Summary = "Select a table, a view, an index, a trigger, a routine or a column.";
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The selection moved on; whatever this was describing is no longer wanted.
        }
        catch (Exception ex)
        {
            Summary = $"Could not describe this object: {ex.Message}";
            Logger.LogError(ex, "Inspector failed on {Node}", node.Name);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadTableAsync(IDatabaseSession session, string table, CancellationToken ct)
    {
        var columns = await session.GetColumnsAsync(table, ct);
        var indexes = await session.GetTableIndexesAsync(table, ct);
        var outgoing = await session.GetForeignKeysAsync(table, ct);
        var incoming = await session.GetReferencingKeysAsync(table, ct);

        foreach (var column in columns)
            Columns.Add(column);

        foreach (var index in indexes)
            Indexes.Add(index);

        foreach (var key in outgoing)
            References.Add(key);

        foreach (var key in incoming)
            ReferencedBy.Add(key);

        BuildDataAccess(columns, indexes);

        var keyColumns = columns.Where(column => column.IsPrimaryKey).Select(column => column.Name).ToList();

        PrimaryKey = keyColumns.Count == 0 ? "none" : string.Join(", ", keyColumns);
        ColumnCount = columns.Count;

        // The count is asked for with the same deadline the tree uses: the inspector is not the place
        // to discover that a table is too big to count.
        RowCount = await session.TryCountRowsAsync(table, ApplicationVm.DatabaseExplorerVm.CountTimeout, ct);

        Definition = await session.GetTableDefinitionAsync(table, ct);

        if (Definition == null)
        {
            Summary = "The definition is not available: this database was written by an engine older "
                + "than format 9.0.0, which did not keep the text of a definition. Showing an "
                + "approximation would be worse than showing nothing.";
        }
    }

    private async Task LoadViewAsync(IDatabaseSession session, string view, CancellationToken ct)
    {
        var columns = await session.GetColumnsAsync(view, ct);

        foreach (var column in columns)
            Columns.Add(column);

        ColumnCount = columns.Count;

        Definition = await session.GetViewDefinitionAsync(view, ct);

        if (Definition == null)
        {
            Summary = "The definition of this view is not available: the database was written before "
                + "format 9.0.0, so the catalogue does not hold the parsed query.";
        }
    }

    private async Task LoadColumnAsync(IDatabaseSession session, DatabaseNode node, CancellationToken ct)
    {
        if (node.ParentName == null)
            return;

        var columns = await session.GetColumnsAsync(node.ParentName, ct);
        var column = columns.FirstOrDefault(candidate => candidate.Name == node.Name);

        if (column == null)
            return;

        Columns.Add(column);

        var indexes = await session.GetTableIndexesAsync(node.ParentName, ct);

        foreach (var index in indexes.Where(index => index.Columns.Contains(column.Name, StringComparer.OrdinalIgnoreCase)))
            Indexes.Add(index);

        BuildDataAccess([column], indexes);

        Summary = $"{column.DataType}{(column.IsNullable ? "" : " NOT NULL")}"
            + (column.IsPrimaryKey ? ", primary key" : string.Empty);
    }

    private async Task LoadRoutineAsync(IDatabaseSession session, string name, CancellationToken ct)
    {
        var routines = await session.GetRoutinesAsync(ct);
        var routine = routines.FirstOrDefault(candidate => candidate.Name == name);

        if (routine == null)
            return;

        Definition = routine.Definition;
        Summary = routine.IsFunction
            ? $"Function returning {routine.DataType}"
            : "Procedure";
    }

    /// <summary>
    /// Which columns can be reached through an index and which cannot.
    ///
    /// The primary key is called out separately, because on this engine having one does not mean
    /// there is an index for it - and that is the case that turns an ordinary insert quadratic.
    /// </summary>
    private void BuildDataAccess(IReadOnlyList<ColumnInfo> columns, IReadOnlyList<IndexInfo> indexes)
    {
        foreach (var column in columns)
        {
            var index = indexes.FirstOrDefault(candidate =>
                candidate.Columns.Count > 0
                && string.Equals(candidate.Columns[0], column.Name, StringComparison.OrdinalIgnoreCase));

            DataAccess.Add(new DataAccessNote
            {
                Column = column.Name,
                IsIndexed = index != null,
                IndexName = index?.Name,
                IsPrimaryKey = column.IsPrimaryKey
            });
        }

        UnindexedKeyWarning = DataAccess.Any(note => note.IsPrimaryKey && !note.IsIndexed)
            ? "The primary key has no index of its own. This engine does not create one, and inserting "
              + "rows with explicit keys degrades sharply as the table grows."
            : null;
    }

    private void Clear()
    {
        Columns.Clear();
        Indexes.Clear();
        References.Clear();
        ReferencedBy.Clear();
        DataAccess.Clear();

        Title = null;
        Subtitle = null;
        Summary = null;
        Definition = null;
        PrimaryKey = null;
        RowCount = null;
        ColumnCount = 0;
        UnindexedKeyWarning = null;
    }

    private static string Describe(DatabaseNodeType type) => type switch
    {
        DatabaseNodeType.Table => "table",
        DatabaseNodeType.View => "view",
        DatabaseNodeType.Index => "index",
        DatabaseNodeType.Trigger => "trigger",
        DatabaseNodeType.Sequence => "sequence",
        DatabaseNodeType.Routine => "routine",
        DatabaseNodeType.Column => "column",
        DatabaseNodeType.Database => "connection",
        _ => "object"
    };

    private async Task CopyDefinitionAsync()
    {
        if (string.IsNullOrEmpty(Definition))
            return;

        var window = ApplicationVm.MainWindow;

        if (window == null)
            return;

        var clipboard = Avalonia.Controls.TopLevel.GetTopLevel(window)?.Clipboard;

        if (clipboard != null)
            await clipboard.SetTextAsync(Definition);
    }

    /// <summary>
    /// Puts the definition into a query tab of the object's OWN connection (WS-3).
    /// </summary>
    private void OpenDefinitionInEditor()
    {
        if (string.IsNullOrEmpty(Definition) || Node == null)
            return;

        var session = ApplicationVm.DatabaseExplorerVm.SessionFor(Node);

        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(Definition, $"{Node.Name} - DDL", session);
    }

    #endregion

    #region Properties

    /// <summary>
    /// What is being described.
    /// </summary>
    [Notify]
    public DatabaseNode? Node { get; private set; }

    [Notify]
    public string? Title { get; private set; }

    /// <summary>
    /// The connection and the kind of object - with several databases open, the name alone is not
    /// enough to say what this is.
    /// </summary>
    [Notify]
    public string? Subtitle { get; private set; }

    /// <summary>
    /// One line about the object, or the reason there is nothing else to show.
    /// </summary>
    [Notify]
    public string? Summary { get; private set; }

    [Notify]
    public bool IsLoading { get; private set; }

    [Notify]
    public long? RowCount { get; private set; }

    [Notify]
    public int ColumnCount { get; private set; }

    [Notify]
    public string? PrimaryKey { get; private set; }

    /// <summary>
    /// What is actually recorded in the catalogue, not a reconstruction from the columns.
    /// </summary>
    [Notify]
    public string? Definition { get; private set; }

    /// <summary>
    /// Said out loud when a table's key has no index behind it.
    /// </summary>
    [Notify]
    public string? UnindexedKeyWarning { get; private set; }

    public ObservableCollection<ColumnInfo> Columns { get; }

    public ObservableCollection<IndexInfo> Indexes { get; }

    /// <summary>
    /// What this table points at.
    /// </summary>
    public ObservableCollection<ForeignKeyInfo> References { get; }

    /// <summary>
    /// What points at this table - what would break if it went.
    /// </summary>
    public ObservableCollection<ForeignKeyInfo> ReferencedBy { get; }

    /// <summary>
    /// Which columns have an index and which do not (2.5).
    /// </summary>
    public ObservableCollection<DataAccessNote> DataAccess { get; }

    public bool HasDefinition => !string.IsNullOrEmpty(Definition);

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand CopyDefinitionCommand { get; private set; } = null!;

    public ICommand OpenDefinitionInEditorCommand { get; private set; } = null!;

    #endregion

    #region Services

    public ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
