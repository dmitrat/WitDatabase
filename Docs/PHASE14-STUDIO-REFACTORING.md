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
| 4 | The window frame |
| 5 | The Explorer and the object inspector |
| 6 | The query workspace |
| 7 | The data grid |
| 8 | The schema designer |
| 9 | Dialogs, settings, language |
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

## Stage 3 - the SQL layer

Three things that all needed the same thing first: Studio has to understand the language it sends.
It now references `OutWit.Database.Parser` directly (the plan's item 4.1), because the alternative -
reimplementing statement boundaries in the client - guarantees that the two drift apart.

### The script is cut by the parser and run a statement at a time (`WS-22`)

`SqlScript.Split` asks the parser where each statement starts and takes the text between one start and
the next. The tab then executes them in order, keeping a `StatementOutcome` for each: what it was, how
many rows, how long, and whether it failed.

Two decisions worth naming, because they are about what happens to a user's data:

- **A script that does not parse does not run at all.** A syntax error is knowable before anything is
  sent, and refusing the whole script is better than applying the first five statements and then
  reporting the sixth.
- **A statement the engine refuses at run time stops the rest.** The statements before it have already
  been applied - each is its own transaction unless the script opened one - so which ones they were is
  shown rather than left to be guessed.

The hand-written DDL scan is gone with it: whether a statement changes the schema is now the parsed
statement's TYPE, not a search for a leading keyword that had to skip comments by itself.

**Error positions are moved back into the tab's coordinates.** The engine counts lines from the start
of whatever it was given, so the sixth statement sent on its own always reports line 1. `ErrorFor`
adds the statement's own position back; a fragment executed from a selection adds the selection's
position on top of that. And the message shown is the first sentence: an engine parse error carries
the whole expected-token set, measured at **1,595 characters** for a one-word mistake, which is kept
in `ErrorDetail` for the Messages tab (`WS-11`).

### Values are bound, not written into the statement (B4)

The editor's `INSERT`, `UPDATE` and `DELETE` are built with placeholders and a `SqlStatement` that
carries the values; `SqlValueFormatter` is now only for showing a person the SQL (`WS-32`).

**Three of the plan's claims about the old path were checked against the engine, and two of them were
wrong.** Recorded here because the plan is what the next reader will believe:

| Claim | Measured |
|---|---|
| Culture-dependent `DECIMAL`/`DATETIME` formatting | **False.** The formatter already used `InvariantCulture` and fixed date formats |
| A `BLOB` cannot be written | **False.** `X'000102FAFBFF'` is accepted and comes back byte for byte |
| A user's string is substituted into the query text | **True, and not exploitable for a plain string.** `EscapeString` doubles the quotes; `O'Brien'; DROP TABLE T; --` is stored whole and the table survives |
| - | **True, and the reason to do this: precision.** `'2026-08-06 12:34:56'` is what the formatter writes for a value with 789 ms, and 789 ms is what comes back missing |
| - | **True: a type the formatter has no case for falls through to `ToString()` unquoted.** `'x'` is written as `x`, and the engine goes looking for a column called `x` |

So the case for parameters is the class rather than the incident: a value never passes through the
language, so there is no escaping step that has to be right and no type that has to have a case.

### Pages (S7, `WS-31`)

A table is read a page at a time: the page is fetched one row longer than it is shown, which is how
"is there a next page" is answered without `COUNT(*)` - a separate counter on this engine that can
disagree with the rows. With a single-column primary key the next page starts from the last key seen
(`WHERE [key] > @anchor ORDER BY [key] LIMIT n`); without one it falls back to `OFFSET` and says so.
The table editor has Previous/Next buttons; the designed grid comes in stage 7.

**The readiness criterion of this stage had to be restated, and the measurement is why.** The plan
asks for "a million-row table opens in constant time". Measured at 100,000 and 400,000 rows, three
runs each, interleaved:

| Shape | 100k | 400k | |
|---|---|---|---|
| whole table, no order | 133 ms | 548 ms | linear, as expected |
| **`LIMIT 200`, no order** | **0 ms** | **0 ms** | **constant - the scan stops early** |
| `ORDER BY Id LIMIT 200` | 310 ms | 1,327 ms | linear, and 2.4x a full scan |
| keyset + `ORDER BY`, at the end | 84 ms | 383 ms | linear |
| `LIMIT 200 OFFSET n-200` | 338 ms | 1,382 ms | linear, 3.6x the keyset form |

`EXPLAIN` names the mechanism: `LIMIT <- ExcludeInternal <- SORT <- SCAN TABLE`. The limit is not
pushed into the sort, and a primary-key range predicate becomes `FILTER` over a full `SCAN`, not a
seek.

And the reason Studio cannot simply drop the `ORDER BY` to get the constant-time open: **without it
the rows come back in INSERTION order** - measured by inserting a scrambled range into both stores and
reading it back - so pages fetched by key would overlap and miss rows. A page that is fast and wrong
is not a page.

**So: correct pages, at a cost that is the engine's.** The keyset form is the cheapest correct one
(3.6x cheaper than `OFFSET` at 400k) and it does not repeat or drop rows when the table changes
underneath it. Making the open constant-time as well needs a planner that pushes a limit into a sort
and an index-ordered scan for the primary key - a change order for the engine, added below.

### How it was measured

Every case reads rows back out of a real database. Then the fix was removed again, four times:

| Sabotage | Result |
|---|---|
| the editor's `UPDATE` written with literals again | only the structural case went red - and that is the finding: the value-with-quotes case **passes** with the old path, because the escaping worked. The test now says so |
| `ReportError` forgets where the executed fragment starts | the selection case: an error reported on line 1 instead of line 10 |
| pages always fetched with `OFFSET` | the first-page shape case; the tiling case stayed green, which is correct - `OFFSET` tiles a table nobody is writing to |
| (from the design) executing the script as one command | `ErrorLine` is never set, so the readiness case goes red |

**In the shipping executable:** a seven-statement script with a missing comma on line 6 reported
*"Line 6, column 23: extraneous input 'Name'"* - one line, no token set - and created nothing. The same
script with the mistake fixed reported *"6 statements executed in 34.52 ms"*, and a value of
`O'Brien; DROP TABLE Steps; --` came back out of the grid whole, with the table still there to read it
from.

### Tests

315 -> 338. `SqlScriptTests` (26) covers cutting and coordinates without a database;
`SqlLayerTests` (13) covers binding, script execution and pages against a real one. The reflection
test for the deleted keyword scanner is gone; what it pinned - a leading comment must not hide a DDL
keyword - is now a case over the parser.

---

## Stage 4 - the window frame

The first stage whose result is something to look at. Five zones (1.1): a title bar that holds the
menu, a contextual toolbar that belongs to the active tab, the connections panel, the workspace, and a
status bar that says four different things depending on what is happening.

### What is in the frame now

- **The title bar (1.2)** holds the menu, the command palette entry in the middle - always in the same
  place - and, on the right, notifications and the theme toggle. The custom chrome and the macOS
  `NativeMenu` variant are NOT done: they cannot be verified here, and a window frame that is wrong on
  a platform nobody can run is worse than a system one that works everywhere.
- **The contextual toolbar (WS-8)** changes completely with the type of the active tab, and its right
  edge is the **connection chip**: the one thing in the window that answers "where is this DELETE
  going" (WS-3). The query editor's own toolbar is gone - two panels of the same buttons is what this
  replaces.
- **The command palette (WS-9)**, on Ctrl+K: commands and every object of every open connection in one
  list, each object saying which database it is in. It is also the missing search in the object tree.
- **The status bar (1.5)**: connection and engine on the left, what is running in the middle with a
  progress bar and Cancel (WS-6), the transaction state and the caret position on the right.
- **Notifications (WS-7)**: a bounded, newest-first list behind the bell, with a dot when something is
  unread. Every entry is also written to the log, which is what makes trimming the list safe. Wired to
  the three things that happen with no dialog to belong to: an import finishing, an export finishing,
  and a background schema reload failing.
- **The keyboard (1.7)**: `Ctrl+K` palette, `F5` the statement under the cursor (WS-25),
  `Ctrl+Shift+F5` the whole script, `Ctrl+Enter` the selection, `Esc` stop, `Ctrl+T` new tab,
  `Ctrl+Shift+T` reopen the last closed one, `Ctrl+R` refresh. **`Ctrl+N` no longer opens a query tab**
  - it creates a database, which is what every other application means by it - and F5 no longer means
  two things at once.

**F5 is the payoff of stage 3.** "The statement under the cursor" needs to know where statements start
and end, which is what `SqlScript.Split` already returns; the caret comes from the editor, which
gained three bound properties for it.

### How it was measured

`WindowFrameTests` - 17 cases over the real ViewModel graph and a real database. Then the fix was
removed, twice:

| Sabotage | Result |
|---|---|
| the palette's object entries stop naming their connection | two cases red: the one that reads the subtitle, and the one that goes to an object of the *second* connection |
| F5 runs the first statement instead of the one at the caret | the F5 case red - one row written, from the wrong statement |

**And in the executable, which found two defects the tests could not.** Both are about focus, and
neither exists at the ViewModel level:

- **Ctrl+K did nothing on the welcome screen.** A `KeyBinding` on the window needs the event to bubble
  from a focused element, and that screen has nothing focusable on it - so the palette could not be
  opened from the one screen where it is most useful. Escape, handled in the window's own `KeyDown`,
  worked; Ctrl+K is handled there now too.
- **The palette opened without the caret in its box**, so the first thing typed went nowhere.

Both were found by pressing the keys in the shipping application, and neither could have been found by
a test of the ViewModel: the command works either way.

### Tests

338 -> 355.

---

## Stage 5 - the Explorer and the object inspector

The tree stops being a list of names, and the panel the frame reserved in stage 4 is filled in.

### What the tree does now

- **A table opens into its columns** (WS-15), read the first time it is opened rather than for every
  table at load: the primary key and the foreign keys are marked, the type is on the right, and a
  NOT NULL column is named in bold. This is the most frequent question anyone asks of a schema, and
  it needed a tab before.
- **A sixth folder: routines** (WS-21). The engine has had functions and procedures since phase 9d and
  the tree has never shown them - which reads, to a user, as the database not having any.
- **Row counts arrive on their own** (WS-16, 2.2). The tree is built from names and is usable at once;
  the counts follow, each with a two-second deadline, and one that misses it is reported as unknown
  rather than waited for. `TryCountRowsAsync` never throws - the pass over ninety tables must not end
  on the first one that is cancelled.
- **Every folder keeps its place with a count**, including an empty one at zero: a node that
  disappears breaks the muscle memory of everyone who knew where it was.
- **A double click opens the DATA** of a table, not its structure (WS-19). Looking at data is what
  people come to a database tool for, by an order of magnitude; the structure stays on the menu.

### The filter, which is not the palette (WS-17)

A filter narrows the tree across every open connection, shows the path to each match - "sales /
Tables / Orders" - counts what it found, and stays until it is cleared. The palette from stage 4 is
the other tool: one jump, and gone. Columns that have been loaded are matched too, because the name of
a column is often all anyone remembers of a schema.

### The inspector (WS-18)

The right panel follows the selection and says what the object is without opening a tab: the row count
and the columns, the indexes, what the table points at and what points at it, and the definition **as
the catalogue holds it** rather than a reconstruction from the columns - or, where the database is
older than format 9.0.0 and the catalogue holds no text, the sentence explaining why there is nothing
to show.

**The part that knows the engine** is `DATA ACCESS`: which columns can be reached through an index and
which cannot. It exists because this engine does **not** create an index for a `PRIMARY KEY`, so a
table can have a key and no index on it - and inserting rows with explicit keys then degrades sharply.
The inspector says so in the panel, from the catalogue, before anyone notices the slowdown.

### How it was measured

`ExplorerTests`, 13 cases over a real database. Two sabotages, each caught by exactly one case: a
filter that looks only at the first connection, and an index that is treated as covering any column it
mentions rather than the one it leads with.

**Two of the cases had to be rewritten because they were races, and the whole suite is what exposed
them.** Both passed when run alone and failed in the full run: one asserted that no row count had
arrived by the time the refresh returned - true only on a fast machine - and the other asked for a
count with a deadline of zero against a count this engine answers instantly. They now assert the two
ends (a usable tree, then correct counts) and a *cancelled* count rather than a timed-out one. A test
of where a background task happens to be is not a test of anything.

**In the executable:** a database created through the dialog, a script run to build a schema, and then
the tree showing six folders with their counts, `Customers 1` / `Orders 1` filled in by the background
pass, and the inspector on `Orders` showing 3 columns, the key, `IX_Orders_CustomerId`,
`Orders.CustomerId -> Customers.Id`, the real `CREATE TABLE`, and the warning that the primary key has
no index of its own.

**A defect of my own, found the same way:** the inspector panel was not in the window at all. The
script that was supposed to add it printed "inspector panel added" and had matched nothing - it never
checked. The tests passed throughout, because they drive the ViewModel. A script that reports success
without verifying is a lie told in a convincing voice.

### Left for later stages

The context-menu matrix of 2.4 beyond the double click, renaming (F2), rebuilding an index, TRUNCATE
and enabling or disabling a trigger: they are schema-changing actions and belong with the designer in
stage 8.

### Tests

355 -> 368.

---

## Stage 6 - the query workspace

Section 3, and the largest stage of the phase. The plan gives no readiness criterion for it, so this
one was written first and everything below is measured against it:

> **One session of work with a query goes through from the first keystroke to the history, and every
> step of it can be checked.** Completion from this database's schema; the mistake underlined where it
> was written; the four panels filled; formatting that loses nothing; a transaction a person opens and
> rolls back, with the rows to prove it; and a history that survives a restart.

`QueryWorkspaceTests` is that session in order, over a real database, and the same session was then
driven through the shipping executable.

### Completion, from the schema the connection already has (WS-24)

`SqlCompletion` reads **tokens**, not a syntax tree, and that is the whole reason it is its own thing:
the text under a caret is half-written, so the parser refuses it, and `SqlScript` - the parser's
answer, and the right one everywhere else - has nothing to say about `SELECT * FROM Ord`.

- After `FROM`, `JOIN`, `INTO`, `UPDATE`: the objects of this database. After `alias.`: the columns of
  exactly that table, resolved from the `FROM x a` in the same statement. **The control is that the
  same caret after a different alias gives a different list** - `c.` offers `Email` and not `Total`,
  `o.` the reverse.
- The language comes from **`WitSql.xshd`, the file that colours it**, so the two cannot disagree. It
  turned out to list a few words under two colours - `REPLACE` is both a keyword and a function there -
  and the more specific answer wins.
- Ordering is exact match, then this database's objects, then keywords, then functions. **Exact means
  the characters as typed**: case-insensitively, typing `To` towards `Total` matched the keyword `TO`
  (the one from `ROLLBACK TO SAVEPOINT`) and put it above the column.
- The design asks for "by how often it is used in this database". No such measurement exists anywhere
  in Studio, so inside a group the order is alphabetical and the code says so rather than inventing a
  ranking.
- A per-connection `SchemaCatalog` holds the names; columns are read the first time something asks and
  kept. It is refreshed **where the tree is refreshed** and nowhere else - a cache with its own opinion
  about when the schema changed would be a second answer to a question the application already answers.

### The mistake, underlined where it was written (3.6)

Two kinds, and they behave differently.

- **Syntax**, as the text is typed: parsed after it has stood still for 400 ms, nothing sent to the
  engine, and the first refusal underlined. This is stage 3's `SqlScript.Split` consumed rather than
  anything new.
- **Semantic**, at execution. The engine gives **no position at all** for these - measured:
  `Table 'Ordres' not found`, `Column 'Totl' not found`, the name in quotes and nothing about where.
  So Studio finds the name among the statement's own tokens, underlines it, and offers the nearest name
  the catalogue does have. **Ordres → Orders is an edit, not a remark**: a Replace button applies it.
- The control is that a failure which is *not* about a name in the text - a `NOT NULL` violation -
  gets no suggestion.

### The four panels (3.4)

Result, Messages, Plan, History. Messages is a line per statement of the script - which is stage 3's
`Statements` finally drawn - plus the failure, the suggestion, and the engine's full text behind an
expander (`WS-11`).

**Deliberately not done: a result tab per `SELECT`, and pinning.** They belong with the grid, which
stage 7 rebuilds; a second result surface built on the current grid would be thrown away.

### The plan, drawn as the tree it already is (WS-27, WS-28)

`EXPLAIN` returns `id`, `parent`, `detail` - a tree that Studio has been showing as three columns of
text. It is a tree now, and two shapes are marked in amber:

| Marked | Why |
|---|---|
| a `SCAN TABLE` under a `FILTER` | an index turns it into a seek, and the panel says so |
| a `SORT` under a `LIMIT` | the limit is not pushed into the sort - stage 3 measured 1,327 ms for a page of 200 rows out of 400,000 |

The negative control is that a plain `SELECT * FROM t` gets a scan and **no mark**: reading a table the
query asked for in full is not a finding, and a panel that marks every scan tells nobody anything.

**The panel says less than the design asks for, because the engine gives less.** `WS-28` asks that an
estimate not be passed off as a measurement, with row counts marked by a tilde. Measured 2026-08-06:
this engine returns **no numbers of any kind** - no estimated rows, no cost, and, since `EXPLAIN`
builds the plan without running it, no facts either. So there is nothing to mark with a tilde and every
highlight is about the SHAPE of the plan. A test pins the three columns, and it will go red the day
`EXPLAIN ANALYZE` arrives.

**The first measurement of all this was wrong, and the reason is the instrument.** Run against the
fixture's three-row `Orders`, `EXPLAIN` never once used an index - not for the indexed `CustomerId`,
not for anything - and "this engine has no index access" was one sentence from being written down. The
planner **refuses to consider an index below ten rows** (`MIN_ROWS_FOR_INDEX`). Every plan case now
fills the table first, and the one that matters shows the scan becoming
`SEARCH TABLE Orders USING INDEX IX_Orders_Total (=)` after a `CREATE INDEX` - which is what makes the
advice worth giving at all.

Still true at forty rows: **`WHERE Id = 7` on the primary key is a full scan**, because this engine
creates no index for a `PRIMARY KEY`. That is the stage-5 inspector's finding, now visible in the plan.

### Formatting through the parser's own serializer

There is no rule engine and there is not going to be one: `WitSqlStatementSerializer` already renders a
stored view or routine for the inspector, and formatting is that plus line breaks. The work is in what
it **refuses**, and all three were measured rather than assumed:

- **A statement with a comment in it is left exactly as written.** The grammar skips `--` and `/* */`
  at the lexer, so a statement rebuilt from its tree comes back without them.
- **DDL cannot be rendered at all** - the serializer throws `NotSupportedException` for `CREATE TABLE`,
  `CREATE INDEX` and `EXPLAIN`. Those stay as they are, and the summary says so.
- **Every rewrite is parsed again and re-serialized before it may replace anyone's text.** This is not
  decorative: `WitSqlExpressionSerializer` renders a subquery as the literal text `SELECT ...` - one of
  the two known causes in the engine's own `GrammarRoundTripTests` - so without the guard, formatting
  `... WHERE CustomerId IN (SELECT Id FROM Customers)` would replace a working query with something
  that is not SQL.

### A transaction a person can hold (WS-26)

Autocommit stays the default. What is new is that it can be turned off, and that Studio tells the truth
about whose transaction it is: **the connection's**. `WitDbConnection` refuses the second with *"A
transaction is already in progress"*, so two query tabs of one database share one, and both are told.
Closing a connection rolls an open one back. All five isolation levels open and undo.

The interaction that had to be designed rather than discovered: the table editor commits its buffer as
one transaction, and a query tab of the same connection may already have one open. The buffer takes a
**savepoint** inside it - released on success, rolled back to on failure - so the editor keeps its
all-or-nothing promise without ever ending a transaction it did not open.

### The history, in a WitDatabase of Studio's own (WS-29)

The one place in the product where Studio is an ordinary consumer of the engine it ships with. Text,
connection **name**, time, duration, rows, status; a repeat raises the existing entry and counts it;
thirty days or five thousand entries. **The connection string and parameter values are never written**,
and the case that says so reads the store file and searches it - with the positive control that the
query itself *is* in there.

The other side is honest and is why `IsAvailable` exists: a defect in the engine would break the
history too, so a store that will not open leaves every query working and the panel says why. Reserved
words cannot be column names here unless quoted - a column called `Text` or `Rows` is refused outright -
so the schema avoids the question.

### Accessibility (S10)

The right pattern for a caret and a selection is `ITextProvider`, and **Avalonia 12 does not have
one**: its automation surface is IValue, IRange, IToggle, ISelection, IInvoke, IExpandCollapse and
IScroll, and nothing about text ranges. So the caret and the error cannot be exposed as structure. What
is reachable is done: the peer's help text carries the caret position and any marked error, and the
editor now raises a property-changed event when the text or the caret moves - without which a screen
reader reads the editor once and never again, which is what "has an automation peer" quietly meant
before.

### How it was measured

Eight sabotages, in two rounds:

| Sabotage | Result |
|---|---|
| no rollback when the connection closes | **green** - the engine discards an uncommitted transaction anyway, so the case was pinning the engine. It now also asserts the session's own answer, which does go red |
| the table editor's batch ignores the open transaction | 2 red |
| no rollback to the savepoint | 1 red |
| statements not counted against the transaction | 1 red |
| the formatter's comment guard removed | 1 red - and the "no comment is lost" control stayed **green**, because all four of its comments were OUTSIDE a statement. Widened with two inside one; then 2 red |
| the round-trip guard removed | 1 red, the subquery case |
| aliases not resolved in completion | 4 red |
| keywords offered where a table belongs | 1 red |
| the missing name not located in the text | 1 red |
| a tab not following its connection's transaction | 3 red |

**And in the executable, which found five defects the ViewModel tests could not:**

- **the status bar drew on top of itself** - the middle section was a centred `StackPanel`, which takes
  the width it wants and overlaps its neighbours when it cannot have it; the connection summary carries
  a full file path, so on a real database there was nothing left for it;
- **opening the History panel showed nothing** - the list was filled only by the Search button, and
  every ViewModel case called Refresh itself, which is the one step a user does not take;
- **the message and the underline disagreed about where the error was**, and the status bar was a third
  answer, because it had been handed the message before the position was corrected;
- **the Replace button for a suggested name was grey** - a `RelayCommand` does not re-ask `CanExecute`
  unless it is told to, and the suggestion appears after the command is built;
- **the plan tree came up collapsed**, showing one word and a chevron.

Then the whole criterion was driven through the shipping application: completion offering this
database's tables after `FROM`; a syntax error underlined as it was typed; `Custmers` underlined on its
own name with *"Did you mean Customers?"* and the replacement applied; Format turning a one-line query
into four; `Begin` → `INSERT` → `Rollback` leaving 2 rows, with the amber *"Transaction open · 1
statement"* chip while it was open; the plan tree marking both the scan under the filter and the sort
under the limit; and the history **surviving a restart of the process**.

### Tests

368 → 450. `TransactionControlTests` (15), `SqlFormatterTests` (12), `SqlCompletionTests` (17),
`QueryHistoryTests` (9), `QueryPlanTests` (8), `QueryWorkspaceTests` (21).

---

## Stage 7 - the data grid

Section 4. The readiness criterion, again written first because the plan gives none:

> **What the grid shows is a question the engine answered, and the grid can show you the question.**
> Sorting and filtering reach the whole table rather than the page; the view becomes a `SELECT` and the
> edit buffer becomes a transaction, both readable before anything happens; a row somebody else changed
> is refused with both values side by side; and a value is shown as the type it is.

### Sorting and filtering are a new query (WS-30)

`GridQuery` is the single place a view becomes SQL - the page, the same view without its page for
**Show SQL**, and the count. One place because what is displayed has to be what was sent: a second
builder would drift, and the first time the two disagreed the feature would be worse than not having
it.

The proof that the sort is the engine's is that it **reaches beyond the page**: with a page of one, the
smallest of three rows is what comes back, which a client sorting what it was given could not know.

The filter row is a small language, one syntax for every type (4.3): a bare word is a substring, and
`= 'new'`, `> 1000`, `10..500`, `NULL`, `IN (1,2,3)`, `LIKE 'A%'` are the rest. Several are joined with
`AND`; anything needing `OR` is what Show SQL is for. **A value never passes through the language** -
`O'Brien` in a filter box is a bound parameter, and the case that says so also checks the table is
still there afterwards.

**The defect the shipping application had is exactly the one WS-30 names.** `DataGridBase` re-sorted
the PAGE it was holding by a column remembered from an earlier session, every time it rebuilt its
columns - so a table opened ordered by its key came up 12..1 under a footer saying "ordered by Id", and
each page was internally sorted and jointly meaningless. A header click did the same live. Both now go
to the engine; the saved client-side sort survives only where the page IS the data, in the query result
grid.

### Show SQL, both ways (WS-32)

The view opens as a `SELECT` in a query tab, and the edit buffer opens as the transaction it will
become - **before** it is applied, which is the only moment at which that is useful. Verified in the
executable: a range filter produced
`SELECT * FROM [Orders] WHERE [Total] BETWEEN 108 AND 111 ORDER BY [Id] ASC`, and it ran.

### The conflict, with both sides (WS-37)

The engine has no optimistic concurrency, so the mechanism had to be measured before it could be
designed: an `UPDATE` that names its row by key **and by the values it was read with** affects one row
if nothing changed and **zero** if something did. So a statement can carry an expected row count, the
batch rolls back when it is not met, and the tab re-reads the row and puts the two values side by side.
"Apply over" is a separate press, because it overwrites somebody else's work.

**An assumption was wrong and the test now says so.** A second connection to the same database file was
expected to be refused - 12.2.0 holds it under an exclusive lock - and it **opens, and sees the same
rows**. That is what makes this a real question rather than a theoretical one, and the conflict cases
use a genuine second connection.

### Values, exactly (WS-33, WS-34)

Measured: the provider returns exact types - `decimal` stays `decimal`, a GUID is a `Guid`, a BLOB is
`byte[]`. So WS-34 is not about conversion in the engine, it is about a client that renders everything
through `ToString` and parses it back through `double`. Nothing here goes through a double. A BLOB is
its size and a hex dump with the first bytes recognised (PNG, JPEG, GIF, PDF, ZIP, GZIP, BMP) and never
text; JSON becomes a tree; a line break in a cell becomes `¶` with the whole text a keystroke away; and
**NULL is never an empty cell**, because an empty string is a value and the two are different things in
the database.

### How it was measured

Three sabotages: the bare-word filter turned into equality (4 red); the page ignoring its filters
(**green** - the case asserted only the row COUNT, and ten rows cut to a page of five look exactly like
six rows cut to a page of five; it asserts the values now, then red); and the version check removed
(3 red).

**And in the executable, three defects the tests could not see:** the client-side re-sort above; the
filter row empty, because the collection was REPLACED after the view had bound the one it started with;
and the footer reading `page0 ·12rows shown`, because `Run` elements put no space between themselves.

**One measurement had to be chased and then discarded.** An early probe reported
`WHERE Status = 'Shipped'` returning one row where `LIKE '%ship%'` returned eleven - a filter answering
with the wrong rows, which is the worst thing that could be true of the feature being built. Six
controlled runs later - across table sizes, with and without case variants, with `SELECT *` and with an
explicit column list, and with different queries run before it - the engine answered correctly every
time and the reading could not be reproduced. It is **not** written up as an engine defect. What did
reproduce, three times, is that **`=` is case-SENSITIVE while `<>` and `LIKE` are case-INSENSITIVE**, so
`col = 'x'` and `col <> 'x'` do not partition a table holding both `'Shipped'` and `'shipped'`. That is
what decides the filter row: a bare word is `LIKE`, because "contains" is expected to ignore case, and
`=` stays exact.

### Left for later, deliberately

- **The filter row is not aligned with the grid's columns.** Avalonia's DataGrid does not publish its
  column widths, and a row that almost lines up is worse than one that plainly does not. The boxes are
  labelled instead.
- The column menu (2.4's equivalent for columns), pinning a result, clipboard paste with type checking,
  and the per-table column settings key: all of section 4 that is about the grid CONTROL rather than
  about what it shows. They belong with a grid that is replaced rather than extended.

### Tests

450 -> 489. `GridQueryTests` (16), `GridEditingTests` (14), `CellValueTests` (9).

---

## Stage 8 - the schema designer

Section 5, and the stage where the design and the engine disagreed most. The plan gives no readiness
criterion for it, so this one was written first and everything below is measured against it:

> **Every edit is text the user saw before it ran; nothing the engine will refuse is offered; and when
> a sequence stops, the report names what is already in the database.**

`SchemaDesignerTests` is one session of schema work in that order, and it was then driven through the
shipping executable.

### The matrix was re-measured before anything was built on it, and three of its claims were wrong

Section 5.2 is a table of what this engine's `ALTER TABLE` will and will not do. It is the whole
foundation of the designer, so it was executed rather than read. Six rounds of probes, ~120 statements,
and the plan did not survive intact:

| The plan says | Measured 2026-08-06 |
|---|---|
| `ALTER COLUMN … TYPE` is accepted but **does not rewrite the rows** | **False - it does rewrite them.** And the real problem is worse: a value that will not convert is silently replaced (`'not a number'` becomes `0`, an INTEGER becomes `01/01/0001`), and changing the type back does not bring it back. 5000 rows in 156 ms |
| dropping a column takes its **keys and constraints** with it | **Half true.** The foreign key goes; the INDEX does not - it stays in the catalogue naming a column that no longer exists, and survives a reopen |
| a trigger may have a `WHEN` condition | **True only with brackets.** `WHEN NEW.Total > 100` is a parse error; `WHEN (NEW.Total > 100)` is accepted. The parser's own message names `'('`, so the editor writes them |

So **WS-40 stands, for a different reason than the one it was written with.** The designer still refuses
to change a type in place - not because the change would miss the data, but because it reaches the data
and destroys what it cannot convert, without a word.

Everything else in the matrix held: `ADD COLUMN` (with `UNIQUE`, `CHECK`, `REFERENCES`, `DEFAULT` and
computed), `DROP COLUMN`, `RENAME`, `SET`/`DROP DEFAULT`, `SET`/`DROP NOT NULL`, `ADD`/`DROP CONSTRAINT`
are one statement each; adding a primary key is refused in those words; there is no syntax at all for
moving a column (`FIRST`, `AFTER`, `MODIFY`, `CHANGE` are four parse errors); and there is no
`ALTER VIEW`, no `ALTER TRIGGER`, no `REINDEX` and no `ALTER INDEX`.

**The matrix lives in the code as data** (`SchemaCapabilities.Matrix`) and `SchemaMatrixTests` runs every
row of it against a real database. A matrix nobody re-measures drifts away from the engine and starts
promising things, which is the exact failure section 5 exists to prevent.

### One rule Studio applies that the engine does not

`ALTER TABLE t ADD COLUMN c INTEGER NOT NULL`, with no default, on a table that already has rows, is
**accepted**. It leaves NULL in every existing row and then refuses every later write to that table -
including an `UPDATE` of an unrelated column. Giving the column a default afterwards repairs new rows
and leaves the NULLs. There is no way back short of a rebuild.

The designer will not write that statement, and says why next to the row. On an empty table the same
statement is allowed, because there the rule would be Studio's invention rather than the engine's
behaviour - `OnAnEmptyTableNothingIsRefusedAsync` is the control.

### Applying is not a transaction, and does not pretend to be (WS-42)

Measured: `ADD COLUMN` and `CREATE TABLE` inside a transaction both **survive a ROLLBACK**. So the edit
set runs statement by statement with no transaction, stops at the first refusal, and reports three
states per statement - applied, failed, not reached.

The sabotage is the argument: wrapping the set in a transaction and rolling back on failure makes the
report say *"Applied 0 of 3"* while the two columns are **in the database**. That is what
`ExecuteBatchAsync` would have given, and it is a promise the engine does not keep.

### The rebuild does not rename, because renaming loses data (5.3)

The design's four steps end with *"rename `Orders__new` to `Orders`"*. On this engine that step is
destructive, and it took three rounds of controls to be sure of it:

- after `ALTER TABLE … RENAME TO`, the table's key generator restarts at zero, and the next generated
  `INSERT` lands on key 1 and **overwrites the row that is there** - silently, reporting one row
  affected. On B-Tree and on LSM, and across a close and reopen;
- a `RENAME COLUMN` does not do it, an `ADD COLUMN` does not do it, and an explicit duplicate key IS
  refused with a `UNIQUE` violation - so it is the generated-key path that skips the check, and the
  rename that leaves it pointing at an occupied key;
- a `UNIQUE` index on the key column turns the overwrite back into a refusal.

So the rebuild copies the rows **out** to a carrier, drops the original, creates it again under its own
name and copies them back - measured to leave the generator correct, including after a reopen. It costs
one more copy of the data and a window in which the table does not exist, which is why the plan says so.
`ARebuiltTableStillGeneratesItsKeysAsync` is the control: with the design's rename put back, the rebuild
plus one insert leaves **3 rows where there should be 4**.

The plan also **counts what the conversion will destroy before it starts**. This engine's `CAST` never
fails - `CAST('not a number' AS INTEGER)` is 0 and so is `CAST('3.9' AS INTEGER)` - so a rebuild that
did not count them would be exactly as quiet as the `ALTER` it replaces. The count is a round trip:
values that do not come back unchanged. In the executable, on two orders of 4812.50 and 1204.00, it
reported one casualty, which is right.

### The rebuild is planned, explained, and NOT run - a defect found in the executable

**Running the rebuild from the dialog left the database unreadable. Twice, on two different databases.**

The file is genuinely damaged: opened with nothing but the ADO.NET provider, outside Studio, it throws
`InvalidDataException: Page 9 is not an overflow page` (and `Page 7` for the second) out of the schema
catalogue's overflow chain. The reproduction is: create a database through the Create dialog, run a
schema script, rebuild a table, leave through **File > Exit** (a clean exit - the first case was
initially blamed on a killed process, and the second ruled that out), then reopen.

**Fourteen controlled runs failed to reproduce it headlessly**, and they are what makes the report worth
anything: the rebuild alone; with the trigger dropped first; with the index dropped first; with both;
with an extra `ADD COLUMN` before it; the same statements typed by hand; over 2000 rows; with one and
with four readers scanning the table throughout; with a second database open in the same process; and at
page sizes 512, 1024, 4096 and 8192. All fourteen reopen correctly. And the control **without a
rebuild** - same creation path, same script, same clean exit - reopens correctly too, which is what
implicates the rebuild rather than anything around it.

So the mechanism is not known, and the honest thing is not to run it:

- the dialog still plans, counts the casualties, names the dependencies and shows the script;
- the button is **not armed**, and says why on screen rather than being mysteriously grey;
- **To the editor** puts the whole plan in a query tab, and running it there is measured to be safe.

`TheRebuildDialogWillNotRunItYetAsync` pins that decision. When the cause is found, delete the test and
arm the button. The two damaged files are kept in this session's scratchpad as evidence.

### The index dialog offers what the engine does, and says what it buys (WS-43)

Measured over 200 rows, so the ten-row threshold below which no index is considered is not what is being
measured:

| Offered | Accepted | Used by the planner |
|---|---|---|
| plain | yes | **yes** - `SEARCH TABLE … USING INDEX` |
| `UNIQUE` | yes | yes, and it enforces uniqueness |
| `INCLUDE` (covering) | yes | **yes** |
| partial, `WHERE` | yes | **no** - a full scan either way |
| `DESC` | yes | **no** - `ORDER BY … DESC LIMIT` still sorts the whole table |
| by expression | yes | **no**, and the catalogue reports the column as `$expr0` |

All six are offered, because all six are stored and a database is not read only by Studio. The two that
buy nothing today say so next to the box: an option that quietly does nothing is worse than one that
explains itself.

`INFORMATION_SCHEMA.INDEXES` publishes nine columns and **none of them is the direction or the included
columns**, so an index Studio recreates during a rebuild is the index the catalogue could describe, not
necessarily the one that was there. The rebuild plan says that out loud.

### The key warning, in three states (WS-44)

A property of this engine rather than general advice: no index is created for a `PRIMARY KEY`, so a key
whose values are supplied by hand makes every insert scan the table. The designer and the index dialog
share the three states - AUTOINCREMENT needs nothing, a hand-set key with an index is fine, a hand-set
key without one is warned about - and a table with no key at all is told what that costs.

### The editors inside the language's boundary (WS-45)

- a trigger body takes only `SELECT`, `INSERT`, `UPDATE`, `DELETE` and `MERGE`; the editor says so and
  checks it with the parser before the engine is asked;
- `SET NEW.column = …` does not parse, and the engine's message for it is
  *"mismatched input 'NEW' expecting TRANSACTION"* - `SET` is being read as `SET TRANSACTION`. The
  editor explains that instead of passing it on, and no template offers the shape;
- `FOR EACH STATEMENT` is a parse error; **omitting the clause** is how a statement trigger is written,
  and the catalogue then reports `ACTION_ORIENTATION = STATEMENT`;
- there is no `ALTER TRIGGER`, so replacing one is a `DROP` and a `CREATE` - and the button says
  "Drop and create", because it is not atomic and the old body is the only copy while it runs;
- **a view whose body the catalogue cannot render is not offered for editing.** A `UNION` and a
  subquery both come back with `VIEW_DEFINITION = NULL` - the phase-8 rule working as designed, the
  renderer refusing to report a rendering that lost something. Editing a view means dropping and
  creating it, and creating it from a body Studio does not have would destroy it.

### Deferred from stage 5, and what they turned out to be

- **F2 renames a table and nothing else.** `ALTER VIEW`, `ALTER INDEX` and `ALTER TRIGGER` do not exist
  in this language, so there is no way to rename a view, an index or a trigger at all. The tree offers
  it only where it works.
- **TRUNCATE** is in the grammar and works.
- **"Rebuild an index"** has no engine support - no `REINDEX`, no `ALTER INDEX`. It is a drop and a
  create, and the menu item says that.
- **Enabling or disabling a trigger** has no engine support either, so it is not offered.

### How it was measured

Five sabotages, each red in exactly the case that exists for it: the column dropped without its index;
the NOT NULL refusal removed; a key column dropped like any other; the `WHEN` brackets removed (red in
two cases - the text and the execution); and the edit set wrapped in a transaction, which produced the
false *"Applied 0 of 3"* above.

**And five defects came out of the running application that no ViewModel test could see:**

1. the section strip said **"Columns 0"** over five rows - a computed property is read once when the
   strip binds, which is before the table has been read;
2. the object inspector went **stale** after every schema change - it is bound to the tree's selection
   and nothing had told it the object underneath had changed;
3. the columns grid **overlapped itself**: the name cut off, `CONSTRAINTS` and `CHANGE` running
   together, and the drop button sitting on top of `AUTOINCREMENT`;
4. the rebuild dialog's **report appeared below the fold** - the one thing the dialog exists to say was
   the one thing off screen;
5. the section strip announced as **`Avalonia.Controls.StackPanel`**, invisible to a screen reader.
   That is the defect `AutomationSurfaceTests` exists for, in an element type it was not looking at:
   the guard now covers `RadioButton` as well, which found three more in the export dialog. `CheckBox`
   (12) and `TabItem` (7) are still outside it.

And the sixth is the corruption above, which is the reason the rebuild is not armed.

### Tests

489 -> 549. `SchemaMatrixTests` (11), `TableRebuildTests` (11), `SchemaDesignerTests` (21),
`SchemaDialogTests` (11), `ExplorerSchemaActionsTests` (6).

### Left for later, deliberately

- **The cause of the corruption.** It is the first thing to pick up: everything else in the rebuild is
  built and tested behind the disarmed button.
- **Sequences.** `CREATE SEQUENCE` takes a name and `START WITH` and nothing else - no `INCREMENT BY`,
  no `MINVALUE`/`MAXVALUE`, no `CYCLE`, no `AS <type>`, though `INFORMATION_SCHEMA.SEQUENCES` publishes
  all of them. `NEXTVAL('s')` works, `NEXT VALUE FOR s` does not. The editor 5.5 asks for is mostly
  unbuildable, so it is not built.
- The dependency dialog of 5.7 as a dialog: the dependencies are shown in the rebuild plan and the
  index that would be orphaned is dropped with its column, but "delete this column?" does not yet stop
  to list what goes with it.
- `CheckBox` and `TabItem` in the automation guard.

---

## Stage 9 - the dialogs, the settings and the language

Section 6 and the parts of section 9 the plan's phase 9 names. The plan gives no readiness criterion
for it, so this one was written first and everything below is measured against it:

> **A dialog asks only what Studio cannot find out for itself; a setting takes effect at the moment it
> is changed; and a value the interface shows can be pasted into SQL unchanged - in either language.**

The last clause carries the control, and it is why the language and the formats are one stage rather
than two: **switching the language must change no value, no identifier, no SQL, no plan operator and
no engine message.** A localisation nobody can prove is separable from the data is a localisation that
will eventually format a decimal.

### Three defects that had shipped, and none of them was new work

They were found by building the stage rather than by looking for them, which is the argument for
re-measuring a design against the engine before implementing it.

**The grid drew values in the machine's locale.** `SqlValueFormatter.FormatForDisplay` returned
numbers and dates unchanged, with a comment saying the DataGrid would render them "culture-aware". It
does: on a ru-RU machine a `DECIMAL` was drawn as `4812,50` and a `DATETIME` as `28.06.2026`, and
neither pastes into a statement. **Four cases were quietly agreeing with it** -
`ConvertDecimalReturnsOriginalValue` and its three neighbours asserted that the value came back
untouched, with the culture-aware comment attached as the reason. They passed because the suite has
only ever run under en-US.

**A ChaCha20-Poly1305 database could not be opened by Studio at all.** `BuildConnectionString` wrote
the literal `Encryption=aes-gcm` into every string it produced, so the provider was handed the wrong
algorithm and answered that the password was wrong - the failure was reported to the user as their own
mistake. Measured both ways: a ChaCha20 database built with the engine now opens through Studio's own
connection, and naming AES-GCM for the same file and the same password does not.

**The name chosen in the Open dialog reached nothing.** `DatabaseSession` always derived its
`DisplayName` from the file name, so the name box was decoration: the session, the tab and the saved
connection all showed `sales.witdb` whatever was typed. Caught by a case asking for the name back
after a reopen.

### What the engine can say about a file before it is opened, which is less than the design assumed

Section 6.2 shows one line under the path box - *"found a B-Tree database, 84 MB, encrypted AES-GCM,
MVCC"*. Measured, that line is obtainable only for an **unencrypted** database. `StorageDetector` reads
the header out of the first page, and in an encrypted database that page is encrypted: it answers
`StoreType = "btree"` (an assumption, not a reading), `EncryptionProvider = "unknown"`, and it cannot
see MVCC, the journal or the page size at all.

Worse: **a file that is not a database fails the same magic-byte check**, so it comes back looking
exactly like an encrypted one. Studio would have asked for the password to a text file and then blamed
the password.

`StorageProbe` therefore answers with what is actually known - `NotFound`, `NotADatabase`, `Database`
or `Unreadable` - and the dialog has three states rather than one.

**A control went red here and the design was wrong, not the control.** The probe first had two states
for a file with no magic bytes, "encrypted" and "encrypted, but possibly not a database", and a case
was written to prove the second was earned. It failed against a *real* encrypted database, because
encryption is exactly what makes the header unreadable - there is no reading that separates them. The
two states became one and the dialog says both. What is left is the half that can be true: a readable
database is not reported as unreadable.

### The settings apply themselves (WS-52)

The window holds **no copy of any setting**. It binds to the one live `Settings` object the whole
application reads, so a change is the change; there is no Save and no Cancel, because there is nothing
to write back and therefore nothing to forget to write back. `Settings` grew from 8 properties to 28
in the design's five sections, and every one of them raises `PropertyChanged` - which is what
"applied immediately" is implemented *with*.

Three sabotages, each red in exactly the case that exists for it: persisting on change removed;
the language unwired from the setting; and reset **swapping** the live object instead of copying onto
it. The third is the trap the design avoids - every open window would be left reading an object nobody
writes to any more, which on screen is indistinguishable from a setting that stopped applying.

`About` is a section rather than a window (WS-53); `AboutViewModel` and `AboutDialog` are deleted. The
file format version it reports is read from the engine's own constant - **1.1**, where the mock-up says
"version 9".

### The language, and the two ways a localisation stops being one

The catalogues are **embedded in the assembly** rather than shipped as satellite assemblies: Studio is
packed for three platforms, and a satellite that fails to arrive turns the interface English with no
error anywhere - a failure indistinguishable from nobody having translated it. A missing key renders as
itself, so it is visible on screen and greppable.

**A language is a file, and that took a second pass.** The version that first shipped had a
general-looking interface over a mechanism hardcoded to exactly two: the offered languages were a
literal list in the constructor, each catalogue needed its own `<EmbeddedResource>` line, the plural
rules were a `switch` on the language code with a case for `"ru"` and an English-shaped default, and
the tests compared `"en"` against `"ru"` **by name** - so a third catalogue could have been embedded,
offered, and checked by nothing.

Now the languages are discovered from the assembly manifest, the csproj globs
`Resources\Strings.*.json`, and each catalogue carries its own header: `$language`, its name in its own
language for the picker, and `$plural`, its family. **Plural rules are families rather than
languages** - `one-other`, `slavic`, `one-form` - so a new language that behaves like one already here
needs no code, and a catalogue naming a family this build does not implement is refused rather than
falling back quietly. Every case walks the languages that shipped and compares each against the base.

Measured rather than claimed: dropping a `Strings.fr.json` in - a file, nothing else - made French
appear, and **38 of 39 cases passed with it**; it was discovered, offered, and checked for complete
keys, for naming itself and for declaring a family. The one failure was the right one - the control
that refuses a copy said *"195 of 195 fr strings are byte-identical to the base"*. The fake French was
then deleted: a translation nobody has done does not belong in the repository.

Every case was run against a broken catalogue before being trusted:

- a Russian catalogue that is a **copy** of the English one: red, 52 of 52 byte-identical. That is the
  control for "every key exists in both languages", which a copy passes perfectly;
- a key removed from Russian: red; the three Russian plural forms collapsed to one: red;
- an engine term translated - `B-Tree` as "Б-дерево": red. **The first attempt at this sabotage stayed
  green, and the sabotage was wrong rather than the test**: it translated a term in a string whose
  English side does not carry it.

**The WS-65 case had to be rewritten because it was powerless.** Asked of an integer count it stayed
green with the service switched to `CultureInfo.CurrentCulture` under ru-RU, because the default
numeric format inserts no group separator in any culture. Only a decimal separator tells them apart -
`4812.50` against `4812,50` - which is exactly the value that will not paste. Now red both ways:
through the thread culture and through the interface language.

### The storage is one choice of three (WS-48)

The Create dialog's storage used to be two independent axes - a store, and a file/memory switch - which
is what allowed "in memory + LSM", a combination the engine answered by writing a database into the
process working directory. Stage 0 refused it. Stage 9 removed the ability to express it, and **the
refusal is deleted with a comment saying why: a check that cannot be reached is a comment pretending to
be code.** The guard that can still be wrong is the other direction and is measured - naming a store
while the database is in memory must not put it back on disk.

The storage decides the next question, which is why it is asked first: a file, an empty folder, or
nothing at all.

### The connection colour finally appears somewhere

Every tab has carried a `ConnectionColorIndex` since stage 2 and **nothing drew it**, so the only sign
a tab belonged to another database was a greyed-out Execute. One palette in `ConnectionColors` now
feeds the swatch row where the colour is chosen and the stripe on the tab where it is read. A picker
whose colour appeared nowhere would have been decoration.

### The saved connections (WS-68)

`connections.json` beside the settings - beside rather than inside, because the two are cleared for
different reasons. Two of its cases are about what the window does not do:

- **"Remove" removes from the LIST**, and there is a case asserting the database is still on disk
  afterwards. Deleting a database from the interface that manages databases is a function that will one
  day be pressed without looking, so it is not offered;
- **a missing database is marked and kept.** The disk may not be mounted, and a row that vanished on
  its own is indistinguishable from lost settings - after which someone creates a new database over a
  path they believe is empty. The control is that a database which is there is not marked.

**No password reaches the file and there is no field for one.** The case reads the JSON rather than the
model, because the model is not what leaks. The credential store of WS-68's second half is still
deferred; `PasswordIsStored` is a note, and today it is only ever false.

### How it was measured, and three defects the running application had

The two rebuilt dialogs were driven in the shipping executable, which has found the defects no
ViewModel could in every stage of this phase. Three again:

1. **A path that was TYPED was never recognised.** The probe ran only from the two Browse buttons, so a
   path typed, pasted, or arriving from the recent list produced no sentence at all - the commonest way
   a path arrives. Every ViewModel case called `ApplyAutoDetectedSettings` itself, so the suite could
   not see it;
2. the three storage cards announced as **`Avalonia.Controls.StackPanel`**, and
3. the six colour swatches as **`Avalonia.Controls.Border`** - six identical unnamed items.

Both of the last two carried `AutomationId`s. This is the defect `AutomationSurfaceTests` exists for,
and **the guard could not see it: it only ever asked about the Id, never about the NAME.** It now has a
second rule - an interactive element whose content is a *panel* rather than text must carry an
`AutomationProperties.Name` - plus `ListBoxItem` in its element list and a control that the new rule is
not measuring an empty set. The first draft of the rule asked for a Name from anything with children
and named forty menu items that announce perfectly well from their `Header`.

Two traps in the tooling, both of which produced a wrong reading before they were understood:

- **MSBuild reads `.en` and `.ru` in a file name as a CULTURE.** `Strings.en.json` and `Strings.ru.json`
  both came out as the single manifest resource `…Resources.Strings.json`, one silently overwriting the
  other. `GetManifestResourceNames` listed the two `.xshd` files and nothing else;
- **`Copy-Item` preserves the source's timestamp**, so restoring a file after a sabotage leaves the
  incremental build convinced nothing changed. One sabotage looked caught and another looked uncaught
  for that reason alone, until the built assembly was read rather than the source.

### The saved connections (WS-68)

`connections.json` beside the settings. Two of its cases are about what the window does NOT do, and
both are the reason it has this shape:

- **"Remove" removes from the LIST**, and there is a case asserting the database is still on disk
  afterwards. Deleting a database from the interface that manages databases is a function that will
  one day be pressed without looking, so it is not offered;
- **a missing database is marked and kept.** The disk may not be mounted, and a row that vanished on
  its own is indistinguishable from lost settings - after which someone creates a new database over a
  path they believe is empty. The control is that a database which is there is not marked.

**No password reaches the file and there is no field for one.** The case reads the JSON rather than
the model, because the model is not what leaks. The credential store of WS-68's second half is still
deferred; `PasswordIsStored` is a note, and today it is only ever false.

And the window found a defect in the work of the commit before it: **the name chosen in the Open
dialog reached nothing.** `DatabaseSession` always derived its `DisplayName` from the file name, so
the name box added for WS-46 was decoration - the session, the tab and the saved connection all showed
`sales.witdb` whatever was typed.

### The import, in batches (WS-50)

The rule that shaped it is the design's: **batches, not one transaction.** A million rows in one
transaction is a million versions in MVCC and a journal that grows until it stops, and a cancel would
then throw away work the user watched happen.

The old code did the opposite - it wrapped the whole file in a transaction unless `ContinueOnError`
was set, one flag meaning "keep going past a bad row" AND "do not be atomic" at once. That is now
three separate choices, and all-or-nothing survives as an opt-in: the default must not be the mode
that fails on the largest file.

**The design's third option was measured before it was offered.** It says the update is done with
`MERGE`; `ImportConflictProbeTests` executes it and both halves hold - the matched row is updated and
the unmatched one inserted. An update path that only updated would silently drop every new row in the
file. Unlike three of stage 8's claims about `ALTER TABLE`, this one was true.

Each answer is measured by reading the VALUES back: Skip leaves the existing row and **still imports
the rest**, which is the whole difference from Abort and exactly what a count cannot see. Two
sabotages - one transaction again, and Abort made not to stop - turn the right cases red.

Every rejected row is kept with its line, the engine's own message and the line itself; the window
shows ten and the report writes all of them. **The line is the one IN THE FILE**, not the data row
number: they differ by one whenever there is a header, and a report saying "row 412" that points at
line 413 costs somebody ten minutes. The first version of that case asserted the data row number, and
the number was the thing to decide rather than the code.

### The export, and where a dump's order actually matters (WS-51)

The scope is chosen first and starts on what the user HAS - a selection if there is one, the page
otherwise. Starting on "everything" is how an export of one row becomes an export of four million. The
three counts are three different numbers, and the third is easy to get wrong: the grid pages
server-side since stage 7, so the page is not the table. Markdown is added and escapes what would
break the table.

The whole-database dump is a WitSQL script that says in its first two lines what it is, because the
difference from a byte copy is the thing people get wrong. The case that matters RUNS it into a
second, empty database and reads the rows back.

**And its control changed what the order is for.** It was written as "a table referencing one that
does not exist yet is refused" - and this engine ACCEPTS that. The dependency sort does not protect
the schema; it protects the DATA, because an INSERT whose foreign key points at a row that is not
there IS refused. Both halves are in the case, since a refusal on its own could be about anything.

A cycle does not hang and does not lose a table: two tables referencing each other are legal, cannot
be ordered, and are written anyway and named.

### The language, in the running application

The shell is swept into the catalogue - 62 keys, the whole menu, the toolbar with its tooltips, the
welcome screen - and the application was switched to Russian to see it. What stayed English is as
important as what changed: the paths, `Ctrl+K` and `Ctrl+O`, and the product name.

**The lint names the fifteen views that are not swept yet rather than implying them.** The rule is
real today for everything already done, so a new hardcoded caption in a swept view goes red
immediately; a lint that waits for the whole sweep guards nothing for as long as the sweep takes. A
second case keeps the list honest, and a control refuses to let almost every view be excused at once.

**What the lint does not cover is written into it**, because switching to Russian showed it: text
built in a ViewModel - the status bar's "Ready", a notification's summary - is outside a markup lint,
and so is `AutomationProperties.Name`, which a screen reader announces and which is English in every
view.

### Three more defects from the running application, and the guard that could not see two of them

The wizard was driven after it was built:

1. **the step strip did not say which step you were on.** The bool converters were bound to `Opacity`,
   which is a double: the binding failed, fell through to its FallbackValue of 1, and all three labels
   were drawn identically. It looked completely normal in a screenshot;
2. **the column mapping appeared on step 2 as well as step 3**, empty - bound to "not the first step";
3. two checkboxes announced as **`Avalonia.Controls.StackPanel`** - the same defect as the storage
   cards and the colour swatches, in the one element type the guard has never covered. `CheckBox` was
   written down as outside it back in stage 8.

The guard covers `CheckBox` now, for the NAME rule only: a checkbox announces from its Content and is
found by that text, so requiring an Id from the dozen already shipping would be a sweep with no defect
behind it. The widened guard flagged exactly the two the application had shown and nothing else, which
is the measurement that the rule is the right width.

**Six defects across two rounds of driving the executable, and no ViewModel test could have seen any
of them.** That is now true of every stage of this phase.

### Tests

550 -> 672.

### Left for the rest of the stage

The fifteen views on the sweep list, and with them the ViewModel strings and the automation names,
which a markup lint cannot reach. `TabItem` is still outside the automation guard. WS-68's credential
store is still deferred, so a saved connection still asks for its password every time.

---

## Stage 10 - the sweep, and the two classes a markup lint cannot reach

Stage 9 shipped the mechanism and named its remainder: fourteen views still carried their own text, and
`LocalizationCoverageTests` said in its own summary that it could not see a string built in a ViewModel
or an `AutomationProperties.Name`. This stage is that remainder. The criterion was written first:

> **Switching the language changes every word Studio wrote and no word it did not** - and what a screen
> reader announces is one of those words.

### What was swept, and what the numbers were

**318 literals in the markup of 17 views**, of which 7 were already-exempt engine terms and gestures.
`NOT_YET_SWEPT` is deleted: there is no list of excused files any more, because there is nothing on it.

Three classes turned up that the count did not include and no rule had been looking for:

- **85 `AutomationProperties.Name` attributes**, every one of them English. They are what a blind person
  hears, and the six windows stage 9 swept announced themselves in English throughout while every test
  passed;
- **captions inside bindings** - `StringFormat='page {0}'`, `TargetNullValue='not counted'` - which are
  markup and which no rule over attributes can see;
- **sentences built out of `<Run>` fragments.** "Total:" + a number + "rows" reads correctly in English
  and cannot be translated at all: another language moves the number, inflects the noun after it, or
  puts the unit first. Eight of these were rewritten as one catalogue entry each.

The catalogue went from **202 entries to 650**, in both languages.

### The mechanism the fragments needed

A value converter is asked once, when its binding evaluates. A sentence assembled by one would stay in
the language it was built in until something else moved on screen - which is the same failure as not
translating it, arriving later.

`LocalizedText` therefore takes the **template through the binding**:

```xml
<MultiBinding Converter="{x:Static conv:LocalizedText.Format}">
    <DynamicResource ResourceKey="S.Query.SyntaxError"/>
    <Binding Path="SyntaxErrorMessage"/>
    <Binding Path="SyntaxErrorLine"/>
</MultiBinding>
```

Swapping the catalogue re-evaluates the whole binding, so these refresh with everything else. A plural
needs a *rule* rather than a template, so its converter takes the **language** as its first input -
nothing reads it; it is the trigger. `LocalizedResources` publishes `S.$language` for exactly this.

Four converters, and the last two were written because the running application asked for them:
`Format`, `Plural`, `Keyed` (a list whose items are their own identity - the settings sections are
compared by value and drawn from it) and `Or` (a translated fallback, which a binding cannot have -
see the defects below).

### The two new rules, and what they are written around

`LocalizationCoverageTests` has three rules now, and the third is **written around destinations rather
than around literals**. "Explorer refreshed {Connection}: {Tables} tables" and "Renamed {old} to {new}"
are the same shape; the first is a log template that must stay in one language and the second is a
sentence in the status bar. Nothing about the strings tells them apart - where they GO does. So the
rule names the properties a view binds and the services that show a message, and skips any line
carrying `Logger.`.

**Every rule was measured in both directions**: a caption put back into `QueryEditor.axaml`, an
announcement put back into `DatabaseExplorer.axaml`, and `StatusText = "Ready"` put back into
`MainWindowViewModel` - each turns its own rule red and only its own, and green again when restored.
The rules were re-measured after they were widened, not before.

**Each rule carries a control that counts the SURFACE, not the findings.** A rule that matches only
literals matches nothing once the sweep is done - and "nothing left to find" and "the rule is reading
the wrong folder" produce the same number. The controls count every caption, every announcement and
every destination *including* the ones that already come from the catalogue.

**Three false positives were designed out rather than exempted**, and each is a case the rule got wrong
on a real file: `SizeToContent="Height"` read as a `Content` of "Height" (fixed with a look-behind),
`&lt;` read as the word "lt" (fixed by decoding entities), and `{0:F1} %` read as prose (fixed by
requiring the letters to be OUTSIDE the placeholders). `EveryRuleCatchesWhatItIsForAndNothingElseTest`
pins all three plus one line of real text per rule.

**The rule was widened twice by things it had missed, and both were found by the application rather
than by reading it.** It read one line at a time, so an assignment whose string begins on the next line
went past it - which is how the status bar's "stage9: 0 tables, 0 views..." survived. And its
look-behind, borrowed from the markup rule, rejected `ApplicationVm.MainWindowVm.StatusText = "..."`
because of the dot in front of it.

### What it still cannot reach, said out loud

It reads text, not a program. A caption assembled in a helper the rules do not name, or rendered by a
model's `ToString`, goes past it - `DataAccessNote` rendered "primary key with no index" that way and
the inspector's panel stayed English under a Russian interface until the executable showed it. The fix
there is worth naming as a rule of its own: **a model cannot reach the catalogue, so a model must not
render itself.**

### Seven defects, and every one of them came from running the application

None was visible to any of the 682 tests.

1. **The tree's six folders** - Tables, Views, Indexes, Triggers, Sequences, Routines - were the only
   English left in the explorer. They are built as node names, which no destination rule covers. The
   fix carries a second lesson: a folder is now remembered as expanded **by a catalogue key**, because
   the memory used to be keyed by the name on screen and would have forgotten every open folder the
   moment somebody switched language;
2. **the status bar's schema summary**, built across two lines (see above);
3. **`Autocommit`**, the transaction state, in two places - the ViewModel's default and a
   `FallbackValue` in the markup;
4. **the settings section list** - General, Editor, Data, Diagnostics, About - sitting in English beside
   its own heading in Russian. The value is the identity there, so only the caption is translated;
5. **the theme name** on the title bar button, written in the window's code-behind;
6. **the inspector's whole DATA ACCESS panel** and the object's subtitle ("stage9 - table");
7. **the grid's own sentence** - "ordered by Id" - built in `GridQuery`, a helper with no ViewModel.

**And one defect I introduced while fixing the third and had to find the same way.**
`FallbackValue={DynamicResource S.Query.Autocommit}` does not evaluate: Avalonia assigns the markup
extension OBJECT, and the status bar read
`Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension`. A binding cannot have a translated
fallback; `LocalizedText.Or` is what it needs instead. **Check the artifact, not the diff** - the build
was clean and the tests were green.

### Two things that were already wrong and are not localisation

- **The tab strip drew a question mark beside every tab with unsaved work.** `Text="?"` has been in
  `WorkspaceTabStrip.axaml` since Studio's first commit - a bullet mangled on the way in. It has no
  letters, so no lint could ever have seen it; it is now `●`. Same class as [[repo-file-encodings]],
  one layer out;
- `Count.Statements` said "стейтмент" in Russian - a transliteration where "оператор" is the word.

### The border held (WS-64)

Nothing on the engine's side of it moved. What stayed in English, deliberately: `B-Tree`, `LSM`, `SQL`,
`CSV`, `JSON`, `MVCC`, `EXPLAIN`, `DDL`, every SQL keyword and clause shown as an example, the isolation
levels, object names, and the engine's own error text inside the sentences that frame it. Measured in
the running application: a `DECIMAL` still draws as `4812.50` with a decimal POINT under a Russian
interface, which is the WS-65 control and the thing that would have broken first.

---

## Stage 11 - the rebuild button, armed

Issue 10 in [KnownIssues.md](KnownIssues.md) carries the six runs; this is what they changed in Studio.

`TableRebuildViewModel.CanRebuild` was a hard `false` and is now `Plan.Steps.Count > 0`. The only
refusal left is an empty plan, and it says so from the catalogue
(`Dialog.Rebuild.NothingToDo`). `TheRebuildDialogWillNotRunItYetAsync` - which pinned the disarming for
one day, deliberately, so it could not be undone silently - is replaced by two cases that pin the
arming in both directions.

### What the run taught, and it is about the instrument

**The obvious experiment could not have failed.** Rebuild a table, leave through `File > Exit`, read the
file: 36 = 36, opens, done. Then the same run ending in `taskkill` gave **36 = 36 and opened as well** -
so the setup could not produce corruption at all, and the clean result certified nothing. A rebuild is
four statements; that is not enough churn to leave a page unevicted.

What gave the pair its power was moving the probe's workload into the application: twelve
`CREATE TABLE`/`DROP TABLE` pairs typed into Studio's own query editor, then killed - **header 32 against
61 pages on disk, and the file will not open**. With the rebuild added and the menu used instead of the
kill: **64 = 64, opens, 20 rows scanned back**. Same application, same cache, same database - only the
ending differs.

So the negative control now lives inside Studio rather than in a console probe: the same application
can be made to produce the failure on demand, which is the only thing that makes its absence mean
anything.

### A localisation class the lint could not see, found by using the dialog

Arming the button made the rebuild dialog a primary path, and it came up with a Russian heading over
four English step titles. Two causes, both now measured:

- **rule 3 matched `=` and not `=>`**, so every EXPRESSION-BODIED destination was invisible to it -
  `NotArmedReason` and `BackupWarning` had been sitting in English since the sweep. Fixed, and
  `RowCountText` (a ternary, which the rule still cannot see) went into the catalogue with them;
- **the rule read `ViewModels` and `Views` only.** It reads `Services` now, and that named a class
  rather than a slip: `SchemaChangeSet.Description`, `TableRebuild.Title` and `QueryPlan.Warning`
  **compose** the sentences the designer, the rebuild dialog and the plan panel show.

That last one is a **named remainder, not a sweep**, in the shape stage 9 used for the fourteen unswept
views. A service that renders is stage 10's "a model must not render itself" one layer out: the fix is
for the plan to carry the change and its parameters and for the ViewModel to say it, and several cases
in `TableRebuildTests` and `SchemaDesignerTests` assert on the English wording today. The three files
are listed in `LocalizationCoverageTests` and the list is asserted **exactly** - a fourth composing
service fails the rule, and so does a listed one that has been fixed and left on the list. Both
directions were measured by sabotage.

Studio 684 -> 685.

---

## Stage 12 - what opening an LSM database in Studio found

Not a stage of the plan. The manifest that landed with `WS-57` (PR #142) changed the rule by which an
LSM store decides which files are live on open; CI was green on it and **Studio had never opened an LSM
database since**. The check was meant to take five minutes. It took longer, and the two defects below
are what it cost - both of them found by running the application, neither visible to 685 tests.

### The manifest, measured in Studio and in both directions

The full record is in `@Evidence/lsm-manifest-studio`, including the two prepared databases. What
matters here:

**The obvious run could not have failed.** Studio opened an LSM database, wrote 50 rows from its own
editor, **compacted twice inside the application**, left through `File > Exit` and reopened with all 89
rows and the deleted key still deleted. Nothing broke - and nothing could have: both compactions
deleted their inputs, so the directory and the manifest name the same files and the old rule and the
new one are indistinguishable. That run measures "the manifest did not break Studio's LSM open", which
is worth knowing and is not what it was for.

**A crashed compaction had to be arranged by hand** - compact, then restore every input except the
flush holding the tombstone, so an unnamed survivor carries the deleted row's value while the merged
output has dropped the tombstone. Two byte-identical folders, one with its manifest renamed away,
opened side by side as two connections in one Studio session:

| | manifest present | manifest renamed away |
|---|---|---|
| **without MVCC** | 2 rows: 6 and 8 | **3 rows: 6, 7 and 8 - `row-007` is back** |
| **with MVCC** | key 7 absent | key 7 absent, and 8 SSTables read instead of 1 |

So the resurrection is reachable through SQL only **without MVCC**: with MVCC a `DELETE` is a versioned
write rather than an LSM tombstone, so a full merge has nothing to drop and a readmitted file has
nothing to unmask. The manifest is obeyed in both arms - the SSTable count proves the exclusion - but
only the non-MVCC arm can tell obedience from indifference, and that is the arm the two kept folders
are in.

### Rule 3's hole was a class of twenty-five, not the one it was named for

Stage 11 recorded `RowCountText` as "a ternary, which the rule still cannot see" and swept it by hand.
That was the whole class, and the rule had to be widened twice:

- **the literal does not have to come straight after the `=`.** `StatusText = count == 1 ? "one" :
  "many"` puts the literals one token further along, and the rule demanded them immediately. Anything
  up to a statement boundary is allowed between the two now, and `(?!=)` is what keeps
  `if (State == "closed")` from being read as an assignment;
- **the destination name is an ENDING, not a whole word.** The list was written as exact identifiers
  and this application names destinations compositionally - `FilterSummary`, `CaretSummary`,
  `PagingNote`, `ConnectionSummary` - so the look-behind that kept the rule out of the middle of an
  identifier was also keeping it out of the front of one. What makes the prefix safe is the `=`: the
  listed word has to be the last thing before the assignment, so `MySubtitleValue` still does not match.

Found the wrong way round for the third time - by reading the status bar of a running Russian
interface, where it said `Query executed successfully in 31.06ms`. Both widenings have their two
controls in `EveryRuleCatchesWhatItIsForAndNothingElseTest`, and the surface count that guards against
a rule reading nothing was widened with them.

The twenty-five went into the catalogue. Two of them were services composing prose, and both were
fixed rather than added to the named remainder:

- **`FormattedScript.Summary` and its five skip reasons** were English sentences built in
  `SqlFormatter`. The record now carries a `FormatSkipReason` **code** per limitation and the counts,
  and `QueryTabViewModel` writes the sentence. `SqlFormatterTests` asserted `Summary` contains "left as
  written" - which was satisfied by *any* reason, and that is how the replacement assertion first named
  the wrong one: the comment in that fixture sits **between** two statements and belongs to neither, so
  what is left alone is the `CREATE`, for a different reason entirely. A new case pins that every enum
  member has words in every language, because a key built from a name fails by printing the key.
- **`BatchResult.ErrorMessage`** held Studio's own conflict sentence, which was then interpolated into
  a Russian one by `Grid.StatementFailed`. The result carries `MatchedRows` and `ExpectedRows` now, and
  only the ENGINE's message is still passed through as text - it arrives in one language and Studio
  cannot translate it.

The three services named as a remainder at stage 11 - `SchemaChangeSet`, `TableRebuild`, `QueryPlan` -
are unchanged and still on the list.

### An LSM directory never said what it was built with

`StorageDetector.DetectDirectory` set the store type and returned. `HasTransactions`, `HasMvcc` and
`HasFileLocking` were therefore `false` for **every** LSM database - not "unknown", but wrong, and it is
the answer a consumer prints. Measured: a database built with MVCC and one built without were
identical through `Detect()`, while `ReadStoredConfiguration()` - two methods away, and called by the
same consumers on the same line - told them apart correctly. `StorageProbe.Look` calls **both** and was
taking the three flags from the first and only `PageSize` from the second.

Visible as Studio's Open dialog saying *«без MVCC»* about every LSM database in existence. It now says
`MVCC` for the one and `без MVCC` for the other, confirmed in the running application.

**And the same read settles encryption.** "Can't detect encryption without opening" was true of the
SSTables and not of the directory: the sidecar has to be readable in the clear, because it is what says
which encryption provider to build. An encrypted LSM database used to be reported as needing no
password, and the failure arrived later as a wrong-password error from the engine. It is recognised
now, which also gives the dialog the one case where its "encrypted, or not a database at all" sentence
does **not** apply - for a folder there is no ambiguity, so `Dialog.Open.Encrypted` names the store and
the transaction model and says the password is needed.

Studio 685 -> 687, Core +3 - the pair of transaction-model cases and the encryption one, all measured
red first.

---

## Phase 10 - the «База» tab

The design's own phase 10 (section 7, `WS-54`…`WS-61`), which the plan puts *after* the change order to
the engine. `WS-57` landed with PR #142, so it is now.

**The criterion, written first because the plan gives none:** the tab says about this database only
what something actually read, it names where each fact came from, and every button on it either does
what it says or is not there.

### What the design asked for and what is actually true

Every one of these was measured rather than assumed, and each changed what got built:

- **There are no levels.** The design's LSM panel draws L0/L1/L2. `m_sstables` is a flat list and a
  compaction merges all of it into one file, so the panel reports the count, the trigger, the memtable
  and the counters, and says in words that this store has no levels.
- **The counters belong to the connection.** They start at zero when the store object is built, so the
  block is titled "since this connection opened" - not a caveat, but what they measure.
- **The page cache counts no hits.** The design left this open ("может не быть, нужно уточнить").
  Neither `PageCacheLru` nor `PageCacheShardedClock` counts a hit or a miss, so a hit rate is absent
  from the **engine** rather than merely unreachable through the provider.
- **An open database cannot be read at all.** With a connection holding the file,
  `StorageDetector.ReadStoredConfiguration` answers null *and* `Detect` answers an empty store type.
  The whole Configuration block was blank on the one screen that exists to show it.
- **Only an equality reaches an index.** `ORDER BY` is answered with a SORT over a full scan and a
  range with a FILTER over one, so an index cannot be walked end to end - which is why the read check
  performs a **seek** and says so on every line that passes.

### What is on the tab

Cards (storage, size, encryption, transaction model, format), the configuration block, a "now" block
about this connection, the LSM panel with `Checkpoint` and `Compact`, and the provenance matrix of
7.3 - which is **data**, walked by a test, in the shape of `SchemaMatrixTests`. Its rows carry
catalogue keys rather than sentences.

Verification by reading (`WS-61`) is a dialog off the tab. It reads every value rather than every row,
counts the rows from the rows and puts `COUNT(*)` beside them, and refuses to call an index checked
when the planner answered without it.

### Carried forward - to be studied and finished

**This list is the phase's own remainder and is asserted nowhere yet.** Each entry is measured unless
it says otherwise.

1. **An open database cannot describe itself, and the fix is engine-side.** Measured: while a
   connection holds the file, `ReadStoredConfiguration` returns null and `Detect` returns an empty
   store type. The tab works around it by reading the header a moment *before* the session opens, and
   the one case with no answer - a database created by that very open - is absent from the screen and
   says why. The proper repair is for an open database to publish its own `ProviderMetadata` through
   the connection, which would also remove the workaround. **A wider consequence, deduced from
   `StorageProbe` and NOT yet driven in the application:** the Open dialog handed the path of a paged
   database Studio already has open would report "there is no database here".
2. **Page cache occupancy is unexposed.** Both caches keep `Count` and `DirtyCount` and neither hands
   them out; this is the single "needs provider access" row left in the matrix.
3. **DONE, 2026-08-08.** `SchemaCapabilities` held English as positional record arguments and no rule
   saw it; it holds catalogue keys now and the lint has a fourth rule for the class. See "The
   localisation hole a positional argument hides in" below.
4. **The toolbar band is empty while the «База» tab is selected.** The query toolbar hides and nothing
   takes its place. Cosmetic, seen in the running application.
5. **The tree's row count did not follow an insert.** Fifty rows went in through the editor and the
   node still said 39 until the database was reopened. Probably the deliberate laziness of the counts
   with their deadline (`WS-16`), but it reads as a disagreement on screen and has not been checked.
6. **THE DUMP IS NOT ROUND-TRIPPABLE, and that is the one thing a dump is for.** Found by executing
   one back into an empty database for the first time - the transfer `WS-58` is built on. Two defects,
   one fixed and one open:
   - **fixed here:** every index came out of the catalogue as `CREATE UNIQUE INDEX`, because
     `IS_UNIQUE` is published as the string `"YES"`/`"NO"` and was being read with `GetBoolean`. A
     dumped database whose non-unique index holds the duplicate values a non-unique index is *for*
     could not be restored at all;
   - **fixed 2026-08-08, and it was the definition rather than the splitter:** a TRIGGER was cut in
     two. See "The dump that could not be run back" below - the same section closes a second object
     that was failing in silence.

   The shape is stage 8's, one layer along: a dump that nobody has ever executed is a claim, and it
   had been shipping since stage 9.

---

## The dump that could not be run back, 2026-08-08

Phase 10's remainder, item 6, finished. **The catalogue's definition was the defect and the splitter
was innocent**, and that was established by measurement before anything was changed - the two
candidates were "the definition arrives incomplete" and "`SqlScript.Split` cuts the compound body
loose at the semicolons inside it", and only one of them is true.

**What the measurement said, in one line each:**

- `GetTriggerDefinitionAsync` returned `INSERT INTO OrdersAudit (OrderId) VALUES (NEW.Id)` - the
  catalogue's `ACTION_STATEMENT`, which is the BODY. There was no `CREATE TRIGGER` in the script at
  all;
- the splitter, asked separately, returns a hand-written `CREATE TRIGGER … BEGIN … END;` with two
  statements in its body as **one** statement out of three, no errors, and the engine accepts it.
  Recorded as `SqlScriptTests.ATriggerBodyIsNotCutLooseFromItsTriggerTest`, written to answer the
  question rather than to guard the behaviour, and kept because the question will be asked again;
- **and the same defect one object along, which nobody had named: a VIEW.** `VIEW_DEFINITION` is the
  view's query, written verbatim, so the script carried a bare `SELECT …;`. Unlike the trigger that
  statement **runs** - the restore reported success on every line and the view was simply not there.
  A loud failure had been hiding a silent one.

**The fix is `DdlWriter`, which already existed.** `CreateTrigger` and `CreateView` are the designer's
writers and every shape in them has been executed against the engine; the session now reads the
catalogue's parts and hands them over instead of growing a second writer. `GetViewDefinitionAsync`
returns the `CREATE VIEW`, and the query alone - which is what the structure tab's editor rewrites -
is `GetViewBodyAsync`, named after what it is.

**The instrument, and it was measured in both directions.** A trigger is assembled from six catalogue
columns and dropping any one of them still produces a trigger - one that fires at the wrong time, on
the wrong event, once instead of per row, or with its `WHEN` gone - so one case over one shape proves
nothing. `ATriggerKeepsItsShapeThroughTheDumpAsync` walks six shapes and compares the row the target
publishes against the row the source published. Sabotaged part by part: fixing `ForEachRow` reddens
the statement-trigger case **and nothing else**, dropping the `WHEN` reddens the `WHEN` case, fixing
the timing and the event reddens the other three. Eight cases were red before the fix and are green
after it.

**`ADatabaseWithATriggerCannotBeMigratedYetAsync` went red exactly as it promised** - `Failed` became
`Transferred` - and is replaced by the ordinary case, which checks the trigger by USING it. The
migration fixture's `BuildSchemaWithoutTriggersAsync` is deleted with it: three cases that used to
migrate a cut-down database now migrate the real one.

### And the two the engine kept

**A restored dump refused the next generated key** - `KnownIssues.md` issue 11, found because the new
trigger case fired the trigger in the copy and the copy would not take the row, and **fixed in the
same branch once it was chased down**. It was never about triggers or about dumps: the MVCC key
encoding is not prefix-free, so writing `Orders`' row-id counter marked `OrdersAudit`'s deleted.
**Fifteen controlled cases built up from nothing did not reproduce it** - they all used names like
`A`/`B` - and bisecting DOWN from the fixture that did took four steps. The two engine pins were
written as pins and inverted when the fix landed.

**`UPDATE OF` is accepted and ignored** - issue 12. The catalogue publishes no column list, so the
rebuilt trigger watches every column; that loses nothing today only because the firing path ignores
the clause too. The two have to be fixed together, and the shape matrix says in its own remarks that
this is the one part it cannot see.

Studio **717 -> 727**, engine +1.

## The localisation hole a positional argument hides in, 2026-08-08

Phase 10's remainder, item 3, and it is rule 4 of the lint rather than a sweep.

**Why the other three rules could never have found it.** Rules 1 to 3 all key on a NAME - an
attribute, an attached property, the identifier before an `=`. A positional record argument has none:

```csharp
new("Add a column", SchemaEditCategory.InPlace, "ADD COLUMN, including UNIQUE, CHECK, …")
```

Eleven rows of that sat in `SchemaCapabilities.Matrix` through the entire stage-10 sweep, with four
more sentences in `NotInTheEngine`, and every rule passed over every one of them.

**So rule 4 reads a PLACE rather than a shape:** a `static` collection in `Services` is a data table,
a data table holds catalogue keys, and a key has no spaces in it. That generalises to the next such
table whatever it calls its parameters, and it has a second half - `EveryKeyInADataTableIsInEveryCatalogueTest`
looks every key up in every language, because turning prose into keys moves the failure from "English
on a Russian screen" to "`Schema.Cap.AddColumn` on both".

**Measured in both directions, and the first version of the rule was wrong.** Its body regex stopped
at the first `]`, so `SqlFormatter.BREAK_BEFORE` - a `string[][]` - was read two literals deep and
reported clean; fixing it took the surface count from **99 to 122**. Then a row of the real matrix was
put back as prose and both new tests went red, naming the file, the table and the string.

**36 strings in two languages**, plus `MarkerOf` and `ReasonOf`, which are the live path
(`ReasonOf` -> `draft.MarkerReason` -> `ToolTip.Tip`).

**A decision that came out of it: `CategoryOfMarker` is gone.** The designer worked out which category
a row was already in by reading its marker WORD back - `"rebuild" => Rebuild` - which is a comparison
against English. **Measured rather than assumed: putting that code back leaves the new test GREEN**,
because the misread degrades to the lowest category and a column row only ever carries `InPlace` or
`Rebuild`. It is written up as what it is - a decision taken by parsing a caption, replaced because
that is not how a decision should be taken, not because a test could see it.

**And the run in the application found what the lint still could not**: the structure tab's own
heading said **"Table Customers"** over a Russian interface. `ObjectTypeDisplay` is an
expression-bodied switch, and rule 3's destination list did not have `Display` in it - the same family
as `NotArmedReason` and `FilterSummary` before it. Fixed, the word added to the list, and the case
pinned verbatim. **That is the fifth hole in this lint found by running the application rather than by
running the lint**, which is the honest measure of what a lint over text can do.

Studio **727 -> 730**.

---

## Find and replace in the editor, 2026-08-08 (9.7, agreed item 2)

A BAND in the tab, not a window - the design's reason is that a modal find dialog covers the very text
being searched, and it holds: the band sits above the editor and never hides a match.

**The half that is a function of the text is `SqlSearch`**, 16 cases with no window near them: the
three toggles, the range for "only in the selection", and replacement. Two decisions in it are worth
keeping: a plain term is ESCAPED (so `COUNT(*)` and `a.b` are text, which is what makes the band usable
for SQL at all), and a half-typed pattern is an ANSWER rather than an exception or a silent "no
matches" - the last reads as "this text does not contain it".

**A case of mine could not fail, and the sabotage found it.** "Only in the selection" was measured
against a selection whose START cut through an occurrence - which the obvious wrong implementation
(find everywhere, keep the offsets inside the range) discards too. The case ends the selection in the
MIDDLE of a word now, which is the only shape that tells the two apart, and it goes red against the
wrong one. Its near-edge sibling is kept as the other half. Walking `ReplaceAll` forwards instead of
backwards corrupts the tail (`a LONGLONGLONGERRR b X c X d`) and its own case caught that on the first
try.

**The recurring grey-button defect was wired out before it could happen** - `PropertyChanged` ->
`RaiseCanExecuteChanged` at the top of the ViewModel - and a case asserts the EVENT was raised, not
merely that `CanExecute` answers true.

**Three things came out of running it, and none was visible to 758 tests:**

- the band ran off the right edge of the editor panel and took the replace and close buttons with it.
  It is a `DockPanel` with the close button docked FIRST and a `WrapPanel` for the rest now: captions
  change length with the language, so a single row is a promise the band cannot keep in every one;
- `Ctrl+H` picked up a stray one-character selection and opened announcing **"1 из 15"** - the number
  of SPACES in the query - over a box that looked empty. A whitespace-only selection is not a term;
- and the Russian caption for whole-word had to be the design's short «Слово», not «Слово целиком»,
  before the row would fit at all.

Verified end to end in the running executable, in Russian: `Ctrl+F` on the editor, «1 из 4», walking to
«3 из 4» with the editor selecting the third match, `Ctrl+H`, Replace All, four words changed and
«совпадений нет» afterwards.

**Not done here, and named:** search in the RESULT GRID, which the design says in the same section is a
different thing - it highlights the current page and offers to become a filter that goes to the whole
table as a query. Mixing it with the text search would make "nothing found" mean two things.

Studio **730 -> 758**.

---

## The keyboard window, 2026-08-08 (9.6, WS-69)

A reference with a search over it, off `Справка` and `Ctrl+?`. **The list is DATA and it is checked
against the application in both directions** (`KeyboardMapTests`): every gesture in the map has to be
bound somewhere that really handles it, and every `KeyBinding` the markup declares has to be in the
map. The second direction is the one that rots - adding a shortcut is one line and nothing about that
line says a catalogue exists elsewhere. Both measured by sabotage: a gesture nothing handles reddens
the first rule only, and dropping a declared one reddens the second only.

**A one-line gesture killed the application, and 769 green tests did not notice.** The design writes
the shortcut as `Ctrl+?`, and that is what went into the markup: `KeyGesture.Parse` reads the `?` as
the name of a MODIFIER and throws *"Requested value '?' was not found"* while the window is being
CONSTRUCTED. Studio did not start at all. Every test in the suite drives a ViewModel and none of them
builds a window, so nothing could see it.

The guard written for that class is the cheapest one that would have caught it:
**`EveryDeclaredGestureCanBeParsedTest` asks Avalonia's own parser the same question the window asks at
startup**, over every `Gesture` and `InputGesture` in every view, and needs no UI. Measured by putting
the gesture back: red, with the same message. The key itself is handled in the window's `KeyDown` as
`Key.OemQuestion`, beside `Ctrl+F` and `Ctrl+K`, which is where the gestures a `KeyBinding` cannot
express already live.

**Reassigning is deliberately absent and the window says so in the reader's language.** The design asks
for it (one field, conflict shown before applying) and it cannot be honest yet: the gestures are
declared in the shell rather than read from `KeyboardMap`, so a box would take a key and change
nothing. Making the keys data-driven is the piece of work that comes first.

**And one thing could not be verified with the instruments here, which is written into the test rather
than glossed:** `Ctrl+?` cannot be driven on this machine - the automation tool refuses to send `?`,
and `SendKeys` does not reach an Avalonia window (measured: `Ctrl+K` sent the same way did not open the
palette either). The window was opened from the MENU in the running application; the shortcut rests on
the rule plus the handler.

Studio **758 -> 770**.

---

## The update check, 2026-08-08 (9.8, WS-70) - section 9 is finished

A message and a link. **Nothing is downloaded and nothing is run**: the installers are signed, but
replacing the executable underneath a running Studio holding a database open is a risk out of all
proportion to saving one click.

**The promise that needed a test is not "nothing was shown" but "nothing was SENT".** The check is off
by default because a tool that reaches out from a machine holding somebody's working database has to
ask once and explicitly - the database may be on a closed network, and the request itself is a fact
about that machine. So the feed is an interface that COUNTS how often it was asked, and
`NothingIsSentWhenTheCheckIsOffTest` asserts the count is zero. Measured: removing the guard turns it
red, and it is the only case that moves.

**A pre-release is never offered**, which in this repository is not an edge case - the newest
`studio-v*` tag is normally a dev build, and a check that offered it would push every user of a
released Studio onto one. Skipping is per VERSION rather than for good: skipping 3.1.0 still hears
about 3.2.0, because "never again" is what the checkbox says properly and reversibly.

**Verified against the real repository**, which is what made it worth doing in the application: with
the setting turned on, Studio read GitHub's release list at startup and the log says
`Update check: OnlyAPrerelease`. The user's setting was backed up and put back afterwards.

**And the run found a gap in the product, not in the code:** the first attempt could not tell whether
the check had concluded "prerelease" or had never reached the network at all, because the only log
line was at Debug. The verdict is written at Information now - a background check that says nothing
about itself is one nobody can support.

Studio **770 -> 794**.

---

## Findings for the engine, not fixed here
**A function over an indexed column returns the WRONG ROWS.** Measured 2026-08-06, and it is the worst
class of defect there is: when a `WHERE` predicate wraps an indexed column in a function, the planner
treats the call as if it were the bare column and seeks the literal in the index.

| Table | Query | No index | With an ordinary index on the column |
|---|---|---|---|
| `V = -7` among 200 others | `WHERE ABS(V) = 7` | the row | **no rows** |
| `Name = 'MIXEDCASE'` | `WHERE LOWER(Name) = 'mixedcase'` | the row | **no rows** |
| the same | `WHERE UPPER(Name) = 'NAME42'` | the row | **no rows** |

Controlled both ways: the answer is correct before the index is created, wrong after, correct again
after `DROP INDEX`, on B-Tree and on LSM, and across a close and reopen. `V + 0 = -7` and `-V = 7` stay
correct, so it is specifically a function CALL. Creating an index by expression does not help - the
planner still picks the plain one. It is quiet on most data, because for values already lower-case or
positive the raw value and the wrapped one are equal; it shows up the moment they are not. `EXPLAIN`
names the index it is using.

**`ALTER TABLE … RENAME TO` restarts the key generator, and the next generated INSERT overwrites a
row.** Measured on both stores and across a reopen; a `RENAME COLUMN` and an `ADD COLUMN` do not do it,
an explicit duplicate key is refused correctly, and a `UNIQUE` index on the key turns the overwrite into
a refusal - so the generated-key path skips the check the explicit path makes. This is what decided the
shape of the schema designer's rebuild.

**`ADD COLUMN … NOT NULL` with no `DEFAULT` is accepted on a table that has rows**, leaves NULL in every
one of them, and then refuses every later write to that table - including an `UPDATE` of an unrelated
column. Studio refuses to write the statement.

**`DROP COLUMN` leaves the index on that column in the catalogue**, over a column that no longer exists,
and it survives a reopen. The foreign key on the column does go with it.

**`ALTER COLUMN … TYPE` rewrites the rows and destroys what it cannot convert**, silently: a word
becomes 0, an INTEGER becomes 01/01/0001, a narrowed `VARCHAR` keeps its long values and stops enforcing
its length. Changing the type back does not bring anything back. `CAST` behaves the same way and never
fails, so nothing in the language will tell a user what a conversion cost.

**`INFORMATION_SCHEMA.COLUMNS.ORDINAL_POSITION` is 1 for every column** of every table, so the catalogue
cannot say what order a table's columns are in.

**A rebuild through Studio's designer left two database files unreadable** - see stage 8. Not
reproducible headlessly in fourteen controlled runs; the files are damaged in the schema catalogue's
overflow chain and the bare provider cannot open them either.



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

**The planner does not use the primary key for order or for range, and does not push a limit into a
sort.** Measured in stage 3 (the table above, with `EXPLAIN`), and it is what decides how fast a
client can show a large table:

| Asked | Plan | Cost |
|---|---|---|
| `SELECT * FROM t LIMIT 200` | `LIMIT <- SCAN TABLE` | constant - the scan stops early |
| `SELECT * FROM t ORDER BY Id LIMIT 200` | `LIMIT <- SORT <- SCAN TABLE` | a full sort of the table, per page |
| `... WHERE Id > n ORDER BY Id LIMIT 200` | `LIMIT <- SORT <- FILTER <- SCAN TABLE` | a full scan, filtered, then sorted |

Three things would each help on their own: a top-N limit pushed into the sort, an index-ordered scan
when the ordering is the primary key, and a seek for a primary-key range predicate. Any consumer that
pages a table wants them; Studio pays for the absence with a linear cost per page and says so in its
own interface rather than hiding it.

**`CREATE FUNCTION` and `CREATE PROCEDURE` work, and two `[Ignore]`d engine tests say they do not.**
`OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests` carries two suppressed cases whose
reason reads *"CREATE FUNCTION does not parse... neither exists anywhere in the stack"* - written
2026-07-29, before phase 9d built the routine subsystem. Measured 2026-08-06 against the shipping
engine: `CREATE FUNCTION AddOne(x INTEGER) RETURNS INTEGER AS BEGIN RETURN x + 1; END` is **accepted**,
and the routine appears in `INFORMATION_SCHEMA.ROUTINES` with its definition. The suppressed tests omit
the `AS`, which is what the grammar requires - so they are pinning a syntax mistake as a missing
capability. Studio now shows routines in the tree because of this (WS-21); the markers themselves are
`Sources/**` and this phase does not touch them.

**A column may not be named after a type keyword.** `CREATE TABLE T (..., Blob BLOB)` is refused -
`mismatched input 'Blob'` - because `BLOB` lexes as a keyword and is not accepted where a column name
is expected. Found while probing the BLOB path; a client cannot work around it and a user with such a
column in another database cannot import it.

**Amended in stage 6:** the refusal is of the **unquoted** name. `CREATE TABLE Q ([Text] VARCHAR(10),
[Rows] INTEGER)` is accepted, and `"x"`, `[x]` and `` `x` `` are all identifiers in the lexer. So there
is a workaround after all - it is that every consumer has to know to quote, and an import from another
database still fails unless it does.

**The planner will not consider an index below ten rows.** `MIN_ROWS_FOR_INDEX = 10` in
`QueryPlanner.Sources.Indexes`: with fewer, `FindBestIndex` is never called and every access is a scan.
Reasonable as a rule and worth knowing about as a **measurement hazard** - it is what made the first
pass at the plan panel conclude that this engine has no index access at all. Any benchmark or probe
about index behaviour on a small fixture is measuring the threshold, not the engine.

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