using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// What the migration is being asked to change about the encryption.
/// </summary>
public enum PasswordChange
{
    /// <summary>A new password for a database that already has one.</summary>
    Replace,

    /// <summary>Encryption where there was none.</summary>
    Encrypt,

    /// <summary>No encryption where there was some.</summary>
    Remove
}

/// <summary>
/// Changing the password (WS-58), which is a migration into a new database.
/// </summary>
/// <remarks>
/// <para>
/// <b>The current password is not asked for, and that is deliberate.</b> The design's window has a
/// field for it; the database is already open in front of the user, which means the password has
/// already been given and accepted. A field that is not checked against anything is theatre, and this
/// application has spent two stages removing exactly that kind of thing.
/// </para>
/// <para>
/// <b>The original is not touched</b>, which is what makes this safe to try and what the window says
/// before it starts.
/// </para>
/// </remarks>
public sealed class ChangePasswordViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseSession m_session;

    #endregion

    #region Constructors

    public ChangePasswordViewModel(ApplicationViewModel applicationVm, IDatabaseSession session)
        : base(applicationVm)
    {
        m_session = session;

        Heading = Localization.Format("Password.Heading", session.DisplayName);
        WasEncrypted = session.Connection.IsEncrypted;
        Change = WasEncrypted ? PasswordChange.Replace : PasswordChange.Encrypt;

        Destination = Suggest(session.Connection.FilePath ?? string.Empty);

        InitCommands();

        // Typing into a box has to reach the button. A RelayCommand does not re-ask its CanExecute
        // because a property changed - it has to be told - and this is the THIRD time in one day that
        // a button stayed grey in front of a filled-in form for exactly that reason. Wired here rather
        // than at each setter so that a field added later cannot forget it.
        PropertyChanged += (_, args) =>
        {
            Refresh();

            if (args.PropertyName != nameof(Change))
                return;

            // Two computed properties are bound in markup and BOTH read Change, which moves whenever
            // a radio button is pressed. A computed property is the right answer to everyone who ASKS
            // and an announcement to nobody - the phase-17 census found 23 of these and IsFiltering
            // was the one that mattered. NeedsPassword was already one of them: choosing "remove the
            // encryption" did not hide the password fields, because nothing ever said it had changed.
            OnPropertyChanged(nameof(NeedsPassword));
            OnPropertyChanged(nameof(IsRewrap));
            OnPropertyChanged(nameof(ActionLabel));
        };
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        MigrateCommand = new RelayCommandAsync(MigrateAsync, () => !IsRunning && CanMigrate);
        CloseCommand = new RelayCommand(() => ShouldCloseDialog?.Invoke(IsDone));
    }

    #endregion

    #region Functions

    private async Task MigrateAsync()
    {
        Steps.Clear();

        IsRunning = true;
        IsDone = false;
        Message = Localization[IsRewrap ? "Password.Rewrapping" : "Password.Running"];

        Refresh();

        if (IsRewrap)
        {
            Rewrap();
            return;
        }

        try
        {
            var target = m_session.Connection.Clone();

            target.FilePath = Destination;
            target.DisplayName = Path.GetFileNameWithoutExtension(Destination);
            target.IsEncrypted = Change != PasswordChange.Remove;
            target.Password = target.IsEncrypted ? NewPassword : null;

            var progress = new Progress<MigrationStep>(step => Steps.Add(Describe(step)));

            var result = await DatabaseMigrator.MigrateAsync(m_session, target, progress);

            Message = Describe(result);
            IsDone = result.Outcome != MigrationOutcome.Failed;

            if (IsDone && ConnectAfterwards)
                await ApplicationVm.OpenDatabaseAsync(target);
        }
        catch (Exception ex)
        {
            Message = Localization.Format("Password.Failed", ex.Message);
            Logger.LogError(ex, "The migration of {Name} did not finish", m_session.DisplayName);
        }
        finally
        {
            IsRunning = false;

            Refresh();
        }
    }

    /// <summary>
    /// Replacing a password on a database that already has one: a rewrap of the wrapped key, which
    /// is 60 bytes in the preamble and leaves every page alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The current password is not asked for here either, and now there is a second reason.</b>
    /// It was already given when this database was opened, and Studio still holds it - so the rewrap
    /// is handed the password the connection is using rather than one typed again. A field that
    /// re-asks for something already accepted is the theatre this window was written to avoid.
    /// </para>
    /// <para>
    /// Synchronous on purpose: it is one page write. Wrapping it in a task and a progress list would
    /// dress up an operation that finishes before the window could redraw.
    /// </para>
    /// </remarks>
    private void Rewrap()
    {
        try
        {
            m_session.ChangePassword(m_session.Connection.Password!, NewPassword!);

            Steps.Add(Localization["Password.Step.Rewrapped"]);

            Message = Localization["Password.RewrapDone"];
            IsDone = true;
        }
        catch (Exception ex)
        {
            Message = Localization.Format("Password.Failed", ex.Message);
            Logger.LogError(ex, "The password of {Name} was not replaced", m_session.DisplayName);
        }
        finally
        {
            IsRunning = false;

            Refresh();
        }
    }

    private string Describe(MigrationStep step) =>
        step.Detail == null
            ? Localization[step.Key]
            : Localization.Format(step.Key, step.Detail);

    /// <summary>
    /// What the migration did, with the verification in it - which is the point of the whole window.
    /// </summary>
    private string Describe(MigrationResult result)
    {
        if (result.Outcome == MigrationOutcome.Failed)
            return Localization.Format("Password.Failed", result.EngineMessage);

        var moved = Localization.Format("Password.Moved",
            Localization.Plural("Count.Tables", result.Verification.Count),
            Localization.Plural("Count.Rows", result.RowsTransferred));

        if (result.Mismatches.Count == 0)
            return $"{moved} · {Localization["Password.RowsMatch"]}";

        var names = string.Join(", ", result.Mismatches.Select(check =>
            Localization.Format("Password.Mismatch", check.Table, check.InSource, check.InTarget)));

        return $"{moved} · {Localization.Format("Password.RowsDoNotMatch", names)}";
    }

    private void Refresh()
    {
        (MigrateCommand as RelayCommandAsync)?.RaiseCanExecuteChanged();
    }

    private static string Suggest(string source)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        var trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folder = Path.GetDirectoryName(trimmed) ?? string.Empty;

        return Path.Combine(folder,
            $"{Path.GetFileNameWithoutExtension(trimmed)}-new{Path.GetExtension(trimmed)}");
    }

    #endregion

    #region Events

    /// <summary>Raised when the dialog showing this ViewModel should close.</summary>
    public event Action<bool>? ShouldCloseDialog;

    #endregion

    #region Properties

    public string Heading { get; }

    /// <summary>Whether the database being migrated is encrypted today.</summary>
    public bool WasEncrypted { get; }

    [Notify] public PasswordChange Change { get; set; }

    /// <summary>Whether a new password has to be typed at all - not when it is being removed.</summary>
    public bool NeedsPassword => Change != PasswordChange.Remove;

    [Notify] public string? NewPassword { get; set; }

    [Notify] public string? NewPasswordAgain { get; set; }

    [Notify] public string Destination { get; set; } = string.Empty;

    /// <summary>Whether to open the new database when the transfer is done.</summary>
    [Notify] public bool ConnectAfterwards { get; set; } = true;

    /// <summary>
    /// Whether this change is a REWRAP rather than a migration - replacing a password on a database
    /// that already has one, which the engine does in 60 bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three conditions, and each one is load-bearing. It has to be a <b>replacement</b>: encrypting
    /// a database that is not encrypted has no wrapped key to rewrap, and removing encryption cannot
    /// leave the pages as ciphertext - both stay migrations and always will. The <b>engine</b> has to
    /// offer it, which is false for a database whose caller owns the key. And Studio has to
    /// <b>know the current password</b>, because unwrapping the key with it is the only check there
    /// is - offered without one, the button would meet an argument exception rather than a refusal.
    /// </para>
    /// </remarks>
    public bool IsRewrap =>
        Change == PasswordChange.Replace
        && m_session.CanChangePassword
        && !string.IsNullOrEmpty(m_session.Connection.Password);

    /// <summary>
    /// What the button does, which is not the same thing in the two cases.
    /// </summary>
    /// <remarks>
    /// «Transfer» is the migration's word and it was on the button for both until 2026-08-15, next to
    /// a checkbox offering to connect to a new database that a rewrap never creates. A branch added
    /// under chrome that still describes the other operation is this application's most familiar
    /// defect - the same shape as a status bar reading «Editing» over a table whose every editing
    /// control is disabled.
    /// </remarks>
    public string ActionLabel =>
        Localization[IsRewrap ? "Password.ReplaceAction" : "Password.Migrate"];

    public bool CanMigrate =>
        (IsRewrap || !string.IsNullOrWhiteSpace(Destination))
        && (!NeedsPassword
            || (!string.IsNullOrEmpty(NewPassword) && NewPassword == NewPasswordAgain));

    [Notify] public bool IsRunning { get; private set; }

    [Notify] public bool IsDone { get; private set; }

    [Notify] public string Message { get; private set; } = string.Empty;

    /// <summary>The steps, as they happen - the same shape the rebuild dialog uses.</summary>
    public ObservableCollection<string> Steps { get; } = [];

    #endregion

    #region Commands

    [Notify] public ICommand MigrateCommand { get; private set; } = null!;

    [Notify] public ICommand CloseCommand { get; private set; } = null!;

    #endregion

    #region Services

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}

/// <summary>
/// Turns the chosen change into "is this the one" for a radio button, and back.
/// </summary>
/// <remarks>
/// The same shape <c>StructureSectionConverter</c> uses, and for the same reason: converting back
/// returns the value named by the parameter when the button is checked, and refuses to answer when it
/// is not - an unchecked radio button must not decide anything.
/// </remarks>
public sealed class PasswordChangeConverter : Avalonia.Data.Converters.IValueConverter
{
    public static PasswordChangeConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
    {
        return value is PasswordChange change
               && parameter is string name
               && Enum.TryParse<PasswordChange>(name, out var expected)
               && change == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
    {
        if (value is true && parameter is string name && Enum.TryParse<PasswordChange>(name, out var change))
            return change;

        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
