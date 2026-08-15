# WitDatabase Studio - Changelog

Studio is versioned separately from the WitDatabase engine and released under its own `studio-v*` tag.
The engine's changelog is `/CHANGELOG.md`.

## 3.0.0-rc.2

**The second candidate.** rc.1 was walked on Windows against a calibration database built to make
wrong answers look different from right ones, and it produced eleven findings; the six that cost a
user most are fixed here, together with the one thing engine 14.0.0 makes Studio responsible for.

Engine: 14.0.0.

### A database in the old encryption format can still be opened, and converted

Engine 14.0.0 refuses a database encrypted before 13.1.0 - its salt is derived from its password and
stored in the clear, and its nonce counter restarts on every open - and tells the user to convert it
**by changing its password**. Studio is the tool that does that, so Studio has to be able to open one.

It now recognises the refusal by its type rather than by its wording, says what happened in a sentence
that names the version and the remedy, and offers a box that opens the database in the old format.
Ticking it and pressing Connect gets the data; a notification then says the conversion is one password
change away. An ordinary refusal - a file that is not a database, a wrong password - is unaffected and
gets its own message, which is what the control case asserts.

Beside it, a smaller thing with a wider reach: **a failed open now carries its reason back**.
`ConnectionManager.OpenAsync` answered null and nothing else, so every refusal reached the dialog as
the same sentence.

### Fixed

- **Three menu items printed a shortcut that does something else.** *New Query Tab* said `Ctrl+N`,
  which opens **New Database**; *View ▸ Refresh* said `F5`, which runs the statement under the cursor;
  and *Query ▸ Execute* said `F5` for a command that is on `Ctrl+Shift+F5`. *New Database…* now prints
  the `Ctrl+N` it has always owned. A rule over every printed gesture in every view is what found the
  third - the two in the report were the two somebody happened to notice.

- **English plurals disagreed with their numbers at one.** The status bar read
  `calib: 6 tables, 1 views, 2 indexes, 1 triggers` while the Database tab, over the same counts at
  the same moment, read `1 table` correctly: one string had its nouns written inside the format with
  raw numbers passed in, and skipped the plural mechanism the rest of the application uses.

- **The `Page cache` line opened with a separator and nothing before it.** The cache kind is a field an
  older file does not carry; an absent field is dropped with its separator now rather than printed as
  an empty slot.

- **A table with no primary key was announced as being edited.** Every editing control was correctly
  disabled while the status bar said `Editing table: NoKey` and the footer advertised `Ctrl+S` and
  `Del`. The words follow the same flag the buttons read.

- **The three storage options in *Create database* did not share a baseline.** They were centred in a
  grid whose row height comes from the tallest description, and **which option looked wrong moved with
  the language** - LSM in English, «В памяти» in Russian.

- **The theme button kept its caption in the language the window was opened in.** It has read the
  catalogue since 3.0.0-dev, but nothing re-read it when the language changed, so «Dark» stayed on a
  Russian window. It is not a missing translation; it is a caption written once.

### Known and unchanged

Four clipped labels, the status bar not following the language, `BOOLEAN` drawn as text rather than a
checkbox, and `''` against `' '` in the grid being indistinguishable. All four are written up with
what each would cost to fix.

## 3.0.0-rc.1

**The first candidate, and the first Studio built with every signature it ships with.** Windows
Authenticode, macOS Developer-ID with notarization, and a GPG-signed `SHA256SUMS` on Linux - the
release pipeline signs every tag that is not `-dev`/`-test`/`-internal`, and until now Studio had
only ever been tagged `-dev`.

It is a **pre-release**: the tag carries a suffix, so it is not the repository's Latest and the
in-application update check will not offer it to anybody on 2.0.0. That is the point of an rc - it is
for testing on all three platforms, not for pulling users onto.

**What is in it** is everything under `3.0.0-dev` below, which is the whole of phases 13 through 18:
the frame, the explorer, the query workspace, the data grid, the schema designer, the dialogs and the
localisation, plus this session's read-only signs, the status bar and tree corrections, and an update
check that can now answer.

**Known and deliberate:** Studio still downloads and runs nothing - the update dialog opens the
release page. Whether it should install its own updates, and what would anchor the trust if it did,
is written up in `/Docs/STUDIO-UPDATE-STUDY-2026-08-14.md` and is not decided.

Engine: 13.1.0.

## 3.0.0-dev - in progress

Studio is being refactored and redesigned. This section collects what has landed on the way; it is
built and released under dev tags only, and is not a supported version. Full detail:
`/Docs/PHASE14-STUDIO-REFACTORING.md`.

### Added

- **A table opens into its columns in the tree**, with its key and its foreign keys marked and the
  type of each column beside it - no tab needed to answer the commonest question about a schema.
- **Functions and procedures have a folder.** The engine has had them for a while; the tree has been
  saying the database has none.
- **Row counts appear next to tables** once they are known. They are asked for in the background with
  a deadline, so a table too large to count never blocks the tree.
- **A filter over every open database**, showing the path to each match and holding its result until
  it is cleared.
- **An object inspector** on the right: columns, indexes, what a table points at and what points at
  it, and the definition the catalogue actually holds. It also says which columns can be reached
  through an index - including when a primary key has none, which on this engine is what makes
  inserting rows with explicit keys slow down as a table grows.
- **A double click opens a table's data** rather than its structure.
- **A command palette on Ctrl+K.** Commands and the objects of every open database in one list, each
  object saying which database it is in. It is also the search the object tree never had.
- **A toolbar that belongs to the active tab**, with the tab's connection named on its right edge -
  the one place in the window that answers "which database will this run against".
- **A status bar that says what is happening**: the connection and its engine, the query that is
  running with a way to stop it, and where the cursor is.
- **A notification list** behind the bell in the title bar, for the things that used to flash through
  the status bar and be gone: an import or export finishing, a schema reload failing.
- **F5 runs the statement the cursor is in**, not the whole script; the whole script is
  Ctrl+Shift+F5. **Ctrl+N now creates a database** rather than a query tab, which is Ctrl+T, and a
  closed query tab comes back with Ctrl+Shift+T.
- **A script is executed one statement at a time**, and each reports what it did. An error names the
  statement and the line it is on, in the coordinates of the editor - including when only a selection
  was executed. A script that does not parse is refused whole rather than applied halfway.
- **A table is read a page at a time**, with Previous and Next in the editor's toolbar. Where the
  table has a single-column primary key the next page starts from the last row seen; where it does
  not, the editor says why paging deeper is slow instead of leaving it unexplained.
- **More than one database can be open at a time.** *Open Database* and the recent files list add a
  connection instead of replacing the one that is open; the explorer grows a root per connection.
- **The structure tab is a schema designer.** Five sections - columns, keys and constraints, indexes,
  triggers, DDL - and a DDL panel that is on screen the whole time: the statements an edit will run
  appear as it is made, not after Apply has been pressed.
- **Every edit says how it will be carried out, in its row.** In place, a rebuild, or a drop and a
  create - the three categories are what this engine's `ALTER TABLE` does and does not do, measured
  rather than assumed, and each marker carries the reason.
- **Applying a set of edits reports what landed.** This engine does not roll DDL back, so the set runs
  a statement at a time, stops at the first refusal, and says which statements are in the database and
  which never ran.
- **A table rebuild is planned in full**: four steps with their SQL, the objects that will be put back,
  the ones that point at the table and will not be, what the catalogue cannot carry across, and how
  many values the type conversion will destroy. Studio does not run it yet - see Known issues - and
  hands the script to the query editor instead.
- **The index dialog offers everything the engine takes** - UNIQUE, several columns, a direction,
  a partial `WHERE`, `INCLUDE`, an expression - and says which of them the planner will actually use.
- **A trigger editor that knows the boundary of the language**: only DML in the body, `WHEN` written
  with the brackets the grammar requires, and an explanation for `SET NEW.column`, which does not parse
  at all. Replacing a trigger says "Drop and create", because that is what it is.
- **F2 renames a table**, and only a table: there is no `ALTER VIEW`, `ALTER INDEX` or `ALTER TRIGGER`
  in this language. **Empty the table** (TRUNCATE) is in the tree's menu beside it.

### Changed

- **A tab belongs to the connection it was opened in and runs there**, whatever is selected in the
  tree. Selecting in the tree moves the focus - where a new tab is opened, where an object is created,
  where export and import work - not the target of a tab that is already open.
- **Disconnecting closes the tabs of that connection only.** It used to close every data and structure
  tab of every database, because the connection status was one event for the whole application.
- **A query tab whose connection is closed keeps its text.** It says which connection it belonged to
  when asked to run, and will not quietly run against another one.

### Fixed

- **Values written by the table editor keep their precision.** Edits were built by writing the value
  into the statement as text, and a date was written to whole seconds - so a time with milliseconds
  came back without them. Values are now bound to the statement instead of written into it, which also
  removes the case where a value of an unexpected type was written with no quoting at all and read as
  a column name.
- **An error message says what happened instead of what the engine would have accepted.** A parse
  error carried the whole set of expected tokens - over a thousand characters - into the status bar.
  The first sentence is shown; the rest is kept for the details.
- **The password of an encrypted database is no longer written to the log file.** `DatabaseService`
  logged the whole connection string - which carries `;Password=…` - on every connection attempt and
  again on every failure. Harmless until 2.0 added a file log, and from that release on the password
  was written to `%AppData%\WitDatabase.Studio\logs\studio.log`: the file users are asked to attach to
  an issue. The log now records the data source, whether encryption was asked for and the store, and
  nothing else.
- **Deleting a row in the table editor now deletes it.** It never worked in any release. Rows read out
  of the database were left in the state that means "not saved yet", and deleting one of those detaches
  it rather than marking it deleted - so the commit threw, nothing was deleted, and every other edit in
  the same buffer was lost with it.
- **A set of edits is applied as one transaction.** It used to be sent one statement at a time: a set
  that failed halfway left behind whatever had already gone in, and said only `Update failed: …`.
  Now it is applied whole or not at all, and a refused set keeps its buffer so that nothing has to be
  retyped.
- **A table with no primary key opens for viewing, and says why.** Editing one built the `WHERE` clause
  from every column of the row, which two identical rows both match - so changing one changed both.
- **Closing a tab with unapplied edits asks.** Apply, discard, or keep the tab open. The same question
  is asked when leaving the application and when disconnecting - while the connection is still there,
  so that applying is still possible.
- **File > Exit closes the application instead of ending the process.** It called `Environment.Exit(0)`,
  which skips everything: the window size was not saved, unapplied edits were not asked about, and the
  connection was never disposed - leaving the database file locked until the operating system reclaimed
  the handle.
- **Creating an LSM database no longer leaves a second, empty one beside it.** Choosing
  `Documents\mydb.witdb` created the database *and* dropped `provider.meta` and `wal.log` into
  `Documents`. Creating an LSM database now asks for a folder, because that is what one is.
- **An in-memory database is the one you get.** The dialog used to build a database, throw it away and
  connect to a different, empty one; combined with LSM it wrote a database into whichever directory the
  application was launched from. That combination is now refused with an explanation.

### Known issues

- **A table rebuild is not run by Studio.** Rebuilding a table from the designer left the database
  file unreadable twice, on two different files, in the shipping application - the schema catalogue's
  overflow chain is damaged and the file cannot be opened by anything afterwards. Fourteen controlled
  runs of the same rebuild outside the application all reopen correctly, so the cause is not yet
  known. Until it is, the dialog plans the rebuild and hands the script to the query editor, where the
  same statements are measured to be safe. Make a copy of the database before running them.

## 2.0.0

The first release since Studio was audited against the engine it ships with. Phase 13 asked what its
259 passing tests actually touched, and the answer was: not the engine. 249 of them drove a permanently
disconnected test double, so the connection dialogs - the only place Studio configures the engine - had
never been run against it once.

A **major**, for three reasons: the UI framework moved a major version, the Open dialog changed shape,
and this is the first release that is genuinely three platforms rather than two.

### Fixed

- **Opening a second database no longer leaves the application connected to neither.** Opening a
  database while one was already open showed the new database in the explorer, added it to Recent
  Files, and then reported `Connected: False` with the welcome screen back and *Close Database*
  disabled - switching databases required restarting. The connection had always succeeded; the *event*
  the entire interface binds to was never raised, because `ConnectAsync` compared against a value
  captured before its own inner `DisconnectAsync` had fired. The service now compares against the last
  status it actually delivered.
- **The Open dialog refuses a database that is not there** instead of silently creating one. A user
  whose file had moved was shown an empty database, which reads as *my data is gone* - and creating the
  schema on it would have written over nothing recoverable.
- **An LSM database can be opened.** It is a *directory*, and the dialog offered only a file picker,
  with no folder option anywhere in the application - so Studio could create an LSM database and never
  reopen one. There is now a **Folder...** button beside **File...**.
- **A dialog reopened after a failure no longer shows the previous attempt's error.**

### Changed

- **The Open dialog stops asking for what the database already knows.** *Enable ACID transactions*,
  *Enable MVCC*, *Enable file locking* and *Storage Engine* have been removed. None of them ever
  reached the engine - clearing *Enable MVCC* got an MVCC database and no message - and since engine
  12.2.0 the file records all four and supplies whatever the connection string does not name.
- **Avalonia 11.3.11 → 12.1.x.** This closes a high-severity advisory in `Tmds.DBus.Protocol`
  (GHSA-xrw6-gwf8-vvr9), which arrives transitively through Avalonia's Linux stack and had been
  reported on every build.
- Studio is no longer packed into a NuGet package on every Release build.

### Added

- **A log file.** `%AppData%/WitDatabase.Studio/logs/studio.log` on Windows, the equivalent on Linux and
  macOS, rolling at 5 MB. Studio is a `WinExe`, which has no console, so the console-only logging it had
  wrote every message - including the one in the connection failure handler - nowhere at all.
- **Unhandled-exception handlers.** Commands are `async void`; an exception escaping one used to end the
  process with no message, no trace and no log.
- **Signed installers for three platforms.** Windows gets an NSIS installer (Authenticode via SSL.com
  eSigner), Linux a `.deb` and `.zip` with a GPG-signed `SHA256SUMS`, macOS a Developer-ID signed,
  **notarized and stapled** `.app` bundle. Each platform also ships a framework-dependent build with a
  `-no-dotnet` suffix for machines that already have the .NET 10 runtime.
- **A platform icon set.** Studio shipped one non-square 256x209 `.ico`; there are now `.ico`, `.icns`,
  `.svg` and `.png`, derived from the same logo by `Assets/Branding/build-icons.py`.

### Known issues

- Creating an LSM database from the Create dialog builds a second, empty database in the folder the
  chosen file lives in and abandons it; in-memory combined with `lsm` writes one into the working
  directory.
- The in-memory option connects to a different database than the one it created, so nothing the dialog
  configured reaches it.
- Closing a table-editor tab with unsaved changes discards them without asking.
- The SQL editor exposes no accessibility peer, so screen readers cannot read or write it.

## 1.0.1 - 2026-01-26

First public release. Windows and Linux only; the macOS job existed but had never run.
