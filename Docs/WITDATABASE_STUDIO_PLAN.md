# WitDatabase Studio - Implementation Plan

**Date:** 2025-02-06  
**Version:** 1.0  
**Framework:** Avalonia UI  

---

## 1. Project Overview

### 1.1 Purpose

WitDatabase Studio is a cross-platform desktop application for managing WitDatabase databases. It is analogous to MySQL Workbench, DBeaver, or DB Browser for SQLite, but specialized for WitDatabase.

### 1.2 Target Audience

- Developers using WitDatabase
- Database administrators
- QA engineers
- Data analysts

### 1.3 Platforms

| Platform | Support |
|----------|---------|
| Windows | Yes |
| macOS | Yes |
| Linux | Yes |

---

## 2. Functionality (MVP)

### 2.1 Connection Management

| Feature | Priority | Description |
|---------|----------|-------------|
| Create new database | P0 | Create database file |
| Open existing database | P0 | Open .witdb file |
| Open with password | P0 | Encrypted database support |
| Recent databases | P1 | Recent files list |
| Save connection strings | P1 | Named connections |
| Auto-reconnect | P2 | On file change detection |

### 2.2 Database Navigation (Database Explorer)

| Feature | Priority | Description |
|---------|----------|-------------|
| Database tree | P0 | TreeView with databases |
| Tables list | P0 | Tables node |
| Views list | P0 | Views node |
| Indexes list | P0 | Indexes node |
| Triggers list | P1 | Triggers node |
| Sequences list | P1 | Sequences node |
| Table structure | P0 | Columns, types, constraints |
| View definition | P0 | Show `VIEW_DEFINITION` for views |
| Index details | P0 | Show index columns, unique flag, filter condition |
| Schema refresh | P0 | Update tree + open panels |
| Search/Filter | P1 | Search by name |

### 2.3 Query Execution (Query Editor)

| Feature | Priority | Description |
|---------|----------|-------------|
| SQL Editor | P0 | Editor with highlighting |
| Execute Query (F5) | P0 | Execute query |
| Execute Selection | P0 | Execute selected text |
| Multiple Result Sets | P0 | Tabs for results |
| Query History | P1 | Query history |
| Save/Load Scripts | P1 | .sql files |
| Auto-complete | P2 | SQL IntelliSense |
| Syntax highlighting | P0 | WitSQL syntax highlighting |
| Error highlighting | P1 | Error highlighting |
| Query plan (EXPLAIN) | P1 | Plan visualization |

### 2.4 Result Viewing (Data Grid)

| Feature | Priority | Description |
|---------|----------|-------------|
| Results table | P0 | DataGrid |
| Pagination | P0 | For large results |
| Column sorting | P0 | Click on header |
| Copy rows | P0 | Copy to clipboard |
| Copy as INSERT | P1 | Generate INSERT statements |
| Export to CSV | P0 | Data export |
| Export to JSON | P1 | Data export |
| Export to SQL | P1 | INSERT statements |
| NULL display | P0 | Visual NULL indicator |
| BLOB preview | P2 | Hex/image preview |

### 2.5 Data Editing (Table Editor)

| Feature | Priority | Description |
|---------|----------|-------------|
| Browse table data | P0 | SELECT * with pagination |
| Edit cell inline | P0 | Double-click to edit |
| Add new row | P0 | Insert row |
| Delete row | P0 | Delete selected |
| Commit changes | P0 | Apply edits |
| Rollback changes | P0 | Discard edits |
| Filter data | P1 | WHERE clause builder |
| Quick filter | P1 | Column-based filter |

### 2.6 Export/Import

| Feature | Priority | Description |
|---------|----------|-------------|
| Export table to CSV | P0 | With options |
| Export table to JSON | P1 | Array of objects |
| Export table to SQL | P1 | INSERT statements |
| Import from CSV | P1 | With mapping |
| Import from JSON | P2 | Array of objects |
| Backup database | P1 | Copy .witdb file |
| Restore database | P1 | Open backup |

### 2.7 Additional Features (Post-MVP)

| Feature | Priority | Description |
|---------|----------|-------------|
| Dark/Light theme | P2 | Theme support |
| Keyboard shortcuts | P1 | Configurable hotkeys |
| Multiple tabs | P0 | Multiple query editors |
| Schema diff | P3 | Compare two databases |
| Data diff | P3 | Compare table data |
| ER Diagram | P3 | Visual schema |
| Query formatter | P2 | SQL beautifier |
| Encryption indicator | P1 | Show lock icon |

### 2.8 Schema Editing (DDL / Designer)

| Feature | Priority | Description |
|---------|----------|-------------|
| Create table (GUI) | P0 | Add table with column grid (name/type/null/default/pk) |
| Drop table | P0 | Drop table from context menu |
| Create view | P0 | Create view via DDL wizard |
| Drop view | P0 | Drop view from context menu |
| Create index | P0 | Create index via DDL wizard |
| Drop index | P0 | Drop index from context menu |
| Export schema (DDL) | P1 | Generate CREATE statements |

---

## 3. Architecture

### 3.1 Technology Stack

| Component | Technology |
|-----------|------------|
| UI Framework | Avalonia UI 11+ |
| MVVM Framework | CommunityToolkit.Mvvm |
| SQL Editor | AvaloniaEdit |
| Icons | Material Design Icons |
| DI Container | Microsoft.Extensions.DependencyInjection |
| Settings | JSON file |
| Logging | Microsoft.Extensions.Logging |

### 3.2 Project Structure

```
Tools/
  OutWit.Database.Studio/
    OutWit.Database.Studio.sln

    OutWit.Database.Studio/                    # Main application
      App.axaml
      App.axaml.cs
      Program.cs

      ViewModels/
        MainWindowViewModel.cs
        ConnectionViewModel.cs
        DatabaseExplorerViewModel.cs
        QueryEditorViewModel.cs
        ResultGridViewModel.cs
        TableEditorViewModel.cs
        ExportViewModel.cs

      Views/
        MainWindow.axaml
        MainWindow.axaml.cs
        ConnectionDialog.axaml
        DatabaseExplorer.axaml
        QueryEditor.axaml
        ResultGrid.axaml
        TableEditor.axaml
        ExportDialog.axaml

      Models/
        ConnectionInfo.cs
        DatabaseNode.cs
        TableInfo.cs
        ColumnInfo.cs
        QueryResult.cs
        Settings.cs

      Services/
        IDatabaseService.cs
        DatabaseService.cs
        IConnectionManager.cs
        ConnectionManager.cs
        IExportService.cs
        ExportService.cs
        ISettingsService.cs
        SettingsService.cs
        ISchemaService.cs

      Converters/
        NullToTextConverter.cs
        BoolToVisibilityConverter.cs
        NodeTypeToIconConverter.cs

      Controls/
        SqlEditor.axaml
        SqlEditor.axaml.cs
        DataGridEx.axaml

      Themes/
        Light.axaml
        Dark.axaml

      Assets/
        Icons/
        Fonts/

      OutWit.Database.Studio.csproj

    OutWit.Database.Studio.Tests/              # Unit tests
      OutWit.Database.Studio.Tests.csproj
```

### 3.3 Component Diagram

```
+-----------------------------------------------------------------------+
|                           Main Window                                  |
+-----------------------------------------------------------------------+
|  +-----------------+  +---------------------------------------------+  |
|  |                 |  |                  Tab Control                 |  |
|  |   Database      |  +---------------------------------------------+  |
|  |   Explorer      |  |  +----------------------------------------+|  |
|  |                 |  |  |           Query Editor                  ||  |
|  |  +- Database 1  |  |  |  +------------------------------------+||  |
|  |  |  +- Tables   |  |  |  |         SQL Editor                  |||  |
|  |  |  |  +- Users |  |  |  |  (AvaloniaEdit + Syntax Highlight)  |||  |
|  |  |  |  +- Orders|  |  |  +------------------------------------+||  |
|  |  |  +- Views    |  |  |  +------------------------------------+||  |
|  |  |  +- Indexes  |  |  |  |         Result Grid                 |||  |
|  |  +- Database 2  |  |  |  |  (DataGrid with pagination)         |||  |
|  |                 |  |  |  +------------------------------------+||  |
|  |                 |  |  +----------------------------------------+|  |
|  |                 |  +---------------------------------------------+  |
|  |                 |  |  +----------------------------------------+|  |
|  |                 |  |  |           Table Editor                  ||  |
|  |                 |  |  |  (DataGrid with edit support)           ||  |
|  |                 |  |  +----------------------------------------+|  |
|  +-----------------+  +---------------------------------------------+  |
+-----------------------------------------------------------------------+
|                           Status Bar                                   |
|  [Connected: demo.witdb] [Rows: 1000] [Time: 0.05s] [Encrypted: Yes]  |
+-----------------------------------------------------------------------+
```

### 3.4 MVVM Architecture

```
+--------------+       +--------------+       +--------------+
|    Views     |<----->|  ViewModels  |<----->|   Services   |
|  (.axaml)    |       |  (.cs)       |       |  (.cs)       |
+--------------+       +--------------+       +------+-------+
                                                     |
                                              +------v-------+
                                              |  WitDatabase |
                                              |   (ADO.NET)  |
                                              +--------------+
```

---

## 4. UI/UX Design

### 4.1 Main Window Layout

```
+-----------------------------------------------------------------------+
|  File   Edit   View   Tools   Help                         [_][O][X]  |
+-----------------------------------------------------------------------+
|  [New] [Open] [Save] |  [Execute >] [Stop #] | [Export] [Import]      |
+-----------------------------------------------------------------------+
|              |  Query 1  x  | Table: Users  x  |  +                    |
|  Databases   +------------------------------------------------------------+
|              |  SELECT * FROM Users                                    |
|  v demo.db   |  WHERE Age > 18                                         |
|    v Tables  |  ORDER BY Name;                                         |
|      Users   |  |                                                      |
|      Orders  |                                                         |
|      Products+------------------------------------------------------------+
|    > Views   |  Results  | Messages | Query Plan                       |
|    > Indexes +------------------------------------------------------------+
|              |  ID | Name      | Age  | Email              | CreatedAt |
|              |  1  | John Doe  | 25   | john@example.com   | 2024-01-01|
|              |  2  | Jane Smith| 30   | jane@example.com   | 2024-01-02|
|              |  3  | Bob Wilson| 22   | bob@example.com    | 2024-01-03|
|              |                                                         |
|              |  < 1 2 3 ... 10 >   Showing 1-100 of 1000               |
+-----------------------------------------------------------------------+
|  Connected: demo.witdb | Rows: 1000 | Time: 0.042s | [Lock] Encrypted |
+-----------------------------------------------------------------------+
```

### 4.2 Connection Dialog

```
+-------------------------------------------------+
|  Open Database                            [X]   |
+-------------------------------------------------+
|                                                 |
|  Database File:                                 |
|  +-----------------------------------+ [Browse] |
|  | C:\Data\myapp.witdb               |          |
|  +-----------------------------------+          |
|                                                 |
|  [x] Database is encrypted                      |
|                                                 |
|  Password:                                      |
|  +-----------------------------------+          |
|  | ************                      |          |
|  +-----------------------------------+          |
|                                                 |
|  [ ] Read-only mode                             |
|                                                 |
|  Storage Engine: [B-Tree      v]                |
|                                                 |
|           [Cancel]        [Connect]             |
+-------------------------------------------------+
```

### 4.3 Export Dialog

```
+-------------------------------------------------+
|  Export Data                              [X]   |
+-------------------------------------------------+
|                                                 |
|  Source: [Table: Users         v]               |
|                                                 |
|  Format:                                        |
|  ( ) CSV (Comma Separated Values)               |
|  (x) JSON (Array of Objects)                    |
|  ( ) SQL (INSERT Statements)                    |
|  ( ) Excel (XLSX)                               |
|                                                 |
|  Options:                                       |
|  [x] Include column headers                     |
|  [ ] Export selected rows only                  |
|  [x] Format dates as ISO 8601                   |
|                                                 |
|  Output File:                                   |
|  +-----------------------------------+ [Browse] |
|  | C:\Export\users.json              |          |
|  +-----------------------------------+          |
|                                                 |
|           [Cancel]        [Export]              |
+-------------------------------------------------+
```

### 4.4 Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New Query Tab |
| `Ctrl+O` | Open Database |
| `Ctrl+S` | Save Query |
| `F5` | Execute Query |
| `Ctrl+Enter` | Execute Query |
| `Ctrl+Shift+E` | Execute Selection |
| `Ctrl+F` | Find |
| `Ctrl+H` | Find & Replace |
| `Ctrl+Space` | Auto-complete (if enabled) |
| `F2` | Rename |
| `Delete` | Delete selected |
| `Ctrl+C` | Copy |
| `Ctrl+V` | Paste |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Ctrl+Tab` | Next Tab |
| `Ctrl+Shift+Tab` | Previous Tab |
| `Ctrl+W` | Close Tab |

---

## 5. Implementation Plan

### Phase 1: Foundation (Week 1-2)

| Task | Description | Estimate |
|------|-------------|----------|
| Project setup | Create solution, projects | 2h |
| Main window | Shell with layout | 4h |
| Connection dialog | Open/create database | 4h |
| Database service | WitDbConnection integration | 4h |
| Settings service | JSON settings persistence | 2h |
| **Total** | | **16h** |

### Phase 2: Database Explorer + Schema Designer (Week 2-4)

| Task | Description | Estimate |
|------|-------------|----------|
| TreeView component | Hierarchical view | 4h |
| Schema loading | Tables, views, indexes | 4h |
| Structure panels | Table/View/Index structure details | 6h |
| Context menu actions | Create/Drop table/view/index | 6h |
| Create Table designer (GUI) | Column grid + DDL generation | 8h |
| DDL wizard (index/view) | Simple editor + validation + execute | 6h |
| Refresh functionality | Update tree | 1h |
| **Total** | | **35h** |

### Phase 3: Query Editor (Week 3-4)

| Task | Description | Estimate |
|------|-------------|----------|
| AvaloniaEdit integration | Base editor setup | 4h |
| WitSQL syntax highlighting | Custom highlighting rules | 8h |
| Execute query | F5 execution | 2h |
| Execute selection | Selected text execution | 2h |
| Multiple tabs | Tab control for queries | 4h |
| **Total** | | **20h** |

### Phase 4: Result Grid (Week 4-5)

| Task | Description | Estimate |
|------|-------------|----------|
| DataGrid setup | Basic grid | 4h |
| Pagination | Load on demand | 4h |
| Sorting | Click-to-sort | 2h |
| Copy functionality | Copy rows, copy as INSERT | 3h |
| NULL display | Visual NULL indicator | 1h |
| **Total** | | **14h** |

### Phase 5: Table Editor (Week 5-6)

| Task | Description | Estimate |
|------|-------------|----------|
| Editable grid | Inline edit | 6h |
| Add row | Insert new row | 2h |
| Delete row | Remove selected | 2h |
| Commit/Rollback | Apply/discard changes | 4h |
| Validation | Type validation | 2h |
| **Total** | | **16h** |

### Phase 6: Export/Import

| Feature | Priority | Description |
|---------|----------|-------------|
| Export table to CSV | P0 | With options |
| Export table to JSON | P1 | Array of objects |
| Export table to SQL | P1 | INSERT statements |
| Import from CSV | P1 | With mapping |
| Import from JSON | P2 | Array of objects |
| Backup database | P1 | Copy .witdb file |
| Restore database | P1 | Open backup |

### Phase 7: Polish (Week 7-8)

| Task | Description | Estimate |
|------|-------------|----------|
| Dark theme | Theme support | 4h |
| Error handling | User-friendly errors | 4h |
| Status bar | Connection status, timing | 2h |
| Recent files | File history | 2h |
| Testing | Manual + unit tests | 8h |
| Documentation | User guide | 4h |
| **Total** | | **24h** |

### MVP Total

| Phase | Hours |
|-------|-------|
| Foundation | 16h |
| Database Explorer | 35h |
| Query Editor | 20h |
| Result Grid | 14h |
| Table Editor | 16h |
| Export/Import | 19h |
| Polish | 24h |
| **Total** | **124h** |

**Time estimate:** 4-6 weeks (at 25-30h/week)

---

## 6. Technical Details

### 6.1 Project File (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <BuiltInComInteropSupport>true</BuiltInComInteropSupport>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.2.6" />
    <PackageReference Include="Avalonia.Desktop" Version="11.2.6" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.6" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.2.6" />
    <PackageReference Include="Avalonia.Diagnostics" Version="11.2.6" Condition="'$(Configuration)' == 'Debug'" />
    <PackageReference Include="AvaloniaEdit" Version="11.2.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Sources\Providers\OutWit.Database.AdoNet\OutWit.Database.AdoNet.csproj" />
  </ItemGroup>
</Project>
```

### 6.2 WitSQL Syntax Highlighting

```csharp
public class WitSqlHighlightingDefinition : IHighlightingDefinition
{
    private static readonly string[] Keywords = 
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES",
        "UPDATE", "SET", "DELETE", "CREATE", "TABLE", "INDEX",
        "DROP", "ALTER", "ADD", "COLUMN", "PRIMARY", "KEY",
        "FOREIGN", "REFERENCES", "NOT", "NULL", "UNIQUE",
        "DEFAULT", "CHECK", "AND", "OR", "IN", "BETWEEN",
        "LIKE", "ORDER", "BY", "ASC", "DESC", "LIMIT",
        "OFFSET", "GROUP", "HAVING", "JOIN", "INNER", "LEFT",
        "RIGHT", "FULL", "OUTER", "ON", "AS", "DISTINCT",
        "UNION", "INTERSECT", "EXCEPT", "CASE", "WHEN", "THEN",
        "ELSE", "END", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION"
    };
    
    private static readonly string[] DataTypes =
    {
        "INT", "INTEGER", "BIGINT", "SMALLINT", "TINYINT",
        "VARCHAR", "TEXT", "CHAR", "BOOLEAN", "BOOL",
        "FLOAT", "DOUBLE", "DECIMAL", "NUMERIC", "REAL",
        "DATE", "TIME", "DATETIME", "TIMESTAMP", "INTERVAL",
        "BLOB", "BINARY", "VARBINARY", "GUID", "UUID", "JSON"
    };
    
    private static readonly string[] Functions =
    {
        "COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE",
        "NULLIF", "CAST", "LOWER", "UPPER", "TRIM", "LENGTH",
        "SUBSTR", "REPLACE", "NOW", "NEWGUID", "ABS", "ROUND"
    };
    
    // ... highlighting rules implementation
}
```

### 6.3 Database Service Interface

```csharp
public interface IDatabaseService
{
    Task<bool> ConnectAsync(ConnectionInfo connection);
    Task DisconnectAsync();
    bool IsConnected { get; }
    
    Task<IReadOnlyList<TableInfo>> GetTablesAsync();
    Task<IReadOnlyList<ViewInfo>> GetViewsAsync();
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync();
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string tableName);
    
    Task<QueryResult> ExecuteQueryAsync(string sql, CancellationToken ct = default);
    Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct = default);
    Task<object?> ExecuteScalarAsync(string sql, CancellationToken ct = default);
    
    Task<int> InsertRowAsync(string tableName, IDictionary<string, object?> values);
    Task<int> UpdateRowAsync(string tableName, IDictionary<string, object?> values, string whereClause);
    Task<int> DeleteRowAsync(string tableName, string whereClause);
}
```

---

## 7. Post-MVP Features (v2.0)

### 7.1 Query Features

- Auto-complete (IntelliSense)
- Query formatter/beautifier
- Query history with search
- Saved queries/snippets
- Parameter binding UI

### 7.2 Schema Features

- ER Diagram generation
- Schema comparison (diff)
- DDL generation wizard
