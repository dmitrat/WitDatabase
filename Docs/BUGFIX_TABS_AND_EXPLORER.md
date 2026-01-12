# Bug Fixes: Tabs and Database Explorer

**Date:** 2025-01-XX  
**Status:** ? Complete

---

## Issues Fixed

### ? Issue 1: Last Tab Close Creates New Tab
**Description:** When there's only one tab and user clicks X to close it, the tab closes but a new one is created with incremented number (Query 1 ? Query 2 ? Query 3...).

**Fix Applied:**
- `WorkspaceTabsViewModel.CloseTab()`: Added check `if (Tabs.Count <= 1) return;`
- `WorkspaceTabsViewModel.UpdateStatus()`: `CanCloseTab` is now false when only 1 tab exists
- `WorkspaceTabsViewModel.CloseAllTabs()`: Keeps at least one tab
- `WorkspaceTabStrip.axaml`: Close button uses `IsEnabled="{Binding ...CanCloseTab}"` with disabled style

**Files Modified:**
- `ViewModels/WorkspaceTabsViewModel.cs`
- `Views/Workspace/WorkspaceTabStrip.axaml`

---

### ? Issue 2: Horizontal ScrollBar Overlaps Tab Strip
**Description:** When tabs overflow, a standard horizontal scrollbar appears that covers half the tab panel.

**Fix Applied:**
- Replaced `ScrollViewer` with `HorizontalScrollBarVisibility="Hidden"`
- Added left/right arrow buttons for scrolling
- Buttons appear only when overflow occurs
- Code-behind handles scroll button visibility based on scroll position

**Files Modified:**
- `Views/Workspace/WorkspaceTabStrip.axaml`
- `Views/Workspace/WorkspaceTabStrip.axaml.cs`

---

### ? Issue 3: New Row Position After Commit
**Description:** After adding a row, filling data, and committing, the new row appears at position 2 instead of at the end.

**Fix Applied:**
- Added `BuildSelectStatement()` method that includes `ORDER BY` clause
- When loading data, if primary key columns exist, data is sorted by them
- This ensures consistent ordering after insert operations

**Files Modified:**
- `ViewModels/Tabs/TableEditTabViewModel.cs`

---

### ? Issue 4: NullReferenceException on Drop Object
**Description:** When selecting "Drop" for a table from Database Explorer context menu, application crashes with NullReferenceException at line 178.

**Fix Applied:**
- Save object name to local variable before clearing `SelectedNode`
- Clear `SelectedNode = null` before calling `RefreshAsync()`
- This prevents stale reference issues during refresh

**Files Modified:**
- `ViewModels/DatabaseExplorerViewModel.cs`

---

## Summary of Changes

| File | Changes |
|------|---------|
| `ViewModels/DatabaseExplorerViewModel.cs` | Fixed Drop to clear selection before refresh |
| `ViewModels/WorkspaceTabsViewModel.cs` | Prevent closing last tab, improved Close logic |
| `ViewModels/Tabs/TableEditTabViewModel.cs` | Added ORDER BY to SELECT for consistent ordering |
| `Views/Workspace/WorkspaceTabStrip.axaml` | Arrow scroll buttons, disabled close button state |
| `Views/Workspace/WorkspaceTabStrip.axaml.cs` | Scroll button visibility handling |

---

## Test Results
? All 162 tests passing
? Build successful
