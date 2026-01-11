using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using OutWit.Database.Studio.Converters;
using OutWit.Database.Studio.Models;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Custom DataGrid for editing table data.
/// Supports inline editing, type validation, and NULL handling.
/// </summary>
public class EditableDataGrid : DataGridBase
{
    #region Styled Properties

    public static readonly StyledProperty<IList<ColumnInfo>?> ColumnInfosProperty =
        AvaloniaProperty.Register<EditableDataGrid, IList<ColumnInfo>?>(nameof(ColumnInfos));

    public static readonly StyledProperty<DataRowView?> SelectedRowViewProperty =
        AvaloniaProperty.Register<EditableDataGrid, DataRowView?>(nameof(SelectedRowView));

    #endregion

    #region Static

    // Shared converter instance
    private static readonly SqlValueConverter s_valueConverter = new();

    static EditableDataGrid()
    {
        ColumnInfosProperty.Changed.AddClassHandler<EditableDataGrid>((grid, e) => grid.OnColumnInfosChanged(e));
    }

    #endregion

    #region Constructors

    public EditableDataGrid()
    {
        IsReadOnly = false;
        SelectionMode = DataGridSelectionMode.Single;
        
        SelectionChanged += OnSelectionChanged;
        CellEditEnding += OnCellEditEnding;
    }

    #endregion

    #region Functions

    protected override DataGridTextColumn CreateColumn(DataColumn dataColumn, int ordinal, string className)
    {
        var columnInfo = ColumnInfos?.FirstOrDefault(c => c.Name == dataColumn.ColumnName);
        var columnCount = ResultView?.Table?.Columns.Count ?? 1;

        return new DataGridTextColumn
        {
            Header = dataColumn.ColumnName,
            Binding = new Binding($"Row.ItemArray[{ordinal}]")
            {
                Converter = s_valueConverter,
                Mode = BindingMode.OneWay // Display only, editing handled manually
            },
            Width = columnCount <= 5
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(120, DataGridLengthUnitType.Pixel),
            MinWidth = 60,
            MaxWidth = 400,
            CanUserSort = true,
            IsReadOnly = columnInfo?.IsPrimaryKey == true || columnInfo?.IsAutoIncrement == true,
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

        if (e.EditingElement is not TextBox textBox)
            return;

        var columnIndex = e.Column.DisplayIndex;
        if (columnIndex < 0 || ResultView?.Table == null || columnIndex >= ResultView.Table.Columns.Count)
            return;

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

            // Notify that cell was edited
            CellEdited?.Invoke(this, new CellEditedEventArgs(rowView, column.ColumnName, newValue));
        }
        catch
        {
            // Conversion failed - cancel edit
            e.Cancel = true;
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Raised when a cell value is edited.
    /// </summary>
    public event EventHandler<CellEditedEventArgs>? CellEdited;

    #endregion

    #region Properties

    /// <summary>
    /// Column information for validation and editing rules.
    /// </summary>
    public IList<ColumnInfo>? ColumnInfos
    {
        get => GetValue(ColumnInfosProperty);
        set => SetValue(ColumnInfosProperty, value);
    }

    /// <summary>
    /// The currently selected row.
    /// </summary>
    public DataRowView? SelectedRowView
    {
        get => GetValue(SelectedRowViewProperty);
        set => SetValue(SelectedRowViewProperty, value);
    }

    #endregion
}

/// <summary>
/// Event args for cell edited event.
/// </summary>
public class CellEditedEventArgs : EventArgs
{
    public CellEditedEventArgs(DataRowView rowView, string columnName, object? newValue)
    {
        RowView = rowView;
        ColumnName = columnName;
        NewValue = newValue;
    }

    public DataRowView RowView { get; }
    public string ColumnName { get; }
    public object? NewValue { get; }
}
