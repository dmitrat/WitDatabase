using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OutWit.Database.Studio.Views;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio;

public partial class App : Application
{
    #region Initialization

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appVm = ApplicationViewModel.Instance;
            
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    #endregion
}