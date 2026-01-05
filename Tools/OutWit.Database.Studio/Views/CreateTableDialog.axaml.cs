using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Dialog for creating a new table.
/// </summary>
public partial class CreateTableDialog : Window
{
    #region Constructors

    public CreateTableDialog()
    {
        InitializeComponent();
    }

    public CreateTableDialog(CreateTableViewModel viewModel) : this()
    {
        DataContext = viewModel;
        
        // Subscribe to completion events
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateTableViewModel.IsCompleted) && viewModel.IsCompleted)
            {
                Close(true);
            }
            else if (e.PropertyName == nameof(CreateTableViewModel.IsCancelled) && viewModel.IsCancelled)
            {
                Close(false);
            }
        };
    }

    #endregion
}
