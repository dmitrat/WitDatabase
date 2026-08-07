using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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

            // The strings the markup binds to, and the theme, both come from settings that apply the
            // moment they change (WS-52). Neither has a Save button to hang the work on, so both are
            // wired here, once, before the first window exists.
            Ui.LocalizedResources.Attach(Resources, appVm.Localization);

            ApplyTheme(appVm.Settings.Current.Theme);

            appVm.Settings.Changed += (_, e) =>
            {
                if (e.PropertyName == nameof(Models.Settings.Theme))
                    ApplyTheme(appVm.Settings.Current.Theme);
            };

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    #endregion

    #region Theme Support

    /// <summary>
    /// Applies the specified theme to the application.
    /// </summary>
    public void ApplyTheme(string themeName)
    {
        RequestedThemeVariant = themeName.ToLowerInvariant() switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>
    /// Gets the current App instance.
    /// </summary>
    public static new App? Current => Application.Current as App;

    #endregion
}