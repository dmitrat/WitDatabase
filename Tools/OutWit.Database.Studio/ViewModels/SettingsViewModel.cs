using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Core;
using OutWit.Database.Core.Builder;
using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.Services.Localization;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// The settings window: five sections, applied immediately (WS-52).
///
/// <para>
/// <b>There is no Save and no Cancel, and this ViewModel holds no copy of any setting.</b> The view
/// binds straight to <see cref="Values"/>, which is the one live <c>Settings</c> object the whole
/// application reads. That is what makes "applied immediately" structural rather than a promise: there
/// is no second copy to write back, so there is nothing to forget to write back.
/// </para>
/// <para>
/// It is also not modal (WS-52). People come here to read, compare and go back to what they were
/// doing, and a modal window forbids all three.
/// </para>
/// </summary>
public sealed class SettingsViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constants

    public const string SECTION_GENERAL = "General";
    public const string SECTION_EDITOR = "Editor";
    public const string SECTION_DATA = "Data";
    public const string SECTION_DIAGNOSTICS = "Diagnostics";
    public const string SECTION_ABOUT = "About";

    private const string WEBSITE_URL = "https://witdatabase.io";
    private const string GITHUB_URL = "https://github.com/dmitrat/WitDatabase";

    #endregion

    #region Events

    /// <summary>Raised when the window showing this ViewModel should close.</summary>
    public event EventHandler? CloseRequested;

    #endregion

    #region Constructors

    public SettingsViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        InitDefaults();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefaults()
    {
        Sections =
        [
            SECTION_GENERAL, SECTION_EDITOR, SECTION_DATA, SECTION_DIAGNOSTICS, SECTION_ABOUT
        ];

        SelectedSection = SECTION_GENERAL;

        Languages = [.. ApplicationVm.Localization.Available];
        AvailableThemes = ["System", "Light", "Dark"];
        AvailableFontSizes = [10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24];
        KeywordCases = ["Upper", "Lower", "AsTyped"];
        DateTimeFormats = ["Iso", "System"];
        NumberFormats = ["Invariant", "System"];
        BinaryDisplays = ["Size", "Hex", "Base64"];
        PageSizes = [100, 200, 500, 1000, 2000, 5000];
        RowLimits = [100, 500, 1000, 5000, 10000];
        LogLevels = ["Errors", "Normal", "Verbose"];
        RecentCounts = [5, 10, 15, 20];

        SettingsFilePath = SettingsService.DefaultPath();
        LogFilePath = FileLoggerProvider.DefaultPath();
    }

    private void InitCommands()
    {
        ClearRecentCommand = new RelayCommandAsync(ClearRecentAsync);
        OpenLogFolderCommand = new RelayCommandAsync(OpenLogFolderAsync);
        OpenLastLogCommand = new RelayCommandAsync(OpenLastLogAsync);
        ResetSettingsCommand = new RelayCommandAsync(ResetSettingsAsync);
        ClearHistoryCommand = new RelayCommandAsync(ClearHistoryAsync);
        CopyDetailsCommand = new RelayCommandAsync(CopyDetailsAsync);
        OpenWebsiteCommand = new RelayCommandAsync(() => Dialogs.OpenUrlAsync(WEBSITE_URL));
        OpenSourcesCommand = new RelayCommandAsync(() => Dialogs.OpenUrlAsync(GITHUB_URL));
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    #endregion

    #region Functions

    /// <summary>Opens the window on a named section - Help &gt; About is this with "About".</summary>
    public void ShowSection(string section)
    {
        if (Sections.Contains(section))
            SelectedSection = section;
    }

    private async Task ClearRecentAsync()
    {
        await ApplicationVm.Settings.ClearRecentFilesAsync();

        OnPropertyChanged(nameof(RecentCount));
    }

    private Task OpenLogFolderAsync()
    {
        var folder = Path.GetDirectoryName(LogFilePath);

        return folder == null ? Task.CompletedTask : Dialogs.RevealAsync(folder);
    }

    private Task OpenLastLogAsync()
    {
        return Dialogs.RevealAsync(LogFilePath);
    }

    private async Task ResetSettingsAsync()
    {
        await ApplicationVm.Settings.ResetAsync();

        // Nothing else needs telling: the view is bound to the live object, and every property on it
        // has just raised its own change.
    }

    private async Task ClearHistoryAsync()
    {
        await ApplicationVm.History.ClearAsync();
    }

    /// <summary>
    /// The block a person pastes into an issue (WS-53). It is cheaper than a template in the tracker,
    /// which gets filled in halfway, and it carries the one thing nobody thinks to include - the file
    /// format version.
    /// </summary>
    public string Details()
    {
        return string.Join(Environment.NewLine,
            $"WitDatabase Studio {StudioVersion}",
            $"Engine           {EngineVersion}",
            $"File format      {FileFormatVersion}",
            $"Runtime          {Environment.Version}",
            $"OS               {Environment.OSVersion}",
            $"Avalonia         {AvaloniaVersion}");
    }

    private Task CopyDetailsAsync()
    {
        return Dialogs.CopyToClipboardAsync(Details());
    }

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            // The SDK appends "+<commit sha>" to the informational version, which is noise in a window
            // and in a bug report alike.
            var plus = informational.IndexOf('+');

            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    #endregion

    #region Properties

    /// <summary>
    /// The live settings. The view binds to this object's properties directly - see the class remarks
    /// for why there is no copy.
    /// </summary>
    public Models.Settings Values => ApplicationVm.Settings.Current;

    public ObservableCollection<string> Sections { get; private set; } = null!;

    [Notify]
    public string SelectedSection { get; set; } = SECTION_GENERAL;

    public ObservableCollection<LanguageOption> Languages { get; private set; } = null!;

    public ObservableCollection<string> AvailableThemes { get; private set; } = null!;

    public ObservableCollection<int> AvailableFontSizes { get; private set; } = null!;

    public ObservableCollection<string> KeywordCases { get; private set; } = null!;

    public ObservableCollection<string> DateTimeFormats { get; private set; } = null!;

    public ObservableCollection<string> NumberFormats { get; private set; } = null!;

    public ObservableCollection<string> BinaryDisplays { get; private set; } = null!;

    public ObservableCollection<int> PageSizes { get; private set; } = null!;

    public ObservableCollection<int> RowLimits { get; private set; } = null!;

    public ObservableCollection<string> LogLevels { get; private set; } = null!;

    public ObservableCollection<int> RecentCounts { get; private set; } = null!;

    /// <summary>How many databases the recent list is holding, so that "Clear" says what it will clear.</summary>
    public int RecentCount => Values.RecentFiles.Count;

    #endregion

    #region About

    public string StudioVersion { get; } = VersionOf(Assembly.GetExecutingAssembly());

    public string EngineVersion { get; } = VersionOf(typeof(WitDatabase).Assembly);

    public string AvaloniaVersion { get; } = VersionOf(typeof(Avalonia.Application).Assembly);

    /// <summary>
    /// The version of the file format this build writes, taken from the engine's own constant rather
    /// than from a number in a document. Shown because "8.x cannot read what 9.0.0 wrote" is exactly
    /// the question people ask in a panic, and the answer must not be a guess.
    /// </summary>
    public string FileFormatVersion { get; } =
        ((DatabaseConstants.FORMAT_VERSION >> 8) & 0xFF).ToString(CultureInfo.InvariantCulture)
        + "." + (DatabaseConstants.FORMAT_VERSION & 0xFF).ToString(CultureInfo.InvariantCulture);

    /// <summary>Where the settings and the history live. Shown rather than described.</summary>
    public string SettingsFilePath { get; private set; } = null!;

    public string LogFilePath { get; private set; } = null!;

    public string WebsiteUrl => WEBSITE_URL;

    public string SourcesUrl => GITHUB_URL;

    #endregion

    #region Commands

    public ICommand ClearRecentCommand { get; private set; } = null!;

    public ICommand OpenLogFolderCommand { get; private set; } = null!;

    public ICommand OpenLastLogCommand { get; private set; } = null!;

    public ICommand ResetSettingsCommand { get; private set; } = null!;

    public ICommand ClearHistoryCommand { get; private set; } = null!;

    public ICommand CopyDetailsCommand { get; private set; } = null!;

    public ICommand OpenWebsiteCommand { get; private set; } = null!;

    public ICommand OpenSourcesCommand { get; private set; } = null!;

    public ICommand CloseCommand { get; private set; } = null!;

    #endregion

    #region Services

    private IDialogService Dialogs => ApplicationVm.Dialogs;

    #endregion
}
