using System.Reflection;
using System.Windows.Input;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// What to do about a newer Studio (9.8, WS-70): a message and a link, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no auto-update and there is no download.</b> The installers are signed, but replacing
/// the executable underneath a running application that holds a database open is a risk out of all
/// proportion to saving one click. The three answers are the design's: open the page, remind me later,
/// or skip this version.
/// </para>
/// <para>
/// <b>"Skip" is per version, not for good</b> - a person who skips 3.1.0 still hears about 3.2.0.
/// Skipping for good is what the checkbox in the settings does, reversibly and where it can be found.
/// </para>
/// </remarks>
public class UpdateViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public UpdateViewModel(ApplicationViewModel applicationVm, UpdateDecision decision)
        : base(applicationVm)
    {
        Decision = decision;

        OpenPageCommand = new RelayCommandAsync(OpenPageAsync);
        RemindLaterCommand = new RelayCommand(Close);
        SkipCommand = new RelayCommandAsync(SkipAsync);
    }

    #endregion

    #region Events

    public event Action? ShouldCloseDialog;

    #endregion

    #region Functions

    private async Task OpenPageAsync()
    {
        if (!string.IsNullOrEmpty(Decision.Release?.Url))
            await ApplicationVm.Dialogs.OpenUrlAsync(Decision.Release.Url);

        Close();
    }

    /// <summary>
    /// Remembers the version so this one is not offered again - and only this one.
    /// </summary>
    public async Task SkipAsync()
    {
        var settings = await ApplicationVm.Settings.LoadAsync();

        settings.SkippedUpdate = Decision.Version;

        await ApplicationVm.Settings.SaveAsync(settings);

        Close();
    }

    private void Close() => ShouldCloseDialog?.Invoke();

    #endregion

    #region Properties

    public UpdateDecision Decision { get; }

    /// <summary>What is running, as the assembly reports it.</summary>
    public static string CurrentVersion { get; } =
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    /// <summary>"Version 3.1.0 is available - you have 3.0.0".</summary>
    public string Headline => Localization.Format("Update.Available",
        Decision.Version ?? string.Empty, CurrentVersion);

    /// <summary>
    /// What changed, in the publisher's words. Never translated (WS-64) and never invented: when the
    /// release has no notes, the block is absent rather than filled with an apology.
    /// </summary>
    public string? Notes => string.IsNullOrWhiteSpace(Decision.Release?.Notes) ? null : Decision.Release!.Notes;

    public bool HasNotes => Notes != null;

    #endregion

    #region Commands

    public ICommand OpenPageCommand { get; }

    public ICommand RemindLaterCommand { get; }

    public ICommand SkipCommand { get; }

    #endregion

    #region Tools

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
