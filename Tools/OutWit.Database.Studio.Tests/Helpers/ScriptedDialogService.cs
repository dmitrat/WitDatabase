using OutWit.Database.Studio.Services;
using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Tests.Helpers;

/// <summary>
/// Stands in for the windows, and for nothing else.
///
/// A picker returns the path this was told to return - the same string the user's click would have
/// produced. The connection dialogs go further and do what the user does inside them: put the path
/// into the ViewModel and press Connect. So the REAL ConnectionViewModel runs - the builder calls,
/// the connection string, the refusals - and only the window is missing.
///
/// That is what makes "open a database and watch the schema load" testable at all. It used to need
/// Avalonia, so it was never tested; the dialogs were the one place Studio configures the engine and
/// the one place nothing had ever run against it.
/// </summary>
public sealed class ScriptedDialogService : IDialogService
{
    #region Fields

    private readonly Func<ConnectionViewModel, Task<bool>>? m_onConnectionDialog;

    #endregion

    #region Constructors

    /// <param name="path">What every picker answers. Null means the user cancelled.</param>
    /// <param name="onConnectionDialog">
    /// What happens inside the Open/Create dialog. Null means the user closed it without connecting.
    /// </param>
    public ScriptedDialogService(string? path = null, Func<ConnectionViewModel, Task<bool>>? onConnectionDialog = null)
    {
        PickedPath = path;
        m_onConnectionDialog = onConnectionDialog;
    }

    /// <summary>
    /// The usual script: the user chooses this path in the dialog and presses Connect.
    /// </summary>
    public static ScriptedDialogService OpeningDatabase(string path, Func<ConnectionViewModel, Task> press)
    {
        return new ScriptedDialogService(path, async viewModel =>
        {
            viewModel.ConnectionInfo.FilePath = path;
            viewModel.ApplyAutoDetectedSettings(path);

            await press(viewModel);

            return viewModel.DialogResult;
        });
    }

    #endregion

    #region IDialogService

    public Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter>? filters = null)
        => Record(nameof(OpenFileAsync), title);

    public Task<string?> SaveFileAsync(string title, string? suggestedFileName = null,
        string? defaultExtension = null, IReadOnlyList<FileFilter>? filters = null)
        => Record(nameof(SaveFileAsync), title);

    public Task<string?> OpenFolderAsync(string title) => Record(nameof(OpenFolderAsync), title);

    public Task<bool> ShowOpenDatabaseAsync(ConnectionViewModel viewModel) => ShowConnection(viewModel);

    public Task<bool> ShowCreateDatabaseAsync(ConnectionViewModel viewModel) => ShowConnection(viewModel);

    public Task ShowSettingsAsync(SettingsViewModel viewModel)
    {
        Shown.Add(nameof(ShowSettingsAsync));
        LastSettings = viewModel;

        return Task.CompletedTask;
    }

    public Task ShowConnectionsAsync(ConnectionsViewModel viewModel) => Note(nameof(ShowConnectionsAsync));

    /// <summary>
    /// The desktop calls record what they were asked to open rather than opening it. That is enough to
    /// check the thing worth checking - that "open the log folder" asks for the folder the log is in
    /// and not for the log, and that the About block put on the clipboard is the one a bug report needs.
    /// </summary>
    public Task<bool> RevealAsync(string path)
    {
        Revealed.Add(path);
        return Task.FromResult(true);
    }

    public Task<bool> OpenUrlAsync(string url)
    {
        Opened.Add(url);
        return Task.FromResult(true);
    }

    public Task<bool> CopyToClipboardAsync(string text)
    {
        Copied = text;
        return Task.FromResult(true);
    }

    public Task ShowExportAsync(ExportViewModel viewModel) => Note(nameof(ShowExportAsync));

    public Task ShowImportAsync(ImportViewModel viewModel) => Note(nameof(ShowImportAsync));

    public Task<bool> ShowCreateTableAsync(CreateTableViewModel viewModel)
        => NoteResult(nameof(ShowCreateTableAsync));

    public Task<bool> ShowCreateViewAsync(CreateViewViewModel viewModel)
        => NoteResult(nameof(ShowCreateViewAsync));

    public Task<bool> ShowCreateIndexAsync(CreateIndexViewModel viewModel)
        => NoteResult(nameof(ShowCreateIndexAsync));

    /// <summary>
    /// The rebuild dialog. Like the connection dialogs, a test can say what the user does inside it -
    /// which is how "press Rebuild and watch the table come back with the new shape" is written
    /// without Avalonia.
    /// </summary>
    public Task<bool> ShowTableRebuildAsync(TableRebuildViewModel viewModel)
    {
        Shown.Add(nameof(ShowTableRebuildAsync));
        LastRebuild = viewModel;

        return OnRebuildDialog?.Invoke(viewModel) ?? Task.FromResult(false);
    }

    public Task<bool> ShowEditTriggerAsync(EditTriggerViewModel viewModel)
    {
        Shown.Add(nameof(ShowEditTriggerAsync));
        LastTrigger = viewModel;

        return OnTriggerDialog?.Invoke(viewModel) ?? Task.FromResult(false);
    }

    /// <summary>
    /// Verification by reading (WS-61). The ViewModel is kept so that a test can run the check the
    /// way the dialog's button would, rather than calling the service behind it.
    /// </summary>
    public Task<bool> ShowReadCheckAsync(ReadCheckViewModel viewModel)
    {
        Shown.Add(nameof(ShowReadCheckAsync));
        LastReadCheck = viewModel;

        return OnReadCheckDialog?.Invoke(viewModel) ?? Task.FromResult(false);
    }

    /// <summary>The byte copy (WS-59), kept so a test can drive it as the button would.</summary>
    public Task<bool> ShowDatabaseCopyAsync(DatabaseCopyViewModel viewModel)
    {
        Shown.Add(nameof(ShowDatabaseCopyAsync));
        LastCopy = viewModel;

        return OnCopyDialog?.Invoke(viewModel) ?? Task.FromResult(false);
    }

    #endregion

    #region Tools

    private Task<string?> Record(string method, string title)
    {
        Shown.Add($"{method}: {title}");
        return Task.FromResult(PickedPath);
    }

    private Task Note(string method)
    {
        Shown.Add(method);
        return Task.CompletedTask;
    }

    private Task<bool> NoteResult(string method)
    {
        Shown.Add(method);
        return Task.FromResult(false);
    }

    private Task<bool> ShowConnection(ConnectionViewModel viewModel)
    {
        Shown.Add(nameof(ShowConnection));

        return m_onConnectionDialog?.Invoke(viewModel) ?? Task.FromResult(false);
    }

    #endregion

    #region Properties

    /// <summary>What the pickers answer.</summary>
    public string? PickedPath { get; set; }

    /// <summary>Every dialog and picker this was asked for, in order.</summary>
    public List<string> Shown { get; } = [];

    /// <summary>What the user does inside the rebuild dialog. Null means they closed it.</summary>
    public Func<TableRebuildViewModel, Task<bool>>? OnRebuildDialog { get; set; }

    /// <summary>What the user does inside the trigger editor.</summary>
    public Func<EditTriggerViewModel, Task<bool>>? OnTriggerDialog { get; set; }

    /// <summary>The last rebuild ViewModel shown - the plan it was built with is worth asserting on.</summary>
    public TableRebuildViewModel? LastRebuild { get; private set; }

    public EditTriggerViewModel? LastTrigger { get; private set; }

    /// <summary>What the user does inside the read check.</summary>
    public Func<ReadCheckViewModel, Task<bool>>? OnReadCheckDialog { get; set; }

    /// <summary>The last read-check ViewModel shown, so a test can run it as the button would.</summary>
    public ReadCheckViewModel? LastReadCheck { get; private set; }

    /// <summary>What the user does inside the copy dialog.</summary>
    public Func<DatabaseCopyViewModel, Task<bool>>? OnCopyDialog { get; set; }

    /// <summary>The last copy ViewModel shown.</summary>
    public DatabaseCopyViewModel? LastCopy { get; private set; }

    /// <summary>The settings ViewModel last shown - which section it opened on is worth asserting.</summary>
    public SettingsViewModel? LastSettings { get; private set; }

    /// <summary>Paths handed to <see cref="RevealAsync"/>, in order.</summary>
    public List<string> Revealed { get; } = [];

    /// <summary>URLs handed to <see cref="OpenUrlAsync"/>, in order.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>The last text put on the clipboard.</summary>
    public string? Copied { get; private set; }

    #endregion
}
