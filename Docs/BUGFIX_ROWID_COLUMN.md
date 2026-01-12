# Fix: _rowid Column Issue in Table Editor

**Date:** 2025-01-12  
**Status:** ? Completed

---

## Problem Description

When using Table Editor to edit table data:
1. The internal `_rowid` column is returned by `SELECT *` and displayed in the grid
2. When committing a new row, `_rowid` is included in the INSERT statement
3. This causes incorrect SQL: `INSERT INTO [users22] ([_rowid], [name], ...) VALUES (NULL, ...)`
4. The database interprets `_rowid` as a column and causes issues

## Root Cause

The WitDatabase engine uses `_rowid` as an internal row identifier. When executing `SELECT *`:
- The `IteratorTableScan` prepends `_rowid` to the row values (needed for UPDATE/DELETE operations)
- When `SELECT *` doesn't apply projection, the internal `_rowid` column was returned to the user
- The Studio received this column and treated it as a regular column

## Solution Implemented

**Engine-Side Fix (Proper Solution):**

### 1. Created `IteratorExcludeInternal`
New iterator that filters out internal columns (like `_rowid`) from results.

**File:** `Sources/Engine/OutWit.Database/Iterators/IteratorExcludeInternal.cs`

Features:
- Identifies internal columns by name (`_rowid`)
- Efficiently maps output columns to source columns
- Only applied when necessary (via `NeedsFiltering` check)

### 2. Modified `QueryPlanner.Clauses.cs`
Updated `ApplyProjection` method to wrap iterator with `IteratorExcludeInternal` when `SELECT *` is used:

```csharp
private IResultIterator ApplyProjection(IResultIterator iterator, IReadOnlyList<ClauseSelectItem> selectList)
{
    // For SELECT *, we need to exclude internal columns like _rowid
    if (IsSelectStar(selectList))
    {
        // Only wrap if the source contains internal columns
        if (IteratorExcludeInternal.NeedsFiltering(iterator.Schema))
        {
            return new IteratorExcludeInternal(iterator);
        }
        return iterator;
    }

    return new IteratorProject(iterator, selectList, m_context);
}
```

### 3. Fast Path Already Handled
The `ApplySelectProjection` method in `StatementExecutor.Select.cs` already correctly filters `_rowid` for SELECT * in fast path execution.

---

## Test Coverage

Created comprehensive test suite: `WitSqlEngineInternalColumnsTests.cs`

| Test | Description |
|------|-------------|
| `SelectStarDoesNotIncludeRowIdTest` | Basic SELECT * |
| `SelectStarFromJoinDoesNotIncludeRowIdTest` | JOIN queries |
| `SelectStarWithAliasDoesNotIncludeRowIdTest` | Table aliases (p.*) |
| `SelectExplicitColumnsDoesNotIncludeRowIdTest` | Explicit column list |
| `SelectStarSchemaDoesNotIncludeRowIdTest` | Schema metadata |
| `UpdateWithWhereStillWorksTest` | UPDATE still works internally |
| `DeleteWithWhereStillWorksTest` | DELETE still works internally |
| `SelectStarFromSubqueryDoesNotIncludeRowIdTest` | Subqueries |
| `SelectDistinctStarDoesNotIncludeRowIdTest` | DISTINCT |
| `SelectStarFromCteDoesNotIncludeRowIdTest` | CTEs |
| `SelectStarUnionDoesNotIncludeRowIdTest` | UNION operations |

**All 11 tests pass** ?

---

## Files Modified

| File | Changes |
|------|---------|
| `Iterators/IteratorExcludeInternal.cs` | **New file** - filters internal columns |
| `Query/QueryPlanner.Clauses.cs` | Modified `ApplyProjection` to use new iterator |
| `Tests/Engine/WitSqlEngineInternalColumnsTests.cs` | **New file** - test suite |

---

## Backward Compatibility

- **No breaking changes** - internal operations (UPDATE, DELETE) still have access to `_rowid`
- Only affects user-facing `SELECT *` results
- Explicit `SELECT _rowid, *` would still work if needed (though not recommended)

---

## Performance Impact

- Minimal overhead - `IteratorExcludeInternal` only applied when:
  1. Query is `SELECT *`
  2. Source schema contains `_rowid`
- Column mapping is computed once at iterator creation
- No additional memory allocations per row beyond the filtered values array
