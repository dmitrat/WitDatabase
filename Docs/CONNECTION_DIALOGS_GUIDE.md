# Connection Dialogs Guide

## Overview

WitDatabase Studio has two separate dialogs for working with databases:

1. **Create Database Dialog** - For creating new databases
2. **Open Database Dialog** - For opening existing databases

---

## Create Database Dialog

### Purpose
Create a new WitDatabase file with customizable settings.

### Features

#### Storage Type
- **File-based**: Creates a .witdb file on disk
  - Requires file path selection via Browse button
  - Supports file locking for multi-process access
- **In-Memory**: Creates a database in RAM
  - No file path needed
  - Fast but data lost on application exit
  - Useful for temporary/testing scenarios

#### Storage Engine
- **btree** (default): B-Tree storage engine
  - Good for general-purpose use
  - Balanced read/write performance
- **lsm**: Log-Structured Merge Tree
  - Optimized for write-heavy workloads
  - Better compression

#### Encryption
- **Disabled** (default): No encryption
- **Enabled**: AES-GCM encryption
  - Requires password
  - Password cannot be recovered if lost

#### Advanced Settings (Collapsible)

**Page Size** (bytes)
- Options: 512, 1024, 2048, **4096 (default)**, 8192, 16384, 32768
- Larger pages = better for larger records
- Smaller pages = better memory efficiency
- **Cannot be changed after database creation**

**Cache Size** (pages)
- Range: 10 - 100,000
- Default: 1,000
- More cache = faster queries but more RAM usage
- Formula: Memory = PageSize × CacheSize

**Enable ACID Transactions**
- Default: Enabled
- Provides atomicity, consistency, isolation, durability
- Recommended for production use

**Enable MVCC**
- Default: Enabled (requires transactions)
- Multi-Version Concurrency Control
- Better read performance with concurrent writes
- Requires more disk space

**Enable File Locking**
- Default: Enabled
- Multi-process safety
- Only available for file-based databases
- Prevents corruption from concurrent access

### Usage

```csharp
// In MainWindowViewModel
private async void NewDatabase()
{
    var result = await ApplicationVm.ConnectionVm.ShowCreateDialogAsync();
    
    if (result && ApplicationVm.ConnectionVm.SelectedConnection != null)
    {
        // Database created and connected
    }
}
```

---

## Open Database Dialog

### Purpose
Open an existing WitDatabase file.

### Features

#### File Selection
- Browse button to select .witdb file
- Supports .witdb and .db extensions
- Recent files list (TODO)

#### Auto-Detection
- Automatically detects:
  - Whether database is encrypted
  - Storage engine used (btree/lsm)
- No need to remember settings

#### Encryption
- If database is encrypted:
  - Checkbox is automatically checked
  - Password field becomes visible
  - Enter password to open

#### Storage Engine
- Detected automatically from file
- Can be overridden if needed
- Usually no need to change

#### Read-Only Mode
- Opens database in read-only mode
- Prevents accidental modifications
- Useful for viewing/analyzing data

### Usage

```csharp
// In MainWindowViewModel
private async Task OpenDatabaseAsync()
{
    var result = await ApplicationVm.ConnectionVm.ShowOpenDialogAsync();
    
    if (result && ApplicationVm.ConnectionVm.SelectedConnection != null)
    {
        // Database opened and connected
    }
}
```

---

## Implementation Details

### ViewModel: ConnectionViewModel

**Properties:**
- `IsNewDatabase` - true for Create, false for Open
- `StorageType` - 0 = File-based, 1 = In-Memory
- `IsFileBased` - computed from StorageType
- `ConnectionInfo` - connection settings
- `SelectedStorageEngine` - btree or lsm
- `PageSizeOptions` - list of valid page sizes
- `SelectedPageSize` - selected page size (default: 4096)
- `CacheSize` - cache size in pages (default: 1000)
- `EnableTransactions` - enable ACID (default: true)
- `EnableMvcc` - enable MVCC (default: true)
- `EnableFileLocking` - enable file locking (default: true)

**Commands:**
- `BrowseFileCommand` - opens file picker (Save for create, Open for open)
- `ConnectCommand` - creates/opens database
- `CancelCommand` - closes dialog

**Methods:**
- `ShowCreateDialogAsync()` - shows CreateDatabaseDialog
- `ShowOpenDialogAsync()` - shows OpenDatabaseDialog

### Views

**CreateDatabaseDialog.axaml:**
- Header with title and description
- Storage type selection (File/In-Memory)
- File path + Browse (only for file-based)
- Storage engine dropdown
- Encryption checkbox + password field
- Advanced settings expander
  - Page size dropdown
  - Cache size numeric up/down
  - Transaction checkboxes
  - File locking checkbox (only for file-based)
- Error message panel
- Cancel and Create buttons

**OpenDatabaseDialog.axaml:**
- Header with title and description
- File path + Browse button
- Encryption checkbox (auto-detected)
- Password field (visible if encrypted)
- Storage engine dropdown (auto-detected)
- Read-only mode checkbox
- Error message panel
- Cancel and Open buttons

---

## Error Handling

Both dialogs display errors in a yellow panel:
- Invalid file path
- Wrong password
- Missing required fields
- Database creation/connection errors

Errors are shown inline without modal popups.

---

## Best Practices

### For Create Dialog

1. **Always backup encryption password** - cannot be recovered
2. **Choose page size carefully** - cannot be changed later
3. **Use default settings** for most cases
4. **Enable MVCC** for concurrent access scenarios
5. **Use In-Memory** only for temporary data

### For Open Dialog

1. **Use Read-Only mode** when viewing/analyzing data
2. **Keep password secure** - no "remember password" option
3. **Let auto-detection work** - usually correct
4. **Close database** when done to release file lock

---

## Testing

See `ConnectionViewModelTests.cs` for comprehensive test coverage:
- 45 tests covering all scenarios
- Initialization tests
- Command tests
- Storage type tests
- Dialog properties tests
- Advanced settings tests
- Error handling tests

---

## Future Enhancements

- [ ] Recent files list in Open Dialog
- [ ] Connection profiles (saved connection strings)
- [ ] Database info display (size, page count, etc.)
- [ ] Validate file path before creating
- [ ] Show recommended settings based on use case
- [ ] Password strength indicator
- [ ] Backup before opening corrupted database

---

*Last updated: 2026-01-04*
