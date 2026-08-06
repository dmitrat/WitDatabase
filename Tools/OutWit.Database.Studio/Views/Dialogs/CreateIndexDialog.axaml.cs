using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>
/// The index dialog (WS-43). The code-behind does only what a multi-selection list makes it do: take
/// what is selected and hand it to the ViewModel, which owns every decision about the index itself.
/// </summary>
public partial class CreateIndexDialog : Window
{
    #region Constructors

    public CreateIndexDialog()
    {
        InitializeComponent();
    }

    #endregion

    #region Event Handlers

    private void AddColumn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateIndexViewModel vm)
            return;

        foreach (var column in Selected())
            vm.AddColumnCommand.Execute(column);
    }

    private void AddIncluded_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CreateIndexViewModel vm)
            return;

        foreach (var column in Selected())
            vm.AddIncludedCommand.Execute(column);
    }

    /// <summary>
    /// An expression index is typed rather than picked - the catalogue cannot list something that does
    /// not exist yet.
    /// </summary>
    private void Expression_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not CreateIndexViewModel vm)
            return;

        var box = this.FindControl<TextBox>("ExpressionBox");

        if (string.IsNullOrWhiteSpace(box?.Text))
            return;

        vm.AddColumnCommand.Execute(box.Text.Trim());
        box.Text = string.Empty;
    }

    private List<string> Selected()
    {
        var list = this.FindControl<ListBox>("AvailableColumnsList");

        return list?.SelectedItems?.Cast<string>().ToList() ?? [];
    }

    #endregion
}
