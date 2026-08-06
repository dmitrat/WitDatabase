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
| 7–9 | The rest of the redesign: grid, schema designer, dialogs |
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
