# ALTER TABLE Implementation TODO

**Created:** 2025-01-29  
**Status:** ? COMPLETED  
**Priority:** P0 (Required for EF Core migrations)

---

## Overview

ALTER TABLE is critical for EF Core migrations. Three main features need implementation:

1. ~~**ADD CONSTRAINT** - Add named constraints to existing table~~ ? COMPLETED
2. ~~**DROP CONSTRAINT** - Remove named constraints~~ ? COMPLETED
3. ~~**ADD COLUMN with DEFAULT** - Populate existing rows with default value~~ ? COMPLETED

---

## 1. ALTER TABLE ADD CONSTRAINT (P0) ? COMPLETED

### Current State
- Parser: ? Supported (`AlterActionAddConstraint`)
- Engine: ? **IMPLEMENTED**

### Implementation Summary

**Completed on:** 2025-01-30

#### Supported Constraint Types

| Constraint Type | SQL Example | Status |
|----------------|-------------|--------|
| CHECK | `ADD CONSTRAINT chk_name CHECK (expr)` | ? Implemented |
| UNIQUE | `ADD CONSTRAINT uq_name UNIQUE (cols)` | ? Implemented |
| FOREIGN KEY | `ADD CONSTRAINT fk_name FOREIGN KEY (cols) REFERENCES ...` | ? Implemented |
| PRIMARY KEY | `ADD CONSTRAINT pk_name PRIMARY KEY (cols)` | ? Not Supported (throws NotSupportedException) |

#### Files Created
- `Definitions/DefinitionNamedConstraint.cs` - Named constraint model with ConstraintType enum

#### Files Modified
- `Definitions/DefinitionTable.cs` - Added `NamedConstraints` property and `GetConstraint()` method
- `Interfaces/IDatabase.cs` - Added `AddConstraint()` and `DropConstraint()` methods
- `WitSqlEngine.Ddl.Tables.cs` - Implemented constraint validation and management
- `Schema/SchemaCatalog.Columns.cs` - Added `AddConstraint()` and `DropConstraint()` methods
- `Statements/StatementExecutor.Ddl.cs` - Added handling for `AlterActionAddConstraint` and `AlterActionDropConstraint`

#### Validation Logic
- **CHECK**: Parses expression and evaluates against all existing rows
- **UNIQUE**: Validates no duplicates exist (NULL values excluded from uniqueness)
- **FOREIGN KEY**: Validates all values exist in referenced table (NULL values allowed)

#### Tests Added (WitSqlEngineAlterTableConstraintTests.cs)
- [x] `AlterTableAddCheckConstraintTest()`
- [x] `AlterTableAddCheckConstraintWithInvalidDataThrowsTest()`
- [x] `AlterTableAddCheckConstraintOnEmptyTableTest()`
- [x] `AlterTableAddUniqueConstraintTest()`
- [x] `AlterTableAddUniqueConstraintOnDuplicatesThrowsTest()`
- [x] `AlterTableAddUniqueConstraintAllowsMultipleNullsTest()`
- [x] `AlterTableAddCompositeUniqueConstraintTest()`
- [x] `AlterTableAddForeignKeyConstraintTest()`
- [x] `AlterTableAddForeignKeyWithInvalidDataThrowsTest()`
- [x] `AlterTableAddForeignKeyAllowsNullTest()`
- [x] `AlterTableAddPrimaryKeyThrowsNotSupportedTest()`

---

## 2. ALTER TABLE DROP CONSTRAINT (P0) ? COMPLETED

### Current State
- Parser: ? Supported (`AlterActionDropConstraint`)
- Engine: ? **IMPLEMENTED**

### Implementation Summary

**Completed on:** 2025-01-30

The `DropConstraint()` method in `WitSqlEngine.Ddl.Tables.cs`:
1. Finds constraint by name in table metadata
2. For UNIQUE: Drops associated index
3. For CHECK/FOREIGN KEY: Removes from metadata
4. For PRIMARY KEY: Throws NotSupportedException

#### Tests Added
- [x] `AlterTableDropCheckConstraintTest()`
- [x] `AlterTableDropUniqueConstraintTest()`
- [x] `AlterTableDropForeignKeyConstraintTest()`
- [x] `AlterTableDropNonExistentConstraintThrowsTest()`
- [x] `AlterTableDropConstraintAlreadyExistsTest()`

---

## 3. ALTER TABLE ADD COLUMN with DEFAULT (P0) ? COMPLETED

### Current State
- Parser: ? Supported
- Engine: ? **IMPLEMENTED** - adds column AND populates existing rows with evaluated default value

### Implementation Summary

**Completed on:** 2025-01-30

The `AddColumn()` method in `WitSqlEngine.Ddl.Tables.cs` now:
1. Parses the DEFAULT expression using `WitSql.ParseExpression()`
2. Creates an `ExpressionEvaluator` to evaluate the default value
3. Checks if the expression is deterministic (e.g., literals, arithmetic) or non-deterministic (e.g., `NOW()`, `NEWGUID()`)
4. For deterministic expressions: evaluates once and reuses the value
5. For non-deterministic expressions: evaluates per row to generate unique values
6. Updates all existing rows with the new column and its default value

#### Helper Methods Added
- `IsDeterministicExpression()` - checks if expression returns same value each call
- `IsDeterministicFunction()` - identifies non-deterministic functions (NOW, NEWGUID, RANDOM, etc.)
- `IsDeterministicCase()` - handles CASE expression determinism

#### Tests Added (WitSqlEngineDdlTests.cs)
- [x] `AlterTableAddColumnWithDefaultPopulatesExistingRowsTest()` - string default
- [x] `AlterTableAddColumnWithNullDefaultTest()` - no default = NULL
- [x] `AlterTableAddColumnWithIntegerDefaultTest()` - integer literal default
- [x] `AlterTableAddColumnWithExpressionDefaultTest()` - computed expression `(1 + 2)`
- [x] `AlterTableAddColumnOnEmptyTableTest()` - empty table + new inserts use default
- [x] `AlterTableAddNotNullColumnWithDefaultTest()` - NOT NULL with DEFAULT
- [x] `AlterTableAddColumnWithNowDefaultGeneratesTimestampsTest()` - `DEFAULT (NOW())`
- [x] `AlterTableAddColumnWithNewGuidDefaultGeneratesUniqueGuidsTest()` - `DEFAULT (NEWGUID())`

---

## 4. Computed Columns in ALTER TABLE (P2)

### Current State
- Parser: ? Supported (`ComputedExpression`, `IsStored`)
- Engine: ? Not implemented for ALTER TABLE

### Implementation Steps (Deferred)

#### Step 4.1: Stored Computed Columns
- [ ] Add column metadata with computed expression
- [ ] Evaluate expression for all existing rows
- [ ] Store computed values

#### Step 4.2: Virtual Computed Columns
- [ ] Add column metadata with computed expression
- [ ] Evaluate on-the-fly during query execution
- [ ] No storage needed

#### Step 4.3: Index on Computed Columns
- [ ] Allow creating index on stored computed column
- [ ] Expression index effectively does this already

---

## File Changes Summary

### Files Created

| File | Purpose |
|------|---------|
| `Definitions/DefinitionNamedConstraint.cs` | Named constraint model with ConstraintType enum |
| `Tests/WitSqlEngineAlterTableConstraintTests.cs` | Constraint tests (18 tests) |

### Files Modified

| File | Changes |
|------|---------|
| `Definitions/DefinitionTable.cs` | Added `NamedConstraints` property, `GetConstraint()` method |
| `Interfaces/IDatabase.cs` | Added `AddConstraint()`, `DropConstraint()` methods |
| `WitSqlEngine.Ddl.Tables.cs` | Implemented constraint methods, ADD COLUMN with DEFAULT |
| `Schema/SchemaCatalog.Columns.cs` | Added `AddConstraint()`, `DropConstraint()` methods |
| `Statements/StatementExecutor.Ddl.cs` | Handle `AddConstraint`, `DropConstraint` actions |
| `WitSqlRow.cs` | Added `Empty` static field |
| `WitSqlEngineDdlTests.cs` | Added tests for ADD COLUMN with DEFAULT |

---

## EF Core Migration Patterns ? NOW SUPPORTED

EF Core generates migrations like:

```sql
-- Adding a new column with default ?
ALTER TABLE "Products" ADD "CreatedAt" DATETIME NOT NULL DEFAULT (NOW());

-- Adding a unique constraint ?
ALTER TABLE "Users" ADD CONSTRAINT "UQ_Users_Email" UNIQUE ("Email");

-- Adding a foreign key ?
ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_Users"
    FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
    ON DELETE CASCADE;

-- Adding a check constraint ?
ALTER TABLE "Products" ADD CONSTRAINT "CHK_Price" CHECK (Price >= 0);

-- Dropping a constraint ?
ALTER TABLE "Users" DROP CONSTRAINT "UQ_Users_Email";
```

All these patterns are now supported!

---

## Notes

- PRIMARY KEY constraint cannot be added/dropped (would require table rebuild)
- UNIQUE constraint creates an implicit unique index named `UQ_{table}_{constraint}`
- NULL values don't violate UNIQUE or FOREIGN KEY constraints
- CHECK constraint allows NULL values (NULL != FALSE)

---

## Test Summary

| Category | Tests | Status |
|----------|-------|--------|
| ADD COLUMN with DEFAULT | 10 | ? |
| ADD CONSTRAINT | 11 | ? |
| DROP CONSTRAINT | 5 | ? |
| Integration | 2 | ? |
| **Total** | **28** | ? |

---

**Last Updated:** 2025-01-30  
**Completed:** 2025-01-30
