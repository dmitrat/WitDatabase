using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// One line of the report, with its words already in the reader's language.
/// </summary>
public sealed record ReadCheckRow(string Name, string Kind, string Result, string? Note, bool IsBad);

/// <summary>
/// Verification by reading (WS-61): walk everything, say what answered, and be exact about what that
/// does and does not prove.
/// </summary>
public sealed class ReadCheckViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Fields

    private readonly IDatabaseSession m_session;

    private CancellationTokenSource? m_cancellation;

    #endregion

    #region Constructors

    public ReadCheckViewModel(ApplicationViewModel applicationVm, IDatabaseSession session)
        : base(applicationVm)
    {
        m_session = session;

        Heading = Localization.Format("ReadCheck.Heading", session.DisplayName);

        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitCommands()
    {
        RunCommand = new RelayCommandAsync(RunAsync, () => !IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        SaveReportCommand = new RelayCommandAsync(SaveReportAsync, () => HasRun);
        CloseCommand = new RelayCommand(() => ShouldCloseDialog?.Invoke(HasRun));
    }

    #endregion

    #region Functions

    private async Task RunAsync()
    {
        Rows.Clear();

        IsRunning = true;
        HasRun = false;
        Summary = Localization["ReadCheck.Running"];

        RefreshCommands();

        m_cancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ReadCheckItem>(item => Rows.Add(Describe(item)));

            Report = await ReadChecker.RunAsync(m_session, progress, m_cancellation.Token);

            Summary = Describe(Report);
            HasRun = true;
        }
        catch (Exception ex)
        {
            Summary = Localization.Format("ReadCheck.Failed", ex.Message);
            Logger.LogError(ex, "The read check of {Name} did not finish", m_session.DisplayName);
        }
        finally
        {
            IsRunning = false;

            m_cancellation?.Dispose();
            m_cancellation = null;

            RefreshCommands();
        }
    }

    /// <summary>
    /// Tells the buttons to ask again.
    /// </summary>
    /// <remarks>
    /// A <c>RelayCommand</c> does not re-evaluate its <c>CanExecute</c> because a property changed -
    /// it has to be told. Found in the running application for the second time: the check finished,
    /// <c>HasRun</c> went true, and "Save the report" stayed grey because a disabled COMMAND beats an
    /// enabled binding.
    /// </remarks>
    private void RefreshCommands()
    {
        (RunCommand as RelayCommandAsync)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (SaveReportCommand as RelayCommandAsync)?.RaiseCanExecuteChanged();
    }

    private void Cancel()
    {
        m_cancellation?.Cancel();
    }

    /// <summary>
    /// Writes the report out as text, which is the form somebody attaches to an issue.
    /// </summary>
    private async Task SaveReportAsync()
    {
        if (Report is not { } report)
            return;

        var path = await ApplicationVm.Dialogs.SaveFileAsync(
            Localization["ReadCheck.SaveReport"],
            suggestedFileName: $"{m_session.DisplayName}-readcheck.txt",
            defaultExtension: ".txt");

        if (string.IsNullOrEmpty(path))
            return;

        var text = new StringBuilder()
            .AppendLine(Heading)
            .AppendLine(Summary)
            .AppendLine();

        foreach (var row in Rows)
        {
            text.AppendLine(string.IsNullOrEmpty(row.Name)
                ? $"{row.Kind}: {row.Result}"
                : $"{row.Kind} {row.Name}: {row.Result}");

            if (!string.IsNullOrEmpty(row.Note))
                text.AppendLine($"    {row.Note}");
        }

        text.AppendLine().AppendLine(Localization["ReadCheck.Limits"]);

        await File.WriteAllTextAsync(path, text.ToString());

        ApplicationVm.Notifications.Information(Localization.Format("ReadCheck.Saved", path));
    }

    /// <summary>
    /// The summary line: what was read, and what of it did not come back.
    /// </summary>
    private string Describe(ReadCheckReport report)
    {
        if (report.WasCancelled)
            return Localization.Format("ReadCheck.Stopped", report.Items.Count);

        var summary = Localization.Format("ReadCheck.Summary",
            Localization.Plural("Count.Tables", report.Tables),
            Localization.Plural("Count.Indexes", report.Indexes));

        if (report.Failed > 0)
            return $"{summary} · {Localization.Format("ReadCheck.Failures", report.Failed)}";

        if (report.Disagreements.Count > 0)
            return $"{summary} · {Localization.Format("ReadCheck.Disagreements", report.Disagreements.Count)}";

        return $"{summary} · {Localization["ReadCheck.AllRead"]}";
    }

    private ReadCheckRow Describe(ReadCheckItem item)
    {
        var kind = Localization[item.Subject switch
        {
            ReadCheckSubject.Table => "ReadCheck.Kind.Table",
            ReadCheckSubject.Index => "ReadCheck.Kind.Index",
            _ => "ReadCheck.Kind.Catalog"
        }];

        var result = item.Outcome switch
        {
            ReadCheckOutcome.Failed => Localization.Format("ReadCheck.Result.Failed", item.EngineMessage),

            // The catalogue counts OBJECTS and everything else counts rows. Reported as "1 строка" for
            // a database of one table until the running application said it out loud.
            _ when item.Subject == ReadCheckSubject.Catalog =>
                Localization.Format("ReadCheck.Result.Catalog",
                    Localization.Plural("Count.Tables", item.RowsRead)),

            ReadCheckOutcome.Inconclusive =>
                Localization.Format("ReadCheck.Result.Inconclusive",
                    Localization.Plural("Count.Rows", item.RowsRead)),

            // The counter and the rows are both stated when they part company, because the point of
            // reading them is that they CAN, and a line that only showed one of them would hide it.
            _ when item.CounterSays is { } counter && counter != item.RowsRead =>
                Localization.Format("ReadCheck.Result.Disagrees",
                    Localization.Plural("Count.Rows", item.RowsRead), counter),

            _ => Localization.Format("ReadCheck.Result.Ok",
                Localization.Plural("Count.Rows", item.RowsRead))
        };

        var isBad = item.Outcome == ReadCheckOutcome.Failed
                    || (item.CounterSays is { } says && says != item.RowsRead);

        return new ReadCheckRow(item.Name, kind, result,
            item.NoteKey == null ? null : Localization[item.NoteKey], isBad);
    }

    #endregion

    #region Events

    /// <summary>Raised when the dialog showing this ViewModel should close.</summary>
    public event Action<bool>? ShouldCloseDialog;

    #endregion

    #region Properties

    public string Heading { get; }

    [Notify] public string Summary { get; private set; } = string.Empty;

    [Notify] public bool IsRunning { get; private set; }

    [Notify] public bool HasRun { get; private set; }

    public ReadCheckReport? Report { get; private set; }

    public ObservableCollection<ReadCheckRow> Rows { get; } = [];

    #endregion

    #region Commands

    [Notify] public ICommand RunCommand { get; private set; } = null!;

    [Notify] public ICommand CancelCommand { get; private set; } = null!;

    [Notify] public ICommand SaveReportCommand { get; private set; } = null!;

    [Notify] public ICommand CloseCommand { get; private set; } = null!;

    #endregion

    #region Services

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    private ILogger<ApplicationViewModel> Logger => ApplicationVm.Logger;

    #endregion
}
