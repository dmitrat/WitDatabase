using Avalonia.Controls;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Dialog for creating a new view.
/// </summary>
public partial class CreateViewDialog : Window
{
    #region Constructors

    public CreateViewDialog()
    {
        InitializeComponent();
    }

    public CreateViewDialog(CreateViewViewModel viewModel) : this()
    {
        DataContext = viewModel;
        
        // Subscribe to completion events
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateViewViewModel.IsCompleted) && viewModel.IsCompleted)
            {
                Close(true);
            }
            else if (e.PropertyName == nameof(CreateViewViewModel.IsCancelled) && viewModel.IsCancelled)
            {
                Close(false);
            }
        };
    }

    #endregion
}
