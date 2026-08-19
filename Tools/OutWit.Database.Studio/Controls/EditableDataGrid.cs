using System.Data;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using OutWit.Common.MVVM.Attributes;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Custom DataGrid for editing table data.
/// Supports inline editing, type validation, and NULL handling.
/// </summary>
public partial class EditableDataGrid : DataGridBase
{
    #region Static

    static EditableDataGrid()
    {
        ColumnInfosProperty.Changed.AddClassHandler<EditableDataGrid>((grid, e) => grid.OnColumnInfosChanged(e));
        RowMarksProperty.Changed.AddClassHandler<EditableDataGrid>((grid, _) => grid.MarkRows());
    }

    #endregion

    #region Constructors

    public EditableDataGrid()
    {
        IsReadOnly = false;
        SelectionMode = DataGridSelectionMode.Single;
        
        SelectionChanged += OnSelectionChanged;
        CellEditEnding += OnCellEditEnding;

        // What a row IS, drawn on the row itself. The tab, the toolbar and the commit button
        // all said that something had changed and none of them said WHICH, so discarding was
        // the only way to find out.
        LoadingRow += (_, e) => Mark(e.Row);

        // A row that is going to be deleted has no current values to edit, and asking for them
        // throws. It stays on screen to be looked at, not to be typed into.
        BeginningEdit += (_, e) =>
        {
            if (e.Row.DataContext is DataRowView { Row.RowState: DataRowState.Deleted })
                e.Cancel = true;
        };
    }

    #endregion

    #region Row marks

    /// <summary>
    /// Bumped by the ViewModel whenever a row is changed, deleted or added, so that the rows
    /// already on screen are re-marked. <c>LoadingRow</c> alone would only mark them as they are
    /// realised, which leaves the row somebody has just pressed Delete on looking untouched.
    /// </summary>
    public static readonly StyledProperty<int> RowMarksProperty =
        AvaloniaProperty.Register<EditableDataGrid, int>(nameof(RowMarks));

    public int RowMarks
    {
        get => GetValue(RowMarksProperty);
        set => SetValue(RowMarksProperty, value);
    }

    private void MarkRows()
    {
        foreach (var row in this.GetVisualDescendants().OfType<DataGridRow>())
            Mark(row);
    }

    private static void Mark(DataGridRow row)
    {
        var state = (row.DataContext as DataRowView)?.Row.RowState;

        row.Classes.Set("row-deleted", state == DataRowState.Deleted);
        row.Classes.Set("row-changed", state == DataRowState.Modified);
        row.Classes.Set("row-added", state == DataRowState.Added);
    }

    #endregion
    #region What a column is drawn as

    /// <summary>
    /// Whether a column is drawn as a checkbox rather than as text.
    /// </summary>
    /// <remarks>
    /// <b>A checkbox has two states and a nullable column has three</b> (WS-34, decided
    /// 2026-08-15). Drawing NULL as an unchecked box would make "nobody has said" look exactly
    /// like "false" in the one place where a person edits the data, so a nullable BOOLEAN keeps
    /// the text it has always had - where NULL is drawn as NULL and can be typed back.
    /// </remarks>
    public static bool WantsACheckBox(ColumnInfo? column)
    {
        return column is { IsNullable: false }
               && string.Equals(column.DataType, "BOOLEAN", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
    #region Functions

    protected override DataGridColumn CreateColumn(DataColumn dataColumn, int ordinal, string className)
    {
        var columnInfo = ColumnInfos?.FirstOrDefault(c => c.Name == dataColumn.ColumnName);
        var columnCount = ResultView?.Table?.Columns.Count ?? 1;

        var width = columnCount <= 5
            ? new DataGridLength(1, DataGridLengthUnitType.Star)
            : new DataGridLength(120, DataGridLengthUnitType.Pixel);

        var readOnly = columnInfo?.IsPrimaryKey == true || columnInfo?.IsAutoIncrement == true;

        // A BOOLEAN that cannot be NULL gets the widget its values have: two states, both of them
        // reachable in one click. A nullable one keeps the text, because NULL is a third state and
        // an unchecked box would be a lie about it.
        if (WantsACheckBox(columnInfo))
        {
            return new DataGridCheckBoxColumn
            {
                Header = dataColumn.ColumnName,
                Binding = new Binding($"Row.ItemArray[{ordinal}]") { Mode = BindingMode.OneWay },
                Width = width,
                MinWidth = 60,
                MaxWidth = 400,
                CanUserSort = true,
                IsReadOnly = readOnly,
                Tag = ordinal
            };
        }

        return new DataGridTextColumn
        {
            Header = dataColumn.ColumnName,
            Binding = new Binding($"Row.ItemArray[{ordinal}]")
            {
                Converter = s_valueConverter,
                Mode = BindingMode.OneWay // Display only, editing handled manually
            },
            Width = width,
            MinWidth = 60,
            MaxWidth = 400,
            CanUserSort = true,
            IsReadOnly = readOnly,
            Tag = ordinal
        };
    }

    protected override string GetColumnClassName(int ordinal) => $"edit-col-{ordinal}";

    private void OnColumnInfosChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (ResultView != null)
            RebuildColumns();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        SetValue(SelectedRowViewProperty, SelectedItem as DataRowView);
    }

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
            return;

        if (e.Row.DataContext is not DataRowView rowView)
            return;

        var index = e.Column.DisplayIndex;
        if (index < 0 || ResultView?.Table == null || index >= ResultView.Table.Columns.Count)
            return;

        // A checkbox has no text to parse: it arrives with the value already in it, and the only
        // thing to do is write it where the text path writes its own.
        if (e.EditingElement is CheckBox box)
        {
            rowView.Row[index] = box.IsChecked == true;

            if (CellEditedCommand?.CanExecute(rowView) == true)
                CellEditedCommand.Execute(rowView);

            return;
        }

        if (e.EditingElement is not TextBox textBox)
            return;

        var columnIndex = index;

        var column = ResultView.Table.Columns[columnIndex];
        var columnInfo = ColumnInfos?.FirstOrDefault(c => c.Name == column.ColumnName);
        var newText = textBox.Text ?? string.Empty;

        try
        {
            object? newValue;

            // Handle NULL input
            if (string.IsNullOrEmpty(newText) ||
                newText.Equals(SqlValueConverter.NULL_DISPLAY_TEXT, StringComparison.OrdinalIgnoreCase))
            {
                if (columnInfo?.IsNullable == true)
                {
                    newValue = DBNull.Value;
                }
                else
                {
                    // Non-nullable column - cancel edit
                    e.Cancel = true;
                    return;
                }
            }
            else
            {
                // Use SqlValueParser for type-safe conversion based on WitSqlType
                newValue = SqlValueParser.Parse(newText, column.DataType);
            }

            // Apply the value directly to the DataRow
            rowView.Row[columnIndex] = newValue;

            // Execute command if bound
            if (CellEditedCommand?.CanExecute(rowView) == true)
            {
                CellEditedCommand.Execute(rowView);
            }
        }
        catch
        {
            // Conversion failed - cancel edit
            e.Cancel = true;
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Column information for validation and editing rules.
    /// </summary>
    [StyledProperty]
    public IList<ColumnInfo>? ColumnInfos { get; set; }

    /// <summary>
    /// The currently selected row.
    /// </summary>
    [StyledProperty]
    public DataRowView? SelectedRowView { get; set; }

    /// <summary>
    /// Command executed when a cell is edited. Parameter is DataRowView.
    /// </summary>
    [StyledProperty]
    public ICommand? CellEditedCommand { get; set; }

    #endregion
}