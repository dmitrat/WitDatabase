using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using OutWit.Common.MVVM.Attributes;
using OutWit.Common.MVVM.Table;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Custom DataGrid that displays TableView data.
/// Automatically generates columns from TableView.HeaderRow.
/// </summary>
public partial class ResultDataGrid : DataGrid
{
    #region Static

    static ResultDataGrid()
    {
        HeaderRowProperty.Changed.AddClassHandler<ResultDataGrid>((grid, e) => grid.OnHeaderRowChanged(e));
        ResultPageProperty.Changed.AddClassHandler<ResultDataGrid>((grid, e) => grid.OnResultPageChanged(e));
    }

    #endregion

    #region Constructors

    public ResultDataGrid()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
        GridLinesVisibility = DataGridGridLinesVisibility.All;
        CanUserResizeColumns = true;
        CanUserReorderColumns = true;
        CanUserSortColumns = false;
    }

    #endregion

    #region Functions

    private void OnHeaderRowChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var headerRow = e.NewValue as TableViewRow;
        
        Columns.Clear();
        
        if (headerRow == null || headerRow.Values.Length == 0)
            return;

        // Generate columns from header row
        for (var i = 0; i < headerRow.Values.Length; i++)
        {
            var cell = headerRow.Values[i];
            var dataGridColumn = new DataGridTextColumn
            {
                Header = cell.Text ?? $"Column{i}",
                Binding = new Binding($"[{i}].Text"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 50
            };

            Columns.Add(dataGridColumn);
        }

    }

    private void OnResultPageChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var page = e.NewValue as TableViewPage;
        ItemsSource = page?.Rows;
    }

    #endregion

    #region Properties

    /// <summary>
    /// The header row with column names.
    /// </summary>
    [StyledProperty]
    public TableViewRow? HeaderRow { get; set; }

    /// <summary>
    /// The page of results to display.
    /// </summary>
    [StyledProperty]
    public TableViewPage? ResultPage { get; set; }


    protected override Type StyleKeyOverride => typeof(DataGrid);

    #endregion
}
