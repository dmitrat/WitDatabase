using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Windows.Input;
using Avalonia.Controls;
// Avalonia 12 moved SetTextAsync off IClipboard and onto ClipboardExtensions.
using Avalonia.Input.Platform;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Ui.Icons;

namespace OutWit.Database.Studio.ViewModels.Tabs;

/// <summary>
/// Which part of the structure tab is on screen. A strip along the top rather than a tree or an
/// accordion: people move between these constantly and the order must never change (5.1).
/// </summary>
public enum StructureSection
{
    Columns,
    Keys,
    Indexes,
    Triggers,
    Ddl
}

/// <summary>
/// One row of the "keys and constraints" section, as the catalogue publishes it.
/// </summary>
public sealed record ConstraintRow(string Name, string Type, string Columns, string? Detail);

/// <summary>
/// One row of the 5.2 matrix, with its words already in the reader's language.
/// </summary>
public sealed record SchemaCapabilityRow(string Change, string Marker, string Reason);

/// <summary>
/// Turns the selected section into "is this the one" for the strip and for the panels beneath it.
///
/// Two-way, because the strip's buttons set it: converting back returns the section named by the
/// parameter when the button is checked, and refuses to answer when it is not - an unchecked radio
/// button must not decide what is on screen.
/// </summary>
public sealed class StructureSectionConverter : Avalonia.Data.Converters.IValueConverter
{
    public static StructureSectionConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is StructureSection section &&
               parameter is string name &&
               Enum.TryParse<StructureSection>(name, out var expected) &&
               section == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is true && parameter is string name && Enum.TryParse<StructureSection>(name, out var section))
            return section;

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}

/// <summary>
/// The structure tab, which since stage 8 is the schema designer (section 5).
///
/// Two things run through all of it.
///
/// <b>The DDL is on screen the whole time (WS-38).</b> A designer is a generator of text that the user
/// has to be able to read: it is the only place where "the button understood me" can be checked, and
/// it is what the user would have written by hand. So the DDL section is not behind a button and the
/// pending edits appear in it as they are made.
///
/// <b>Every edit says how it will be carried out, in the row, before Apply (WS-39).</b> The three
/// categories are not a Studio invention - they are what this engine's ALTER TABLE does and does not
/// do, measured rather than assumed, and <see cref="SchemaCapabilities"/> holds the matrix.
/// </summary>
public class StructureTabViewModel : WorkspaceTabViewModel
{
    #region Constructors

    public StructureTabViewModel(ApplicationViewModel applicationVm, IDatabaseSession session,
        string objectName, DatabaseNodeType objectType)
        : base(applicationVm, session)
    {
        ObjectName = objectName;
        ObjectType = objectType;
        Title = Localization.Format("Tab.StructureOf", objectName);

        InitDefault();
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Columns = [];
        Constraints = [];
        Indexes = [];
        Triggers = [];
        Refusals = [];
        SelectedSection = StructureSection.Columns;
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;
        Columns.CollectionChanged += (_, _) => Recompute();

        // The markers and their reasons are TEXT this ViewModel builds, so they do not follow a
        // language change on their own the way a DynamicResource does. Without this a tab left open
        // across a switch keeps saying "rebuild" over a Russian interface until the next edit.
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Recompute();

        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(NotInTheEngine));
    }

    /// <summary>
    /// A closed tab stops listening. The service outlives every tab, so a subscription left behind
    /// would keep a closed tab's whole graph alive and recompute it on every language change.
    /// </summary>
    public override void OnClosed()
    {
        Localization.LanguageChanged -= OnLanguageChanged;

        base.OnClosed();
    }

    private void InitCommands()
    {
        RefreshCommand = new RelayCommandAsync(LoadStructureAsync);

        AddColumnCommand = new RelayCommand(AddColumn);
        DeleteColumnCommand = new RelayCommand<ColumnDraft>(DeleteColumn);
        RestoreColumnCommand = new RelayCommand<ColumnDraft>(RestoreColumn);

        ApplyCommand = new RelayCommandAsync(ApplyAsync);
        RevertCommand = new RelayCommand(Revert);
        RebuildCommand = new RelayCommandAsync(RebuildAsync);

        CreateIndexCommand = new RelayCommandAsync(CreateIndexAsync);
        DropIndexCommand = new RelayCommandAsync<IndexInfo>(DropIndexAsync);
        RecreateIndexCommand = new RelayCommandAsync<IndexInfo>(RecreateIndexAsync);

        DropTriggerCommand = new RelayCommandAsync<TriggerInfo>(DropTriggerAsync);
        EditTriggerCommand = new RelayCommandAsync<TriggerInfo>(EditTriggerAsync);
        CreateTriggerCommand = new RelayCommandAsync(() => EditTriggerAsync(null));

        CopyDdlCommand = new RelayCommandAsync(CopyDdlAsync);
    }

    #endregion

    #region WorkspaceTabViewModel

    public override WorkspaceTabType TabType => WorkspaceTabType.Structure;

    public override string IconPath => ObjectType switch
    {
        DatabaseNodeType.Table => StudioIcons.PATH_DB_TABLE,
        DatabaseNodeType.View => StudioIcons.PATH_DB_VIEW,
        DatabaseNodeType.Index => StudioIcons.PATH_DB_INDEX,
        _ => StudioIcons.PATH_DB_TABLE
    };

    /// <summary>
    /// Keyed by connection as well as by object, for the same reason the editor is: two databases can
    /// both have a table called Orders.
    /// </summary>
    public override string? UniqueId => $"structure:{Session?.Id}:{ObjectType}:{ObjectName}";

    #endregion

    #region Loading

    /// <summary>
    /// Loads the structure of the object.
    /// </summary>
    public async Task LoadStructureAsync()
    {
        // This object belongs to one connection: the one the tab was opened in (WS-3).
        var session = Session;

        if (session?.IsConnected != true)
            return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            switch (ObjectType)
            {
                case DatabaseNodeType.Table:
                    await LoadTableAsync(session);
                    break;

                case DatabaseNodeType.View:
                    await LoadViewAsync(session);
                    break;

                case DatabaseNodeType.Index:
                    await LoadIndexAsync(session);
                    break;

                default:
                    ErrorMessage = Localization["Structure.Unsupported"];
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = Localization.Format("Structure.LoadFailed", ex.Message);
            Logger.LogError(ex, "Failed to load structure for {Type} {Name}", ObjectType, ObjectName);
        }
        finally
        {
            IsLoading = false;
            UpdateStatus();
        }
    }

    private async Task LoadTableAsync(IDatabaseSession session)
    {
        var columns = await session.GetColumnsAsync(ObjectName);
        var foreignKeys = await session.GetForeignKeysAsync(ObjectName);

        m_suppressRecompute = true;

        Columns.Clear();

        foreach (var column in columns)
        {
            var draft = new ColumnDraft(column);

            var fk = foreignKeys.FirstOrDefault(f =>
                string.Equals(f.FromColumn, column.Name, StringComparison.OrdinalIgnoreCase));

            if (fk != null)
            {
                draft.ReferencesTable = fk.ToTable;
                draft.ReferencesColumn = fk.ToColumn;
            }

            draft.PropertyChanged += OnDraftChanged;
            Columns.Add(draft);
        }

        m_suppressRecompute = false;

        Indexes = (await session.GetTableIndexesAsync(ObjectName)).ToList();
        Triggers = (await session.GetTableTriggersAsync(ObjectName)).ToList();
        Constraints = await ReadConstraintsAsync(session, foreignKeys);

        TableDdl = await session.GetTableDefinitionAsync(ObjectName) ?? string.Empty;
        HasRows = await session.HasAnyRowsAsync(ObjectName);

        ColumnCount = Columns.Count(c => !c.IsDeleted);

        UpdateKeyWarning();
        Recompute();

        ApplicationVm.MainWindowVm.StatusText = Localization.Format("Structure.Loaded",
            Localization.Plural("Count.Columns", columns.Count), ObjectName);
        Logger.LogInformation("Loaded structure for table {Name}: {Count} columns", ObjectName, columns.Count);
    }

    /// <summary>
    /// The keys, uniques, checks and foreign keys, from TABLE_CONSTRAINTS and KEY_COLUMN_USAGE. The
    /// CHECK expression itself is not in TABLE_CONSTRAINTS - there is no CHECK_CONSTRAINTS view here -
    /// so a column check is read from the column, which is where the catalogue keeps it.
    /// </summary>
    private async Task<List<ConstraintRow>> ReadConstraintsAsync(
        IDatabaseSession session, IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        var rows = new List<ConstraintRow>();

        try
        {
            var result = await session.ExecuteQueryAsync(
                "SELECT tc.CONSTRAINT_NAME, tc.CONSTRAINT_TYPE, kcu.COLUMN_NAME " +
                "FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " +
                "LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu " +
                "ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND kcu.TABLE_NAME = tc.TABLE_NAME " +
                $"WHERE tc.TABLE_NAME = '{ObjectName.Replace("'", "''")}'");

            if (string.IsNullOrEmpty(result.ErrorMessage) && result.Data != null)
            {
                var grouped = new Dictionary<string, (string Type, List<string> Columns)>();

                foreach (DataRow row in result.Data.Rows)
                {
                    var name = row[0] as string ?? string.Empty;
                    var type = row[1] as string ?? string.Empty;
                    var column = row[2] as string;

                    if (!grouped.TryGetValue(name, out var entry))
                        grouped[name] = entry = (type, []);

                    if (!string.IsNullOrEmpty(column))
                        entry.Columns.Add(column);
                }

                foreach (var (name, entry) in grouped)
                {
                    var detail = entry.Type.Equals("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
                        ? ForeignKeyDetail(name, foreignKeys)
                        : null;

                    rows.Add(new ConstraintRow(name, entry.Type, string.Join(", ", entry.Columns), detail));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Unable to read the constraints of {Table}", ObjectName);
        }

        // A column CHECK does not appear in TABLE_CONSTRAINTS with its expression, so it is shown from
        // the column - the same fact, from the place that has it.
        foreach (var column in Columns.Where(c => !string.IsNullOrWhiteSpace(c.CheckExpression)))
        {
            var name = DdlWriter.CheckName(ObjectName, column.Name);

            if (rows.All(r => !string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                rows.Add(new ConstraintRow(name, "CHECK", column.Name, column.CheckExpression));
        }

        return rows;
    }

    private static string? ForeignKeyDetail(string name, IReadOnlyList<ForeignKeyInfo> foreignKeys)
    {
        var fk = foreignKeys.FirstOrDefault();

        return fk == null ? null : $"-> {fk.ToTable}({fk.ToColumn})";
    }

    private async Task LoadViewAsync(IDatabaseSession session)
    {
        var columns = await session.GetColumnsAsync(ObjectName);

        m_suppressRecompute = true;
        Columns.Clear();

        foreach (var column in columns)
            Columns.Add(new ColumnDraft(column));

        m_suppressRecompute = false;

        // The BODY, not the CREATE VIEW: this is the text the editor lets a person rewrite, and it is
        // put back inside a CREATE VIEW by DdlWriter when the change is applied.
        m_suppressRecompute = true;
        ViewDefinition = await session.GetViewBodyAsync(ObjectName);
        m_loadedViewDefinition = ViewDefinition;
        m_suppressRecompute = false;

        // A view whose body the catalogue cannot render comes back NULL - measured for a UNION and for
        // a subquery. Editing means DROP and CREATE, and creating from a body Studio does not have
        // would destroy the view. So it is shown as unreadable rather than as empty.
        CanEditView = !string.IsNullOrWhiteSpace(ViewDefinition);

        // The note beneath the DDL is about a VIEW whose body the catalogue could not return. It used
        // to be shown on the negation of CanEditView alone, which is false for every table - so every
        // table's DDL section carried a paragraph about UNION and subqueries. Measured on AspNetRoles.
        ShowsViewNote = !CanEditView;

        SelectedSection = StructureSection.Ddl;

        ApplicationVm.MainWindowVm.StatusText = Localization.Format("Structure.Loaded",
            Localization.Plural("Count.Columns", columns.Count), ObjectName);
    }

    private async Task LoadIndexAsync(IDatabaseSession session)
    {
        var definition = await session.GetIndexDefinitionAsync(ObjectName);

        TableDdl = definition ?? string.Empty;
        SelectedSection = StructureSection.Ddl;

        var result = await session.ExecuteQueryAsync(
            "SELECT TABLE_NAME, COLUMN_NAME, IS_UNIQUE, FILTER_CONDITION FROM INFORMATION_SCHEMA.INDEXES " +
            $"WHERE INDEX_NAME = '{ObjectName.Replace("'", "''")}' ORDER BY ORDINAL_POSITION");

        m_suppressRecompute = true;
        Columns.Clear();

        if (string.IsNullOrEmpty(result.ErrorMessage) && result.Data != null)
        {
            foreach (DataRow row in result.Data.Rows)
            {
                IndexTableName ??= row[0] as string;
                IndexIsUnique ??= (row[2] as string)?.Equals("YES", StringComparison.OrdinalIgnoreCase);
                IndexFilterCondition ??= row[3] as string;

                Columns.Add(new ColumnDraft
                {
                    Name = row[1] as string ?? string.Empty,
                    DataType = string.Empty
                });
            }
        }

        m_suppressRecompute = false;

        ApplicationVm.MainWindowVm.StatusText = Localization.Format("Structure.Loaded",
            Localization.Plural("Count.Columns", Columns.Count), ObjectName);
    }

    #endregion

    #region Editing

    private void AddColumn()
    {
        var draft = new ColumnDraft
        {
            Name = NextColumnName(),
            DataType = "VARCHAR",
            MaxLength = 50,
            IsNullable = true
        };

        draft.PropertyChanged += OnDraftChanged;
        Columns.Add(draft);

        SelectedColumn = draft;
    }

    private string NextColumnName()
    {
        var index = 1;

        while (Columns.Any(c => string.Equals(c.Name, $"Column{index}", StringComparison.OrdinalIgnoreCase)))
            index++;

        return $"Column{index}";
    }

    private void DeleteColumn(ColumnDraft? draft)
    {
        if (draft == null)
            return;

        // A column that was only ever a draft leaves without ceremony; one that exists in the database
        // is marked, because the row has to keep showing what will happen to it.
        if (draft.IsNew)
        {
            draft.PropertyChanged -= OnDraftChanged;
            Columns.Remove(draft);
        }
        else
        {
            draft.IsDeleted = true;
        }

        Recompute();
    }

    private void RestoreColumn(ColumnDraft? draft)
    {
        if (draft == null)
            return;

        draft.IsDeleted = false;
        Recompute();
    }

    private void Revert()
    {
        ApplyReport = null;
        _ = LoadStructureAsync();
    }

    /// <summary>
    /// Works out the pending change set from the drafts, marks each row with its category, and puts
    /// the DDL on screen. Called after every keystroke that could change an answer - the panel is not
    /// allowed to be behind the grid.
    /// </summary>
    private void Recompute()
    {
        if (m_suppressRecompute)
            return;

        if (ObjectType == DatabaseNodeType.View)
        {
            RecomputeViewBody();
            return;
        }

        if (ObjectType != DatabaseNodeType.Table)
            return;

        Pending = SchemaChangeSet.Build(ObjectName, Columns.ToList(), Indexes, HasRows, out var refusals);
        Refusals = refusals.ToList();

        foreach (var draft in Columns)
        {
            draft.Marker = null;
            draft.MarkerCategory = null;
            draft.MarkerReason = null;
        }

        foreach (var edit in Pending.Edits)
        {
            var draft = Columns.FirstOrDefault(c =>
                string.Equals(c.Original?.Name ?? c.Name, edit.Column, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.Name, edit.Column, StringComparison.OrdinalIgnoreCase));

            if (draft == null)
                continue;

            // A row can carry more than one edit; the heaviest category is the one that decides what
            // Apply will do, so it is the one shown.
            if (draft.MarkerCategory == null || edit.Category > draft.MarkerCategory)
            {
                draft.MarkerCategory = edit.Category;
                draft.Marker = Localization[SchemaCapabilities.MarkerOf(edit.Category)];
                draft.MarkerReason = Localization[SchemaCapabilities.ReasonOf(edit.Kind)];
            }
        }

        PendingSql = Pending.Sql;
        PendingCount = Pending.Count;
        NeedsRebuild = Pending.NeedsRebuild;
        ColumnCount = Columns.Count(c => !c.IsDeleted);

        UpdateStatus();
    }

    /// <summary>
    /// The same for a VIEW, whose whole structure is one piece of text: a body that differs from the
    /// one that was loaded is one <see cref="SchemaEditKind.ReplaceViewBody"/> edit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>That kind had never been constructed by anything.</b> It existed, it had a category and a
    /// sentence in the catalogue describing how it would be carried out, and no code path produced one
    /// - <c>CreateViewViewModel</c> executes its own SQL and can only CREATE - so a view's body could
    /// not be edited from the interface at all. Found while fixing issue 12 and left open as a product
    /// decision; the decision was to give the editor the replace.
    /// </para>
    /// <para>
    /// It is a DROP and a CREATE, which is not atomic and is why the panel says so. The set goes
    /// through the same <c>ApplyAsync</c> as everything else, and it is the first change set made
    /// entirely of <c>DropCreate</c> - the shape that was a silent no-op until 2026-08-09 and would
    /// have left Apply grey until <see cref="SchemaChangeSet.HasSomethingToRun"/>.
    /// </para>
    /// </remarks>
    private void RecomputeViewBody()
    {
        Pending = new SchemaChangeSet(ObjectName);
        Refusals = [];

        var body = ViewDefinition?.Trim() ?? string.Empty;
        var loaded = m_loadedViewDefinition?.Trim() ?? string.Empty;

        if (CanEditView && body.Length > 0 && !string.Equals(body, loaded, StringComparison.Ordinal))
        {
            Pending.Add(new SchemaEdit
            {
                Kind = SchemaEditKind.ReplaceViewBody,
                Table = ObjectName,
                Description = Localization.Format("Structure.ReplaceViewBody", ObjectName),
                Statements =
                [
                    DdlWriter.DropView(ObjectName),
                    DdlWriter.CreateView(ObjectName, body)
                ]
            });
        }

        PendingSql = Pending.Sql;
        PendingCount = Pending.Count;
        NeedsRebuild = false;

        UpdateStatus();
    }

    // CategoryOfMarker used to live here: it read the row's marker WORD back to find out which
    // category the row was already in. That is a comparison against English, so the first Russian
    // marker would have matched none of its cases and every row would have answered "in place" -
    // silently letting a lighter edit overwrite a heavier one's marker. The row carries the category
    // itself now.

    #endregion

    #region Applying

    /// <summary>
    /// Runs the in-place edits and reports (WS-42). When the set also holds something that needs the
    /// table rebuilt, the in-place part is applied first and the rebuild is offered - which is 5.7's
    /// rule that a refusal becomes the next step rather than a dead end.
    /// </summary>
    private async Task ApplyAsync()
    {
        var session = Session;

        if (session?.IsConnected != true || Pending == null || Pending.IsEmpty)
            return;

        if (Refusals.Count > 0)
        {
            ErrorMessage = Refusals[0];
            return;
        }

        IsApplying = true;
        ErrorMessage = null;

        try
        {
            ApplyReport = await Pending.ApplyAsync(session, Logger);

            if (ApplyReport.IsComplete)
            {
                ApplicationVm.MainWindowVm.StatusText = $"{ObjectName}: {ApplyReport.Summary}";
                ApplicationVm.Notifications.Information($"{ObjectName}: {ApplyReport.Summary}");
            }
            else
            {
                ErrorMessage = ApplyReport.ErrorMessage;
                ApplicationVm.MainWindowVm.StatusText = $"{ObjectName}: {ApplyReport.Summary}";
            }

            await RefreshEverythingAsync(session);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Logger.LogError(ex, "Failed to apply schema changes to {Table}", ObjectName);
        }
        finally
        {
            IsApplying = false;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Opens the rebuild conversation for the pending shape (5.3). The plan is worked out here and
    /// shown before anything runs.
    /// </summary>
    private async Task RebuildAsync()
    {
        var session = Session;

        if (session?.IsConnected != true)
            return;

        try
        {
            IsApplying = true;

            var plan = await TableRebuild.PlanAsync(session, ObjectName, Columns.ToList());
            var rebuildVm = new TableRebuildViewModel(ApplicationVm, session, plan);

            var done = await Dialogs.ShowTableRebuildAsync(rebuildVm);

            if (done)
                await RefreshEverythingAsync(session);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Logger.LogError(ex, "Failed to plan the rebuild of {Table}", ObjectName);
        }
        finally
        {
            IsApplying = false;
            UpdateStatus();
        }
    }

    #endregion

    #region Indexes and triggers

    private async Task CreateIndexAsync()
    {
        var session = Session;

        if (session?.IsConnected != true)
            return;

        var vm = new CreateIndexViewModel(ApplicationVm) { TableName = ObjectName };

        await vm.LoadForTableAsync(ObjectName);

        if (await Dialogs.ShowCreateIndexAsync(vm))
            await LoadStructureAsync();
    }

    private async Task DropIndexAsync(IndexInfo? index)
    {
        if (index == null || Session?.IsConnected != true)
            return;

        await RunDdlAsync(DdlWriter.DropIndex(index.Name), $"Dropped index {index.Name}");
    }

    /// <summary>
    /// "Rebuild an index" from stage 5's deferred list. There is no REINDEX and no ALTER INDEX on this
    /// engine, so it is a drop and a create - and it is named that way in the menu, because a button
    /// called Rebuild that silently does something else is the thing section 5 is against.
    /// </summary>
    private async Task RecreateIndexAsync(IndexInfo? index)
    {
        if (index == null || Session?.IsConnected != true)
            return;

        var draft = new IndexDraft
        {
            Name = index.Name,
            Table = index.TableName,
            Columns = index.Columns.Select(c => new IndexColumn(c)).ToList(),
            IsUnique = index.IsUnique,
            FilterCondition = index.FilterCondition
        };

        var set = new SchemaChangeSet(ObjectName);

        set.Add(new SchemaEdit
        {
            Kind = SchemaEditKind.DropConstraint,
            Table = ObjectName,
            Description = Localization.Format("Structure.RecreateIndexPlan", index.Name),
            Statements = [DdlWriter.DropIndex(index.Name), DdlWriter.CreateIndex(draft)]
        });

        ApplyReport = await set.ApplyAsync(Session!, Logger);

        if (!ApplyReport.IsComplete)
            ErrorMessage = ApplyReport.ErrorMessage;

        await LoadStructureAsync();
    }

    private async Task DropTriggerAsync(TriggerInfo? trigger)
    {
        if (trigger == null || Session?.IsConnected != true)
            return;

        await RunDdlAsync(DdlWriter.DropTrigger(trigger.Name), $"Dropped trigger {trigger.Name}");
    }

    /// <summary>
    /// Opens the trigger editor on an existing trigger, or on a new one when
    /// <paramref name="trigger"/> is null.
    /// </summary>
    /// <remarks>
    /// <b>Nothing opened that dialog until 2026-08-09.</b> `EditTriggerViewModel` was built in stage 8,
    /// has a window and six cases driving it, and no command anywhere in the application called
    /// `ShowEditTriggerAsync` - found by looking for it in the running Studio while checking that the
    /// new UPDATE OF field appeared. A dialog nothing opens is decoration, which is the same rule the
    /// notification service was wired up under in stage 4.
    /// </remarks>
    private async Task EditTriggerAsync(TriggerInfo? trigger)
    {
        var session = Session;

        if (session?.IsConnected != true)
            return;

        var vm = new EditTriggerViewModel(ApplicationVm, session, ObjectName, trigger);

        if (await Dialogs.ShowEditTriggerAsync(vm))
            await LoadStructureAsync();
    }

    private async Task RunDdlAsync(string sql, string success)
    {
        var session = Session;

        if (session?.IsConnected != true)
            return;

        try
        {
            await session.ExecuteNonQueryAsync(sql);

            ApplicationVm.MainWindowVm.StatusText = success;

            await RefreshEverythingAsync(session);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message.Split('\n')[0];
            Logger.LogError(ex, "Failed to run {Sql}", sql);
        }
    }

    /// <summary>
    /// Everything that was looking at this table has to be told, not just this tab.
    ///
    /// Found in the running application: after a column was added and after a rebuild, the object
    /// inspector still said "Columns 4" and still showed the old CREATE TABLE. It is bound to the
    /// tree's selection and nothing had told it that the object underneath had changed.
    /// </summary>
    private async Task RefreshEverythingAsync(IDatabaseSession session)
    {
        await session.Catalog.RefreshAsync();
        await ApplicationVm.DatabaseExplorerVm.RefreshAsync(session);

        await LoadStructureAsync();

        var selected = ApplicationVm.DatabaseExplorerVm.SelectedNode;

        if (selected != null && string.Equals(selected.Name, ObjectName, StringComparison.Ordinal))
            await ApplicationVm.InspectorVm.LoadAsync(selected);
    }

    private async Task CopyDdlAsync()
    {
        var mainWindow = ApplicationVm.MainWindow;

        if (mainWindow == null)
            return;

        var clipboard = TopLevel.GetTopLevel(mainWindow)?.Clipboard;

        if (clipboard != null)
            await clipboard.SetTextAsync(FullDdl);
    }

    #endregion

    #region Tools

    private void UpdateStatus()
    {
        CanRefresh = !IsLoading && Session?.IsConnected == true;
        // APPLICABLE, not InPlace. The gate asked for an in-place edit until 2026-08-09, which is the
        // same blind spot that made ApplyAsync a silent no-op one layer down: a change set made only
        // of DropCreate edits - a trigger replacement is one - has statements to run and would have
        // left the Apply button grey in front of them. Latent rather than broken, because the designer
        // produces no such edit today and the two dialogs call ApplyAsync directly; the two questions
        // now have one answer, so a category added later reaches the button and the executor together.
        CanApply = !IsApplying && Session?.IsConnected == true && PendingCount > 0 &&
                   Pending?.HasSomethingToRun == true && Refusals.Count == 0;
        CanRebuild = !IsApplying && Session?.IsConnected == true && NeedsRebuild;
        HasPending = PendingCount > 0;
        IsTable = ObjectType == DatabaseNodeType.Table;
    }

    /// <summary>
    /// WS-44, and it is a property of THIS engine rather than general advice: no index is created for a
    /// PRIMARY KEY, so a key whose values are supplied by the user - rather than by AUTOINCREMENT -
    /// makes every insert scan the table to check uniqueness.
    /// </summary>
    private void UpdateKeyWarning()
    {
        var keys = Columns.Where(c => c.IsPrimaryKey).ToList();

        if (keys.Count == 0)
        {
            KeyWarning = Localization["Structure.Key.None"];
            KeyWarningIsSevere = true;
            return;
        }

        if (keys.All(k => k.IsAutoIncrement))
        {
            KeyWarning = Localization["Structure.Key.AutoIncrement"];
            KeyWarningIsSevere = false;
            return;
        }

        var covered = keys.All(key => Indexes.Any(index =>
            index.Columns.FirstOrDefault()?.Equals(key.Name, StringComparison.OrdinalIgnoreCase) == true));

        if (covered)
        {
            KeyWarning = Localization["Structure.Key.Indexed"];
            KeyWarningIsSevere = false;
            return;
        }

        KeyWarning = Localization.Format("Structure.Key.ManualNoIndex",
            string.Join(", ", keys.Select(k => k.Name)));
        KeyWarningIsSevere = true;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((StructureTabViewModel vm) => vm.IsLoading) ||
            e.IsProperty((StructureTabViewModel vm) => vm.IsApplying))
            UpdateStatus();

        // A view's whole structure is its body, so editing it is the same event a column draft raises
        // for a table: the DDL panel must not be behind the text box.
        if (e.IsProperty((StructureTabViewModel vm) => vm.ViewDefinition))
            Recompute();

        // FullDdl is computed from these two and announces nothing of its own, so the DDL section was
        // bound to a value that arrives AFTER it - measured 2026-08-11 on a table, where the section
        // was simply empty. Both inputs are [Notify]; this is what carries their movement to the one
        // property the markup reads.
        if (e.IsProperty((StructureTabViewModel vm) => vm.TableDdl) ||
            e.IsProperty((StructureTabViewModel vm) => vm.PendingSql))
            OnPropertyChanged(nameof(FullDdl));

        // FullDdl is computed from these two and announces nothing of its own, so the DDL section was
        // bound to a value that arrives AFTER it - measured 2026-08-11 on a table, where the section
        // was simply empty. Both inputs are [Notify]; this is what carries their movement to the one
        // property the markup reads.


        // FullDdl is computed from these two and announces nothing of its own, so the DDL section was
        // bound to a value that arrives AFTER it - measured 2026-08-11 on a table, where the section
        // was simply empty. Both inputs are [Notify]; this is what carries their movement to the one
        // property the markup reads.

    }

    private void OnDraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The marker is written BY the recompute; reacting to it would loop.
        if (e.PropertyName is nameof(ColumnDraft.Marker) or nameof(ColumnDraft.MarkerReason) or
            nameof(ColumnDraft.MarkerCategory) or nameof(ColumnDraft.HasMarker))
            return;

        Recompute();
    }

    protected override void OnSessionStatusChanged(bool isConnected)
    {
        UpdateStatus();
    }

    protected override void OnSessionChanged()
    {
        UpdateStatus();
    }

    #endregion

    #region Properties

    public string ObjectName { get; }

    public DatabaseNodeType ObjectType { get; }

    /// <remarks>
    /// Found by RUNNING the application in Russian while checking the marker: the tab's own heading
    /// said "Table Customers" over an interface that was Russian everywhere else. Rule 3 could not see
    /// it - its destination list did not have <c>Display</c> in it - which is the same hole as
    /// <c>NotArmedReason</c> and <c>FilterSummary</c> before it. The list has it now.
    /// </remarks>
    public string ObjectTypeDisplay => Localization[ObjectType switch
    {
        DatabaseNodeType.Table => "Structure.ObjectType.Table",
        DatabaseNodeType.View => "Structure.ObjectType.View",
        DatabaseNodeType.Index => "Structure.ObjectType.Index",
        _ => "Structure.ObjectType.Other"
    }];

    /// <summary>
    /// The columns, as drafts: what the catalogue said, and what has been typed over it.
    /// </summary>
    [Notify]
    public ObservableCollection<ColumnDraft> Columns { get; set; } = null!;

    [Notify]
    public ColumnDraft? SelectedColumn { get; set; }

    [Notify]
    public List<ConstraintRow> Constraints { get; set; } = null!;

    [Notify]
    public List<IndexInfo> Indexes { get; set; } = null!;

    [Notify]
    public List<TriggerInfo> Triggers { get; set; } = null!;

    [Notify]
    public StructureSection SelectedSection { get; set; }

    /// <summary>
    /// How many columns the strip shows. A notified property rather than a computed one: found in the
    /// running application, where the strip said "Columns 0" over five rows - a computed property is
    /// read once when the strip binds, which is before the table has been read.
    /// </summary>
    [Notify]
    public int ColumnCount { get; private set; }

    /// <summary>
    /// The pending edits. Null until the first recompute.
    /// </summary>
    [Notify]
    public SchemaChangeSet? Pending { get; private set; }

    /// <summary>
    /// The DDL of the pending edits - what Apply will run, visible while it is still being decided
    /// (WS-38).
    /// </summary>
    [Notify]
    public string PendingSql { get; private set; } = string.Empty;

    [Notify]
    public int PendingCount { get; private set; }

    [Notify]
    public bool HasPending { get; private set; }

    [Notify]
    public bool NeedsRebuild { get; private set; }

    /// <summary>
    /// Edits Studio will not write, and why. They are not failures - nothing has been attempted - so
    /// they sit above the Apply button rather than in the error line.
    /// </summary>
    [Notify]
    public List<string> Refusals { get; private set; } = null!;

    /// <summary>
    /// The table as it is now, from the catalogue.
    /// </summary>
    [Notify]
    public string TableDdl { get; set; } = string.Empty;

    /// <summary>
    /// What the DDL section shows: the object as it stands, and beneath it whatever is pending.
    /// </summary>
    public string FullDdl => string.IsNullOrEmpty(PendingSql)
        ? TableDdl
        : $"{TableDdl}\n\n-- pending, will run on Apply:\n{PendingSql}";

    [Notify]
    public bool HasRows { get; private set; }

    [Notify]
    public DdlApplyReport? ApplyReport { get; private set; }

    public bool HasApplyReport => ApplyReport != null;

    [Notify]
    public string? KeyWarning { get; private set; }

    [Notify]
    public bool KeyWarningIsSevere { get; private set; }

    [Notify]
    public bool IsLoading { get; set; }

    [Notify]
    public bool IsApplying { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    [Notify]
    public bool CanRefresh { get; private set; }

    [Notify]
    public bool CanApply { get; private set; }

    [Notify]
    public bool CanRebuild { get; private set; }

    [Notify]
    public bool IsTable { get; private set; }

    [Notify]
    public string? ViewDefinition { get; set; }

    /// <summary>
    /// False when the catalogue cannot render this view's body. Changing a view means dropping and
    /// creating it, and creating it from a body Studio does not have would lose it.
    /// </summary>
    [Notify]
    public bool CanEditView { get; private set; }

    /// <summary>
    /// Whether to explain that a view's body could not be read. Only a VIEW can be in that state, and
    /// only a view ever sets this.
    /// </summary>
    [Notify]
    public bool ShowsViewNote { get; private set; }

    public bool HasViewDefinition => !string.IsNullOrWhiteSpace(ViewDefinition);

    [Notify]
    public string? IndexTableName { get; set; }

    [Notify]
    public bool? IndexIsUnique { get; set; }

    [Notify]
    public string? IndexFilterCondition { get; set; }

    public bool HasIndexDetails => !string.IsNullOrWhiteSpace(IndexTableName) ||
                                   IndexIsUnique is not null ||
                                   !string.IsNullOrWhiteSpace(IndexFilterCondition);

    /// <summary>
    /// The matrix of 5.2, so the designer can show the rule as well as apply it - in words rather than
    /// in catalogue keys, which is the ViewModel's job and not the data table's.
    /// </summary>
    /// <remarks>
    /// <b>Nothing binds this yet</b>, and it is projected anyway: the table holds keys now, so a view
    /// that bound the raw rows tomorrow would render <c>Schema.Cap.AddColumn</c> at somebody. Same
    /// shape as the «База» tab's provenance matrix, which does have a view.
    /// </remarks>
    public IReadOnlyList<SchemaCapabilityRow> Capabilities => SchemaCapabilities.Matrix
        .Select(capability => new SchemaCapabilityRow(
            Localization[capability.ChangeKey],
            Localization[SchemaCapabilities.MarkerOf(capability.Category)],
            Localization[capability.ReasonKey]))
        .ToList();

    /// <summary>
    /// What the engine cannot do at all, in the reader's language (WS-55's rule for the designer).
    /// </summary>
    public IReadOnlyList<string> NotInTheEngine => SchemaCapabilities.NotInTheEngine
        .Select(key => Localization[key])
        .ToList();

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; private set; } = null!;

    public ICommand AddColumnCommand { get; private set; } = null!;

    public ICommand DeleteColumnCommand { get; private set; } = null!;

    public ICommand RestoreColumnCommand { get; private set; } = null!;

    public ICommand ApplyCommand { get; private set; } = null!;

    public ICommand RevertCommand { get; private set; } = null!;

    public ICommand RebuildCommand { get; private set; } = null!;

    public ICommand CreateIndexCommand { get; private set; } = null!;

    public ICommand DropIndexCommand { get; private set; } = null!;

    public ICommand RecreateIndexCommand { get; private set; } = null!;

    public ICommand DropTriggerCommand { get; private set; } = null!;

    public ICommand EditTriggerCommand { get; private set; } = null!;

    public ICommand CreateTriggerCommand { get; private set; } = null!;

    public ICommand CopyDdlCommand { get; private set; } = null!;

    #endregion

    #region Services

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    private IDialogService Dialogs => ApplicationVm.Dialogs;

    #endregion

    #region Fields

    private bool m_suppressRecompute;

    /// <summary>
    /// The view body as it was read from the catalogue, so that "the text differs" is a question about
    /// what is in the database rather than about what the box happens to hold.
    /// </summary>
    private string? m_loadedViewDefinition;

    #endregion

    #region Localization

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
