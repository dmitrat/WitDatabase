# Phase 13 - WitDatabase Studio, audited against the engine it ships with

Twelve phases measured `Sources/**`. Studio lives under `Tools/`, and no phase has ever looked at it.
It is in the solution, CI builds it and runs its 259 tests, and they pass - so the question this phase
opens with is not *"is it tested"* but *"what do those tests touch"*, and then the one Dmitry actually
asked: **it is not very stable in use. What, exactly?**

The answer to the second question was not found by reading code and was not found by the fixture built
to find it. It was found by launching the application and opening two databases.

## 1. What the existing suite covers, measured rather than assumed

`OutWit.Database.Studio.Tests`: **259 tests, 733 ms**. That duration is the first thing worth reading -
259 tests that open no database run in well under a second.

| what the tests drive | count |
|---|---|
| `FakeDatabaseService` - `IsConnected => false`, `ConnectAsync => false`, every collection empty | 249 |
| a real `DatabaseService` over a real B+Tree database (`StudioDbHarness`) | 10 |

So **the connection dialogs, the only place Studio configures the engine, had never been run against
the engine**. The fake is permanently disconnected, which means every ViewModel test exercises the
"nothing happened" branch and nothing else. Nine of the configurations the Create dialog can express
had never been built once.

**CI runs these tests, and that half of the reputation holds.** `ci.yml` is a single `Build and Test`
job on `ubuntu-latest` running `dotnet test OutWit.slnx`, and `OutWit.Database.Studio.Tests.dll` is in
the list of assemblies it executed at `afec9c8` - read out of the run log rather than inferred from the
colour.

## 2. Instrument S - `StudioEngineContactTests`

Drives the **real** `ConnectionViewModel`, from a real `ApplicationViewModel`, over a real
`DatabaseService`, over the real engine. The only thing replaced is the file picker, and it is replaced
by what its bindings write: `ConnectionInfo.FilePath`, and `ApplyAutoDetectedSettings` - the method the
Browse button calls, extracted from `OpenExistingDatabaseAsync` so a probe can reach it without a
window.

**The question is a round trip, not a connection.** Create the database the dialog offers, write eight
rows, close, reopen through the Open dialog, and scan the rows back - never `COUNT(*)`, which on this
engine is separate state. Asking only *"did Connect return true"* reports success for a dialog that
creates one database and connects to a different one, which is what two of these cases do.

**Both controls are built in, and the negative one had to be replaced.**

- **Positive:** the default path - a file, a B+Tree, defaults everywhere - must create, reopen and
  return its rows. If it fails, nothing else in the fixture is evidence.
- **Negative, first attempt:** a path that does not exist must be refused. **It went green**, and that
  is finding S2 rather than a broken control. The negative control is now a text file that is not a
  database, which is refused.

### 2.1 The instrument was wrong before its subject, for the thirteenth time

It reported the most interesting case backwards. The hypothesis was that `Store=lsm` loses every row,
because the Create dialog hands `WithLsmTree` the *parent directory* of the chosen file. Built and run:
**all eight rows come back.** See S3 - the hypothesis was refuted by implementing it, the defect that
survives is a different one, and the pin was rewritten to what was measured.

And the fixture **could not see its own most serious finding**. Every case builds a fresh
`DatabaseService`; `Program.cs` registers **one as a singleton** and reuses it for every open. An
instrument that hands each case a clean service cannot see a defect that only exists on the second use
of a dirty one. S1 was found by driving the shipping executable, and only then written into the fixture.

## 3. Findings

### S1 - switching databases leaves every view believing it is disconnected

**The headline defect, and the one the "not very stable" reputation is made of.** Open a database, then
open another without closing the first: the Database Explorer shows the second database's node, the
path is added to Recent Files - and the status bar reads `Connected: False`, the welcome screen comes
back, the query tab is gone, and `File > Close Database` is disabled. Switching databases requires
restarting the application.

**Isolated by execution.** A fresh application opening an existing database connects correctly - that is
the control. The failure needs a database to be open already; it was reproduced with two databases that
both existed, so it is about the second open and not about the first target being absent.

**The connection is not what fails.** `DatabaseService.ConnectAsync`:

```csharp
var wasConnected = IsConnected;      // true
await DisconnectAsync();             // raises ConnectionStatusChanged(false) from its own comparison
...open the new connection...
RaiseConnectionStatusChangedIfNeeded(wasConnected);   // true == true, so nothing is raised
```

`wasConnected` is captured **before** the inner `DisconnectAsync` fires its own event. The interface
hears `connected`, then `disconnected`, and is **never told about the second connection**. Measured:
the event stream is `[True, False]` while `IsConnected` is `True`.

So the service is connected and every view believes it is not, which is exactly the contradictory state
the screenshots show. Pinned in `OpeningASecondDatabaseLeavesEveryViewBelievingItIsDisconnectedTest`,
which asserts the event stream rather than the connection - because the connection is fine.

### S2 - the Open dialog cannot fail on a path that does not exist; it creates one

Typing a path that is not there and pressing Open **creates the database, and the directory with it**,
reports success, and adds it to Recent Files. A user whose file has moved is shown an empty database,
which reads as *my data is gone* - and the natural next step writes a schema over nothing.

**Attributed rather than assumed:** `WitDbConnection` creates on open with no Studio code in the path
(`AttributionTheEngineItselfCreatesOnOpenTest`). That is reasonable for a provider and wrong for a
dialog whose title is *Open Database*. **The fix belongs in Studio**, which should refuse a path that
does not exist before it builds a connection string.

### S3 - `Store=lsm` abandons a second database in the user's own folder

`ConnectionViewModel` calls `builder.WithLsmTree(Path.GetDirectoryName(ConnectionInfo.FilePath))` - the
**folder** the user picked a file in, not the file. Choosing `C:\Users\Me\Documents\mydb.witdb` writes
`provider.meta` and `wal.log` into `Documents`, builds a database there, and abandons it: the rows are
written after the reconnect, so they land in the database the *connection string* builds.

**The rows survive** - the hypothesis that this loses data was implemented and refuted. Two things save
it, and neither is the dialog: the data goes to the connection string's database, and **12.2.0's
restoration** reads the store back out of that directory's `provider.meta` sidecar, so the reopen -
which names no store at all - still gets an LSM database. Before 12.2.0 this path could not have worked.

**At its worst:** in-memory combined with `lsm` calls `WithLsmTree(".")`, so an LSM database is written
into the **process working directory** - for an installed application, wherever it was launched from.

### S4 - four controls on the Open dialog reach nothing, and the file now supplies all four

`ConnectionInfo.BuildConnectionString` emits only `Data Source`, `Mode=ReadOnly`, `Encryption=aes-gcm`,
`Password` and `Store`. The Open dialog offers **Enable ACID transactions**, **Enable MVCC**, **Enable
file locking** and **Storage Engine**. The first three are read into the ViewModel by auto-detection and
then dropped on the floor - clearing *Enable MVCC* on the Open dialog gets an MVCC database and no
message.

Since 12.2.0 the file remembers all four, so **the fix is to take them off the Open dialog rather than
to wire them up**. This is the answer to the question the phase opened with: the UI does present
settings the file now supplies itself.

### S5 - an LSM database cannot be opened at all

An LSM database is a **directory**. `OpenFilePickerAsync` cannot select one, and there is no folder
picker anywhere in the application - `grep` for `OpenFolderPicker` returns nothing across all 86 files.
Typing the path does not help either: `ApplyAutoDetectedSettings` guards on `File.Exists`, which is
false for a directory, so detection is skipped and `btree` stays selected.

So Studio can *create* an LSM database and can never *reopen* one.

### S6 - the in-memory option connects to a different database than it created

`WithMemoryStorage()`, build, **dispose**, then reconnect over `Data Source=:memory:`. An in-memory
database keeps nothing after its last connection closes, so everything the dialog configured is
discarded and the user lands on a second, empty database. It works, in the sense that an empty scratch
database is what they wanted - but page size, cache size and the transaction model reach nothing.

### S7 - nothing is written down when something goes wrong

`Program.cs` configures `AddConsole()` and nothing else, and `OutputType` is **WinExe** - which has no
console. Every `LogError` in the application, including the one in `ConnectAsync`'s catch, goes nowhere
a user or a support engineer can read. **There is no log file.**

There is also **no global exception handler**: no `AppDomain.CurrentDomain.UnhandledException`, no
`TaskScheduler.UnobservedTaskException`, no dispatcher hook. `RelayCommandAsync.Execute` is
`async void`, so an exception escaping any command body ends the process with no message and no trace.

For an application whose reputation is instability, **there is currently no way to find out why.**

### S8 - the primary input is invisible to assistive technology

The SQL editor does not appear in the window's accessibility tree at all, and text typed to the focused
element does not reach it. Most buttons are announced by their layout class - the toolbar's buttons are
named `Avalonia.Controls.StackPanel`, the icon buttons `Avalonia.Controls.PathIcon`.

### S9 - three packaging accidents

- **Studio is packed as a NuGet package.** `Directory.Build.props` sets `GeneratePackageOnBuild` for
  every project that is not a test project and does not opt out; Studio does not opt out, so every
  Release build produces `OutWit.Database.Studio.1.0.1.nupkg` - a desktop GUI application shipped as a
  library. Visible in the CI log at `afec9c8`.
- **`<Version>1.0.1</Version>` is hard-coded** and has not moved while the engine went to 12.2.0.
  Studio does not use MinVer, unlike every shipped package.
- **The release tag shares the engine's namespace.** `release.yml` derives its tag from that version, so
  the last Studio release is tagged **`v1.0.1`**, sitting in the same `v*` list as `v12.2.0`. Nothing
  fires on it automatically - `pack.yml` and `publish.yml` are `workflow_dispatch` only - so this is a
  collision of meaning rather than of triggers, and WitCloud already solved it with a separate
  `client-v*`.

### S10 - macOS has never been built, not once

`release.yml` has a three-OS matrix, and `buildMacOS` **defaults to `false`**. Read out of the run
history rather than the YAML: six dispatches, the last successful one on **2026-01-26**, and its jobs
were `Prepare Release Info`, `Build windows-latest`, `Build ubuntu-latest`, `Create Release`. There is
no macOS job in any run.

**Confirmed against the artifact rather than the status:** release `v1.0.1` carries exactly two assets,
`WitDatabase.Studio-win-x64.zip` and `WitDatabase.Studio-linux-x64.tar.gz`.

This is the `ci-branch-never-run` shape again - a green workflow whose interesting branch has never
executed.

### S11 - a high-severity advisory is shipping, and Avalonia is a major behind

`Tmds.DBus.Protocol` 0.21.2 carries **GHSA-xrw6-gwf8-vvr9, high severity**, reported as `NU1903` on
every build of Studio in CI. It arrives transitively through Avalonia's Linux stack. CI's *Check shipped
packages for vulnerabilities* step does not cover it, because Studio is not a shipped package.

| package | current | latest |
|---|---|---|
| Avalonia, Desktop, Fluent, Inter | 11.3.11 | **12.1.1** |
| Avalonia.Controls.DataGrid | 11.3.11 | 12.1.2 |
| Avalonia.AvaloniaEdit | 11.3.0 | 12.0.0 |
| Avalonia.Diagnostics | 11.3.11 | 11.3.18 (12.x has no match yet) |
| Microsoft.Extensions.* | 10.0.2 | 10.0.10 |

## 4. What was looked for and not found

Recorded because a null result is only worth anything when the search that produced it is described.

- **No drift from 12.0.0's removals.** Studio uses neither `Parallel Mode` nor `Max Writers`, and
  `BuildConnectionString` emits nothing the engine no longer accepts. Both checks negative.
- **`MVCC=false` is not slow.** It measured 4 s against 230 ms for every other round trip on the first
  run. Interleaved three rounds with the first discarded: **211 / 234 ms** against btree's **634 /
  655 ms**. The 4 s was warm-up, the ranges do not separate, and the honest finding is *no measurable
  difference*.
- **Settings storage is already cross-platform.** `SettingsService` uses
  `Environment.SpecialFolder.ApplicationData`, which resolves correctly on all three platforms.
- **The B+Tree round trip is clean** under every option the dialog offers: defaults, encryption,
  `MVCC=false`, `Transactions=false`, `FileLocking=false` and a 16384-byte page size all create,
  reopen and return all eight rows.
- **12.2.0 is load-bearing for Studio**, which nobody had noticed: the Open dialog names no store, no
  page size, no cache and no transaction model, so everything but the data source is supplied by the
  file. Studio's LSM reopen (S3) works *only* because of it.

## 5. Ledger

Unchanged at **45 suppressed entries (31 `[Ignore(…)]` + 14 `Ignore =`) plus 2 `[Explicit]`**, counted
with the commands on this branch. Nothing in `Sources/**` was touched by this phase.

## 6. Verification

`OutWit.Database.Studio.Tests`, the CI filter, on this branch:

| | passed | failed |
|---|---|---|
| before | 259 | 0 |
| after | **275** | 0 |

The sixteen added are Instrument S. The one change to shipping code is the extraction of
`ApplyAutoDetectedSettings` out of `OpenExistingDatabaseAsync`, which the Browse path still calls; no
behaviour moved with it.

## 7. What this phase does not do

No defect above is fixed here. The audit was asked for first, and S1, S2, S4 and S5 each change what the
dialogs do, which is a decision rather than a repair. Every finding is pinned by a test that states the
inversion its fix must produce, so the fixes can be taken in any order and each proves itself.
