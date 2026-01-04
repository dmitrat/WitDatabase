# Test Plan - Phase 1: Foundation & Connection Dialog

**Project:** WitDatabase Studio  
**Phase:** 1 - Foundation (Basic UI, Connection Dialog, Settings)  
**Status:** Complete  
**Last Updated:** 2026-01-04

---

## Test Objectives

Verify that Phase 1 deliverables are working correctly:
- Main window displays correctly
- Connection dialogs (Create and Open) function properly
- File-based and In-Memory databases can be created
- Encryption and advanced settings work
- Settings are persisted

---

## Test Environment

- **OS:** Windows 10/11
- **Framework:** .NET 10
- **UI Framework:** Avalonia 11.x
- **Build Configuration:** Debug/Release

---

## Test Cases

### TC1.1: Application Startup

**Objective:** Verify application starts correctly

**Steps:**
1. Build the solution in Debug mode
2. Run `OutWit.Database.Studio` project
3. Observe main window

**Expected Results:**
- ? Main window opens without errors
- ? Window title is "WitDatabase Studio"
- ? Status bar shows "Ready"
- ? Menu bar has File, Tools, Help menus
- ? Toolbar has New Database, Open Database, Close Database buttons
- ? Database Explorer panel is visible on the left (empty)
- ? Main content area is visible on the right (empty)

**Pass Criteria:** All expected results are met

---

### TC1.2: Create Database Dialog - File-Based

**Objective:** Verify Create Database dialog opens and displays correctly

**Steps:**
1. Click "New Database" button in toolbar (or File ? New Database)
2. Observe the Create Database dialog

**Expected Results:**
- ? Dialog opens with title "Create New Database"
- ? Two tabs visible: "General" and "Advanced"
- ? General tab is selected by default
- ? Storage Type: ComboBox with "File-based database" selected
- ? Database File: TextBox (empty) + "Browse..." button visible
- ? Storage Engine: ComboBox with "btree" selected
- ? "Enable database encryption (AES-GCM)" checkbox unchecked
- ? "Create" button is DISABLED (no file path yet)
- ? "Cancel" button is ENABLED

**Pass Criteria:** All expected results are met

---

### TC1.3: Browse File Dialog

**Objective:** Verify file selection through Browse button

**Steps:**
1. Open Create Database dialog
2. Click "Browse..." button
3. In the Save File dialog:
   - Navigate to desired location (e.g., C:\Temp)
   - Enter filename: "test_database.witdb"
   - Click "Save"
4. Observe the Database File TextBox

**Expected Results:**
- ? Save File dialog opens with title "Create New Database"
- ? Default extension is ".witdb"
- ? After clicking Save:
  - Database File TextBox shows selected path (e.g., "C:\Temp\test_database.witdb")
  - "Create" button becomes ENABLED

**Pass Criteria:** File path appears in TextBox, Create button enabled

---

### TC1.4: Create File-Based Database (Basic)

**Objective:** Create a simple file-based database with default settings

**Steps:**
1. Open Create Database dialog
2. Click "Browse...", select path: "C:\Temp\test_basic.witdb"
3. Leave all other settings as default:
   - Storage Engine: btree
   - Encryption: unchecked
   - Advanced settings: default
4. Click "Create"

**Expected Results:**
- ? Dialog shows "Creating database..." progress indicator
- ? Dialog closes after 1-2 seconds
- ? Main window status bar shows "Connected to C:\Temp\test_basic.witdb"
- ? Database Explorer shows database node with name "test_basic.witdb"
- ? Database node is expanded by default
- ? Folders visible: Tables, Views, Indexes
- ? All folders are empty (no items)
- ? File exists on disk: C:\Temp\test_basic.witdb

**Pass Criteria:** Database created, visible in explorer, file exists on disk

---

### TC1.5: Create File-Based Database (With Encryption)

**Objective:** Create encrypted database

**Steps:**
1. Open Create Database dialog
2. Browse to: "C:\Temp\test_encrypted.witdb"
3. Check "Enable database encryption (AES-GCM)"
4. Password field appears
5. Enter password: "MySecurePass123!"
6. Click "Create"

**Expected Results:**
- ? Password field appears when encryption checkbox is checked
- ? Warning message visible: "? Warning: Password cannot be recovered if lost"
- ? Create button enabled after entering password
- ? Database created successfully
- ? Database appears in explorer
- ? File on disk is encrypted (cannot be opened without password)

**Pass Criteria:** Encrypted database created, password required

---

### TC1.6: Create InMemory Database

**Objective:** Create in-memory database

**Steps:**
1. Open Create Database dialog
2. Change Storage Type to "In-Memory database"
3. Observe UI changes
4. Click "Create"

**Expected Results:**
- ? Database File field becomes HIDDEN
- ? Browse button becomes HIDDEN
- ? Storage Engine remains visible (btree/lsm)
- ? Create button is ENABLED immediately (no file path needed)
- ? After clicking Create:
  - Dialog closes
  - Status bar shows "Connected to :memory:"
  - Database Explorer shows ":memory:" node
  - Folders visible: Tables, Views, Indexes

**Pass Criteria:** InMemory database created without file path

---

### TC1.7: Advanced Settings - Page Size

**Objective:** Verify advanced settings work correctly

**Steps:**
1. Open Create Database dialog
2. Browse to: "C:\Temp\test_advanced.witdb"
3. Switch to "Advanced" tab
4. Observe default settings
5. Change Page Size to 8192
6. Change Cache Size to 2000
7. Click "Create"

**Expected Results:**
- ? Advanced tab contains:
  - Page Size: ComboBox [512, 1024, 2048, 4096, 8192, 16384, 32768]
  - Cache Size: NumericUpDown (10-100,000)
  - Enable ACID transactions: checked
  - Enable MVCC: checked (indented under transactions)
  - Enable file locking: checked
- ? Helpful hints visible under each setting
- ? Database created with custom settings
- ? Settings applied correctly (can be verified in database file metadata)

**Pass Criteria:** Advanced settings apply correctly

---

### TC1.8: Advanced Settings - Transactions

**Objective:** Verify transaction settings work correctly

**Steps:**
1. Open Create Database dialog
2. Browse to: "C:\Temp\test_no_mvcc.witdb"
3. Switch to "Advanced" tab
4. Uncheck "Enable MVCC"
5. Observe MVCC checkbox state
6. Click "Create"

**Expected Results:**
- ? MVCC checkbox is indented (shows dependency on Transactions)
- ? MVCC hint is visible when Transactions enabled
- ? When MVCC unchecked:
  - Transactions still enabled
  - MVCC disabled
- ? Database created successfully

**Steps (Part 2):**
7. Create another database: "test_no_transactions.witdb"
8. Uncheck "Enable ACID transactions"
9. Observe MVCC checkbox

**Expected Results (Part 2):**
- ? MVCC checkbox becomes DISABLED when Transactions unchecked
- ? MVCC hint becomes hidden
- ? Database created without transaction support

**Pass Criteria:** Transaction settings apply correctly, MVCC depends on Transactions

---

### TC1.9: Open Existing Database

**Objective:** Open existing file-based database

**Steps:**
1. Close current database (if any)
2. Click "Open Database" button
3. Observe Open Database dialog
4. Click "Browse..."
5. Select existing database: "C:\Temp\test_basic.witdb"
6. Click "Open"

**Expected Results:**
- ? Open Database dialog has simpler UI (no advanced settings)
- ? After selecting file:
  - File path appears in TextBox
  - "Open" button enabled
- ? If database is encrypted:
  - Encryption checkbox auto-checked
  - Password field appears
- ? After clicking Open:
  - Database loads successfully
  - Explorer shows database structure

**Pass Criteria:** Existing database opens correctly

---

### TC1.10: Open Encrypted Database

**Objective:** Open encrypted database with password

**Steps:**
1. Click "Open Database"
2. Browse to encrypted database: "C:\Temp\test_encrypted.witdb"
3. Observe password field appears automatically
4. Enter WRONG password: "WrongPassword"
5. Click "Open"
6. Observe error message
7. Enter CORRECT password: "MySecurePass123!"
8. Click "Open"

**Expected Results:**
- ? Encryption checkbox auto-checked when encrypted database detected
- ? Password field appears automatically
- ? With wrong password:
  - Error message: "Connection error: Invalid password" (or similar)
  - Dialog remains open
- ? With correct password:
  - Database opens successfully
  - Explorer shows structure

**Pass Criteria:** Encrypted database requires correct password

---

### TC1.11: Recent Files List

**Objective:** Verify recent files are saved and loaded

**Steps:**
1. Create/Open several databases:
   - test_basic.witdb
   - test_encrypted.witdb
   - test_advanced.witdb
2. Close application
3. Restart application
4. Open File menu (or Recent Files dropdown if implemented)

**Expected Results:**
- ? Recent files list contains opened databases
- ? List shows file paths (not :memory: databases)
- ? List persisted across application restarts
- ? Clicking recent file opens database

**Pass Criteria:** Recent files saved and restored correctly

---

### TC1.12: Cancel Dialog

**Objective:** Verify Cancel button works

**Steps:**
1. Click "New Database"
2. Fill in some fields (file path, password, etc.)
3. Click "Cancel"

**Expected Results:**
- ? Dialog closes immediately
- ? No database created
- ? Main window unchanged
- ? Status bar unchanged

**Pass Criteria:** Cancel aborts operation cleanly

---

### TC1.13: Validation - Missing File Path

**Objective:** Verify validation for required fields

**Steps:**
1. Open Create Database dialog
2. Leave Database File empty
3. Observe Create button state
4. Try to bypass validation (if possible)

**Expected Results:**
- ? Create button is DISABLED when file path empty
- ? Cannot create database without file path

**Pass Criteria:** Validation prevents invalid input

---

### TC1.14: Validation - Encryption Without Password

**Objective:** Verify encryption requires password

**Steps:**
1. Open Create Database dialog
2. Browse to file
3. Check "Enable encryption"
4. Leave password empty
5. Observe Create button

**Expected Results:**
- ? Create button DISABLED when encryption checked but password empty
- ? Button enables after entering password

**Pass Criteria:** Encrypted database requires password

---

### TC1.15: Storage Engine Selection

**Objective:** Verify storage engine options work

**Steps:**
1. Create database with btree engine: "test_btree.witdb"
2. Create database with lsm engine: "test_lsm.witdb"
3. Verify both databases work

**Expected Results:**
- ? Both btree and lsm engines available in dropdown
- ? Hint texts visible:
  - "• btree: Balanced read/write performance"
  - "• lsm: Optimized for write-heavy workloads"
- ? Both databases create successfully
- ? Storage engine persisted in database metadata

**Pass Criteria:** Both engines work correctly

---

## Test Summary Template

| Test Case | Status | Notes |
|-----------|--------|-------|
| TC1.1: Application Startup | ? | |
| TC1.2: Create Database Dialog | ? | |
| TC1.3: Browse File Dialog | ? | |
| TC1.4: Create File-Based Database | ? | |
| TC1.5: Create Encrypted Database | ? | |
| TC1.6: Create InMemory Database | ? | |
| TC1.7: Advanced Settings - Page Size | ? | |
| TC1.8: Advanced Settings - Transactions | ? | |
| TC1.9: Open Existing Database | ? | |
| TC1.10: Open Encrypted Database | ? | |
| TC1.11: Recent Files List | ? | |
| TC1.12: Cancel Dialog | ? | |
| TC1.13: Validation - Missing File Path | ? | |
| TC1.14: Validation - Encryption Password | ? | |
| TC1.15: Storage Engine Selection | ? | |

**Legend:** ? Pass | ? Fail | ?? Partial | ? Not Tested

---

## Known Issues

1. ~~FilePath not updating in TextBox after Browse~~ - **FIXED**
2. ~~Create button not enabling for InMemory~~ - **FIXED**
3. ~~InMemory database not appearing in tree~~ - **FIXED**
4. ~~Databases not creating/appearing in tree~~ - **FIXED**
5. ~~PlatformImpl is null error~~ - **FIXED**
   - Missing InitializeComponent() in UserControl constructors
   - Added to DatabaseExplorer and TableStructure
6. **CURRENT:** All known issues resolved! ??
   - Ready for testing Phase 1

---

## Test Data

**Test Files Location:** `C:\Temp\WitDatabase_Tests\`

**Test Databases:**
- `test_basic.witdb` - Simple database, no encryption
- `test_encrypted.witdb` - Encrypted, password: "MySecurePass123!"
- `test_advanced.witdb` - Custom page size (8192), cache (2000)
- `test_btree.witdb` - BTree engine
- `test_lsm.witdb` - LSM engine
- `test_no_mvcc.witdb` - Transactions without MVCC
- `test_no_transactions.witdb` - No transactions

---

## Cleanup

After testing, delete test files:
```powershell
Remove-Item -Path "C:\Temp\WitDatabase_Tests\" -Recurse -Force
