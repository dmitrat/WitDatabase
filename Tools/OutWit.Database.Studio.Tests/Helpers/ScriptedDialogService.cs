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

    public Task ShowSettingsAsync(SettingsViewModel viewModel) => Note(nameof(ShowSettingsAsync));

    public Task ShowAboutAsync(AboutViewModel viewModel) => Note(nameof(ShowAboutAsync));

    public Task ShowExportAsync(ExportViewModel viewModel) => Note(nameof(ShowExportAsync));

    public Task ShowImportAsync(ImportViewModel viewModel) => Note(nameof(ShowImportAsync));

    public Task<bool> ShowCreateTableAsync(CreateTableViewModel viewModel)
        => NoteResult(nameof(ShowCreateTableAsync));

    public Task<bool> ShowCreateViewAsync(CreateViewViewModel viewModel)
        => NoteResult(nameof(ShowCreateViewAsync));

    public Task<bool> ShowCreateIndexAsync(CreateIndexViewModel viewModel)
        => NoteResult(nameof(ShowCreateIndexAsync));

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

    #endregion
}
