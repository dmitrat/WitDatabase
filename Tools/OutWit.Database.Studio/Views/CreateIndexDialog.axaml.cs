using Avalonia.Controls;
using Avalonia.Interactivity;
using OutWit.Database.Studio.ViewModels;
using System.Linq;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Dialog for creating a new index.
/// </summary>
public partial class CreateIndexDialog : Window
{
    #region Constructors

    public CreateIndexDialog()
    {
        InitializeComponent();
    }

    public CreateIndexDialog(CreateIndexViewModel viewModel) : this()
    {
        DataContext = viewModel;
        
        // Subscribe to completion events
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateIndexViewModel.IsCompleted) && viewModel.IsCompleted)
            {
                Close(true);
            }
            else if (e.PropertyName == nameof(CreateIndexViewModel.IsCancelled) && viewModel.IsCancelled)
            {
                Close(false);
            }
        };
    }

    #endregion

    #region Event Handlers

    private void AddColumn_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as CreateIndexViewModel;
        if (vm == null)
            return;

        var availableList = this.FindControl<ListBox>("AvailableColumnsList");
        if (availableList?.SelectedItems == null)
            return;

        foreach (var item in availableList.SelectedItems.Cast<string>().ToList())
        {
            if (!vm.SelectedColumns.Contains(item))
            {
                vm.SelectedColumns.Add(item);
            }
        }
    }

    private void RemoveColumn_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as CreateIndexViewModel;
        if (vm == null)
            return;

        var selectedList = this.FindControl<ListBox>("SelectedColumnsList");
        if (selectedList?.SelectedItems == null)
            return;

        foreach (var item in selectedList.SelectedItems.Cast<string>().ToList())
        {
            vm.SelectedColumns.Remove(item);
        }
    }

    private void MoveUp_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as CreateIndexViewModel;
        if (vm == null)
            return;

        var selectedList = this.FindControl<ListBox>("SelectedColumnsList");
        if (selectedList?.SelectedItem == null)
            return;

        var item = (string)selectedList.SelectedItem;
        var index = vm.SelectedColumns.IndexOf(item);
        
        if (index > 0)
        {
            vm.SelectedColumns.Move(index, index - 1);
            selectedList.SelectedItem = item;
        }
    }

    private void MoveDown_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as CreateIndexViewModel;
        if (vm == null)
            return;

        var selectedList = this.FindControl<ListBox>("SelectedColumnsList");
        if (selectedList?.SelectedItem == null)
            return;

        var item = (string)selectedList.SelectedItem;
        var index = vm.SelectedColumns.IndexOf(item);
        
        if (index < vm.SelectedColumns.Count - 1)
        {
            vm.SelectedColumns.Move(index, index + 1);
            selectedList.SelectedItem = item;
        }
    }

    #endregion
}
