using System.Globalization;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// A byte copy of the database (WS-59), and opening it as a separate connection afterwards (WS-60).
/// </summary>
/// <remarks>
/// <para>
/// <b>Restoring is not an operation here.</b> Putting a copy back means closing the connection, moving
/// a file and opening it again; a "restore" button would promise a rollback inside a running
/// application that is not going to happen. What Studio does instead is open the copy as another
/// connection, which is the thing it can actually do and the thing that lets the two be compared.
/// </para>
/// <para>
/// <b>A paged database is closed for the duration.</b> Measured: while the engine holds the file
/// nothing can read it - not <c>File.Copy</c>, not a stream that asks for every share flag there is.
/// So the copy closes the connection, takes the bytes and opens it again, and the dialog says so
/// BEFORE anything happens: the reopened database is a new session, and a tab whose connection closed
/// keeps its text but does not adopt the next one.
/// </para>
/// </remarks>
public sealed class DatabaseCopyViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private IDatabaseSession m_session;

    #endregion

    #region Constructors

    public DatabaseCopyViewModel(ApplicationViewModel applicationVm, IDatabaseSession session)
        : base(applicationVm)
    {
        m_session = session;

        Source = session.Connection.FilePath ?? string.Empty;
        IsDirectory = Directory.Exists(Source);

        Heading = Localization.Format("Copy.Heading", session.DisplayName);
        Destination = SuggestDestination(Source, IsDirectory);

        Parts = string.Join(", ", DatabaseCopier.PartsOf(Source).Select(Path.GetFileName));

        // Said before anything happens rather than after it fails.
        WillCloseTheConnection = !IsDirectory;
        HasOpenTransaction = session.HasOpenTransaction;

        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        CopyCommand = new RelayCommandAsync(CopyAsync, () => !IsRunning);
        OpenCopyCommand = new RelayCommandAsync(OpenCopyAsync, () => IsDone);
        CloseCommand = new RelayCommand(() => ShouldCloseDialog?.Invoke(IsDone));
    }

    #endregion

    #region Functions

    private async Task CopyAsync()
    {
        if (string.IsNullOrWhiteSpace(Destination))
            return;

        IsRunning = true;
        IsDone = false;
        Message = Localization["Copy.Running"];

        RefreshCommands();

        try
        {
            var connection = m_session.Connection.Clone();
            var closed = false;

            // A paged database has to be let go of. The connection is reopened whether the copy worked
            // or not - leaving the user's database closed because a BACKUP failed would be the worse
            // half of two bad outcomes.
            if (WillCloseTheConnection && m_session.IsConnected)
            {
                await ApplicationVm.Connections.CloseAsync(m_session);
                closed = true;
            }

            var result = await DatabaseCopier.CopyAsync(m_session, Destination, Verify);

            if (closed && await ApplicationVm.Connections.OpenAsync(connection) is { } reopened)
                m_session = reopened;

            Message = Describe(result);
            IsDone = result.Outcome == CopyOutcome.Copied;
        }
        catch (Exception ex)
        {
            Message = Localization.Format("Copy.Failed", ex.Message);
            Logger.LogError(ex, "The copy of {Name} did not finish", m_session.DisplayName);
        }
        finally
        {
            IsRunning = false;

            RefreshCommands();
        }
    }

    /// <summary>
    /// WS-60: the copy is opened as its own connection, in its own colour, beside the original.
    /// </summary>
    private async Task OpenCopyAsync()
    {
        var connection = m_session.Connection.Clone();

        connection.FilePath = Destination;
        connection.DisplayName = Path.GetFileNameWithoutExtension(
            Destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (await ApplicationVm.Connections.OpenAsync(connection) != null)
            ShouldCloseDialog?.Invoke(true);
    }

    private string Describe(CopyResult result)
    {
        if (result.Outcome == CopyOutcome.SourceIsHeldOpen)
            return Localization["Copy.HeldOpen"];

        if (result.Outcome == CopyOutcome.Failed)
            return Localization.Format("Copy.Failed", result.EngineMessage);

        var copied = Localization.Format("Copy.Done",
            Localization.Plural("Count.Parts", result.Parts.Count), ByteSize.Format(result.Bytes));

        return result.Verified switch
        {
            true => $"{copied} · {Localization.Format("Copy.Verified", result.ObjectsInCopy)}",
            false => $"{copied} · {Localization.Format("Copy.NotVerified", result.EngineMessage)}",
            null => copied
        };
    }

    private void RefreshCommands()
    {
        (CopyCommand as RelayCommandAsync)?.RaiseCanExecuteChanged();
        (OpenCopyCommand as RelayCommandAsync)?.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// A name with today's date in it, which is what a person about to take a backup would type.
    /// </summary>
    private static string SuggestDestination(string source, bool isDirectory)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        var trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folder = Path.GetDirectoryName(trimmed) ?? string.Empty;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return isDirectory
            ? Path.Combine(folder, $"{Path.GetFileName(trimmed)}-{stamp}")
            : Path.Combine(folder,
                $"{Path.GetFileNameWithoutExtension(trimmed)}-{stamp}{Path.GetExtension(trimmed)}");
    }

    #endregion

    #region Events

    /// <summary>Raised when the dialog showing this ViewModel should close.</summary>
    public event Action<bool>? ShouldCloseDialog;

    #endregion

    #region Properties

    public string Heading { get; }

    public string Source { get; }

    /// <summary>An LSM database is a folder, and it copies without being closed.</summary>
    public bool IsDirectory { get; }

    /// <summary>The files and folders that will be taken - longer than a person expects.</summary>
    public string Parts { get; }

    [Notify] public string Destination { get; set; } = string.Empty;

    /// <summary>Whether to open the copy afterwards and read its schema.</summary>
    [Notify] public bool Verify { get; set; } = true;

    /// <summary>Whether taking this copy means letting go of the database first.</summary>
    public bool WillCloseTheConnection { get; }

    /// <summary>
    /// A transaction is open, so a copy taken now would be of a database mid-decision.
    /// </summary>
    public bool HasOpenTransaction { get; }

    [Notify] public bool IsRunning { get; private set; }

    [Notify] public bool IsDone { get; private set; }

    [Notify] public string Message { get; private set; } = string.Empty;

    #endregion

    #region Commands

    [Notify] public ICommand CopyCommand { get; private set; } = null!;

    [Notify] public ICommand OpenCopyCommand { get; private set; } = null!;

    [Notify] public ICommand CloseCommand { get; private set; } = null!;

    #endregion

    #region Services

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
