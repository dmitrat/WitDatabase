# WitDatabase Studio - Changelog

Studio is versioned separately from the WitDatabase engine and released under its own `studio-v*` tag.
The engine's changelog is `/CHANGELOG.md`.

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
