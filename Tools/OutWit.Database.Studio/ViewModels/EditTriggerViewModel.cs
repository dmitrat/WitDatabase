using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using OutWit.Common.Aspects;
using OutWit.Common.MVVM.Commands;
using OutWit.Common.MVVM.ViewModels;
using OutWit.Common.Utils;
using OutWit.Database.Studio.Models;
using OutWit.Database.Studio.Services;

namespace OutWit.Database.Studio.ViewModels;

/// <summary>
/// The trigger editor (WS-45): a body inside the boundary of the language, with the boundary said out
/// loud before anything is run rather than shown as the engine's error afterwards.
///
/// The four things it knows, all measured on 2026-08-06:
///
/// - the body takes <b>only</b> SELECT, INSERT, UPDATE, DELETE and MERGE. The engine refuses anything
///   else with a good message, and the editor gets there first;
/// - <c>SET NEW.column = ...</c> does not parse at all, and the error the engine gives for it is
///   <i>"mismatched input 'NEW' expecting TRANSACTION"</i> - SET is being read as SET TRANSACTION. So
///   the BEFORE-trigger-that-fills-a-field template is not offered, and typing one is explained;
/// - a WHEN condition must be <b>parenthesised</b>. The editor writes the brackets, which is the
///   difference between a working trigger and a parse error;
/// - there is no ALTER TRIGGER and none to enable or disable one, so changing a trigger is a DROP and
///   a CREATE, and the editor says that on the button.
/// </summary>
public class EditTriggerViewModel : ViewModelBase<ApplicationViewModel>
{
    #region Events

    public event Action<bool> ShouldCloseDialog = delegate { };

    #endregion

    #region Constructors

    public EditTriggerViewModel(ApplicationViewModel applicationVm, IDatabaseSession session,
        string table, TriggerInfo? existing = null)
        : base(applicationVm)
    {
        Session = session;
        Table = table;
        Existing = existing;

        InitDefault();
        InitEvents();
        InitCommands();
    }

    #endregion

    #region Initialization

    private void InitDefault()
    {
        Timings = ["BEFORE", "AFTER", "INSTEAD OF"];
        Events = ["INSERT", "UPDATE", "DELETE"];

        if (Existing != null)
        {
            Name = Existing.Name;
            Timing = Existing.Timing;
            Event = Existing.Event;
            UpdateColumnsText = string.Join(", ", Existing.UpdateColumns);
            ForEachRow = Existing.IsRowTrigger;
            Condition = Trim(Existing.Condition);
            Body = Existing.Body ?? string.Empty;
        }
        else
        {
            Name = $"TR_{Table}_";
            Timing = "AFTER";
            Event = "INSERT";
            ForEachRow = true;
            Body = string.Empty;
        }

        Validate();
    }

    private void InitEvents()
    {
        PropertyChanged += OnPropertyChanged;

        Localization.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Heading));
            OnPropertyChanged(nameof(SaveText));

            // The refusals are built once, when the body changes, so switching language would leave
            // whichever ones are on screen in the language they were built in - stage 10's "a
            // converter is asked ONCE", in a list rather than in a binding. Rebuilding them is what
            // a translated refusal means.
            Validate();
        };
    }

    private void InitCommands()
    {
        SaveCommand = new RelayCommandAsync(SaveAsync);
        CancelCommand = new RelayCommand(() => ShouldCloseDialog(false));
        SendToEditorCommand = new RelayCommand(SendToEditor);
    }

    #endregion

    #region Functions

    /// <summary>
    /// Checks the body against the language before the engine sees it, and says which rule was broken.
    /// </summary>
    public void Validate()
    {
        Problems = [];
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            problems.Add(Localization["Dialog.Trigger.Problem.NoName"]);

        if (string.IsNullOrWhiteSpace(Body))
        {
            problems.Add(Localization["Dialog.Trigger.Problem.EmptyBody"]);
        }
        else
        {
            if (Body.Contains("SET NEW.", StringComparison.OrdinalIgnoreCase) ||
                Body.Contains("SET OLD.", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(Localization["Dialog.Trigger.Problem.AssignsNewOrOld"]);
            }

            var split = SqlScript.Split(Body);

            if (!split.IsSuccess)
            {
                // The engine's own message, which is English and stays that way: it is the parser
                // speaking, not Studio. The sentence around it is translated.
                problems.Add(Localization.Format("Dialog.Trigger.Problem.DoesNotParse",
                    split.Errors[0].Message));
            }
            else
            {
                foreach (var statement in split.Statements.Where(s => s.ChangesSchema))
                {
                    problems.Add(Localization.Format("Dialog.Trigger.Problem.ChangesSchema",
                        statement.Summary));
                }
            }
        }

        Problems = problems;
        IsValid = problems.Count == 0;

        GeneratedDdl = BuildSql();
    }

    /// <summary>
    /// What will run. For an existing trigger that is two statements, because there is no ALTER
    /// TRIGGER - and the panel shows both, so nobody is surprised by the DROP.
    /// </summary>
    public string BuildSql()
    {
        var create = DdlWriter.CreateTrigger(ToDraft());

        return Existing == null
            ? create
            : $"{DdlWriter.DropTrigger(Existing.Name)}\n{create}";
    }

    public TriggerDraft ToDraft() => new()
    {
        Name = Name,
        Table = Table,
        Timing = Timing,
        Event = Event,
        UpdateColumns = ParseUpdateColumns(),
        ForEachRow = ForEachRow,
        Condition = string.IsNullOrWhiteSpace(Condition) ? null : Condition,
        Body = Body
    };

    private async Task SaveAsync()
    {
        if (!IsValid)
            return;

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            var set = new SchemaChangeSet(Table);

            set.Add(new SchemaEdit
            {
                Kind = Existing == null ? SchemaEditKind.AddColumn : SchemaEditKind.ReplaceTriggerBody,
                Table = Table,
                Description = Existing == null
                    ? Localization.Format("Trigger.Create.Description", Name)
                    : Localization.Format("Trigger.Replace.Description", Name),

                // A replacement is a DROP and a CREATE, and it is NOT atomic on this engine: if the
                // create fails, the trigger is gone. The report names what ran, which is the only
                // honest answer available (WS-42).
                Statements = Existing == null
                    ? [DdlWriter.CreateTrigger(ToDraft())]
                    : [DdlWriter.DropTrigger(Existing.Name), DdlWriter.CreateTrigger(ToDraft())]
            });

            var report = await set.ApplyAsync(Session, ApplicationVm.Logger);

            if (!report.IsComplete)
            {
                ErrorMessage = report.IsPartial
                    ? Localization.Format("Trigger.PartialFailure", report.ErrorMessage)
                    : report.ErrorMessage;

                return;
            }

            await Session.Catalog.RefreshAsync();
            await ApplicationVm.DatabaseExplorerVm.RefreshAsync(Session);

            ApplicationVm.MainWindowVm.StatusText = Existing == null
                ? Localization.Format("Status.TriggerCreated", Name)
                : Localization.Format("Status.TriggerReplaced", Name);

            ShouldCloseDialog(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message.Split('\n')[0];
            ApplicationVm.Logger.LogError(ex, "Failed to save trigger {Name}", Name);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void SendToEditor()
    {
        ApplicationVm.WorkspaceTabsVm.OpenQueryTab(BuildSql(), $"{Name} - DDL", Session);
        ShouldCloseDialog(false);
    }

    /// <summary>
    /// The typed column list, as columns. The clause belongs to UPDATE alone, so it is dropped for any
    /// other event rather than written into SQL the grammar refuses - and dropping it here is what
    /// makes switching the event back and forth in the dialog harmless.
    /// </summary>
    private IReadOnlyList<string> ParseUpdateColumns()
    {
        if (!IsUpdateEvent || string.IsNullOrWhiteSpace(UpdateColumnsText))
            return [];

        return UpdateColumnsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string? Trim(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return null;

        var text = condition.Trim();

        return text.Length > 1 && text[0] == '(' && text[^1] == ')' ? text[1..^1].Trim() : text;
    }

    #endregion

    #region Event Handlers

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.IsProperty((EditTriggerViewModel vm) => vm.Event))
            OnPropertyChanged(nameof(IsUpdateEvent));

        if (e.IsProperty((EditTriggerViewModel vm) => vm.Name) ||
            e.IsProperty((EditTriggerViewModel vm) => vm.Body) ||
            e.IsProperty((EditTriggerViewModel vm) => vm.Condition) ||
            e.IsProperty((EditTriggerViewModel vm) => vm.Timing) ||
            e.IsProperty((EditTriggerViewModel vm) => vm.Event) ||
            e.IsProperty((EditTriggerViewModel vm) => vm.UpdateColumnsText) ||
            e.IsProperty((EditTriggerViewModel vm) => vm.ForEachRow))
            Validate();
    }

    #endregion

    #region Properties

    public IDatabaseSession Session { get; }

    public string Table { get; }

    /// <summary>
    /// The window's heading. It was a <c>StringFormat</c> in the markup, where no lint over attributes
    /// could see it and no catalogue could reach it.
    /// </summary>
    public string Heading => Localization.Format("Dialog.Trigger.Heading", Table);

    /// <summary>
    /// The trigger being replaced, or null when one is being created.
    /// </summary>
    public TriggerInfo? Existing { get; }

    public bool IsReplacing => Existing != null;

    /// <summary>
    /// What the save button says. "Save" would be a lie about a DROP followed by a CREATE.
    /// </summary>
    public string SaveText => IsReplacing
        ? Localization["Dialog.Trigger.DropAndCreate"]
        : Localization["Common.Create"];

    [Notify]
    public string Name { get; set; } = string.Empty;

    [Notify]
    public string Timing { get; set; } = "AFTER";

    [Notify]
    public string Event { get; set; } = "INSERT";

    /// <summary>
    /// The columns of <c>UPDATE OF</c>, comma-separated as they are written in SQL. Empty means every
    /// column.
    /// </summary>
    /// <remarks>
    /// The field exists because the engine honours the clause since 2026-08-09. Without it, opening an
    /// existing <c>UPDATE OF Price</c> trigger here and pressing the button would DROP it and create
    /// one watching every column - a silent widening, in the one place where the user is looking
    /// straight at the trigger.
    /// </remarks>
    [Notify]
    public string? UpdateColumnsText { get; set; }

    /// <summary>
    /// Whether the column list applies at all - the grammar allows <c>OF</c> only after UPDATE.
    /// </summary>
    public bool IsUpdateEvent => Event.Equals("UPDATE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// FOR EACH ROW. Unticked writes no FOR EACH clause at all - FOR EACH STATEMENT is a parse error
    /// on this engine, and leaving the clause out is how a statement trigger is spelled.
    /// </summary>
    [Notify]
    public bool ForEachRow { get; set; } = true;

    [Notify]
    public string? Condition { get; set; }

    [Notify]
    public string Body { get; set; } = string.Empty;

    [Notify]
    public string? GeneratedDdl { get; private set; }

    [Notify]
    public List<string> Problems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

    [Notify]
    public bool IsValid { get; private set; }

    [Notify]
    public bool IsSaving { get; set; }

    [Notify]
    public string? ErrorMessage { get; set; }

    public List<string> Timings { get; private set; } = null!;

    public List<string> Events { get; private set; } = null!;

    /// <summary>
    /// The sentence under the body, always visible - the boundary is part of the editor, not an error
    /// message waiting to happen.
    /// </summary>
    public string LanguageNote => Localization["Dialog.Trigger.LanguageNote"];

    #endregion

    #region Services

    private Services.Localization.ILocalizationService Localization => ApplicationVm.Localization;

    #endregion

    #region Commands

    public ICommand SaveCommand { get; private set; } = null!;

    public ICommand CancelCommand { get; private set; } = null!;

    public ICommand SendToEditorCommand { get; private set; } = null!;

    #endregion
}
