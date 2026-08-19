using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Styling;
using Avalonia.Threading;
using OutWit.Common.MVVM.Attributes;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Base class for DataGrids that display DataView results.
/// Provides common functionality for column generation and NULL value styling.
/// </summary>
public abstract partial class DataGridBase : DataGrid
{
    #region Static

    static DataGridBase()
    {
        ResultViewProperty.Changed.AddClassHandler<DataGridBase>((grid, e) => grid.OnResultViewChanged(e));
        ColumnSettingsProperty.Changed.AddClassHandler<DataGridBase>((grid, e) => grid.OnColumnSettingsChanged(e));
    }

    #endregion

    #region Converters

    protected static SqlValueConverter s_valueConverter = new();

    private static SqlValueBrushConverter s_brushConverter = new();

    #endregion

    #region Fields

    private readonly List<IStyle> m_dynamicStyles = [];
    private bool m_isUpdatingFromSettings;
    private bool m_isUpdatingSettings;
    private bool m_saveScheduled;
    private Dictionary<string, double>? m_lastSavedWidths;

    #endregion

    #region Constructors

    protected DataGridBase()
    {
        InitDefaults();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        AutoGenerateColumns = false;
        GridLinesVisibility = DataGridGridLinesVisibility.All;
        CanUserResizeColumns = true;
        CanUserReorderColumns = true;
        CanUserSortColumns = true;
    }

    private void InitEvents()
    {
        ColumnReordered += OnColumnReordered;
        LayoutUpdated += OnLayoutUpdated;
        Sorting += OnSorting;
    }

    /// <summary>
    /// A click on a header asks the ENGINE to sort, and the grid does not sort what it is holding
    /// (WS-30).
    ///
    /// This is what the DataGrid does by default, and it is the lie the decision is about: the grid
    /// has one page of a table, so sorting it puts the largest of a THOUSAND rows at the top and
    /// presents it as the largest of five thousand. Nobody notices until they page.
    ///
    /// When no command is bound - the query result grid, which holds the whole answer to a query -
    /// the default sorting is left alone, because there the page IS the data.
    /// </summary>
    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        var command = SortCommand;

        if (command == null)
            return;

        e.Handled = true;

        var column = e.Column.Header?.ToString();

        if (!string.IsNullOrEmpty(column) && command.CanExecute(column))
            command.Execute(column);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Clears dynamically created styles.
    /// </summary>
    protected void ClearDynamicStyles()
    {
        foreach (var style in m_dynamicStyles)
            Styles.Remove(style);

        m_dynamicStyles.Clear();
    }

    /// <summary>
    /// Creates a column for the specified DataColumn.
    /// </summary>
    protected virtual DataGridColumn CreateColumn(DataColumn dataColumn, int ordinal, string className)
    {
        var column = new DataGridTextColumn
        {
            Header = dataColumn.ColumnName,
            Binding = new Binding($"Row.ItemArray[{ordinal}]")
            {
                Converter = s_valueConverter,
                Mode = BindingMode.OneWay
            },
            MinWidth = 50,
            CanUserSort = true,
            Tag = ordinal
        };

        // Apply saved width if available
        if (ColumnSettings != null)
        {
            var settings = ColumnSettings.GetOrCreate(dataColumn.ColumnName);
            if (settings.Width.HasValue && settings.Width.Value > 0)
            {
                column.Width = new DataGridLength(settings.Width.Value);
            }
            else
            {
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
            }
        }
        else
        {
            column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        }

        return column;
    }

    /// <summary>
    /// Creates a style for NULL value display in the specified column.
    /// </summary>
    protected IStyle CreateNullValueStyle(int ordinal, string className)
    {
        var cellStyle = new Style(x => x.OfType<DataGridCell>().Class(className));
        var foregroundBinding = new Binding($"Row.ItemArray[{ordinal}]")
        {
            Converter = s_brushConverter,
        };
        cellStyle.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, foregroundBinding));
        return cellStyle;
    }

    /// <summary>
    /// Rebuilds columns based on the current ResultView.
    /// </summary>
    protected virtual void RebuildColumns()
    {
        Columns.Clear();
        ClearDynamicStyles();

        if (ResultView?.Table == null)
        {
            ItemsSource = null;
            return;
        }

        m_isUpdatingFromSettings = true;
        try
        {
            foreach (DataColumn col in ResultView.Table.Columns)
            {
                var ordinal = col.Ordinal;
                var className = GetColumnClassName(ordinal);

                var dataGridColumn = CreateColumn(col, ordinal, className);
                dataGridColumn.CellStyleClasses.Add(className);
                Columns.Add(dataGridColumn);

                var cellStyle = CreateNullValueStyle(ordinal, className);
                Styles.Add(cellStyle);
                m_dynamicStyles.Add(cellStyle);
            }

            ItemsSource = ResultView;

            // Apply sort if saved
            ApplySavedSort();
        }
        finally
        {
            m_isUpdatingFromSettings = false;
        }
    }

    /// <summary>
    /// Saves current column widths to ColumnSettings.
    /// </summary>
    private void SaveCurrentColumnWidths()
    {
        if (ColumnSettings == null || m_isUpdatingFromSettings)
            return;

        m_isUpdatingSettings = true;
        try
        {
            foreach (var column in Columns)
            {
                if (column.Header is not string headerName)
                    continue;

                var actualWidth = column.ActualWidth;
                
                // Only save if width is meaningful (> MinWidth)
                if (actualWidth <= column.MinWidth)
                    continue;

                // Check if width actually changed
                if (m_lastSavedWidths != null && 
                    m_lastSavedWidths.TryGetValue(headerName, out var lastWidth) &&
                    Math.Abs(lastWidth - actualWidth) < 1.0)
                    continue;

                var settings = ColumnSettings.GetOrCreate(headerName);
                settings.Width = actualWidth;
                settings.DisplayIndex = column.DisplayIndex;

                m_lastSavedWidths ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                m_lastSavedWidths[headerName] = actualWidth;
            }
        }
        finally
        {
            m_isUpdatingSettings = false;
        }
    }

    /// <summary>
    /// Applies saved sort from ColumnSettings.
    /// </summary>
    private void ApplySavedSort()
    {
        // Not when the sort belongs to the query (WS-30). This line re-sorted the PAGE by a column
        // remembered from an earlier session, every time the columns were rebuilt - so a table opened
        // ordered by its key came up in some other order, and each page was internally sorted and
        // jointly meaningless. Found by running the application: rows arrived 12..1 under a footer
        // that said "ordered by Id".
        if (SortCommand != null)
            return;

        if (ColumnSettings == null || string.IsNullOrEmpty(ColumnSettings.SortColumn))
            return;

        if (ResultView == null)
            return;

        var sortDirection = ColumnSettings.SortAscending ? "ASC" : "DESC";
        ResultView.Sort = $"[{ColumnSettings.SortColumn}] {sortDirection}";
    }

    /// <summary>
    /// Gets the CSS class name for a column.
    /// </summary>
    protected virtual string GetColumnClassName(int ordinal)
    {
        return $"col-{ordinal}";
    }

    /// <summary>
    /// Called when ResultView property changes.
    /// </summary>
    protected virtual void OnResultViewChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // Clear last saved widths cache when data changes
        m_lastSavedWidths = null;
        RebuildColumns();
    }

    /// <summary>
    /// Called when ColumnSettings property changes.
    /// </summary>
    protected virtual void OnColumnSettingsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        // If we have data, rebuild to apply new settings
        if (ResultView?.Table != null)
        {
            m_lastSavedWidths = null;
            RebuildColumns();
        }
    }

    #endregion

    #region Event Handlers

    private void OnColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (ColumnSettings == null || m_isUpdatingFromSettings)
            return;

        SaveCurrentColumnWidths();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // Skip if no settings, updating, or already scheduled
        if (ColumnSettings == null || m_isUpdatingFromSettings || m_isUpdatingSettings || m_saveScheduled)
            return;

        if (Columns.Count == 0)
            return;

        // Debounce: schedule save on background priority to coalesce multiple updates
        m_saveScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            m_saveScheduled = false;
            SaveCurrentColumnWidths();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Called by derived classes when column width changes.
    /// </summary>
    protected void NotifyColumnWidthChanged(DataGridColumn column)
    {
        if (ColumnSettings == null || m_isUpdatingFromSettings || m_isUpdatingSettings)
            return;

        if (column.Header is string headerName)
        {
            var settings = ColumnSettings.GetOrCreate(headerName);
            settings.Width = column.ActualWidth > 0 ? column.ActualWidth : null;
        }
    }

    /// <summary>
    /// Called by derived classes when sort changes.
    /// </summary>
    protected void NotifySortChanged(string? columnName, bool ascending)
    {
        if (ColumnSettings == null || m_isUpdatingFromSettings)
            return;

        ColumnSettings.SortColumn = columnName;
        ColumnSettings.SortAscending = ascending;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The DataView to display.
    /// </summary>
    [StyledProperty]
    public DataView? ResultView { get; set; }

    /// <summary>
    /// Column settings for persistence.
    /// </summary>
    [StyledProperty]
    public GridColumnSettings? ColumnSettings { get; set; }

    /// <summary>
    /// Who sorts, when a header is clicked (WS-30). Bound by a grid that holds ONE PAGE of a table,
    /// where sorting what is held would be sorting a sample; left null by one that holds a whole
    /// result, where the page is the data and the built-in sorting is honest.
    /// </summary>
    [StyledProperty]
    public System.Windows.Input.ICommand? SortCommand { get; set; }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    #endregion
}
