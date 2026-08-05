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

### S13 - closing a table editor with unsaved changes discards them silently

Found while assessing how complete Studio is, from two `// TODO: Show confirmation dialog` markers -
and then **proved by execution rather than reported from the reading**, because on this project a
careful read is not evidence.

`TableEditTabViewModel.CanClose()` returns `true` unconditionally, and
`WorkspaceTabsViewModel.CloseTab` calls `OnClosed()` the moment it says yes - which disposes the edited
`DataTable`. The same happens on Refresh. So a user who edits cells and presses the tab's X loses the
work, with no prompt and no message.

Driven through the real close path (the tab strip's `CloseTabCommand`, not `CanClose` directly): edit a
cell, `HasChanges` is true, the tab closes without objection, the database still reads `original`, and
`EditableData` is null so there is nothing left to recover from.

**The positive control is what makes that mean anything.** *"The database still says original"* would
read identically if the editor could not save at all. `ControlCommittingATableEditDoesReachTheDatabaseTest`
drives the same edit and presses Commit: the new value reaches the database. Only with that green does
the case above describe lost work rather than a broken editor.

Not fixed - it needs a confirmation dialog, which is a UI decision.

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

## 5. What was fixed, and how each fix was proved

Dmitry, 2026-08-04: **S1, S2 and S7 first, then the rest.** S12 was found while verifying them and is a
two-line repair, so it went in with them. Every fix inverts the pin that recorded the defect, and every
one was re-checked in the running application rather than only in the suite - which matters here,
because S1 was invisible to the suite in the first place.

### 5.1 S1 - the fix removes the captured value rather than patching the call site

`RaiseConnectionStatusChangedIfNeeded` no longer takes a `wasConnected` argument. The service records
the last status it actually **delivered** and compares the live state against that, so no caller can
reintroduce a stale capture. The event stream for a switch is now `connected → disconnected →
connected`; the middle one is real and the views should see it, because the first database genuinely is
closed before the second opens.

**Verified in the application**, on the sequence that produced the defect: open a database, then open
another without closing it. `Connected: True`, the query tab intact, the explorer and the status bar
naming the same database. Before, this gave `Connected: False`, the welcome screen and a disabled
*Close Database*.

### 5.2 S2 - the dialog refuses, the engine still creates

The Open path refuses a `Data Source` that is neither a file nor a directory - both, so the refusal is
already right for LSM when S5 is done - and says which path it could not find. The engine's
create-on-open is untouched and still asserted by `AttributionTheEngineItselfCreatesOnOpenTest`, because
it is correct for a provider.

**One thing this exposed, which is worth recording:** the first version of the S1 test opened two
databases that did not exist and was **relying on the defect S2 fixes**. It went red the moment the
refusal landed. A test that needs a defect in order to pass is a test that was measuring the wrong thing.

### 5.3 S7 - a log a support engineer can read, and handlers that write the failure down

`FileLoggerProvider` writes to `%AppData%/WitDatabase.Studio/logs/studio.log`, rolling at 5 MB and
keeping three. No new dependency: it is one file appended under a lock, and it never throws into the
application it exists to describe - which is asserted, against a path that cannot be created.

`Program.Main` installs `AppDomain.CurrentDomain.UnhandledException` and
`TaskScheduler.UnobservedTaskException` before Avalonia starts, with a fallback that writes straight to
the same file for failures that predate the service provider. They do not swallow anything; they make
sure the failure is written down before it takes the process.

**Verified by execution:** after a session the log holds 51 lines, including the S2 refusal at `[WRN]`
and the connection lifecycle. Three tests cover the provider, one of them a control - a message below
the minimum level must not be written, without which "the log contains what we asked for" would pass for
a provider that writes everything.

### 5.4 S11 - Avalonia 11.3.11 to 12.1.x, and the advisory closes

Dmitry took the major deliberately, and the reason is not freshness: **`Tmds.DBus.Protocol` goes
0.21.2 to 0.94.1**, which closes GHSA-xrw6-gwf8-vvr9. Measured on the dependency graph rather than
assumed.

**The upgrade cost one line of source.** Avalonia 12 moved `SetTextAsync` off `IClipboard` and onto
`ClipboardExtensions`, so `QueryTabViewModel` needed `using Avalonia.Input.Platform;`. Nothing else in
86 files failed to compile.

**Two things had to be given up or accepted, and both are recorded rather than absorbed:**

- **`Avalonia.Diagnostics` has no 12.x release at all** - 105 versions on nuget.org, the highest
  11.3.18. It was a Debug-only DevTools reference and is now removed. That loses the F12 inspector in
  Debug builds until the package catches up.
- **`OutWit.Common.MVVM.Avalonia` 2.0.4 still declares a dependency on Avalonia 11.3.11**, and it is a
  separate repository. NuGet's nearest-wins gives Studio Avalonia 12, so the package runs against a
  major it was not compiled for. **It works** - the application starts, binds, opens a database and
  loads the explorer, with nothing in the log - but that is evidence for the surface Studio uses, not a
  guarantee. It should be rebuilt against Avalonia 12 and released before Studio ships on this.

**Verified by execution, not by the suite**, which matters because the 279 tests are headless
ViewModel tests and instantiate no visual tree: the application was launched, a database opened through
the dialog, and the explorer and workspace loaded, with no error or warning written to the new log.

`Microsoft.Extensions.*` went 10.0.2 to 10.0.10 in the same change.

**Still open, and not Studio's:** `SQLitePCLRaw.lib.e_sqlite3` (GHSA-2m69-gcr7-jv3q) and
`Microsoft.OpenApi` (GHSA-v5pm-xwqc-g5wc) carry high-severity advisories in the benchmark, oracle-test
and sample projects. None of them is a shipped package, and none is Studio.

### 5.5 S4 and S5 - the Open dialog stops asking for what the file already knows

Taken together because they are the same dialog and the same misconception: that Studio has to tell the
engine what a database is.

**S4 - four controls removed rather than wired up.** The Open dialog offered *Enable ACID
transactions*, *Enable MVCC*, *Enable file locking* and a *Storage Engine*, and
`BuildConnectionString` emitted none of the first three. Since 12.2.0 the file records all four and
supplies whatever the connection string does not name, so asking the user is asking them to override a
correct answer with a guess. The Advanced tab is gone and the header now says where the configuration
comes from.

The test that pinned the defect was replaced by one that guards the property which made those controls
dishonest, and is still true: the Open path names only `Data Source` and what the user genuinely
chooses (`Mode=ReadOnly`, encryption). If a keyword goes back into it, it must come with a control that
works.

**S5 - an LSM database can be opened.** It is a *directory*, and the dialog had a file picker with no
folder option anywhere in the application, so Studio could create an LSM database and never reopen one.
Typing the path did not help either: `ApplyAutoDetectedSettings` guarded on `File.Exists`, silently
skipping detection for one of the two stores. There is now a **Folder...** button beside **File...**,
and detection uses the same file-or-directory test the S2 refusal uses.

**Proved end to end rather than at the seam:** the test builds a real LSM database through the engine,
writes a row, closes it, then drives the dialog at the directory - detection reports `lsm`, the
connection opens, and the row comes back.

### 5.6 S12 - a fresh dialog showed the previous attempt's error

Found in the application while confirming S2: `InitDefault` replaced `ConnectionInfo` and every setting
but left `ErrorMessage` alone, so a dialog reopened after a refusal came back still showing it. It now
clears, and it also unsubscribes the old `ConnectionInfo`'s handler, which was accumulating one
subscription per dialog.

## 6. Ledger

Unchanged at **45 suppressed entries (31 `[Ignore(…)]` + 14 `Ignore =`) plus 2 `[Explicit]`**, counted
with the commands on this branch. Nothing in `Sources/**` was touched by this phase.

## 6a. The release pipeline, modelled on the WitCloud client

`studio-release.yml` replaces `release.yml`, which is deleted rather than left beside it: keeping two
release workflows for one product, one of them writing into the engine's tag namespace, is worse than
having one.

### 6a.1 What changed against the workflow it replaces

| | old `release.yml` | new `studio-release.yml` |
|---|---|---|
| trigger | `workflow_dispatch` only, tag derived from the csproj version | **`studio-v*` tag**, plus dispatch |
| tag namespace | `v1.0.1` - the engine's `v*` list | `studio-v*`, separate by construction |
| packaging | `dotnet publish` + `Compress-Archive`/`tar` | **Avalonia Parcel**: NSIS installer, `.deb`, `.zip` |
| tracks | self-contained only | self-contained **and** framework-dependent (`-no-dotnet`) |
| macOS | `macos-13`, defaulted **off**, never ran | `macos-14` (arm64), always in the matrix |
| signing | none | macOS Developer-ID + notarization, Linux GPG-signed `SHA256SUMS`, Windows Authenticode via SSL.com eSigner |
| missing platform | silent | **`::warning::NO ARTIFACT FOR <os>`**, per platform |

**Every signing step is a no-op when its secrets are absent**, exactly as the WitCloud client does for
eSigner, so a release still ships unsigned rather than failing. The committed `.parcel` says `AdHoc` for
macOS and the workflow patches it to `P12Certificate` only when `APPLE_CERT_P12_BASE64` exists - so a
fork, or this repository before the certificates are bought, still packs.

Windows signing keeps WitCloud's quota rule: the eSigner tier is ~240 signatures a year, so
`-dev`/`-test`/`-internal` tags are not signed unless `sign=true` forces it.

### 6a.2 Stated, not left to be discovered

`v1.0.1` shipped two platforms of the three its matrix listed and said nothing, because `buildMacOS`
defaulted to false. The release job now **enumerates the three platform labels and emits a warning for
each one with no artifact**. macOS is `continue-on-error` for now - it has never built once, and it must
not be able to block a release the two proven platforms produce - which is exactly why the warning has
to exist.

**`osx-x64` is not built.** The workflow it replaces offered it and never ran it. Apple Silicon is the
target that matters first; the header says so rather than leaving a reader to infer it from the matrix.

### 6a.3 The icons, which did not exist

Studio shipped one icon: `WitDatabase.ico`, a **single 256x209 image**. Windows stretches a non-square
icon; macOS `.icns` and Linux packaging will not take one, and neither format existed. `Assets/Branding`
now holds `.ico` (7 sizes), `.icns` (7 sizes), `.svg` and `.png`, **derived from the logo Studio already
had** rather than redrawn - squared onto a transparent canvas without rescaling the artwork.
`build-icons.py` sits beside them so the derivation is reproducible and the branding has one source.

### 6a.4 Measured locally before spending a CI cycle

Parcel 1.0.5 packed `win-x64` on this machine and the artifact was checked rather than the exit code:
`WitDatabaseStudio.x64.1.0.1.zip`, 50 MB, **258 entries**, 111.5 MB unpacked, containing
`WitDatabaseStudio.exe`. It was then **extracted and run** - the window opens, renders and carries the
new icon.

One thing worth separating: the automation tool refused to launch that executable with
`0x80040201`. Started directly it runs fine. **The instrument failed, not the artifact** - checked
before it could become a finding.

Linux and macOS cannot be settled here. CI is the arbiter, as it was for `FileLocking=false`.

### 6a.5 The first run: macOS builds, and Windows breaks

Run 30939311128, `workflow_dispatch` at `version=0.0.0-dev`, all twelve secrets present.

| job | |
|---|---|
| pack linux-x64 (selfcontained / framework) | **success** |
| pack macos-arm64 (selfcontained / framework) | **success - the first macOS build in this project's history** |
| pack windows-x64 (selfcontained / framework) | **failure** |

Verified against the release assets rather than the job status: six packages and six `SHA256SUMS`
files, the Linux ones carrying `.asc` detached signatures - so GPG signing works. macOS produced
`WitDatabaseStudio-0.0.0-dev-macos-arm64.zip` (48 MB) and its `-no-dotnet` track (15.7 MB).

**The missing-platform warning fired, which is the point of having written it:**
`##[warning]NO ARTIFACT FOR windows-x64 - this release does not cover it.` It was added because
`v1.0.1` shipped two platforms of three in silence, and on its first outing it caught exactly that
happening again.

**Three defects, all of them in this workflow rather than in Studio.**

1. **`docker run ghcr.io/sslcom/codesigner` cannot work on `windows-latest`.** That runner's Docker
   daemon is in *Windows container* mode and the image is Linux-only:
   `no matching manifest for windows(10.0.26100)/amd64`, exit 125. The WitCloud client uses the
   official `sslcom/esigner-codesign@develop` action, which handles this; copying its *shape* without
   copying its *mechanism* is what produced this.
2. **The signing gate defaulted the wrong way.** It skipped only when the tag looked internal - so a
   `workflow_dispatch` from `main` (ref name `main`, matching no internal pattern) fell through to
   signing, and would have spent quota on a throwaway build. It now defaults to **not** signing and
   turns on only for a public tag or an explicit `sign=true`, which is what WitCloud does.
3. **A signing failure destroyed the build.** Both Windows packages were built and then thrown away
   because a later step failed. The signing step is `continue-on-error` now, with a warning when no
   signed file appears: **unsigned-and-shipped beats signed-and-absent.**

The first is the interesting one. It is the same shape the audit kept finding one level down - a
correct piece of code that one route does not go through - except here the route was a platform, and
the only way to find out was to run it.

### 6a.6 The second run: all three platforms, and a step that could not fail

Run 30940536637, same dispatch. **All seven jobs green, 18 assets, all three platforms.**

Checked against the artifacts rather than the names:

| | |
|---|---|
| `...windows-x64.exe`, 49.7 MB | a real NSIS installer - `MZ` header, `Nullsoft` marker at offset 96188 |
| `...macos-arm64.zip`, 48.3 MB | a real bundle - `WitDatabaseStudio.app/Contents/Info.plist`, `Contents/MacOS/`, and `_CodeSignature/CodeResources`, so Developer-ID signing landed |
| `SHA256SUMS-linux-*.txt.asc` | GPG detached signatures present |
| `should_sign=false` | the quota gate held: no eSigner signature spent on a throwaway build |
| `NO ARTIFACT FOR` | not emitted - the only occurrence in the log is the echoed script source |

**And a defect that the green hid.** The log carries `status: Invalid` twice: **Apple rejected the
notarization of both macOS tracks**, and the step reported success anyway. `notarytool`'s exit code
does not follow its verdict, and nothing read the verdict - so the step could not fail. It is the
`COUNT(*)` lesson in another costume: never take a proxy for the answer.

It now parses the status as JSON, warns when it is not `Accepted`, **fetches the submission log so the
reason is visible** - the run above left only the word "Invalid" to work from - and staples the ticket
into the bundle when Apple does accept. It is `continue-on-error`, for the same reason the Windows
signing step is: notarization must not be able to destroy a build.

**Worth knowing before diagnosing it:** the WitCloud client carries the same unsolved problem, and says
so in its own workflow - *"macOS notarization of the multi-file bundle is still unsolved"*. So the
current state is **signed but not notarized**: the application runs on macOS and Gatekeeper warns on
first launch. What the rejection reason actually is, the next run will say.

### 6a.7 The third run: the reason, at last, and it is one line

Run 30971614703. Seven jobs green, 18 assets, all three platforms, `should_sign=false`, zero
missing-platform warnings. And this time the step **said what it found** instead of reporting success:

```
Notarization status: Invalid
##[warning]Notarization returned 'Invalid' - the bundle is signed but NOT notarized...
```

The submission log it now fetches contains **exactly one issue**, identical on both tracks:

```json
"statusSummary": "Archive contains critical validation errors",
"statusCode": 4000,
"issues": [{
  "severity": "error",
  "path": "…/WitDatabaseStudio.app/Contents/MacOS/WitDatabaseStudio",
  "message": "The signature of the binary is invalid.",
  "architecture": "arm64"
}]
```

**That is a diagnosis rather than a symptom.** It is not the bundle layout, not a missing hardened
runtime, not the `.zip` packaging, and not the managed assemblies - Apple objects to **one file**: the
apphost, `Contents/MacOS/WitDatabaseStudio`, whose own signature is invalid. The bundle around it is
signed (`_CodeSignature/CodeResources` is present and Apple read it), so what is broken is the seal on
the native executable inside.

That is the known shape for a .NET application bundle: nested Mach-O binaries must be signed
**innermost-first** - every `.dylib` and the apphost, with hardened runtime and a secure timestamp -
and only then the `.app` itself. Signing the bundle alone leaves the apphost carrying whatever
signature `dotnet publish` gave it, which notarization rejects.

**The next step is therefore specific:** sign the nested binaries before Parcel seals the bundle, or
re-sign the apphost and re-seal afterwards. Not attempted here - it is a change to how the macOS
artifact is built, and it deserves its own measurement rather than being bolted onto the run that
diagnosed it.

**And the shape worth keeping:** three runs, and each one only became useful because the previous one
was made able to fail. Run 1 hid a broken platform behind a green job; run 2 hid a rejected
notarization behind a green step; run 3 printed the reason. A pipeline earns trust by being made
capable of reporting bad news, one refusal at a time.

## 7. Verification

`OutWit.Database.Studio.Tests`, the CI filter, on this branch:

| | passed | failed |
|---|---|---|
| before the phase | 259 | 0 |
| audit, before any fix | 275 | 0 |
| after S1, S2, S7, S12 | 279 | 0 |
| after S4, S5 | 280 | 0 |
| after the S13 probe and its control | **282** | 0 |

Nothing under `Sources/**` was touched, so no engine suite can be affected by this phase.

## 8. What this phase does not do

**S3, S6, S8 and S10 are not fixed here.** S3 and S6 need the Create dialog's storage handling rebuilt -
it builds an LSM database in the wrong place and connects to a different one than it created. S8 is the
missing automation peer on the SQL editor. S10 - macOS - is not a code change at all: the pipeline now
builds it, and CI has to say whether it works.

Every one of them is pinned by a test that states the inversion its fix must produce, so they can be
taken in any order and each proves itself when it lands.
