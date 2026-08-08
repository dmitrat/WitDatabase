using Avalonia.Controls;

namespace OutWit.Database.Studio.Views.Dialogs;

/// <summary>
/// Changing the password, which is a migration into a new database (WS-58).
/// </summary>
public partial class ChangePasswordDialog : Window
{
    #region Constructors

    public ChangePasswordDialog()
    {
        InitializeComponent();
    }

    #endregion
}
