using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
// Avalonia 12 moved SetTextAsync off IClipboard and onto ClipboardExtensions - the same import the
// query tab needs for its copy commands.
using Avalonia.Input.Platform;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>One row of the keyboard window, with its words already in the reader's language.</summary>
public sealed record KeyboardRow(string Action, string Gesture, string Scope);

/// <summary>
/// The keyboard window (9.6, WS-69): a reference with a search over it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reassigning is NOT here, and that is a decision rather than an omission.</b> The design asks for
/// it - one field with the conflict shown before it applies - and it cannot be honest yet: the
/// gestures are declared in the window's markup and in its key handler, not read from
/// <see cref="KeyboardMap"/>. A box that accepted a new gesture would change nothing, which is worse
/// than not offering one. Making the keys data-driven is the piece of work that comes first, and the
/// window says so where a person can read it.
/// </para>
/// <para>
/// What IS here is the half that has to be true: the list is the same data
/// <c>KeyboardMapTests</c> checks against the application in both directions, so a shortcut that
/// stopped working, or one that was added, cannot stay out of this window quietly.
/// </para>
/// </remarks>
public class KeyboardHelpViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Constructors

    public KeyboardHelpViewModel(ApplicationViewModel applicationVm)
        : base(applicationVm)
    {
        Rows = [];

        CopyCommand = new RelayCommandAsync(CopyAsync);
        CloseCommand = new RelayCommand(() => ShouldCloseDialog?.Invoke());

        PropertyChanged += OnPropertyChanged;

        Refresh();
    }

    #endregion

    #region Events

    public event Action? ShouldCloseDialog;

    #endregion

    #region Functions

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Filter))
            Refresh();
    }

    /// <summary>
    /// The list, filtered by what was typed - over the ACTION and the GESTURE both, because a person
    /// arrives here from either end: "what does Ctrl+R do" and "how do I refresh".
    /// </summary>
    private void Refresh()
    {
        Rows.Clear();

        var isMac = KeyboardMap.IsMac;

        foreach (var shortcut in KeyboardMap.Shortcuts)
        {
            var row = new KeyboardRow(
                Localization[shortcut.ActionKey],
                KeyboardMap.Display(shortcut.Gesture, isMac),
                Localization[$"Keys.Scope.{shortcut.Scope}"]);

            if (Matches(row))
                Rows.Add(row);
        }

        IsEmpty = Rows.Count == 0;
    }

    private bool Matches(KeyboardRow row)
    {
        if (string.IsNullOrWhiteSpace(Filter))
            return true;

        var filter = Filter.Trim();

        return row.Action.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
               || row.Gesture.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The whole list as text, which is what somebody pasting it into an issue needs.
    /// </summary>
    /// <remarks>
    /// The FILTERED list, not all of it: what is on screen is what a person means by "copy this".
    /// </remarks>
    private async Task CopyAsync()
    {
        CopiedText = string.Join(Environment.NewLine,
            Rows.Select(row => $"{row.Gesture}\t{row.Action} ({row.Scope})"));

        var top = ApplicationVm.MainWindow == null
            ? null
            : Avalonia.Controls.TopLevel.GetTopLevel(ApplicationVm.MainWindow)?.Clipboard;

        if (top != null)
            await top.SetTextAsync(CopiedText);

        ApplicationVm.Notifications.Information(Localization["Keys.Copied"]);
    }

    #endregion

    #region Properties

    public ObservableCollection<KeyboardRow> Rows { get; }

    [Notify]
    public string? Filter { get; set; }

    [Notify]
    public bool IsEmpty { get; private set; }

    /// <summary>Why there is no rebinding here, in the reader's language.</summary>
    public string RebindingNote => Localization["Keys.NoRebinding"];

    /// <summary>
    /// What the last Copy put on the clipboard. Kept because a test cannot read the real clipboard on
    /// a headless run, and "the command did not throw" is not evidence that anything was copied.
    /// </summary>
    [Notify]
    public string? CopiedText { get; private set; }

    #endregion

    #region Commands

    public ICommand CopyCommand { get; }

    public ICommand CloseCommand { get; }

    #endregion

    #region Tools

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
