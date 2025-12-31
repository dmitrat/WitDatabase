# TODO: Fix ORDER BY Aggregate Expression Bug

## Problem ? FIXED

Benchmark `Complex aggregation - WitDb` was failing with `KeyNotFoundException` when executing:

```sql
SELECT Region, COUNT(*), SUM(Amount), AVG(Amount), MIN(Quantity), MAX(Quantity)
FROM Sales
GROUP BY Region
ORDER BY SUM(Amount) DESC
```

### Root Cause

`IteratorSort.CompareRows()` was evaluating `ORDER BY SUM(Amount)` expression against the **result row** from `IteratorGroupBy`, but that row only contained aggregated values - the original `Amount` column didn't exist.

### Solution Implemented

Created a new expression type `WitSqlExpressionOrderByColumnIndex` and modified `QueryPlanner.ApplyOrderByClauseForAggregate()` to resolve ORDER BY aggregate expressions to their corresponding SELECT list column indices at query planning time.

## Files Changed

### New Files
- `Sources/Engine/OutWit.Database.Parser/Expressions/WitSqlExpressionOrderByColumnIndex.cs`
  - New expression type for ORDER BY column index references

### Modified Files
- `Sources/Engine/OutWit.Database/Query/QueryPlanner.cs`
  - `PlanAggregateQuery()` now calls `ApplyOrderByClauseForAggregate()` instead of `ApplyOrderByClause()`

- `Sources/Engine/OutWit.Database/Query/QueryPlanner.Clauses.cs`
  - Added `ApplyOrderByClauseForAggregate()` method
  - Added `ResolveAggregateOrderBy()` method
  - Added `ResolveAggregateExpression()` method
  - Added `AggregateExpressionsMatch()` method
  - Added `ExpressionsMatch()` method
  - Added `IsAggregateFunction()` method

- `Sources/Engine/OutWit.Database/Expressions/ExpressionEvaluator.cs`
  - Added handling for `WitSqlExpressionOrderByColumnIndex` in `Evaluate()` method
  - Added `EvaluateOrderByColumnIndex()` method

### Test Files
- `Sources/Engine/OutWit.Database.Tests/Statements/StatementExecutorSelectTests.cs`
  - Added `SelectWithGroupByOrderByAggregateTest()`
  - Added `SelectComplexAggregationWithOrderByTest()`

## How It Works

1. **Query Planning Phase**: When planning an aggregate query with ORDER BY:
   - `ResolveAggregateExpression()` scans each ORDER BY expression
   - If expression is an aggregate function (e.g., `SUM(Amount)`), it searches the SELECT list for a matching aggregate
   - When found, replaces the aggregate expression with `WitSqlExpressionOrderByColumnIndex { ColumnIndex = N }`

2. **Execution Phase**: When `IteratorSort` evaluates ORDER BY:
   - `ExpressionEvaluator.Evaluate()` recognizes `WitSqlExpressionOrderByColumnIndex`
   - Returns `row[columnIndex]` directly from the aggregated result row
   - No need to re-evaluate the aggregate function

## Test Coverage

```csharp
// Test 1: Simple ORDER BY aggregate
SELECT Region, COUNT(*), SUM(Amount) 
FROM Sales 
GROUP BY Region 
ORDER BY SUM(Amount) DESC

// Test 2: Complex aggregation with ORDER BY
SELECT Region, COUNT(*), SUM(Amount), AVG(Amount), MIN(Quantity), MAX(Quantity)
FROM Sales
GROUP BY Region
ORDER BY SUM(Amount) DESC
```

## Verification

```
Passed!  - Failed: 0, Passed: 36, Skipped: 0 - GROUP BY tests
Passed!  - Failed: 0, Passed: 27, Skipped: 0 - SELECT tests
```

## Related Optimization

This fix also improves performance by:
- Avoiding re-computation of aggregates during sorting
- Using direct column index access instead of expression evaluation
- Reducing memory allocations during ORDER BY

## Status

- [x] Root cause identified
- [x] Solution implemented
- [x] New expression type created
- [x] ExpressionEvaluator updated
- [x] QueryPlanner updated
- [x] Unit tests added
- [x] All tests passing
