using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OutWit.Database.Studio.Ui.Icons;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    #region Fields

    /// <summary>
    /// Set once the tabs have been asked about unapplied work, so that the second pass through
    /// OnClosing - and a close requested by File &gt; Exit, which asked already - does not ask twice.
    /// </summary>
    private bool m_closeConfirmed;

    #endregion

    #region Constructors

    public MainWindow()
    {
        DataContext = ApplicationViewModel
            .Instance
            .ResetOwnerWindow(this);

        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;
        KeyDown += OnWindowKeyDown;

        // File > Exit ends here rather than in Environment.Exit, so both ways out of Studio run the
        // same close: window state saved, connection disposed, file lock released.
        ApplicationViewModel.Instance.ShutdownRequested += OnShutdownRequested;

        // The palette is a text prompt: opening it without putting the caret in the box means the
        // first thing typed goes nowhere.
        ApplicationViewModel.Instance.PaletteVm.PropertyChanged += OnPaletteChanged;
        ThemeToggleButton.Click += OnThemeToggleClick;
        
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    #endregion

    #region Event Handlers

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var settings = await ApplicationViewModel.Instance.Settings.LoadAsync();
            
            App.Current?.ApplyTheme(settings.Theme);
            UpdateThemeButton();
            
            // Apply window size
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            
            if (settings.WindowState == "Maximized")
            {
                WindowState = WindowState.Maximized;
            }
            
            await ApplicationViewModel.Instance.MainWindowVm.InitializeAsync();
            
            ApplicationViewModel.Instance.MainWindowVm.RecentFiles.CollectionChanged += OnRecentFilesChanged;
            UpdateRecentFilesMenu();
            UpdateRecentFilesList();
        }
        catch
        {
            // Use defaults on error
        }
    }

    /// <summary>
    /// The keys the palette needs while it is open, and Escape everywhere else.
    ///
    /// A KeyBinding cannot do this: the palette's own text box swallows Enter and the arrows, and
    /// Escape has to mean two different things depending on what is happening. Handled here, before
    /// the focused control sees them.
    /// </summary>
    private void OnWindowKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var app = ApplicationViewModel.Instance;

        // Ctrl+K is handled here rather than only as a KeyBinding: a KeyBinding needs the event to
        // bubble from a focused element, and the welcome screen has nothing focusable on it - the
        // palette then cannot be opened from the one screen where it is most useful. Measured by
        // pressing it: nothing happened, while Escape - handled here - worked.
        if (e.Key == Avalonia.Input.Key.K && e.KeyModifiers == Avalonia.Input.KeyModifiers.Control)
        {
            app.PaletteVm.OpenCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (app.PaletteVm.IsOpen)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.Escape:
                    app.PaletteVm.CloseCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Avalonia.Input.Key.Enter:
                    app.PaletteVm.AcceptCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Avalonia.Input.Key.Down:
                    app.PaletteVm.MoveDownCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Avalonia.Input.Key.Up:
                    app.PaletteVm.MoveUpCommand.Execute(null);
                    e.Handled = true;
                    return;
            }

            return;
        }

        if (e.Key != Avalonia.Input.Key.Escape)
            return;

        // Escape stops a running query (1.7) and closes the notification list. Neither is destructive,
        // so neither asks.
        if (app.MainWindowVm.AreNotificationsVisible)
        {
            app.MainWindowVm.HideNotificationsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (app.WorkspaceTabsVm.IsExecuting && app.WorkspaceTabsVm.CurrentExecutingTab != null)
        {
            app.WorkspaceTabsVm.CurrentExecutingTab.StopQueryCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPaletteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModels.CommandPaletteViewModel.IsOpen))
            return;

        if (ApplicationViewModel.Instance.PaletteVm.IsOpen)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => PaletteQueryBox.Focus());
    }

    private void OnShutdownRequested(object? sender, EventArgs e)
    {
        // RequestShutdownAsync has already asked the tabs.
        m_closeConfirmed = true;
        Close();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // The close button is the other way out, and it has to ask the same question. Closing cannot
        // wait for an answer, so the first pass is cancelled and a second one is started once the
        // answer is in.
        if (!m_closeConfirmed)
        {
            e.Cancel = true;

            if (!await ApplicationViewModel.Instance.WorkspaceTabsVm.ConfirmCloseAllAsync())
                return;

            m_closeConfirmed = true;
            Close();
            return;
        }

        // FIRST, and synchronously (issue 10). This is what flushes the databases, and it cannot
        // sit after an await: OnClosing is 'async void', so the first await hands control back to
        // Avalonia, which closes the window and ends the process - the continuation never runs.
        // Measured twice: with this call after the await, the file still came out with a header
        // older than its own pages.
        ApplicationViewModel.Instance.CloseDatabases();

        try
        {
            var state = WindowState == WindowState.Maximized ? "Maximized" : "Normal";
            var width = WindowState == WindowState.Normal ? Width : 1200;
            var height = WindowState == WindowState.Normal ? Height : 800;
            
            await ApplicationViewModel.Instance.MainWindowVm.SaveWindowStateAsync(width, height, state);
        }
        catch
        {
            // Ignore save errors on close
        }

    }

    private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRecentFilesMenu();
        UpdateRecentFilesList();
    }

    private async void OnThemeToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var newTheme = isDark ? "Light" : "Dark";
        
        App.Current?.ApplyTheme(newTheme);
        
        // Save to settings
        try
        {
            var settings = await ApplicationViewModel.Instance.Settings.LoadAsync();
            settings.Theme = newTheme;
            await ApplicationViewModel.Instance.Settings.SaveAsync(settings);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateThemeButton();
    }

    #endregion

    #region Functions

    private void UpdateThemeButton()
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

        ThemeIcon.Data = Avalonia.Media.Geometry.Parse(isDark ? StudioIcons.PATH_THEME_LIGHT : StudioIcons.PATH_THEME_DARK);

        // The name of a theme is Studio's own word, and this button was the last English on the
        // window after the sweep - found by switching the running application, not by any test.
        var localization = ViewModels.ApplicationViewModel.Instance.Localization;

        ThemeText.Text = localization[isDark ? "Settings.Theme.Light" : "Settings.Theme.Dark"];
    }

    private void UpdateRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        
        var recentFiles = ApplicationViewModel.Instance.MainWindowVm.RecentFiles;
        
        foreach (var file in recentFiles)
        {
            var menuItem = new MenuItem
            {
                Header = file.FileName,
                Command = ApplicationViewModel.Instance.MainWindowVm.OpenRecentCommand,
                CommandParameter = file.FilePath
            };
            menuItem.SetValue(ToolTip.TipProperty, file.FilePath);
            
            RecentFilesMenu.Items.Add(menuItem);
        }
    }

    private void UpdateRecentFilesList()
    {
        RecentFilesList.Children.Clear();
        
        var recentFiles = ApplicationViewModel.Instance.MainWindowVm.RecentFiles;
        
        foreach (var file in recentFiles)
        {
            var border = new Border
            {
                Padding = new Avalonia.Thickness(8, 6),
                CornerRadius = new Avalonia.CornerRadius(4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Background = Brushes.Transparent
            };
            
            var stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Spacing = 2 };
            
            stack.Children.Add(new TextBlock 
            { 
                Text = file.FileName,
                FontWeight = FontWeight.Medium
            });
            
            stack.Children.Add(new TextBlock 
            { 
                Text = file.Directory,
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            
            border.Child = stack;
            border.SetValue(ToolTip.TipProperty, file.FilePath);
            
            var filePath = file.FilePath;
            
            border.PointerEntered += (_, _) => border.Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
            border.PointerExited += (_, _) => border.Background = Brushes.Transparent;
            
            border.PointerPressed += (_, _) =>
            {
                ApplicationViewModel.Instance.MainWindowVm.OpenRecentCommand.Execute(filePath);
            };
            
            RecentFilesList.Children.Add(border);
        }
    }

    #endregion
}