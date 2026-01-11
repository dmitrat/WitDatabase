using System.Collections;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Styling;
using OutWit.Common.MVVM.Attributes;
using OutWit.Database.Studio.Converters;

namespace OutWit.Database.Studio.Controls;

/// <summary>
/// Custom DataGrid that displays DataView results.
/// Supports NULL value display and automatic column generation.
/// </summary>
public partial class ResultDataGrid : DataGrid
{
    #region Static

    static ResultDataGrid()
    {
        ResultViewProperty.Changed.AddClassHandler<ResultDataGrid>((grid, e) => grid.OnResultViewChanged(e));
    }

    #endregion

    #region Fields

    private readonly List<IStyle> m_dynamicStyles = new ();

    private readonly SqlValueBrushConverter m_valueBrushConverter = new ();

    #endregion

    #region Constructors

    public ResultDataGrid()
    {
        InitDefaults();
        InitEvents();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
        GridLinesVisibility = DataGridGridLinesVisibility.All;
        CanUserResizeColumns = true;
        CanUserReorderColumns = true;
        CanUserSortColumns = true;
        SelectionMode = DataGridSelectionMode.Extended;
    }

    private void InitEvents()
    {
        SelectionChanged += OnSelectionChanged;
    }

    #endregion

    #region Functions

    private void ClearDynamicStyles()
    {
        foreach (var s in m_dynamicStyles)
            Styles.Remove(s);

        m_dynamicStyles.Clear();
    }


    private void OnResultViewChanged(AvaloniaPropertyChangedEventArgs e)
    {
        Columns.Clear();
        ClearDynamicStyles();

        if (e.NewValue is not DataView view || view.Table == null )
            ItemsSource = null;
        else
        {
            foreach (DataColumn col in view.Table.Columns)
            {

                var ordinal = col.Ordinal;
                var className = $"result-col-{ordinal}";

                var dataGridColumn = new DataGridTextColumn
                {
                    Header = col.ColumnName,
                    Binding = new Binding($"Row.ItemArray[{ordinal}]")
                    {
                        Converter = new SqlValueConverter(),
                    },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    MinWidth = 50,
                    CanUserSort = true,
                    Tag = ordinal
                };

                dataGridColumn.CellStyleClasses.Add(className);

                Columns.Add(dataGridColumn);

                var cellStyle = new Style(x => x.OfType<DataGridCell>().Class(className));

                var foregroundBinding = new Binding($"Row.ItemArray[{ordinal}]")
                {
                    Converter = m_valueBrushConverter,
                };

                cellStyle.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, foregroundBinding));

                Styles.Add(cellStyle);
                m_dynamicStyles.Add(cellStyle);
            }

            ItemsSource = view;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = new List<DataRowView>();
        
        foreach (var item in SelectedItems)
        {
            if (item is DataRowView rowView)
            {
                selected.Add(rowView);
            }
        }

        SetValue(SelectedRowsProperty, selected);
    }

    #endregion

    #region Properties

    /// <summary>
    /// The DataView to display.
    /// </summary>
    [StyledProperty]
    public DataView? ResultView { get; set; }

    /// <summary>
    /// The currently selected rows.
    /// </summary>
    [StyledProperty]
    public IList? SelectedRows { get; set; }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    #endregion
}