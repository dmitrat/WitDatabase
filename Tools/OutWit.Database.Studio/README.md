# WitDatabase Studio

Cross-platform desktop application for managing WitDatabase databases.

## Overview

WitDatabase Studio is a graphical management tool for WitDatabase, similar to MySQL Workbench or DB Browser for SQLite. Built with Avalonia UI, it runs on Windows, macOS, and Linux.

## Features

### ? Phase 1 Complete: Foundation

- **Project Structure**: .NET 10.0 Avalonia MVVM application
- **Architecture**: MVVM pattern using OutWit.Common.MVVM.Avalonia
- **Models**: ConnectionInfo, Settings, DatabaseNode, TableInfo, ColumnInfo, QueryResult
- **Services**: DatabaseService (ADO.NET integration), SettingsService (JSON persistence)
- **ViewModels**: ApplicationViewModel (Singleton), MainWindowViewModel, ConnectionViewModel, DatabaseExplorerViewModel, QueryEditorViewModel, TableStructureViewModel
- **Menu Commands**: New Database, Open Database, Close Database, Exit, Refresh
- **Connection Dialog**: Full-featured connection dialog with encryption support
- **Unit Tests**: 59 NUnit 4 tests (all passing ?)

### ? Phase 2 Complete: Database Explorer

- **TreeView Component**: Database schema tree with icons
- **Schema Loading**: Tables, views, indexes, triggers, sequences
- **Table Structure Panel**: Column details with types, nullable, primary keys
- **Context Menus**: Browse Data, View Definition, Drop, Refresh
- **UI Layout**: Resizable splitter, status bar with connection info
- **Converters**: Node type to icon converter

### ?? Phase 3 Planned: Query Editor

- Query Editor with WitSQL syntax highlighting
- Result Grid with pagination
- Execute/Cancel commands
- Query history

## Development Status

Current Phase: **Phase 2 Complete** ?

| Phase | Status | Tasks |
|-------|--------|-------|
| Foundation | ? Complete | Project setup, Models, Services, ViewModels, Menu Commands, Connection Dialog, Tests |
| Database Explorer | ? Complete | TreeView ?, Schema loading ?, Table Structure ?, Context menus ? |
| Query Editor | ?? Planned | SQL Editor, Execute queries, Syntax highlighting |
| Result Grid | ?? Planned | DataGrid, Pagination, Export |
| Table Editor | ?? Planned | Edit cells, Add/Delete rows, Commit/Rollback |
| Export/Import | ?? Planned | CSV/JSON/SQL export, Import |
| Polish | ?? Planned | Themes, Error handling, Testing |

## Usage

### Opening a Database

1. Click **File ? Open Database...** or press `Ctrl+O`
2. Enter database file path (e.g., `C:\Data\myapp.witdb`)
3. If encrypted, check "Database is encrypted" and enter password
4. Select storage engine (btree or lsm)
5. Click **Connect**

### Creating a New Database

1. Click **File ? New Database...**
2. Enter new database file path
3. Configure encryption (optional)
4. Select storage engine
5. Click **Connect**

### Browsing Data

1. Expand database tree in Database Explorer
2. Click on a table to view its structure
3. Right-click on table ? **Browse Data** to view records
4. Use context menu for additional actions (View Definition, Drop, etc.)

## Architecture

### ViewModels Hierarchy

```
ApplicationViewModel (Singleton)
??? MainWindowViewModel
??? ConnectionViewModel
??? DatabaseExplorerViewModel
??? QueryEditorViewModel
??? TableStructureViewModel
```

All ViewModels inherit from `ViewModelBase<ApplicationViewModel>` and can communicate through the parent `ApplicationViewModel`.

### Code Style

- Follows `CODE_STYLE_GUIDE.md`
- Uses `NotifyAttribute` from OutWit.Common.Aspects for property change notifications
- Uses `DelegateCommand<object>` from OutWit.Common.MVVM for commands
- Models inherit from `ModelBase` with `Is()` and `Clone()` methods
- All code and documentation in English only
- Minimal code-behind (only constructors)

### Test Style

- NUnit 4 framework
- Test class names end with `Tests` (plural)
- Test method names end with `Test` (singular)
- PascalCase, no underscores
- 59 tests currently (all passing)

## Technology Stack

- **.NET 10.0**
- **Avalonia UI 11.3.10** - Cross-platform UI framework
- **OutWit.Common.MVVM.Avalonia 2.0.2** - MVVM framework
- **OutWit.Common.Aspects** - Property change aspects
- **OutWit.Database.AdoNet** - Database access
- **Microsoft.Extensions.DependencyInjection** - DI container
- **Microsoft.Extensions.Logging** - Logging
- **NUnit 4** - Unit testing

## Building

```bash
dotnet build Tools/OutWit.Database.Studio/OutWit.Database.Studio.csproj
```

## Running Tests

```bash
dotnet test Tools/OutWit.Database.Studio.Tests/OutWit.Database.Studio.Tests.csproj
```

## Running the Application

```bash
dotnet run --project Tools/OutWit.Database.Studio/OutWit.Database.Studio.csproj
```

## Project Structure

```
Tools/OutWit.Database.Studio/
??? Models/              # Data models
??? ViewModels/          # MVVM ViewModels
??? Views/               # Avalonia views (.axaml)
?   ??? MainWindow.axaml
?   ??? ConnectionDialog.axaml
?   ??? DatabaseExplorer.axaml
?   ??? TableStructure.axaml
??? Services/            # Business logic services
??? Converters/          # Value converters
??? Controls/            # Custom controls (planned)
??? Themes/              # Light/Dark themes (planned)
??? Assets/              # Icons, fonts

Tools/OutWit.Database.Studio.Tests/
??? Models/              # Model tests
??? ViewModels/          # ViewModel tests
??? Converters/          # Converter tests
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open Database |
| `Ctrl+N` | New Database |
| `F5` | Refresh Schema |
| `Ctrl+W` | Close Database |

## Next Steps

See `WITDATABASE_STUDIO_PLAN.md` for the complete implementation plan.

## License

Part of the WitDatabase project.
