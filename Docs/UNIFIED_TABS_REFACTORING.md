# Unified Tabs Refactoring Progress

**Date Started:** 2025-01-XX  
**Status:** ? Complete (Basic Migration)

---

## Overview

??????????? ??????? ????? ??? ??????????? Query, Table Edit ? Structure ????? ? ?????? ?????????.

### ????
1. ? ???? TabControl ??? ???? ????? ????? (Query, Edit, Structure)
2. ? ???????? ????? ?? ???????????? ???? Database Explorer
3. ? ?????????????? ???????????? ?? ????? ???
4. ? ??????????? ??????????? ????? (pin)
5. ? ?????????????? ???????????? ????? ??? ???? ?? ???????

---

## Completed Steps

### ? Step 1: WorkspaceTabType enum
- File: `Models/WorkspaceTabType.cs`
- Values: Query, TableEdit, Structure

### ? Step 2: WorkspaceTabViewModel base class
- File: `ViewModels/Tabs/WorkspaceTabViewModel.cs`
- Properties: Title, DisplayTitle, IsModified, IsPinned, UniqueId
- Abstract: TabType, IconPath
- Virtual: OnActivated, OnDeactivated, CanClose, OnClosed

### ? Step 3: QueryTabViewModel (moved to Tabs namespace)
- File: `ViewModels/Tabs/QueryTabViewModel.cs`
- Inherits from WorkspaceTabViewModel
- IconPath: PATH_QUERY_EXECUTE
- UniqueId: FilePath

### ? Step 4: TableEditTabViewModel
- File: `ViewModels/Tabs/TableEditTabViewModel.cs`
- Inherits from WorkspaceTabViewModel
- IconPath: PATH_DB_TABLE
- UniqueId: `edit:{TableName}`
- Full table editing functionality (add/delete/commit/rollback)

### ? Step 5: StructureTabViewModel
- File: `ViewModels/Tabs/StructureTabViewModel.cs`
- Inherits from WorkspaceTabViewModel
- IconPath: depends on ObjectType
- UniqueId: `structure:{ObjectType}:{ObjectName}`
- Supports Table, View, Index structure viewing

### ? Step 6: New icons in StudioIcons
- PATH_TAB_QUERY
- PATH_TAB_TABLE_EDIT
- PATH_TAB_STRUCTURE
- PATH_TAB_PIN
- PATH_TAB_UNPIN

### ? Step 7: WorkspaceTabsViewModel
- File: `ViewModels/WorkspaceTabsViewModel.cs`
- Manages all tab types
- Methods: OpenQueryTab, OpenTableEditTabAsync, OpenStructureTabAsync
- Duplicate tab prevention via UniqueId
- Pin/Unpin functionality
- Auto-close data tabs on disconnect

### ? Step 8: WorkspaceTabStrip.axaml
- File: `Views/Workspace/WorkspaceTabStrip.axaml`
- Unified tab strip with icons, pin/unpin, close buttons
- Context menu with Pin/Unpin, Close, Save options

### ? Step 9: WorkspaceTabs.axaml
- File: `Views/Workspace/WorkspaceTabs.axaml`
- Container with DataTemplates for each tab type

### ? Step 10: TableEditView.axaml
- File: `Views/Workspace/TableEditView.axaml`
- View for TableEditTabViewModel
- Toolbar with Add/Delete/Commit/Rollback

### ? Step 11: StructureView.axaml
- File: `Views/Workspace/StructureView.axaml`
- View for StructureTabViewModel
- Column list with type, nullable, key, default

### ? Step 12: Updated ApplicationViewModel
- Added WorkspaceTabsVm
- Marked old ViewModels as [Obsolete]

### ? Step 13: Updated DatabaseExplorerViewModel
- Replaced BrowseData with SelectTop100/SelectTop1000
- Added ViewStructureCommand
- Uses WorkspaceTabsVm for all operations

### ? Step 14: Updated DatabaseExplorer.axaml
- Select Data submenu (Top 100, Top 1000)
- View Structure menu item

### ? Step 15: Updated MainWindow.axaml
- Replaced TabControl with WorkspaceTabs
- Updated all commands to WorkspaceTabsVm

### ? Step 16: Updated Tests
- DatabaseExplorerViewModelTests updated
- QueryTabViewModelTests updated

---

## Remaining Tasks (Optional Cleanup)

### ?? Remove Legacy Files (when ready)
- ViewModels/QueryTabsViewModel.cs (currently has [Obsolete])
- ViewModels/TableEditorViewModel.cs (currently has [Obsolete])
- ViewModels/TableStructureViewModel.cs (currently has [Obsolete])
- Views/Query/QueryTabs.axaml (still referenced by old system)
- Views/Query/QueryTabStrip.axaml (still referenced by old system)
- Views/TableEditor.axaml (replaced by TableEditView.axaml)
- Views/TableStructure.axaml (replaced by StructureView.axaml)

---

## Files Created/Modified

| File | Status | Notes |
|------|--------|-------|
| Models/WorkspaceTabType.cs | ? Created | New enum |
| ViewModels/Tabs/WorkspaceTabViewModel.cs | ? Created | Base class |
| ViewModels/Tabs/QueryTabViewModel.cs | ? Created | Moved from ViewModels |
| ViewModels/Tabs/TableEditTabViewModel.cs | ? Created | New, replaces TableEditorViewModel |
| ViewModels/Tabs/StructureTabViewModel.cs | ? Created | New, replaces TableStructureViewModel |
| ViewModels/WorkspaceTabsViewModel.cs | ? Created | Replaces QueryTabsViewModel |
| Ui/Icons/StudioIcons.cs | ? Updated | New icons |
| ViewModels/ApplicationViewModel.cs | ? Updated | Added WorkspaceTabsVm |
| ViewModels/DatabaseExplorerViewModel.cs | ? Updated | Uses WorkspaceTabsVm |
| Views/Workspace/WorkspaceTabStrip.axaml | ? Created | New tab strip |
| Views/Workspace/WorkspaceTabs.axaml | ? Created | New container |
| Views/Workspace/TableEditView.axaml | ? Created | New view |
| Views/Workspace/StructureView.axaml | ? Created | New view |
| Views/DatabaseExplorer.axaml | ? Updated | New menu items |
| Views/MainWindow.axaml | ? Updated | Uses WorkspaceTabs |
| Tests/ViewModels/DatabaseExplorerViewModelTests.cs | ? Updated | New commands |
| Tests/ViewModels/QueryTabViewModelTests.cs | ? Updated | New namespace |

---

## Build Status
? **Build Successful**

New unified tabs system is fully functional!
