# Phase 14 - WitDatabase Studio, refactoring and redesign

Phase 13 asked what Studio's tests actually touched and fixed what that exposed. This phase asks a
different question: **is Studio pleasant to use.** The answer is a design project of nine sections and
a canon of seventy decisions (`WS-1`…`WS-70`), and an implementation plan whose stages are ordered by
dependency rather than by section number.

The design lives outside the repository, in a local `@Design` folder alongside `@NuGet`. Nothing in it
is committed; this document carries what the repository needs to know.

Stages, from that plan:

| Stage | Content |
|---|---|
| **0** | Security and data loss. No interface change |
| 1 | Tests against the real engine; automation surface |
| 2 | Multi-connection: `IConnectionManager` instead of one singleton connection |
| 3 | The SQL layer: parameters, paging, script splitting |
| 4–9 | The redesign itself: window frame, explorer, query workspace, grid, schema designer, dialogs |
| 10 | The Database tab, after the ADO.NET provider gains maintenance access (`WS-57`) |

Releases while this runs are **dev tags only** - no eSigner signatures are spent until the interface
reaches its final shape.

---

## Stage 0 - security and data loss

Seven defects. Six were named in the plan; the seventh was found while building the instrument for the
other two, and it had been in every release Studio has ever had.

Every one was measured in both directions: red before the fix, green after, and where the defect could
not be expressed as a failing test, the fix was removed again and the result recorded.

### B1 - the password of an encrypted database was written to the log file

`ConnectionInfo.BuildConnectionString()` appends `;Password={Password}`, and `DatabaseService`
logged that string whole: once on every connection attempt, and again in the `catch`. This was
harmless while Studio was a `WinExe` writing to a console that does not exist. **2.0 added a file
log**, and from that release on the password was written to
`%AppData%\WitDatabase.Studio\logs\studio.log` - the file users are asked to attach to an issue.

`ToLogString()` keeps the data source, the read-only flag, whether encryption was asked for and the
store, and replaces the secret. `BuildConnectionString()` is unchanged and still goes to the engine.

**How it was measured.** Through the shipping logging path - a real `FileLoggerProvider` at the level
`Program.ConfigureServices` sets, writing to a real file, which is then read. The claim is "the
password is on disk", so the disk is what answers it. Both cases were red first, with the password in
the log twice.

The main assertion is a **null result** - a substring is absent - so it carries two positive controls:
the data source must be in the log (we are reading the right file at a level that writes), and a
separate case writes the password through the same provider and requires the search to find it.
Without those, the fixture would pass against a provider that writes nothing at all.

### B5 - `Environment.Exit(0)` skipped every part of closing down

File > Exit ended the process where it stood. `MainWindow.OnClosing` never ran, so the window size was
not saved and nothing was asked about unapplied edits; the service provider was never disposed, and
since 12.2.0 that leaves the database under an **exclusive file lock** until the operating system
reclaims the handle.

Exit now requests shutdown, and the window closes itself - the same path the close button takes. Both
paths ask the tabs first.

**How it was measured.** The old behaviour cannot be pinned by a test: a test that called it would take
the test host with it. So it was measured the other way round - `Environment.Exit(0)` was restored and
the fixture run: *"The active test run was aborted. Reason: Test host process crashed"*, no results at
all. That is what "no cleanup" looks like from outside.

### B6 - closing a tab with unapplied edits discarded them silently

`TableEditTabViewModel.CanClose()` returned `true` unconditionally, over a
`// TODO: Show confirmation dialog if HasChanges`, and `OnClosed()` disposed the edited `DataTable` a
line later - so there was nothing left to recover from.

Closing now asks: **Apply · Discard changes · Keep open**, through `IConfirmationService` so that the
ViewModel layer needs no window and a test can answer. The same question guards leaving the
application and disconnecting the database, and it is asked **before** the connection goes away -
afterwards the only honest offer left would be to discard. An Apply that the engine refuses keeps the
tab and its buffer.

Where no host has supplied a confirmation service, the answer is **Keep open**. Silence must never be
what destroys work.

### B2 - the edit buffer was applied one statement at a time

`CommitAsync` sent deletions, insertions and updates as separate `ExecuteNonQueryAsync` calls, each in
its own `try/catch`, collected the failures and showed the first three. A set that failed halfway left
behind whatever had already gone in, and the user was told `Update failed: …` with no way of knowing
what was in the database.

The buffer now becomes one script and goes through `DatabaseService.ExecuteBatchAsync` in one
transaction: committed whole, or rolled back whole with the buffer kept and the table **not** reloaded
- a reload would discard exactly the work the message says was not saved.

**How it was measured, and why the first green proved nothing.** The case was written before the fix
and passed anyway, because B7 below was aborting the commit before it could write. Two sabotages
settled it:

- Leaving `command.Transaction` unset changes **nothing**: the provider applies the connection's open
  transaction to every command on it. Worth knowing, and not a reason to omit the assignment - that is
  the ADO.NET contract, and a consumer reading the code should not have to know the provider's habits.
- Removing the transaction entirely turns the case red with the partial-application signature: the
  delete applied, the accepted update applied, the third statement refused.

### B3 - `UPDATE` and `DELETE` without a primary key were built from every column

`BuildWhereClause` fell back to a condition over **all** columns of the row when the table had no
primary key. That is not a unique condition: two identical rows both match it, so an update meant for
one changed both and a delete removed both - and the affected-row count that would have said so was
never read. It also compared `BLOB` columns in a `WHERE` clause.

The fallback is gone. A table with no primary key opens for viewing, with a banner that says why
(`WS-35`) rather than a row of buttons that are grey for no stated reason.

### B7 - deleting a row never worked, in any release

**Not in the plan. Found while building the instrument for B2 and B3.**

`PopulateDataTableAsync` fills the result table with `Rows.Add` and never calls `AcceptChanges`, so
every row read out of the database sat in state `Added` - which means "this row is not in the database
yet", the opposite of the truth. The editor believed it, and `DataRow.Delete()` on an `Added` row
**detaches** it instead of marking it deleted. The row left the table, `FindOriginalRowIndex` read a
detached row, and `RowNotInTableException` took the whole commit into its `catch`.

So deleting a row deleted nothing, said `Commit failed: This row has been removed from a table…`, and
took every other edit in the same buffer down with it.

Verified by execution before and after: the row now leaves the database, and the delete travels in the
same transaction as the rest of the buffer.

### S3 and S6 - the Create dialog built one database and abandoned another

Pulled forward from stage 9 because the mechanism is local and the litter is real. Three pinned tests
inverted:

- **LSM built a second database in the user's folder.** `WithLsmTree` was handed
  `Path.GetDirectoryName(FilePath)` - the folder the user picked a file *in* - so choosing
  `C:\Users\Me\Documents\mydb.witdb` dropped `provider.meta` and `wal.log` into Documents and
  abandoned them. The chosen path is the database now, and because a B-Tree database is a file while
  an LSM database is a folder, creating one asks for a **folder** (`WS-48`).
- **In-memory combined with LSM wrote into the working directory.** `WithLsmTree(".")` - for an
  installed application, wherever it was launched from. The combination is refused, and the refusal
  says which of the two choices cannot be had.
- **In-memory connected to a different database than it created.** The dialog built one with
  `WitDatabaseBuilder`, disposed it, and connected over `Data Source=:memory:` - and every connection
  to `:memory:` gets its own private database. Nothing is built first now: the connection creates the
  database and owns it.

The LSM case keeps its round trip - 8 of 8 rows back - and gained a control that the database asked
for **does** exist under the chosen path, so that "no litter" cannot pass for "nothing was created".
Measured listing after the fix: `created.witdb\provider.meta`, `created.witdb\sst_000000.sst` and the
index directory; nothing beside them.

### Tests

282 → 301, all green. Four fixtures are new or rewritten:

| Fixture | Covers |
|---|---|
| `CredentialLeakTests` | B1, through the real file logger, with two positive controls |
| `ShutdownPathTests` | B5 and the exit half of B6, including the control that a clean exit asks nothing |
| `TableEditingTests` | B2, B3, B7, each read back out of a real database |
| `StudioEngineContactTests` | three inverted pins (S3, S6, S13) |

---

## Stage 1 - an honest test foundation, and Studio can be driven

301 → 304 tests, and the number is the least of it: 249 of the old ones drove a `FakeDatabaseService`
that is permanently disconnected and answers every question with an empty collection. Every double is
deleted. The two that remain stand in for a PERSON - `ScriptedConfirmationService`,
`ScriptedDialogService` - and both of them answer a question rather than pretend to be a service.

- **`StudioFixture`** builds the real service graph, the real ViewModel graph and a real database on
  B-Tree, LSM or in-memory, with a schema that has an autoincrement key, a foreign key, an index, a
  trigger, a view and a table with **no** primary key. Its own tests check it before anything is built
  on it, and one case reuses a single service across two connections, because that is the lifetime
  `Program.cs` gives it.
- **`IDialogService` / `DialogService`**: no ViewModel constructs a window any more. This is what made
  "File > Open Database, and the schema appears" testable at all - it needed Avalonia before, so it had
  never run.
- **The automation surface**: `SqlEditorAutomationPeer` (AvaloniaEdit ships none, so the editor was not
  an element at all), plus `AutomationId` on 111 buttons and menu items. Studio can now be driven end
  to end by UI automation, which is how stage 2 was verified in the shipping executable.
- `QueryTabsViewModel` and the dead `QueryToolbar` are gone; `SettingsService` takes its path as a
  parameter, so a test with the real service cannot write the developer's own settings.

---

## Stage 2 - multi-connection

`DatabaseService` was a singleton holding one `WitDbConnection` and a **global**
`ConnectionStatusChanged`. Four ViewModels listened to it. So a tab could only ever run against
whatever Studio was connected to last, and disconnecting anything closed every data and structure tab
of every database. The plan says to do this in one pass, and it is right: a half-cut connection is
worse than an uncut one.

### The cut

| Was | Is |
|---|---|
| `IDatabaseService` (one connection, `ConnectAsync`, `CurrentConnection`) | `IDatabaseSession` - one connection, created FOR it, with its OWN `StatusChanged` |
| the same singleton, reused for every open | `IConnectionManager` - `Sessions`, `Active`, `SessionOpened` / `SessionClosed` / `ActiveChanged` |
| a tab reads `ApplicationVm.Database` | a tab holds `Session` and runs there, whatever the tree has selected (`WS-3`) |
| one root in the explorer | one root per connection; every node carries its `ConnectionId` |
| disconnect closes every tab | disconnect closes the tabs of THAT connection (`WS-13`) |

Opening and closing belong to the manager, not to the session, because they are what the collection is
made of: a session opened behind the manager's back would belong to nobody. A session **clones** the
`ConnectionInfo` it is given - the dialog goes on editing its own instance afterwards.

Consequences worth naming, because they are decisions rather than mechanics:

- **Opening a database no longer closes the one that is open.** `File > Open Database` and the recent
  files list both add a connection. `OpenRecentAsync` used to call the `async void` `CloseDatabaseAsync`
  without awaiting it and then connect over the top of the close.
- **`Close Database` closes the active connection only**, and asks only ITS tabs about unapplied work.
- **A tab that loses its connection is kept, not closed** - the text in a query tab is usually its only
  copy. It becomes unbound, says so when asked to run, and does **not** adopt the next connection
  opened: that would run a query against a database the user never chose for it. A tab that has never
  had a connection does adopt the first one, which is what keeps "start Studio, type a query, open a
  database, press Execute" working.
- **The tree selection moves the focus, not the target.** Selecting a node makes its connection active
  - where a new tab is opened, where the object dialogs create their objects, where export and import
  read and write. An already open tab is unaffected.
- The same table name in two databases is two tabs: the session is part of a tab's identity.
- Import captures its session once, for the whole import: it is a loop of thousands of statements
  inside one transaction, and re-reading the active connection each time would let a click in the tree
  move the target halfway through.

`ConnectionProfile` and `ICredentialStore` (the plan's item 4 for this stage, `S6`/`WS-68`) are **not**
done here. They are a security change with a per-platform credential store behind them, and nothing in
the connection cut depends on them; `ConnectionInfo` still carries the password in memory, as it did.

### How it was measured

**Readiness, in the plan's own words:** two databases open, an `INSERT` in a tab of the first, the rows
in the first, while the tree has the second selected. `MultiConnectionTests` runs that **twice, once
per role** - a tab that quietly used the active connection would pass a single-direction version half
the time - and exercises both execution paths, the workspace toolbar and the button inside the tab.
Every "the rows landed here" is paired with "and not there".

**Then the fix was removed again, three times, and the measurement is the red:**

| Sabotage | Result |
|---|---|
| the query tab and the toolbar use `ActiveSession` instead of the tab's | readiness red in **both** directions, plus the orphan-tab case |
| `SessionClosed` closes every data and structure tab, as the global event did | `ClosingOneConnectionClosesOnlyItsTabsTest` red: the other connection's editor is gone |
| the table editor commits to `ActiveSession` | the edit lands in the *other* database - caught by reading the value back, not by a status |

**And in the shipping executable, because that is what found the worst defect of phase 13.** Two
databases opened through `File > Open Database`; two roots in the tree, `alpha` and `beta`; `Customers`
of **beta** selected; `INSERT` executed in the tab belonging to **alpha**; read back in the same tab:
`seed of alpha` + `written from the alpha tab`. Then `Close Database` on alpha: alpha's root gone,
beta's editor tab still open with its row, status *"Disconnected from alpha"*, `Connected: True`, and
alpha's orphaned query tab keeping its text with Execute greyed out. After `File > Exit` the process
was gone, both `.witdb` files were free, and reading them from outside Studio confirmed the artifact:
**alpha 2 rows, beta 1**.

### Tests

304 → 315, all green, ~9 s. New and rewritten:

| Fixture | Covers |
|---|---|
| `MultiConnectionTests` | the readiness case both ways, the editor's target, tab identity per connection, `WS-13`, the orphaned tab's refusal, one root per connection, adoption, name collisions |
| `StudioFixture` | opens N databases through one manager; `OpenAnotherAsync`, and `CountRowsAsync` takes the session to read from |
| `StudioEngineContactTests` | the S1 case rewritten: opening a second database must ADD a connection, and the first must hear nothing at all |
| `DatabaseExplorerViewModelTests` | a control that a node whose connection is gone offers no command |

### Left for the frame (stages 4-9)

A tab carries `ConnectionName` and `ConnectionColorIndex` already, and nothing draws them yet: the
2 px coloured stripe on the tab is `WS-3`'s, and it belongs to the window frame. Until then the only
sign that a tab belongs to a closed connection is that Execute is disabled and says why when pressed.

---

## Findings for the engine, not fixed here

**`UPDATE <table> SET <column that does not exist> = 'x'` is accepted.** Measured 2026-08-05 while
looking for a violation the engine reliably refuses:

| Statement | Result |
|---|---|
| `UPDATE P SET Name = <26 chars>` into `VARCHAR(5)` | refused - *Value too long for column 'P.Name'* |
| `UPDATE P SET Name = NULL` on `NOT NULL` | refused - *NOT NULL constraint failed* |
| `UPDATE P SET Qty = -5` against `CHECK (Qty >= 0)` | refused - *CHECK constraint failed* |
| `UPDATE P SET Id = 1` onto an existing key | refused - *UNIQUE constraint failed* |
| **`UPDATE P SET Nope = 'x'`** | **accepted** |

Four of the five refusals are exactly what a client needs. The fifth is a silent no-op: a typo in a
column name updates nothing and reports success. This belongs to `Sources/**`, which this phase does
not touch.

---

## Amendments to the plan, proposed 2026-08-05

1. **The automation surface belongs in stage 1, not stage 6.** Stages 4–9 are the whole redesign, and
   there is currently no way to verify any of it except by eye: the SQL editor has no automation peer
   and most buttons announce as `Avalonia.Controls.StackPanel`, so UI automation can neither drive nor
   read the shell. `S8` should widen from "accessibility" to an automation surface across the frame,
   plus a smoke run that drives the shipping executable through open → query → grid. It fails today,
   which is the measurement.
2. **B2 does not depend on B4.** The plan's text says "after B4" while its own stage table puts them
   four stages apart. A transaction is a property of the connection, not of how values are formatted;
   parameterisation replaces the literals inside the same commit path later.
3. **S3 and S6 move to stage 0** (done). The visual redesign of the dialog stays in stage 9.
4. **Versions.** The mock-ups show Studio 3.0.0, and that is where the redesign lands. Until then the
   build is `3.0.0-dev` and releases are dev tags.
