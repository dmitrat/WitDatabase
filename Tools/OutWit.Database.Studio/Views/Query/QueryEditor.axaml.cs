using Avalonia.Controls;
using Avalonia.Input;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels.Tabs;

namespace OutWit.Database.Studio.Views.Query;

/// <summary>
/// Query editor view for SQL execution.
/// </summary>
public partial class QueryEditor : UserControl
{
    #region Constructors

    public QueryEditor()
    {
        InitializeComponent();

        AddHandler(DoubleTappedEvent, OnDoubleTapped, handledEventsToo: true);
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// A double click on a history entry puts the query back in the editor (3.7). It does not RUN it:
    /// what comes out of a history is usually about to be edited, and running it unasked would be a
    /// write nobody pressed anything for.
    /// </summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not QueryTabViewModel tab)
            return;

        if (e.Source is not Control { DataContext: QueryHistoryEntry entry })
            return;

        tab.UseHistoryEntryCommand.Execute(entry);
    }

    #endregion
}
