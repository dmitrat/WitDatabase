using Avalonia.Controls;
// Avalonia 12 moved SetTextAsync off IClipboard and onto ClipboardExtensions.
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OutWit.Database.Studio.ViewModels;
using OutWit.Database.Studio.Views.Dialogs;

namespace OutWit.Database.Studio.Services;

/// <summary>
/// The Avalonia side of <see cref="IDialogService"/> - the only place in Studio that constructs a
/// window, and the only one that needs to.
///
/// The owner arrives through a callback rather than being held: the ViewModel graph is built before
/// any window exists, and this service outlives every one of them.
/// </summary>
public sealed class DialogService : IDialogService
{
    #region Fields

    private readonly Func<Window?> m_owner;

    #endregion

    #region Constructors

    public DialogService(Func<Window?> owner)
    {
        m_owner = owner;
    }

    #endregion

    #region Pickers

    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<FileFilter>? filters = null)
    {
        var owner = m_owner();

        if (owner == null)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = Convert(filters)
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public async Task<string?> SaveFileAsync(
        string title,
        string? suggestedFileName = null,
        string? defaultExtension = null,
        IReadOnlyList<FileFilter>? filters = null)
    {
        var owner = m_owner();

        if (owner == null)
            return null;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = defaultExtension,
            FileTypeChoices = Convert(filters)
        });

        return file?.Path.LocalPath;
    }

    public async Task<string?> OpenFolderAsync(string title)
    {
        var owner = m_owner();

        if (owner == null)
            return null;

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    private static List<FilePickerFileType>? Convert(IReadOnlyList<FileFilter>? filters)
    {
        return filters?
            .Select(filter => new FilePickerFileType(filter.Name) { Patterns = [.. filter.Patterns] })
            .ToList();
    }

    #endregion

    #region The desktop

    /// <summary>
    /// Through Avalonia's launcher rather than <c>Process.Start</c>: Studio ships on three platforms
    /// and the shell command differs on each. A path that does not exist answers false rather than
    /// throwing - the log file has not been written yet on a first run, and that is not a failure.
    /// </summary>
    public async Task<bool> RevealAsync(string path)
    {
        var owner = m_owner();

        if (owner == null || string.IsNullOrWhiteSpace(path))
            return false;

        var launcher = TopLevel.GetTopLevel(owner)?.Launcher;

        if (launcher == null)
            return false;

        if (File.Exists(path))
            return await launcher.LaunchFileInfoAsync(new FileInfo(path));

        return Directory.Exists(path) && await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
    }

    public async Task<bool> OpenUrlAsync(string url)
    {
        var owner = m_owner();

        var launcher = owner == null ? null : TopLevel.GetTopLevel(owner)?.Launcher;

        return launcher != null && await launcher.LaunchUriAsync(new Uri(url));
    }

    public async Task<bool> CopyToClipboardAsync(string text)
    {
        var owner = m_owner();

        var clipboard = owner == null ? null : TopLevel.GetTopLevel(owner)?.Clipboard;

        if (clipboard == null)
            return false;

        await clipboard.SetTextAsync(text);

        return true;
    }

    #endregion

    #region Windows

    public Task<bool> ShowOpenDatabaseAsync(ConnectionViewModel viewModel)
        => ShowConnectionDialogAsync(viewModel, new OpenDatabaseDialog());

    public Task<bool> ShowCreateDatabaseAsync(ConnectionViewModel viewModel)
        => ShowConnectionDialogAsync(viewModel, new CreateDatabaseDialog());

    /// <summary>
    /// The connection dialogs close themselves from inside the ViewModel - the Connect command has to
    /// finish, and only it knows whether it succeeded. The ViewModel asks by raising an event; this is
    /// the only thing that turns that request into <c>Window.Close()</c>.
    /// </summary>
    private async Task<bool> ShowConnectionDialogAsync(ConnectionViewModel viewModel, Window dialog)
    {
        var owner = m_owner();

        if (owner == null)
            return false;

        dialog.DataContext = viewModel;

        void OnCloseRequested(object? sender, EventArgs e) => dialog.Close();

        viewModel.CloseRequested += OnCloseRequested;

        try
        {
            await ShowAsync(dialog, owner);
        }
        finally
        {
            viewModel.CloseRequested -= OnCloseRequested;
        }

        return viewModel.DialogResult;
    }

    public Task ShowSettingsAsync(SettingsViewModel viewModel)
        => SettingsDialog.ShowAsync(RequireOwner(), viewModel);

    public Task ShowConnectionsAsync(ConnectionsViewModel viewModel)
        => ConnectionsDialog.ShowAsync(RequireOwner(), viewModel);

    public Task ShowKeyboardHelpAsync(KeyboardHelpViewModel viewModel)
        => KeyboardHelpDialog.ShowAsync(RequireOwner(), viewModel);

    public Task ShowExportAsync(ExportViewModel viewModel)
        => ExportDialog.ShowAsync(RequireOwner(), viewModel);

    public Task ShowImportAsync(ImportViewModel viewModel)
        => ImportDialog.ShowAsync(RequireOwner(), viewModel);

    public async Task<bool> ShowCreateTableAsync(CreateTableViewModel viewModel)
    {
        var dialog = new CreateTableDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowCreateViewAsync(CreateViewViewModel viewModel)
    {
        var dialog = new CreateViewDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowCreateIndexAsync(CreateIndexViewModel viewModel)
    {
        var dialog = new CreateIndexDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowTableRebuildAsync(TableRebuildViewModel viewModel)
    {
        var dialog = new TableRebuildDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowReadCheckAsync(ReadCheckViewModel viewModel)
    {
        var dialog = new ReadCheckDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowChangePasswordAsync(ChangePasswordViewModel viewModel)
    {
        var dialog = new ChangePasswordDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowDatabaseCopyAsync(DatabaseCopyViewModel viewModel)
    {
        var dialog = new DatabaseCopyDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    public async Task<bool> ShowEditTriggerAsync(EditTriggerViewModel viewModel)
    {
        var dialog = new EditTriggerDialog { DataContext = viewModel };

        void OnClose(bool success) => dialog.Close(success);

        viewModel.ShouldCloseDialog += OnClose;

        try
        {
            return await ShowForResultAsync(dialog);
        }
        finally
        {
            viewModel.ShouldCloseDialog -= OnClose;
        }
    }

    private async Task<bool> ShowForResultAsync(Window dialog)
    {
        var owner = m_owner();

        if (owner == null)
            return false;

        var result = Dispatcher.UIThread.CheckAccess()
            ? await dialog.ShowDialog<bool?>(owner)
            : await Dispatcher.UIThread.InvokeAsync(() => dialog.ShowDialog<bool?>(owner));

        return result == true;
    }

    private static Task ShowAsync(Window dialog, Window owner)
    {
        return Dispatcher.UIThread.CheckAccess()
            ? dialog.ShowDialog(owner)
            : Dispatcher.UIThread.InvokeAsync(() => dialog.ShowDialog(owner));
    }

    /// <summary>
    /// These four dialogs take an owner because they were written that way. Called only from paths
    /// that have already established there is a window; the pickers above are the ones that must
    /// survive its absence.
    /// </summary>
    private Window RequireOwner()
    {
        return m_owner() ?? throw new InvalidOperationException(
            "A dialog was requested before the main window existed.");
    }

    #endregion
}
