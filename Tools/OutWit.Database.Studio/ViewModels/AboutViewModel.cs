using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// ViewModel for About dialog.
/// </summary>
public sealed class AboutViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    private const string WEBSITE_URL = "https://witdatabase.io";
    private const string AUTHOR_URL = "https://ratner.io";
    private const string GITHUB_URL = "https://github.com/dmitrat/WitDatabase";

    #endregion

    #region Events

    public event Action? DialogClosed;

    #endregion

    #region Constructors

    public AboutViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefaults();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        
        Version = version != null 
            ? $"{version.Major}.{version.Minor}.{version.Build}" 
            : "1.0.0";
        
        ProductName = "WitDatabase Studio";
        Copyright = $"© {DateTime.Now.Year} Dmitry Ratner. All rights reserved.";
        Description = "A cross-platform database management tool for WitDatabase.";
    }

    private void InitCommands()
    {
        OpenWebsiteCommand = new RelayCommand(OpenWebsite);
        OpenAuthorCommand = new RelayCommand(OpenAuthor);
        OpenGitHubCommand = new RelayCommand(OpenGitHub);
        CloseCommand = new RelayCommand(Close);
    }

    #endregion

    #region Command Functions

    private void OpenWebsite()
    {
        OpenUrl(WEBSITE_URL);
    }

    private void OpenAuthor()
    {
        OpenUrl(AUTHOR_URL);
    }

    private void OpenGitHub()
    {
        OpenUrl(GITHUB_URL);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening URL
        }
    }

    private void Close()
    {
        DialogClosed?.Invoke();
    }

    #endregion

    #region Properties

    public string ProductName { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public string Copyright { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public string WebsiteUrl => WEBSITE_URL;

    public string AuthorUrl => AUTHOR_URL;

    public string GitHubUrl => GITHUB_URL;

    #endregion

    #region Commands

    public ICommand OpenWebsiteCommand { get; private set; } = null!;

    public ICommand OpenAuthorCommand { get; private set; } = null!;

    public ICommand OpenGitHubCommand { get; private set; } = null!;

    public ICommand CloseCommand { get; private set; } = null!;

    #endregion
}
