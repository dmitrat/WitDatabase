# TODO: Fix ADO.NET Transaction Tests

## Problem Description

Multiple transaction tests in `WitDbTransactionTests` are failing. After detailed diagnosis, the root cause has been identified.

### Failing Tests:
- `RollbackRevertsDataTest` - Expected 0 rows, got 1
- `RollbackAsyncWorksTest` - Expected 0 rows, got 1
- `RollbackToSavepointRevertsToPointTest` - Expected 1 row, got 2
- `DisposeRollsBackUncommittedTransactionTest` - Expected 0 rows, got 1
- `DisposeAsyncRollsBackUncommittedTransactionTest` - Expected 0 rows, got 1
- `CommitPersistsDataTest` - Also failing
- And related tests...

## Root Cause Analysis

### Diagnostic Output
```
Initial - Engine.CurrentTransaction is null: True
After BeginTransaction - Engine.CurrentTransaction is null: False
After BeginTransaction - Engine.CurrentTransaction type: MvccTransaction
After INSERT - Engine.CurrentTransaction is null: False
Count within transaction: 1
After Rollback - Engine.CurrentTransaction is null: True
Count after rollback: 1  <-- Should be 0!
```

### Issue: SchemaCatalog writes bypass transaction

The `SchemaCatalog` class has methods that write directly to the store instead of through the active transaction:

```csharp
// In SchemaCatalog.SaveTableRowId():
if (transaction != null)
{
    transaction.Put(keyBytes.AsSpan(), rowIdBytes);
}
else
{
    m_store.Put(keyBytes.AsSpan(), rowIdBytes);  // <-- Direct write!
}
```

And `InsertRow` calls `GetNextRowId` without passing the transaction:

```csharp
// In WitSqlEngine.InsertRow():
if (!hasRowId)
{
    rowId = m_schema.GetNextRowId(tableName);  // <-- No transaction passed!
}
```

But the main issue is that **SchemaCatalog uses `m_store` which is the underlying `MvccTransactionalStore`**, not the current transaction. When `MvccTransactionalStore.Put()` is called:

```csharp
// In MvccTransactionalStore.Put():
public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
{
    using var _ = m_lockManager?.AcquireWriteLock();
    m_mvccStore.Put(key, value);  // Writes directly to MVCC store with timestamp!
}
```

This creates a committed record (transactionId=0) directly in the store, which is visible after rollback.

### The Core Problem

With `MVCC=true` (default):
1. `WitDbConnection.BeginTransaction()` creates an `MvccTransaction`
2. `MvccTransaction.Put()` buffers changes in `m_changes` (correct)
3. BUT `SchemaCatalog` operations write directly to `MvccTransactionalStore`
4. `MvccTransactionalStore.Put()` creates committed records with `transactionId=0`
5. After `Rollback()`, these records are still visible because they were never part of the transaction

The `MvccTransaction.Rollback()` only clears `m_changes` buffer, but cannot undo direct writes to the store.

## Solution Options

### Option A: Pass transaction to SchemaCatalog operations (Recommended)

Ensure all SchemaCatalog operations go through the current transaction:

**File:** `Sources/Engine/OutWit.Database/Engine/WitSqlEngine.Dml.Operations.cs`
```csharp
// In InsertRow():
if (!hasRowId)
{
    rowId = m_schema.GetNextRowId(tableName, m_currentTransaction);  // Pass transaction
}

// Similar changes for IncrementRowCount, DecrementRowCount, etc.
```

**File:** `Sources/Engine/OutWit.Database/Schema/SchemaCatalog.cs`
- Already has overloads that accept `ITransaction`
- Need to ensure all callers pass the transaction

### Option B: Make SchemaCatalog use the store from WitSqlEngine context

Instead of using `m_store` directly, SchemaCatalog should use a store accessor that respects the current transaction.

### Option C: Use traditional transactions instead of MVCC by default

Change `WitDbConnectionStringBuilder.Mvcc` default to `false`:
```csharp
public bool Mvcc
{
    get => GetValue(KEY_MVCC, false);  // Changed from true to false
    set => SetValue(KEY_MVCC, value);
}
```

**Note:** This was tried but causes other issues with locking.

## Recommended Fix

### Step 1: Update InsertRow to pass transaction

```csharp
// In WitSqlEngine.InsertRow():
public void InsertRow(string tableName, WitSqlRow row)
{
    // ... existing code ...
    
    if (!hasRowId)
    {
        rowId = m_schema.GetNextRowId(tableName, m_currentTransaction);  // Pass transaction
    }

    // ... existing code ...
    
    // Update row count with transaction
    m_schema.IncrementRowCount(tableName, 1, m_currentTransaction);  // Already done
}
```

### Step 2: Update DeleteRow to pass transaction

```csharp
// In WitSqlEngine.DeleteRow():
m_schema.DecrementRowCount(tableName, 1, m_currentTransaction);  // Already done
```

### Step 3: Review all SchemaCatalog calls in WitSqlEngine

Ensure all calls to SchemaCatalog methods pass `m_currentTransaction` where appropriate.

## Testing Plan

After implementing fixes:

1. Run diagnostic tests:
   ```bash
   dotnet test --filter "Name~Diagnose"
   ```

2. Run all transaction tests:
   ```bash
   dotnet test --filter "FullyQualifiedName~Transaction"
   ```

3. Run full ADO.NET test suite:
   ```bash
   dotnet test Sources/Providers/OutWit.Database.AdoNet.Tests
   ```

## Files to Modify

1. `Sources/Engine/OutWit.Database/Engine/WitSqlEngine.Dml.Operations.cs`
   - Update `InsertRow` to pass `m_currentTransaction` to `GetNextRowId`
   
2. Verify these are already correct:
   - `IncrementRowCount` - already passes transaction
   - `DecrementRowCount` - already passes transaction
   - `ResetRowId` - already passes transaction
   - `ResetRowCount` - already passes transaction

## Related Issues

The `SchemaCatalog` currently stores metadata directly in `m_store` which is the `MvccTransactionalStore`. This means schema changes (like row ID sequences) are NOT transactional when MVCC is used. This is a design limitation that may need to be addressed separately.
