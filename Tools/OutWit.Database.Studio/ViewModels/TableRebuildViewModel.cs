using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Abstract;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// One step as the dialog shows it: the title, the state, and the statements behind it.
/// </summary>
public sealed class RebuildStepViewModel : NotifyPropertyChangedBase
{
    public RebuildStepViewModel(int number, RebuildStep step)
    {
        Number = number;
        Step = step;
        Title = step.Title;
        Sql = string.Join("\n", step.Statements);
    }

    public int Number { get; }

    public RebuildStep Step { get; }

    public string Title { get; }

    public string Sql { get; }

    [Notify]
    public string State { get; set; } = "waiting";

    [Notify]
    public bool IsRunning { get; set; }

    [Notify]
    public bool IsDone { get; set; }

    [Notify]
    public bool IsFailed { get; set; }
}

/// <summary>
/// The rebuild conversation of 5.3: the whole plan before anything runs, the steps ticking off while
/// it does, and an honest account if it stops (WS-41).
///
/// The dialog shows three things the design asks for and one it does not, because the engine made it
/// necessary:
///
/// - the steps, with their SQL, before the button that starts them;
/// - the objects that will be put back and the ones that will not;
/// - the request to make a copy first, because nothing here can be undone;
/// - and <b>how many values will not survive the conversion</b>. This is the extra one. On this engine
///   CAST never fails - <c>CAST('not a number' AS INTEGER)</c> is 0 and so is <c>CAST('3.9' AS
///   INTEGER)</c> - so a type change silently replaces what it cannot convert. A rebuild that did not
///   count them first would be exactly as quiet as the ALTER it replaces.
/// </summary>
public class TableRebuildViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool> ShouldCloseDialog = delegate { };

    #endregion

    #region Constructors

    public TableRebuildViewModel(ApplicationViewModel applicationVm, IDatabaseSession session, TableRebuildPlan plan)
        : base(applicationVm)
    {
        Session = session;
        Plan = plan;

        InitDefault();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Steps = [];

        for (var i = 0; i < Plan.Steps.Count; i++)
            Steps.Add(new RebuildStepViewModel(i + 1, Plan.Steps[i]));

        Script = Plan.Script;
        Table = Plan.Table;

        RowCountText = Plan.RowCount is { } rows
            ? $"{rows:N0} row(s) will be carried across"
            : "the number of rows is not known - the count did not come back in time";

        Casualties = Plan.Casualties.ToList();
        Dependencies = Plan.Dependencies.ToList();
        Recreated = Plan.Recreated.ToList();
        Losses = Plan.Losses.ToList();

        HasCasualties = Casualties.Count > 0;
        HasDependencies = Dependencies.Count > 0;
        HasLosses = Losses.Count > 0;

        // NOT ARMED, and this is deliberate. Measured 2026-08-06 in the shipping application, twice, on
        // two databases: a rebuild run from this dialog left the file unreadable - the schema
        // catalogue's overflow chain is broken and the bare provider cannot open it either
        // ("Page 9 is not an overflow page"). Fourteen headless runs of the same rebuild - with and
        // without the trigger and the index, over 2000 rows, with readers scanning throughout, with a
        // second database open, at four page sizes, and with the same statements typed by hand - all
        // reopen correctly, so what the application does differently is not yet known.
        //
        // Until it is, the dialog does everything except run it: the plan, the casualties, the
        // dependencies and the script. Running that script by hand in the query editor IS measured to
        // be safe, so the user loses the button and keeps the operation.
        CanRebuild = false;
    }

    private void InitCommands()
    {
        RebuildCommand = new RelayCommandAsync(RebuildAsync);
        ShowScriptCommand = new RelayCommand(() => IsScriptVisible = !IsScriptVisible);
        SendToEditorCommand = new RelayCommand(SendToEditor);
        CancelCommand = new RelayCommand(() => ShouldCloseDialog(false));
        CloseCommand = new RelayCommand(() => ShouldCloseDialog(IsComplete));
    }

    #endregion

    #region Functions

    private async Task RebuildAsync()
    {
        if (!CanRebuild)
            return;

        IsRunning = true;
        CanRebuild = false;
        ErrorMessage = null;

        try
        {
            var report = await TableRebuild.RunAsync(Session, Plan, OnStep, ApplicationVm.Logger);

            IsComplete = report.IsComplete;
            Report = report;
            Summary = report.Summary;
            Recovery = report.Recovery.ToList();
            HasRecovery = Recovery.Count > 0;

            foreach (var step in Steps)
            {
                step.IsRunning = false;
                step.IsDone = step.Step.Outcome == DdlOutcome.Applied;
                step.IsFailed = step.Step.Outcome == DdlOutcome.Failed;
                step.State = step.Step.Outcome switch
                {
                    DdlOutcome.Applied => "done",
                    DdlOutcome.Failed => step.Step.ErrorMessage ?? "failed",
                    _ => "not reached"
                };
            }

            if (report.IsComplete)
            {
                ApplicationVm.Notifications.Information(Localization.Format("Dialog.Rebuild.Done", Table));
                ApplicationVm.MainWindowVm.StatusText = Localization.Format("Dialog.Rebuild.Done", Table);
            }
            else
            {
                ErrorMessage = report.ErrorMessage;
                ApplicationVm.Notifications.Error(Localization.Format("Dialog.Rebuild.Stopped", Table), report.Summary);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ApplicationVm.Logger.LogError(ex, "The rebuild of {Table} threw", Table);
        }
        finally
        {
            IsRunning = false;
            HasRun = true;
        }
    }

    /// <summary>
    /// Puts the whole plan into a query tab. While the button is not armed this is how a rebuild is
    /// carried out - and the same statements, run this way, are measured to leave the file readable.
    /// </summary>
    private void SendToEditor()
    {
        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(
            $"-- Rebuild of {Table}, planned by Studio.\n" +
            $"-- Make a copy of the database first: none of this can be undone.\n\n{Script}",
            $"{Table} - rebuild",
            Session);

        ShouldCloseDialog(false);
    }

    private void OnStep(RebuildStep step)
    {
        foreach (var vm in Steps)
        {
            if (!ReferenceEquals(vm.Step, step))
                continue;

            vm.IsRunning = true;
            vm.State = Localization["Dialog.Rebuild.Step.Running"];
        }
    }

    #endregion

    #region Properties

    public IDatabaseSession Session { get; }

    public TableRebuildPlan Plan { get; }

    [Notify]
    public string Table { get; private set; } = string.Empty;

    /// <summary>
    /// The window's heading - a <c>StringFormat</c> in the markup before, which is a place a catalogue
    /// cannot reach.
    /// </summary>
    public string Heading => Localization.Format("Dialog.Rebuild.Heading", Table);

    [Notify]
    public ObservableCollection<RebuildStepViewModel> Steps { get; private set; } = null!;

    [Notify]
    public string Script { get; private set; } = string.Empty;

    [Notify]
    public bool IsScriptVisible { get; set; }

    [Notify]
    public string RowCountText { get; private set; } = string.Empty;

    /// <summary>
    /// Values that will not survive the conversion, counted before anything runs.
    /// </summary>
    [Notify]
    public List<string> Casualties { get; private set; } = null!;

    [Notify]
    public bool HasCasualties { get; private set; }

    /// <summary>
    /// What points at this table and will not be repaired by the rebuild.
    /// </summary>
    [Notify]
    public List<string> Dependencies { get; private set; } = null!;

    [Notify]
    public bool HasDependencies { get; private set; }

    [Notify]
    public List<string> Recreated { get; private set; } = null!;

    /// <summary>
    /// What the rebuild cannot carry across because the catalogue does not publish it.
    /// </summary>
    [Notify]
    public List<string> Losses { get; private set; } = null!;

    [Notify]
    public bool HasLosses { get; private set; }

    [Notify]
    public bool IsRunning { get; private set; }

    [Notify]
    public bool HasRun { get; private set; }

    [Notify]
    public bool IsComplete { get; private set; }

    [Notify]
    public bool CanRebuild { get; private set; }

    [Notify]
    public string? Summary { get; private set; }

    [Notify]
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// The statements that get back to where the rebuild started, from wherever it stopped.
    /// </summary>
    [Notify]
    public List<string> Recovery { get; private set; } = [];

    [Notify]
    public bool HasRecovery { get; private set; }

    public TableRebuildReport? Report { get; private set; }

    /// <summary>
    /// Why the button is not armed. On screen, above it - a disabled button with no explanation is the
    /// thing this whole section is against.
    /// </summary>
    public string NotArmedReason =>
        $"Studio will not run this itself yet. Measured on 2026-08-06: a rebuild run from here left the " +
        $"database unreadable - twice, on two files - and the same statements run in the query editor " +
        $"did not. Until that is understood, use \"To the editor\" and run the script yourself, after " +
        $"making a copy.";

    public bool IsArmed => CanRebuild;

    /// <summary>
    /// The sentence the design insists on, and it is not decoration: nothing below can be undone.
    /// </summary>
    public string BackupWarning =>
        $"Make a copy of the database first. The rebuild cannot be undone, and this engine does not roll " +
        $"DDL back - a statement that has run has run. If it stops between steps, {Table} may exist only " +
        $"as {Plan.Carrier}, and the dialog will say so.";

    #endregion

    #region Commands

    public ICommand RebuildCommand { get; private set; } = null!;

    public ICommand ShowScriptCommand { get; private set; } = null!;

    public ICommand SendToEditorCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand CloseCommand { get; private set; } = null!;

    #endregion

    #region Services

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion
}
