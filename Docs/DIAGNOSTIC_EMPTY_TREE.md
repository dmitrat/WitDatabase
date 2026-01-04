# Quick Diagnostic Guide - Empty Database Explorer

**Issue:** Database Explorer tree remains empty after successful connection  
**Symptoms:**
- Status bar shows "Connected: True"
- File path is displayed: "C:\Workspace\test\database22.witdb"  
- TreeView panel is completely empty (no nodes visible)

---

## Diagnostic Steps

### Step 1: Check Logs

**Action:** View Output window in Visual Studio

**Location:** View ? Output ? Show output from: Debug

**Look for:**
```
[Info] NewDatabase command invoked
[Info] ShowCreateDialogAsync returned: True
[Info] Setting CurrentConnection to: C:\Workspace\test\database22.witdb
[Info] Calling DatabaseExplorerVm.RefreshAsync()
[Info] RefreshAsync called. IsConnected: True/False  ? CHECK THIS
[Info] Starting schema load...
[Info] Database name: database22
[Info] Loading tables...
[Info] Loaded 0 tables
[Info] Loading views...
[Info] Loaded 0 views
[Info] Loading indexes...
[Info] Loaded 0 indexes
[Info] Setting Nodes collection with 1 root nodes
[Info] Nodes.Count after assignment: 1
[Info] RefreshAsync completed. IsLoading: False, Nodes.Count: 1
```

**Key Check Points:**
1. **Is `RefreshAsync called`?** - If NO, problem in MainWindowViewModel
2. **Is `IsConnected: True`?** - If NO, connection failed
3. **Is `Nodes.Count after assignment: 1`?** - If NO, node creation failed
4. **Are there any exceptions?** - If YES, check error message

---

### Step 2: Check Connection Status

**In Code:** `Tools\OutWit.Database.Studio\Services\DatabaseService.cs`

**Check:**
```csharp
public bool IsConnected => m_connection?.State == ConnectionState.Open;
```

**Possible Issues:**
- ? `m_connection` is null
- ? Connection state is not Open
- ? Connection closed after creation

**Fix:** Ensure `ConnectAsync()` succeeds and connection remains open

---

### Step 3: Check UI Binding

**In XAML:** `Tools\OutWit.Database.Studio\Views\DatabaseExplorer.axaml`

**Check:**
```xml
<TreeView ItemsSource="{Binding DatabaseExplorerVm.Nodes}"
          SelectedItem="{Binding DatabaseExplorerVm.SelectedNode}">
```

**Possible Issues:**
- ? DataContext not set correctly
- ? Binding path incorrect
- ? ApplicationViewModel singleton not initialized

**Verify:**
```csharp
// In DatabaseExplorer.axaml.cs
public DatabaseExplorer()
{
    InitializeComponent();
    DataContext = ApplicationViewModel.Instance;  ? CHECK THIS
}
```

---

### Step 4: Check Nodes Collection

**In Code:** `Tools\OutWit.Database.Studio\ViewModels\DatabaseExplorerViewModel.cs`

**Check:**
```csharp
[Notify]
public List<DatabaseNode> Nodes { get; set; } = null!;
```

**Possible Issues:**
- ? `[Notify]` aspect not working
- ? PropertyChanged not firing
- ? Collection assigned but UI not updated

**Test:**
```csharp
// After assignment
m_logger.LogInformation("Nodes.Count: {Count}", Nodes.Count);
m_logger.LogInformation("Nodes[0].Name: {Name}", Nodes[0]?.Name);
m_logger.LogInformation("Nodes[0].Children.Count: {Count}", Nodes[0]?.Children.Count);
```

---

### Step 5: Manual Refresh Test

**Action:** Click Refresh button in Database Explorer header

**Expected:** Tree should populate

**If Tree Populates:**
- Problem is in automatic refresh call
- Check MainWindowViewModel.NewDatabase() logic

**If Tree Still Empty:**
- Problem is in RefreshAsync() or UI binding
- Check logs from Step 1

---

## Common Root Causes

### 1. Connection Not Established

**Symptom:** `IsConnected: False` in logs

**Cause:**
```csharp
// In ConnectionViewModel.ConnectAsync()
var success = await m_databaseService.ConnectAsync(ConnectionInfo);
if (!success) {
    // Connection failed but dialog closed anyway
}
```

**Fix:**
- Ensure `ConnectAsync()` returns true
- Check connection string is valid
- Verify database file exists and is not corrupt

### 2. Nodes Collection Not Updating UI

**Symptom:** `Nodes.Count: 1` in logs but TreeView empty

**Cause:**
- ObservableCollection not used
- PropertyChanged not firing
- UI not on correct thread

**Fix:**
```csharp
// Option 1: Use ObservableCollection
[Notify]
public ObservableCollection<DatabaseNode> Nodes { get; set; }

// Option 2: Create new List instance
Nodes = new List<DatabaseNode>(newNodes);

// Option 3: Ensure property changed fires
private List<DatabaseNode> m_nodes = new();
public List<DatabaseNode> Nodes
{
    get => m_nodes;
    set
    {
        if (m_nodes != value)
        {
            m_nodes = value;
            OnPropertyChanged(nameof(Nodes));
        }
    }
}
```

### 3. DataContext Not Set

**Symptom:** No logs, no errors, completely silent

**Cause:**
```csharp
// DatabaseExplorer.axaml.cs
public DatabaseExplorer()
{
    InitializeComponent();
    // DataContext = ApplicationViewModel.Instance; ? MISSING!
}
```

**Fix:**
```csharp
public DatabaseExplorer()
{
    InitializeComponent();
    DataContext = ApplicationViewModel.Instance;
}
```

### 4. Async/Await Issue

**Symptom:** RefreshAsync starts but never completes

**Cause:**
- Deadlock in async code
- Exception swallowed
- Task not awaited properly

**Fix:**
```csharp
// In MainWindowViewModel.NewDatabase()
try {
    await ApplicationVm.DatabaseExplorerVm.RefreshAsync();
    m_logger.LogInformation("RefreshAsync completed");  ? Add this
} catch (Exception ex) {
    m_logger.LogError(ex, "RefreshAsync failed");
}
```

---

## Quick Fixes

### Fix 1: Force UI Update

```csharp
// In DatabaseExplorerViewModel.RefreshAsync()
Nodes = newNodes;
OnPropertyChanged(nameof(Nodes));  // Manually trigger
await Task.Delay(10);  // Give UI time to update
```

### Fix 2: Use ObservableCollection

```csharp
// Change property type
[Notify]
public ObservableCollection<DatabaseNode> Nodes { get; set; } = new();

// In RefreshAsync
Nodes.Clear();
foreach (var node in newNodes)
{
    Nodes.Add(node);
}
```

### Fix 3: Check Thread

```csharp
// Ensure UI updates on UI thread
await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
{
    Nodes = newNodes;
});
```

---

## Test Commands

### Enable Detailed Logging

```csharp
// In appsettings.json or Program.cs
builder.Logging.SetMinimumLevel(LogLevel.Trace);
```

### Breakpoint Locations

1. **ConnectionViewModel.ConnectAsync()** - Line where `ConnectAsync` is called
2. **MainWindowViewModel.NewDatabase()** - Line where `RefreshAsync` is called  
3. **DatabaseExplorerViewModel.RefreshAsync()** - First line, check IsConnected
4. **DatabaseExplorerViewModel.RefreshAsync()** - Line where `Nodes = newNodes`

### Watch Variables

```
m_databaseService.IsConnected
Nodes.Count
newNodes.Count
rootNode.Children.Count
```

---

## Expected Output (Success Case)

```
[Info] NewDatabase command invoked
[Info] Database file created: C:\Workspace\test\database22.witdb
[Info] Attempting to connect to database: C:\Workspace\test\database22.witdb
[Info] Attempting to connect with connection string: Data Source=C:\Workspace\test\database22.witdb;Store=btree
[Info] Successfully connected to database: C:\Workspace\test\database22.witdb
[Info] ShowCreateDialogAsync returned: True
[Info] Setting CurrentConnection to: C:\Workspace\test\database22.witdb
[Info] Calling DatabaseExplorerVm.RefreshAsync()
[Info] RefreshAsync called. IsConnected: True
[Info] Starting schema load...
[Info] Database name: database22
[Info] Loading tables...
[Info] Loaded 0 tables
[Info] Loading views...
[Info] Loaded 0 views
[Info] Loading indexes...
[Info] Loaded 0 indexes
[Info] Setting Nodes collection with 1 root nodes
[Info] Nodes.Count after assignment: 1
[Info] RefreshAsync completed. IsLoading: False, Nodes.Count: 1
[Info] Database schema loaded for: C:\Workspace\test\database22.witdb
```

**UI Should Show:**
```
??? database22
  ?? Tables (0)
  ??? Views (0)
  ?? Indexes (0)
  ? Triggers (0)
  ?? Sequences (0)
```

---

## If All Else Fails

1. **Clean and Rebuild:**
   ```powershell
   dotnet clean
   dotnet build
   ```

2. **Delete bin/obj folders:**
   ```powershell
   Remove-Item -Recurse -Force .\bin\, .\obj\
   ```

3. **Restart Visual Studio**

4. **Check for Avalonia version conflicts:**
   ```xml
   <PackageReference Include="Avalonia" Version="11.x.x" />
   ```

5. **Try simple test:**
   ```csharp
   // In DatabaseExplorerViewModel constructor
   Nodes = new List<DatabaseNode>
   {
       new DatabaseNode { Name = "Test", NodeType = DatabaseNodeType.Database }
   };
   ```

If "Test" node appears ? Problem is in RefreshAsync logic  
If "Test" node doesn't appear ? Problem is in UI binding

---

## Contact

If issue persists after following this guide, provide:
1. Full log output from Output window
2. Screenshot of empty Database Explorer
3. Connection string used
4. Database file path

---

**Last Updated:** 2026-01-04  
**Status:** Investigating
