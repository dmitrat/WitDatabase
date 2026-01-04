# Test Plan - Phase 2: Database Explorer

**Project:** WitDatabase Studio  
**Phase:** 2 - Database Explorer (TreeView, Schema Loading, Context Menus)  
**Status:** Complete  
**Last Updated:** 2026-01-04

---

## Test Objectives

Verify that Phase 2 deliverables are working correctly:
- Database Explorer TreeView displays schema
- Tables, Views, Indexes are loaded and displayed
- Table structure panel shows column details
- Context menu commands work
- Schema refresh works

---

## Test Environment

- **OS:** Windows 10/11
- **Framework:** .NET 10
- **UI Framework:** Avalonia 11.x
- **Build Configuration:** Debug/Release
- **Prerequisites:** Phase 1 tests passed

---

## Test Setup

Before running Phase 2 tests, create a test database with sample data:

```sql
-- Create test database: C:\Temp\test_schema.witdb

-- Tables
CREATE TABLE Customers (
    CustomerId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Email TEXT UNIQUE,
    CreatedDate TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE Orders (
    OrderId INTEGER PRIMARY KEY,
    CustomerId INTEGER NOT NULL,
    OrderDate TEXT NOT NULL,
    TotalAmount REAL DEFAULT 0.0,
    FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId)
);

CREATE TABLE Products (
    ProductId INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    Price REAL NOT NULL,
    Stock INTEGER DEFAULT 0
);

-- Views
CREATE VIEW CustomerOrders AS
SELECT c.Name, c.Email, o.OrderId, o.OrderDate, o.TotalAmount
FROM Customers c
JOIN Orders o ON c.CustomerId = o.CustomerId;

CREATE VIEW ProductInventory AS
SELECT ProductId, Name, Price, Stock,
       Price * Stock AS TotalValue
FROM Products;

-- Indexes
CREATE INDEX idx_customers_email ON Customers(Email);
CREATE INDEX idx_orders_customer ON Orders(CustomerId);
CREATE INDEX idx_orders_date ON Orders(OrderDate);
CREATE UNIQUE INDEX idx_products_name ON Products(Name);
```

---

## Test Cases

### TC2.1: Database Explorer - Initial Display

**Objective:** Verify Database Explorer displays correctly after connection

**Steps:**
1. Open database: "C:\Temp\test_schema.witdb"
2. Observe Database Explorer panel (left side)

**Expected Results:**
- ? Database Explorer panel visible on left
- ? TreeView contains root node: "test_schema.witdb" (??? icon)
- ? Root node is expanded by default
- ? Three folder nodes visible:
  - ?? Tables (with count: "Tables (3)")
  - ??? Views (with count: "Views (2)")
  - ?? Indexes (with count: "Indexes (4)")
- ? All folders collapsed by default
- ? No loading overlay visible

**Pass Criteria:** TreeView displays correct structure

---

### TC2.2: Expand Tables Folder

**Objective:** Verify Tables folder expands and shows tables

**Steps:**
1. Open database with test schema
2. Click on "Tables" folder node (or expand arrow)
3. Observe table nodes

**Expected Results:**
- ? Tables folder expands
- ? Three table nodes visible:
  - ?? Customers
  - ?? Orders
  - ?? Products
- ? Tables sorted alphabetically
- ? Icons consistent (?? for all tables)

**Pass Criteria:** All tables displayed correctly

---

### TC2.3: Expand Views Folder

**Objective:** Verify Views folder expands and shows views

**Steps:**
1. Expand "Views" folder
2. Observe view nodes

**Expected Results:**
- ? Views folder expands
- ? Two view nodes visible:
  - ??? CustomerOrders
  - ??? ProductInventory
- ? Views sorted alphabetically
- ? Icons consistent (??? for all views)

**Pass Criteria:** All views displayed correctly

---

### TC2.4: Expand Indexes Folder

**Objective:** Verify Indexes folder expands and shows indexes

**Steps:**
1. Expand "Indexes" folder
2. Observe index nodes

**Expected Results:**
- ? Indexes folder expands
- ? Four index nodes visible:
  - ?? idx_customers_email
  - ?? idx_orders_customer
  - ?? idx_orders_date
  - ?? idx_products_name
- ? Indexes sorted alphabetically
- ? Icons consistent (?? for all indexes)

**Pass Criteria:** All indexes displayed correctly

---

### TC2.5: Table Structure Panel - Select Table

**Objective:** Verify table structure displays when table selected

**Steps:**
1. Expand Tables folder
2. Click on "Customers" table node
3. Observe Table Structure panel (right side, below toolbar)

**Expected Results:**
- ? Table Structure panel appears
- ? Panel title: "Table: Customers"
- ? Column list displays 4 columns:
  
  | Column | Type | Nullable | PK | Default |
  |--------|------|----------|----|---------||
  | CustomerId | INTEGER | No | ?? | - |
  | Name | TEXT | No | | - |
  | Email | TEXT | Yes | | - |
  | CreatedDate | TEXT | Yes | | CURRENT_TIMESTAMP |

- ? Primary key indicator (??) visible
- ? Default values displayed
- ? Nullable/Not Nullable correctly shown

**Pass Criteria:** Table structure displays correctly

---

### TC2.6: Table Structure Panel - Different Tables

**Objective:** Verify structure panel updates for different tables

**Steps:**
1. Click "Customers" table ? observe structure
2. Click "Orders" table ? observe structure
3. Click "Products" table ? observe structure

**Expected Results:**
- ? Structure panel updates for each table
- ? **Orders** table shows:
  - CustomerId with FK indicator (if implemented)
  - TotalAmount with default 0.0
- ? **Products** table shows:
  - Stock with default 0
- ? Each table shows correct columns

**Pass Criteria:** Structure panel updates correctly for each table

---

### TC2.7: Table Structure Panel - View Selection

**Objective:** Verify selecting view clears/hides structure panel

**Steps:**
1. Select "Customers" table (structure visible)
2. Click "CustomerOrders" view
3. Observe structure panel

**Expected Results:**
- ? Structure panel either:
  - Clears and shows "Select a table to view structure"
  - OR shows view structure (if implemented)
  - OR hides completely

**Pass Criteria:** UI handles view selection gracefully

---

### TC2.8: Context Menu - Browse Data (Table)

**Objective:** Verify Browse Data command for tables

**Steps:**
1. Right-click on "Customers" table
2. Observe context menu
3. Click "Browse Data"
4. Observe Query Editor (if Phase 3 implemented) or result

**Expected Results:**
- ? Context menu appears with options:
  - ?? Browse Data
  - ?? Refresh
  - ??? Drop
- ? "Browse Data" option enabled
- ? After clicking "Browse Data":
  - Query Editor (if available) shows: `SELECT * FROM Customers LIMIT 100`
  - OR Data grid displays table data
- ? Status bar shows action

**Pass Criteria:** Browse Data generates correct SQL

---

### TC2.9: Context Menu - Browse Data (View)

**Objective:** Verify Browse Data command for views

**Steps:**
1. Right-click on "CustomerOrders" view
2. Click "Browse Data"

**Expected Results:**
- ? Context menu appears
- ? "Browse Data" enabled for views
- ? Query generated: `SELECT * FROM CustomerOrders LIMIT 100`
- ? View data displayed

**Pass Criteria:** Browse Data works for views

---

### TC2.10: Context Menu - View Definition (View)

**Objective:** Verify View Definition command

**Steps:**
1. Right-click on "CustomerOrders" view
2. Observe context menu options
3. Click "View Definition"

**Expected Results:**
- ? Context menu shows "View Definition" option
- ? "View Definition" enabled for views only
- ? After clicking:
  - Dialog or panel shows SQL definition:
    ```sql
    CREATE VIEW CustomerOrders AS
    SELECT c.Name, c.Email, o.OrderId, o.OrderDate, o.TotalAmount
    FROM Customers c
    JOIN Orders o ON c.CustomerId = o.CustomerId;
    ```
- ? SQL displayed in read-only format

**Pass Criteria:** View definition displays correctly

---

### TC2.11: Context Menu - Drop Table

**Objective:** Verify Drop command for tables (with confirmation)

**Steps:**
1. Right-click on "Products" table
2. Click "Drop"
3. Observe confirmation dialog
4. Click "Yes" to confirm

**Expected Results:**
- ? Context menu shows "Drop" option (???)
- ? Confirmation dialog appears:
  - Title: "Confirm Drop"
  - Message: "Are you sure you want to drop table 'Products'? This action cannot be undone."
  - Buttons: Yes, No
- ? After clicking Yes:
  - Table deleted from database
  - "Products" node removed from TreeView
  - Tables folder count updates: "Tables (2)"
  - Status bar: "Table 'Products' dropped successfully"

**Pass Criteria:** Drop command works with confirmation

---

### TC2.12: Context Menu - Drop Index

**Objective:** Verify Drop command for indexes

**Steps:**
1. Right-click on "idx_products_name" index
2. Click "Drop"
3. Confirm action

**Expected Results:**
- ? Confirmation dialog appears
- ? After confirmation:
  - Index deleted
  - Node removed from TreeView
  - Indexes count updates: "Indexes (3)"

**Pass Criteria:** Index drop works correctly

---

### TC2.13: Context Menu - Refresh Schema

**Objective:** Verify Refresh command reloads schema

**Steps:**
1. Open database
2. Externally modify database (add table using SQL editor or external tool):
   ```sql
   CREATE TABLE NewTable (Id INTEGER PRIMARY KEY, Data TEXT);
   ```
3. Right-click on database root node
4. Click "Refresh"

**Expected Results:**
- ? "Refresh" option visible in context menu (??)
- ? After clicking Refresh:
  - Loading overlay briefly appears
  - TreeView reloads
  - New table "NewTable" appears in Tables folder
  - Tables count updates: "Tables (4)"
  - Status bar: "Schema refreshed"

**Pass Criteria:** Refresh detects external changes

---

### TC2.14: Refresh Button in Toolbar

**Objective:** Verify toolbar Refresh button

**Steps:**
1. Open database
2. Click Refresh button in main toolbar
3. Observe behavior

**Expected Results:**
- ? Refresh button visible in toolbar
- ? Clicking button refreshes entire schema
- ? Same behavior as context menu Refresh

**Pass Criteria:** Toolbar refresh works

---

### TC2.15: Empty Database Display

**Objective:** Verify UI for empty database

**Steps:**
1. Create new empty database: "empty_test.witdb"
2. Observe Database Explorer

**Expected Results:**
- ? Database node visible: "empty_test.witdb"
- ? Folders visible but with (0) counts:
  - Tables (0)
  - Views (0)
  - Indexes (0)
- ? Expanding folders shows no children
- ? Empty state message: "No tables found" (or similar)

**Pass Criteria:** Empty database handled gracefully

---

### TC2.16: Large Schema Performance

**Objective:** Verify performance with many objects

**Preparation:**
```sql
-- Create 100 tables
CREATE TABLE Table_001 (Id INTEGER PRIMARY KEY, Data TEXT);
CREATE TABLE Table_002 (Id INTEGER PRIMARY KEY, Data TEXT);
-- ... (repeat up to Table_100)

-- Create 50 indexes
CREATE INDEX idx_001 ON Table_001(Data);
-- ... (repeat)
```

**Steps:**
1. Open database with 100+ objects
2. Observe load time
3. Expand folders
4. Navigate between nodes

**Expected Results:**
- ? Database loads in < 5 seconds
- ? TreeView renders smoothly
- ? Scrolling is responsive
- ? No UI freezing

**Pass Criteria:** Good performance with large schemas

---

### TC2.17: Node Selection State

**Objective:** Verify selection state is maintained

**Steps:**
1. Select "Customers" table
2. Expand "Views" folder
3. Select "CustomerOrders" view
4. Observe selection highlighting

**Expected Results:**
- ? Selected node highlighted with accent color
- ? Only one node selected at a time
- ? Previous selection cleared when new node selected
- ? Structure panel updates with selection

**Pass Criteria:** Selection behaves correctly

---

### TC2.18: Keyboard Navigation

**Objective:** Verify keyboard navigation in TreeView

**Steps:**
1. Click on database root node
2. Press ? key multiple times
3. Press ? to expand folders
4. Press ? to collapse folders
5. Press Enter on table node

**Expected Results:**
- ? Arrow keys navigate through nodes
- ? ? expands collapsed nodes
- ? ? collapses expanded nodes
- ? Enter key activates selected node (opens structure)
- ? Tab key moves focus to next control

**Pass Criteria:** Keyboard navigation works smoothly

---

### TC2.19: Error Handling - Corrupted Database

**Objective:** Verify error handling for database errors

**Steps:**
1. Open database
2. Manually corrupt database file (overwrite with random bytes)
3. Click Refresh in Database Explorer

**Expected Results:**
- ? Error message displayed in UI
- ? Error message: "Failed to load schema: [error details]"
- ? TreeView shows error state
- ? Application doesn't crash
- ? User can close database and open another

**Pass Criteria:** Errors handled gracefully

---

### TC2.20: Context Menu - Disabled Commands

**Objective:** Verify commands are enabled/disabled appropriately

**Steps:**
1. Right-click on different node types
2. Observe which commands are available

**Expected Results:**
- ? **Database node:** Refresh, Drop (database) - if implemented
- ? **Tables folder:** Refresh
- ? **Table node:** Browse Data, Refresh, Drop
- ? **Views folder:** Refresh
- ? **View node:** Browse Data, View Definition, Drop
- ? **Indexes folder:** Refresh
- ? **Index node:** Drop
- ? Inappropriate commands disabled (grayed out)

**Pass Criteria:** Context menus contextual and correct

---

## Test Summary Template

| Test Case | Status | Notes |
|-----------|--------|-------|
| TC2.1: Database Explorer Display | ? | |
| TC2.2: Expand Tables | ? | |
| TC2.3: Expand Views | ? | |
| TC2.4: Expand Indexes | ? | |
| TC2.5: Table Structure - Select Table | ? | |
| TC2.6: Table Structure - Different Tables | ? | |
| TC2.7: Table Structure - View Selection | ? | |
| TC2.8: Context Menu - Browse Data (Table) | ? | |
| TC2.9: Context Menu - Browse Data (View) | ? | |
| TC2.10: Context Menu - View Definition | ? | |
| TC2.11: Context Menu - Drop Table | ? | |
| TC2.12: Context Menu - Drop Index | ? | |
| TC2.13: Context Menu - Refresh | ? | |
| TC2.14: Toolbar Refresh | ? | |
| TC2.15: Empty Database | ? | |
| TC2.16: Large Schema Performance | ? | |
| TC2.17: Node Selection State | ? | |
| TC2.18: Keyboard Navigation | ? | |
| TC2.19: Error Handling | ? | |
| TC2.20: Context Menu - Disabled Commands | ? | |

**Legend:** ? Pass | ? Fail | ?? Partial | ? Not Tested

---

## Known Issues

1. ~~Table structure not loading~~ - **FIXED**
2. ~~Context menu not appearing~~ - **FIXED**
3. **Current:** Need to verify all commands work end-to-end

---

## Test Data Files

**Location:** `C:\Temp\WitDatabase_Tests\Phase2\`

**Files:**
- `test_schema.witdb` - Database with sample schema (tables, views, indexes)
- `empty_test.witdb` - Empty database
- `large_schema.witdb` - Database with 100+ objects

**SQL Script:** `test_schema_setup.sql` (included above)

---

## Performance Benchmarks

| Operation | Expected Time | Acceptable Range |
|-----------|---------------|------------------|
| Load schema (10 tables) | < 500ms | < 1s |
| Load schema (100 tables) | < 2s | < 5s |
| Expand folder | < 100ms | < 200ms |
| Load table structure | < 200ms | < 500ms |
| Refresh schema | < 1s | < 3s |

---

## Cleanup

```powershell
Remove-Item -Path "C:\Temp\WitDatabase_Tests\Phase2\" -Recurse -Force
```
