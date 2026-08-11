# WitDatabase Studio

Cross-platform desktop client for WitDatabase, built with Avalonia. Windows, macOS and Linux from one
build.

**Not released.** The version is `3.0.0-dev` and Studio is deliberately excluded from the package
release: the seven library packages ship on their own cadence, and this tool is tagged `-dev` until
its own design work closes.

## What it does

- **Several databases at once.** One tree, one root per open connection, and a colour per connection
  carried on its tabs. An action on a node goes to the connection that node came from - never to
  whichever connection happens to be active - and closing one leaves the others alone.
- **Query tabs.** WitSQL syntax highlighting, completion, error underlining in the text, an execution
  plan, messages, and per-tab history. A tab runs in the connection it was opened against, and keeps
  running there however the tree selection moves.
- **Result grid and table editor.** Read results, or edit a table's rows in place with commit and
  rollback. A table with no primary key cannot be edited, and says so rather than failing on save.
- **Schema work.** Create and drop tables, views, indexes and triggers; rename; truncate; a structure
  tab per object. Every destructive action asks a question that states its consequences - the foreign
  keys that will dangle, the indexes that go with it, the rows that will be lost.
- **Import and export.** Export to CSV, JSON or SQL; import from CSV or JSON, with a preview of
  what will be read before anything is written.
- **Maintenance.** Read check, rebuild, copy, change the password.
- **Object inspector.** What an object is, what it costs to read, which indexes it has, what
  references it and what it references, and its `CREATE` statement.
- **Two languages** (English, Russian) and **two themes**, dark by default.

## Running it

```bash
dotnet run --project Tools/OutWit.Database.Studio/OutWit.Database.Studio.csproj
```

Open a database with `Ctrl+O`, create one with `Ctrl+N`, or pick one from the recent list on the
welcome screen. `Ctrl+K` opens the command palette, which reaches every command and every object by
name.

## Keyboard

The full map lives in `Services/KeyboardMap.cs` and is what the Keys window shows. It is checked
against the application in both directions by `KeyboardMapTests`: every gesture listed has to be
bound somewhere that really handles it, and every binding declared in the markup has to be listed. A
help window that quietly disagrees with the keys is worse than no help window.

| | |
|---|---|
| `Ctrl+K` | command palette |
| `Ctrl+N` / `Ctrl+O` / `Ctrl+R` | new database, open database, refresh |
| `Ctrl+T` / `Ctrl+Shift+T` / `Ctrl+W` | new query tab, reopen the last closed, close |
| `Ctrl+S` / `Ctrl+Shift+S` | save the query, save as |
| `F5` / `Ctrl+Shift+F5` / `Ctrl+Enter` | run the statement, the script, the selection |
| `Escape` | stop a running query, or close the find band |
| `Ctrl+F` / `Ctrl+H` / `F3` / `Shift+F3` | find, replace, next, previous |
| `Ctrl+B` | hide or show the object tree |
| `F2` / `F4` / `Delete` | in the tree: rename, structure, drop |
| `Ctrl+?` | the keyboard reference itself |

In the tree, a **double click** opens a table's data, a **middle click** opens it in a tab that does
not come to the front, and **typing letters** walks the selection to the first matching node.

## Layout of the project

```
Tools/OutWit.Database.Studio/
+-- Models/          # Connection info, settings, tree nodes, schema descriptions
+-- ViewModels/      # One per surface; Tabs/ holds the query, structure and table-edit tabs
+-- Views/           # MainWindow, DatabaseExplorer, ObjectInspector
|   +-- Dialogs/     # 18 task and service dialogs
|   +-- Query/       # The SQL editor surface
|   +-- Workspace/   # Tab strip, database view, structure view, table editor
+-- Controls/        # SqlEditor (AvaloniaEdit), the data grids, error underlining
+-- Services/        # Connections, sessions, settings, export, import, history, localisation
+-- Themes/          # Design tokens, type scale, metrics, control styles (see Themes/README.md)
+-- Ui/Icons/        # 76 outline icons as path data
+-- Converters/      # Value converters
+-- Resources/       # Strings.en.json, Strings.ru.json

Tools/OutWit.Database.Studio.Tests/
```

## Architecture

`ApplicationViewModel` is the single root: every other ViewModel hangs off it and reaches its
siblings through it. Views own the code-behind that genuinely belongs to a view - focus, key
handling, layout the markup cannot express - and nothing else.

The one rule worth stating for anyone reading the tests: **an interaction that a test can drive
through a ViewModel belongs in the ViewModel.** Type-ahead in the tree is the shape - the buffer and
the timeout are in the view, the search is in `DatabaseExplorerViewModel.JumpTo`, and only the search
carries the behaviour.

## Tests

```bash
dotnet test Tools/OutWit.Database.Studio.Tests/OutWit.Database.Studio.Tests.csproj
```

**853 tests**, and their honest limit is written into them: nearly all drive ViewModels over a real
database through the real `ConnectionManager`, `DatabaseSession` and `SettingsService`. Only two
things are stood in for, and both are people rather than services - the answer to a confirmation
dialog and the file picker.

What that cannot cover is what a window does when it is built, and this project has the scars: a
`Ctrl+?` gesture that `KeyGesture.Parse` refuses stopped Studio starting while 769 tests were green,
and a `{StaticResource}` in a control style would have done the same. Both are now guarded by
headless Avalonia tests (`Themes/DesignTokenTests`, `Services/KeyboardMapTests`) that ask Avalonia's
own parser and resource system the questions the window will ask.

## Technology

- .NET 10, Avalonia 12.1.1, Fluent theme
- AvaloniaEdit 12.0.0 for the SQL editor, Avalonia DataGrid 12.1.2 for results
- OutWit.Common.MVVM.Avalonia 3.0.0, OutWit.Common.Aspects for property notification
- OutWit.Database.AdoNet for everything that reaches a database
- Microsoft.Extensions DependencyInjection and Logging
- NUnit 4, Avalonia.Headless.NUnit for the tests that need a real visual tree

## License

Licensed under the Apache License, Version 2.0. See `LICENSE`.

## Attribution (optional)

If you use WitDatabase Studio in a product, a mention is appreciated (but not required), for example:
"Powered by WitDatabase https://witdatabase.io/".

## Trademark / Project name

"WitDatabase" and the WitDatabase logo are used to identify the official project by Dmitry Ratner.

You may:

- refer to the project name in a factual way (e.g., "built with WitDatabase");
- use the name to indicate compatibility (e.g., "WitDatabase-compatible").

You may not:

- use "WitDatabase" as the name of a fork or a derived product in a way that implies it is the official project;
- use the WitDatabase logo to promote forks or derived products without permission.
