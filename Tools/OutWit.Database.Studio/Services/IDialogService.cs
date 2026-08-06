using OutWit.Database.Studio.ViewModels;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// A file type offered by a picker, in the ViewModel layer's own terms - no Avalonia type crosses
/// this line.
/// </summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>
/// Everything Studio shows on top of the main window: pickers and dialogs.
///
/// It exists so that no ViewModel knows what a Window is. They did: ConnectionViewModel reached
/// through <c>ApplicationVm.MainWindow.StorageProvider</c> and constructed
/// <c>new CreateDatabaseDialog()</c> itself, MainWindowViewModel called four <c>ShowAsync</c> methods
/// on concrete windows, and the explorer three more. The cost was not tidiness: the scenario "open a
/// database and watch the schema load" could not be run without Avalonia, which is a large part of why
/// 249 tests drove a disconnected double instead.
///
/// Every method answers with data - a path, a yes or no - never with a window.
/// </summary>
public interface IDialogService
{
    #region Pickers

    /// <summary>Asks for an existing file. Null when the user cancels, or when there is no window.</summary>
    Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter>? filters = null);

    /// <summary>Asks where to write a file. Null when the user cancels.</summary>
    Task<string?> SaveFileAsync(
        string title,
        string? suggestedFileName = null,
        string? defaultExtension = null,
        IReadOnlyList<FileFilter>? filters = null);

    /// <summary>Asks for a folder - an LSM database is one, and so is an export target.</summary>
    Task<string?> OpenFolderAsync(string title);

    #endregion

    #region The desktop

    /// <summary>
    /// Opens a file or folder in whatever the system uses for it. The diagnostics section needs it for
    /// the log folder and the last log: "open the folder" saves half the correspondence on an issue,
    /// and telling someone a path and asking them to find it does not.
    /// </summary>
    Task<bool> RevealAsync(string path);

    /// <summary>Opens a URL in the system browser.</summary>
    Task<bool> OpenUrlAsync(string url);

    /// <summary>Puts text on the clipboard - the About section's block of versions for a bug report.</summary>
    Task<bool> CopyToClipboardAsync(string text);

    #endregion

    #region Windows

    /// <summary>Shows the Open dialog over the given ViewModel; true when a connection was made.</summary>
    Task<bool> ShowOpenDatabaseAsync(ConnectionViewModel viewModel);

    /// <summary>Shows the Create dialog; true when a database was created and connected to.</summary>
    Task<bool> ShowCreateDatabaseAsync(ConnectionViewModel viewModel);

    Task ShowSettingsAsync(SettingsViewModel viewModel);

    Task ShowExportAsync(ExportViewModel viewModel);

    Task ShowImportAsync(ImportViewModel viewModel);

    Task<bool> ShowCreateTableAsync(CreateTableViewModel viewModel);

    Task<bool> ShowCreateViewAsync(CreateViewViewModel viewModel);

    Task<bool> ShowCreateIndexAsync(CreateIndexViewModel viewModel);

    /// <summary>
    /// The rebuild conversation of 5.3. True when the table was rebuilt.
    /// </summary>
    Task<bool> ShowTableRebuildAsync(TableRebuildViewModel viewModel);

    /// <summary>
    /// The trigger editor (WS-45). True when a trigger was created or replaced.
    /// </summary>
    Task<bool> ShowEditTriggerAsync(EditTriggerViewModel viewModel);

    #endregion
}

/// <summary>
/// The answer when nothing has supplied a real dialog service - a headless run, a design-time
/// instance, a host that forgot to register one.
///
/// Every picker returns null and every dialog returns false: nothing was chosen, because nobody was
/// asked. Deliberately not an exception - a ViewModel that cannot show a dialog should carry on being
/// a ViewModel, and a test that does not care about dialogs should not have to stub nine methods.
/// </summary>
public sealed class NoDialogService : IDialogService
{
    public Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter>? filters = null)
        => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(string title, string? suggestedFileName = null,
        string? defaultExtension = null, IReadOnlyList<FileFilter>? filters = null)
        => Task.FromResult<string?>(null);

    public Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);

    public Task<bool> RevealAsync(string path) => Task.FromResult(false);

    public Task<bool> OpenUrlAsync(string url) => Task.FromResult(false);

    public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(false);

    public Task<bool> ShowOpenDatabaseAsync(ConnectionViewModel viewModel) => Task.FromResult(false);

    public Task<bool> ShowCreateDatabaseAsync(ConnectionViewModel viewModel) => Task.FromResult(false);

    public Task ShowSettingsAsync(SettingsViewModel viewModel) => Task.CompletedTask;

    public Task ShowExportAsync(ExportViewModel viewModel) => Task.CompletedTask;

    public Task ShowImportAsync(ImportViewModel viewModel) => Task.CompletedTask;

    public Task<bool> ShowCreateTableAsync(CreateTableViewModel viewModel) => Task.FromResult(false);

    public Task<bool> ShowCreateViewAsync(CreateViewViewModel viewModel) => Task.FromResult(false);

    public Task<bool> ShowCreateIndexAsync(CreateIndexViewModel viewModel) => Task.FromResult(false);

    public Task<bool> ShowTableRebuildAsync(TableRebuildViewModel viewModel) => Task.FromResult(false);

    public Task<bool> ShowEditTriggerAsync(EditTriggerViewModel viewModel) => Task.FromResult(false);
}
