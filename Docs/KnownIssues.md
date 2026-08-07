# Known issues

Defects found by using WitDatabase as a real application backend, not by unit
testing it. They come from two applications.

The first is **WitAnalytics** (`dmitrat/WitAnalytics`), which ships
two EF Core providers — PostgreSql for production and WitDatabase as a
portability proof-of-concept and test backend — and runs the *same* query and
migration suite against both. Everything below is a case where PostgreSql passes
and WitDatabase does not.

Each entry states what was verified and what is still a hypothesis; the causes
marked "not identified" are exactly that — the symptom is reproducible, the root
cause was not chased into the engine.

| # | Area | Severity | Status | One line |
|---|------|----------|--------|----------|
| [1](#1-alter-table-add-column-is-unusable-schema-cannot-evolve) | EF migrations + engine DDL | **Blocker** | **FIXED** | A schema can be created but never changed |
| [2](#2-inline-date-literals-are-rejected-by-the-parser) | EF query translation | Major | open — root-caused | `WitSqlParsingException` on an inlined `DateOnly` |
| [3](#3-intstring-conversion-inside-a-query-is-not-translated) | EF query translation | Minor | open — root-caused | `group.Key.ToString()` does not translate |

A full audit of the engine and both providers is in
[AUDIT-2026-07.md](AUDIT-2026-07.md); §0 of that document is the execution-verified
part and supersedes the analysis below wherever the two disagree.

---

## 1. `ALTER TABLE ADD COLUMN` is unusable — schema cannot evolve

> **FIXED.** Both halves came from the EF provider's service registration, not from
> the engine, and both are now resolved. `ALTER TABLE ADD COLUMN` in the engine was
> never the problem — twelve variants of it were verified correct.
>
> **Root cause of 1b:** `AddEntityFrameworkWitDb` never registered an
> `IProviderConventionSetBuilder`, so EF Core used the **core** builder and the whole
> relational convention set — including `TableNameFromDbSetConvention` — was absent.
> Default table names therefore came from the entity CLR type instead of the `DbSet`
> property, so the same model produced `Website` here and `Websites` on PostgreSql.
> The hand-written `AddColumn(table: "Websites")` was copied from the PostgreSql
> migration and referenced a table that genuinely did not exist;
> `Table 'Websites' not found` was correct behaviour. Fixed by
> `Metadata/WitConventionSetBuilder.cs`.
>
> **Root cause of 1a:** `WitModelRuntimeInitializer` installed a `RelationalModelFactory`
> that called `RelationalModel.Create(..., designTime: false)` unconditionally, so the
> *design-time* model handed to `MigrationsModelDiffer` was read-optimized and the differ
> threw. `WitMigrationsModelDiffer` then swallowed that exception — returning an empty
> operation list when `source != null` (the silent empty migration) and a lossy
> hand-built list when `source == null` (which is why generated `CreateTable` operations
> had no `maxLength`, listed columns in name order, and dropped unique/check constraints
> in three empty `catch` blocks). Both classes were deleted; EF Core's stock
> implementations are correct once the convention set builder is registered.
>
> **Verified:** `dotnet ef migrations add` on a changed model now emits a real
> `AddColumn` with `maxLength` and a populated `Down()`, byte-for-byte equivalent to what
> the PostgreSql provider produces for the same model change. Regression tests:
> `EntityFramework.Tests/Metadata/WitConventionSetBuilderTests.cs` and
> `EntityFramework.Tests/MigrationTests/SchemaEvolutionRegressionTests.cs` — the latter
> applies two migrations in sequence to a real file and reads the new column back, as
> this document asked for. Eleven of the thirteen fail if the fix is reverted.
>
> **Breaking change:** default table names are now the `DbSet` property name. Existing
> `.witdb` files carry the old singular names; either recreate them or pin the old name
> with `ToTable("Website")`.

The original analysis follows, kept for the record.

**Severity: blocker.** Any product on this provider is frozen at its initial
schema. Adding one nullable column is enough to hit it.

Two distinct defects sit on top of each other.

### 1a. The migrations differ silently drops the operation

```
dotnet ef migrations add AddWebsiteExcludedPaths \
  --project OutWit.Analytics.Database.WitDatabase --context WitAnalyticsDbContext
```

after adding a single `public string? ExcludedPaths { get; set; }` to an entity
(configured `HasMaxLength(1000)`).

**Expected** — the same as the PostgreSql provider produces from the identical
model change:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "ExcludedPaths", table: "Websites", maxLength: 1000, nullable: true);
}
```

**Actual** — an empty migration:

```csharp
protected override void Up(MigrationBuilder migrationBuilder) { }
protected override void Down(MigrationBuilder migrationBuilder) { }
```

The command reports success and **still updates the model snapshot**, which is
the dangerous part: the snapshot now contains `ExcludedPaths`, so EF believes the
column exists while the database will never receive it. The failure is silent —
nothing in the tooling output hints that an operation was dropped.

Suspected location: `Sources/Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsModelDiffer.cs`.
The SQL generator is *not* the problem — `WitMigrationsSqlGenerator` (~line 92)
handles `AddColumnOperation` and emits `ALTER TABLE … ADD COLUMN …` correctly.

### 1b. A hand-written `AddColumn` fails at runtime

Writing the operation by hand produces valid SQL that then fails to execute:

```
System.InvalidOperationException: Table 'Websites' not found
   at OutWit.Database.Engine.WitSqlEngine.AddColumn(String tableName, DefinitionColumn column)
```

The table demonstrably exists: the preceding migration in the same run created
it, and every subsequent query reads it happily. Reproduced on a brand-new
database file (`Data Source=…/x.witdb`) with `db.Database.Migrate()`.

**Not a transaction-visibility problem.** Applying migrations one at a time via
`IMigrator.Migrate(migrationName)` — separate calls, separate transactions —
fails identically.

Trail followed, all consistent, so the mismatch is further in:

- `WitSqlEngine.AddColumn` → `m_schema.GetTable(tableName)` returns `null`
  (`Sources/Engine/OutWit.Database/Engine/WitSqlEngine.Ddl.Tables.cs:158`)
- `SchemaCatalog.GetTable` is a plain dictionary lookup with no normalisation
  (`Sources/Engine/OutWit.Database/Schema/SchemaCatalog.Tables.cs:16`), while
  `CreateTable` stores `table.Name` verbatim
- the parser normalises identifiers through the **same** helper for CREATE and
  ALTER (`WitSqlVisitor.Helpers.cs:78 GetTableName` → `NormalizeIdentifier`), so
  a quoting/casing difference at parse time looks unlikely
- `StatementExecutor.ExecuteAlterTable` passes `alterTable.TableName` straight
  through (`StatementExecutor.Ddl.Tables.cs:219`)

**Root cause: not identified.** Worth checking whether the catalog the ALTER path
reads is the same instance the CREATE path wrote to, and whether the catalog is
reloaded (and thus reset) between migration statements.

### Why the existing tests miss it

`Sources/Providers/OutWit.Database.EntityFramework.Tests/MigrationTests/MigrationsTests.cs`
asserts on **generated SQL strings only** — nothing is executed against a real
database — and it uses `AddColumnOperation` exclusively *inside*
`CreateTableOperation.Columns`. A standalone `ALTER TABLE ADD COLUMN` is never
generated and never run. A regression test should apply two migrations in
sequence to a real file and then read the new column back.

### Workaround in use downstream

WitAnalytics squashed its WitDatabase migration history into a single
`InitialCreate` (acceptable only because that provider is its dev/test backend
and the `.witdb` files are disposable). PostgreSql keeps the real incremental
migration. A product with real data on WitDatabase has no such escape.

---

## 2. Inline DATE literals are rejected by the parser

> **Root-caused, not yet fixed — and wider than described here.** This is not a
> `DateOnly` problem, it is a **typed-literal** problem, and it also breaks `DateTime`,
> `DateTimeOffset` and `TimeOnly`, plus every `HasData` seed row with a temporal column.
> `Storage/WitTypeMappingSource.cs:75-78` uses EF Core's stock `DateOnlyTypeMapping`,
> `TimeOnlyTypeMapping`, `DateTimeTypeMapping` and `DateTimeOffsetTypeMapping`, whose
> default `SqlLiteralFormatString` is the SQL-standard typed literal. Measured, driving
> the provider's real `GenerateSqlLiteral` into the engine:
>
> | CLR value | literal EF emits | engine |
> |---|---|---|
> | `DateOnly` | `DATE '2026-07-01'` | rejected |
> | `TimeOnly` | `TIME '13:45:30'` | rejected |
> | `DateTime` | `TIMESTAMP '2026-07-01 13:45:30.0000000'` | rejected — `TIMESTAMP` is not even a token |
> | `DateTimeOffset` | `TIMESTAMP '…+03:00'` | rejected |
>
> The grammar has `DATE`/`TIME`/`DATETIME` only as *function* names. Plain string
> literals work (`'2026-07-01'`, `CAST('2026-07-01' AS DATE)`), so the fix belongs in the
> grammar as typed literals producing *typed* values — emitting bare quoted strings from
> the provider instead would trade a loud parse error for silently wrong rows, because
> Text-vs-DateTime comparison falls back to ordinal string comparison.

**Severity: major** — the failing shape is the one people write first.

A `DateOnly` value inlined into the generated SQL throws
`WitSqlParsingException`; the identical query with the value passed as a
**parameter** works. In EF terms, this means a captured local variable is fine
while a constructed constant is not:

```csharp
// throws WitSqlParsingException — the literal is inlined into the SQL
db.Events.Where(item => item.DateLocal >= new DateOnly(2026, 7, 1));

// works — EF parameterises the captured local
var from = new DateOnly(2026, 7, 1);
db.Events.Where(item => item.DateLocal >= from);
```

Rule adopted downstream: never inline `new DateOnly(...)` in a LINQ expression,
always go through a local. That happens to be the production query shape anyway,
which is why this only bit during test writing — a nastier version of the same
bug would surface in code paths where EF chooses to inline a constant.

Note that `DateOnly` → `DATE` mapping itself works correctly, as does
`byte` → `UTINYINT`; this is purely about literals in generated SQL.

---

## 3. `int`→`string` conversion inside a query is not translated

> **Root-caused, not yet fixed. The engine is not at fault** — all four conversion forms
> work when executed directly:
> `CAST(DeviceType AS VARCHAR)`, `CAST(… AS VARCHAR(20))`, `CAST(… AS TEXT)` and
> `CONVERT(VARCHAR, DeviceType)` all return `'42'`. The gap is that no
> `object.ToString()` / `Convert.*` translator is registered in
> `Query/WitMethodCallTranslatorProvider.cs`, so EF has nothing to emit. Cheap fix, and
> the same registration covers the rest of `Convert.To*`.
>
> The aside below about enums is also inaccurate: `RelationalTypeMapping.NormalizeEnumValue`
> converts int-backed enums in both `CreateParameter` and `GenerateSqlLiteral`, so enum
> properties do work. `char` and non-`int`-backed enums are the genuine gaps, and
> `WitDbBulkExtensions` bypasses type mappings entirely via raw reflection.

**Severity: minor** — easy to work around, but the failure is a translation
error rather than a graceful client-side fallback.

```csharp
db.Events
  .GroupBy(item => item.DeviceType)          // int column
  .Select(group => new { Key = group.Key.ToString(), Count = group.LongCount() });
```

does not translate. Workaround: materialise first and map in memory —

```csharp
var rows = await query.Select(g => new { g.Key, Count = g.LongCount() }).ToArrayAsync();
var mapped = rows.Select(r => new { Key = DeviceName(r.Key), r.Count });
```

Enums are stored as `int` in this schema precisely because WitDatabase does not
map enum CLR properties, so this pattern is common wherever a stored code has to
be presented as a name.

---

---

## Found by WitDatabase Studio, 2026-08-06

The second source of entries in this document, and the same kind: an application
using the engine rather than a suite testing it. **Studio's schema designer**
(phase 14 stage 8) had to be built against what `ALTER TABLE` actually does, so
its whole surface was executed — roughly 120 statements over six rounds — and
what came back was six defects, five of them data-integrity.

Each is reproduced by a test in the engine's own suite, named below, and every
one of those tests was run against the unfixed engine first.

| # | Area | Severity | Status | One line |
|---|------|----------|--------|----------|
| [4](#4-a-function-over-an-indexed-column-returns-the-wrong-rows) | Query planner | **Blocker** | **FIXED** | Creating an index changes the answer of a query |
| [5](#5-rename-to-restarts-the-key-generator-and-the-next-insert-overwrites-a-row) | DDL + row ids | **Blocker** | **FIXED** | A renamed table's next INSERT destroys a row |
| [6](#6-alter-column--type-destroys-the-values-it-cannot-convert) | DDL | **Blocker** | **FIXED** | A column of text becomes a column of zeroes |
| [7](#7-add-column--not-null-with-no-default-closes-a-table-for-writing) | DDL | Major | **FIXED** | An accepted ALTER makes every later write fail |
| [8](#8-drop-column-leaves-the-index-on-that-column-behind) | DDL + catalogue | Major | **FIXED** | An index over a column that no longer exists |
| [9](#9-ordinal_position-is-1-for-every-column) | INFORMATION_SCHEMA | Minor | **FIXED** | The catalogue cannot say what order columns are in |
| [10](#10-studios-exit-never-wrote-a-database-down) | Studio + storage | **Blocker** | **FIXED** | Leaving Studio lost everything since the last flush |

---

## 4. A function over an indexed column returns the wrong rows

> **FIXED.** `Optimizers/OptimizerQuery.cs`. Regression tests:
> `AuditVerification/IndexedFunctionPredicateTests` — 8 cases, 4 of which fail
> against the unfixed engine.

`WHERE ABS(V) = 7` over a table holding `V = -7` answered with the row while
there was no index, and with **nothing** once an ordinary index on `V` existed.
`DROP INDEX` made the answer right again. The same with `LOWER` and `UPPER` over
a text column; `V + 0 = -7` and `-V = 7` stayed correct throughout, so it is
specifically a function **call**.

**Cause.** `TryExtractPredicate` records a predicate written around a call — say
`LOWER(S) = 'x'` — with the column **inside** the call, `S`, plus the expression
text. `FindMatchingPredicate` then matched on the column name first, so a plain
index on `S` answered it by seeking `'x'` among the raw values. Three more sites
matched the same way (`CountMatchedLeadingColumns`, `CollectCompositeSeekValues`,
`TryOptimizeForBetween`); all four now share one rule: a plain index column
answers only a predicate about the bare column, and a column indexed BY an
expression answers only a predicate about that same expression.

**Why it stayed quiet.** Where the raw value already equals the wrapped one — a
lower-case name, a positive number — the wrong seek finds the right row. It shows
up the moment they differ, and `EXPLAIN` names the index it is using.

Measured over 200 rows, above `MIN_ROWS_FOR_INDEX`, on B-Tree and on LSM, and
across a close and reopen.

---

## 5. `RENAME TO` restarts the key generator, and the next INSERT overwrites a row

> **FIXED.** `Schema/SchemaCatalog.Tables.cs` and
> `Engine/WitSqlEngine.Dml.Operations.cs`. Regression tests:
> `AuditVerification/RenamedTableKeyGeneratorTests` — 7 cases, 5 of which fail
> against the unfixed engine.

After `ALTER TABLE R RENAME TO R2`, a table holding keys 1 and 2 answered the
next generated INSERT with key **1** and wrote over the row that was there —
silently, reporting one row affected. Reproduced on both stores and across a
reopen.

**Cause, two halves.** The generator is persisted under a key built from the
table NAME, and `RenameTable` carried the definition, the indexes and the row
count across but not the counter — so the renamed table had none, which reads as
zero. (The mirror image was found by the test written for the first half: a table
later created under the OLD name inherited the orphaned counter and started at 3.)
The second half is that a generated key is trusted: the statement layer fills an
AUTOINCREMENT column in and marks it so the UNIQUE check is skipped, which is why
an explicit duplicate was refused correctly while a generated one was not.

**Fix.** The rename moves the counter, old record deleted; and a generated key
that lands on an existing row is refused rather than written. The extra check is
one point lookup per insert — interleaved measurements, four runs each way, gave
2.420 / 2.307 / 2.215 / 2.204 ms per row with it and 2.212 / 2.239 / 2.270 /
2.213 without, so it does not show above the spread.

---

## 6. `ALTER COLUMN … TYPE` destroys the values it cannot convert

> **FIXED.** `Engine/WitSqlEngine.Ddl.Tables.cs`. Regression tests:
> `AuditVerification/AlterColumnTypeDataLossTests` — 8 cases, 3 of which fail
> against the unfixed engine.

A `VARCHAR` column holding `'not a number'` became a column holding `0`, with no
error; `'3.9'` became `0` too, and an integer read as a DATETIME became
`01/01/0001`. Changing the type back brought nothing back — the rows had been
rewritten. One accepted statement could empty a column of its meaning.

**Cause.** The conversion goes through `AsInt64` and its neighbours, and those
answer a failed parse with a default: `long.TryParse(text, out var v) ? v : 0`.
That is a reasonable rule for an expression and a destructive one for a rewrite of
stored data.

**Fix.** A text value that does not read as the new type is refused, naming the
value, before anything is written — which is what PostgreSql answers for the same
statement. The narrowing conversions are deliberately still allowed: a decimal
read as an integer truncates, which is a defined conversion rather than a value
with nothing to become.

`CAST` behaves the same way and is **not** changed here: it is an expression, its
result is not stored, and changing what `AsInt64` does to a bad parse would reach
every comparison in the engine. It is why Studio's rebuild counts the values that
will not survive before it converts anything.

---

## 7. `ADD COLUMN … NOT NULL` with no DEFAULT closes a table for writing

> **FIXED.** `Engine/WitSqlEngine.Ddl.Tables.cs`. Regression tests:
> `AuditVerification/AlterTableColumnFindingsTests`.

On a table that already had rows the statement was **accepted**. Every existing
row got NULL in a column declared NOT NULL, and from then on the engine refused
every write to that table — including an `UPDATE` of an unrelated column, because
the row it was handed violated the constraint. Giving the column a default
afterwards repairs new rows and leaves the NULLs; there was no way back short of
rebuilding the table.

**Fix.** Refused, with a message that says a DEFAULT would work — again what
PostgreSql answers. The rows are asked rather than the row-count cache, which on
this engine is separate state that can disagree with them. On an **empty** table
the same statement is still accepted, and there is a test for that: refusing there
would be a rule the engine invented.

**The refusal broke nothing.** Engine 2342, ADO.NET 1016, EF Core 544 — no test
anywhere depended on the old permissiveness.

---

## 8. `DROP COLUMN` leaves the index on that column behind

> **FIXED.** `Engine/WitSqlEngine.Ddl.Tables.cs`. Regression tests:
> `AuditVerification/AlterTableColumnFindingsTests`.

The catalogue went on listing an index over a column that no longer existed, and
it survived a reopen. The foreign keys and named constraints on the column did go
with it — only the indexes stayed.

**Fix.** They are dropped through `DropIndex`, not removed from the catalogue,
because the entries in storage have to go too: a dropped index that keeps its
entries is adopted by the next index created under the same name (issue in
`DroppedIndexStorageTests`, fixed earlier).

---

## 9. `ORDINAL_POSITION` is 1 for every column

> **FIXED.** `Schema/SchemaCatalog.Tables.cs`. Regression tests:
> `AuditVerification/ColumnOrdinalPositionTests` — 5 cases, 3 of which fail
> against the unfixed engine.

`INFORMATION_SCHEMA.COLUMNS` published `ORDINAL_POSITION = 1` for every column of
every table, so ordering by it — which is what the column is for — left the
columns in whatever order the catalogue happened to return.

**Cause.** Only `ADD COLUMN` and `DROP COLUMN` numbered the columns. `CREATE
TABLE` left every one of them at the default zero, and the view publishes
`Ordinal + 1`.

---

## 10. Studio's exit never wrote a database down

> **FIXED**, 2026-08-07. `ApplicationViewModel.CloseDatabases`, called first and
> synchronously from `MainWindow.OnClosing`. Regression tests:
> `ExitFlushTests.ClosingTheDatabasesWritesTheHeaderTest` and
> `TheDatabasesAreClosedBeforeTheFirstAwaitTest`, both measured against the defect.

**This was filed as "a table rebuild through Studio left two files unreadable" and
the rebuild had nothing to do with it.** It is the workload that churns the schema
catalogue hardest - the catalogue is one big value in an overflow chain, rewritten
and freed on every DDL statement - so it correlated, and sixteen controlled runs
outside the application could not reproduce it because every one of them closed the
database properly.

### What the files said

Read underneath the engine - no page manager, no catalogue - both casualties carry
the same signature and the control from the same session does not:

| | header `TotalPageCount` | pages in the file | free list | freed pages |
|---|---|---|---|---|
| `stage8.witdb` (broken) | 10 | **12** | empty | 9 and 10, holding `$schema:_tables` |
| `stage8b.witdb` (broken) | 9 | **11** | empty | 7 and 9, the same |
| `stage8c.witdb` (control) | 3 | 3 | empty | none |

**The header on disk was older than the pages.** A freed page is distinguishable
from a page the file merely grew into: `FreePage` writes a full `PageHeader` -
`FreeSpaceStart = pageSize` - and touches only the first 16 bytes, so the body
survives. Pages 9 and 10 still read `Customers | Name3 | Email3`. A page the file
grew into is 16 zero bytes and an empty body. Both files have both kinds.

### Why that makes a file unreadable

Only `Flush` writes the header - `PageManager.Flush` writes it first, then the
cache, then storage. A page reaches the disk on its own whenever the cache
**evicts** it (`EvictSlot` writes if `IsDirty`). So a process that ends without
disposing leaves a mixture: evicted pages are current, unevicted pages and the
header are not. The catalogue then points at a page whose free was recorded while
the update that moved the pointer off it was not - `Page N is not an overflow page`.

Reproduced on demand: create, churn, then `Environment.Exit(0)`. With the default
cache the whole file fits, nothing is ever evicted, and the file is a consistent
OLDER snapshot that opens cleanly - which is the quiet form of the same defect,
silently missing everything since the last flush. With `CacheSize=8` the abandoned
file is **BROKEN** and the same work closed properly **OPENS**.

### The cause

`MainWindow.OnClosing` is `async void`. Its first `await` - saving the window state
- hands control back to Avalonia, which closes the window and ends the process.
**Everything after that await never runs.** `Program.Main`'s `finally` is entered,
`ServiceProvider.Dispose()` is entered and never returns, and
`ConnectionManager.Dispose` was never reached at all. Traced with a writer that
bypasses the logger, since the logger is itself being disposed.

Putting the close *after* the await changed nothing, and that measurement is what
named the cause. It is now first, and synchronous.

**Measured in the shipping application, by the invariant that a closed file's header
must count the pages the file has**: before, header 2 against 8 pages; after, 6 = 6,
and the file shrank to its real size.

### The rebuild button is armed again, and this is what settled it

The disarming was right at the time and wrong about its subject, so arming it needed
its own run rather than an inference. `CanRebuild` is now `Plan.Steps.Count > 0` -
the only reason left to refuse is an empty plan - and
`TheRebuildDialogWillNotRunItYetAsync` is replaced by `TheRebuildDialogIsArmedAsync`,
which pins both directions.

Six runs, 2026-08-07, all in the shipping application except the last, all read
afterwards with a raw dump and a reopen. The rebuild is a column type change
(`VARCHAR(200)` -> `INTEGER`) driven from the designer with the **Пересобрать…**
button, not with Apply:

| run | workload | how it ended | header vs file | reopen |
|---|---|---|---|---|
| 1 | rebuild, default cache | `File > Exit` | 36 = 36 | OPENS |
| 2 | rebuild, `CacheSize=8` | `File > Exit` | 36 = 36 | OPENS |
| 3 | rebuild, `CacheSize=8` | **killed** | 36 = 36 | OPENS |
| 4 | 24 DDL statements, `CacheSize=8` | **killed** | **32 against 61** | **BROKEN** |
| 5 | 24 DDL + rebuild, `CacheSize=8` | `File > Exit` | 64 = 64 | OPENS, 20 rows |
| 6 | the console probe | `Environment.Exit` | **5 against 32** | **BROKEN** |

**Run 3 is the one worth reading.** Killing Studio right after a rebuild left the
file perfectly readable - so runs 1 and 2 prove nothing on their own. A rebuild is
four statements; that is not enough churn to leave anything unevicted, and an
instrument that cannot produce the failure cannot certify its absence. Run 4 is what
gave the pair its power: the same twelve `CREATE TABLE`/`DROP TABLE` pairs the probe
uses, typed into Studio's own query editor and then killed, reproduce the casualties'
signature inside the application. Run 5 is therefore the real verdict - a workload
that *is* heavy enough to break, plus the rebuild, ending through the menu, comes
back whole. The rows were counted by scanning them, not with `COUNT(*)`, which on
this engine is separate state.

The 20 `Email` values came back as `0`. That is not damage: they were strings, the
dialog counted them before anything ran and said so on screen, and this engine's
`CAST` never fails.

### What is still open, and it is the engine's half

Studio no longer ends without flushing, but **an abrupt end still leaves an
unreadable file** - a crash, a kill, a power cut. Run 4 above is that defect
reproduced in the shipping application on demand, with no probe involved: work in
the query editor, kill the process, and the file will not open. Pages reach the disk
by eviction with nothing ordering them against the header, so any interruption can
leave the two at different vintages.

**And it belongs to ONE configuration, which is the default.** Measured 2026-08-07,
the same probe and the same workload each time - 400 rows and 240 DDL statements at
`CacheSize=8`, so the file is far larger than the cache and eviction is certain -
each run ended with `Environment.Exit(0)`, which runs no `Dispose` and no flush:

| configuration | header vs file | reopen | rows |
|---|---|---|---|
| **MVCC - the default** | **52 against 635** | **BROKEN** | - |
| `MVCC=false` | 14 = 14 | opens | 400 of 400 |
| `MVCC=false;Journal=wal` | 14 = 14 | opens | 400 of 400 |
| `MVCC=false;Journal=rollback` | 14 = 14 | opens | 400 of 400 |

So the transactional store **already survives an abrupt end** at a cache size that
forces eviction, and it does so with no journal at all. The MVCC store does not.

**The two journals cannot be combined with MVCC**, and the builder says so out loud:
*"A transaction journal cannot be combined with MVCC: the MVCC store keeps its own
versions and takes no journal."* So the default configuration has no crash-recovery
mechanism, and no setting turns one on.

The first comparison of these journal modes was **worthless and looked fine**: at the
original scale a non-MVCC database is two pages, so nothing is ever evicted and every
arm came back clean, journal or no journal. What the scale bought was the ability to
fail. Nothing about a passing run says which of the two it was.

Whether this is fixed in the storage layer or recorded as a deliberate limit of the
MVCC store is **not decided here**.

---

## Verifying a fix

WitAnalytics is a ready-made regression harness: its stats test fixture runs the
whole read-side query suite against **both** providers, and its migration suite
covers the schema-evolution path.

```
git clone https://github.com/dmitrat/WitAnalytics
dotnet test OutWit.Analytics.Tests/OutWit.Analytics.Tests.csproj
```

The WitDatabase side needs no configuration (embedded file backend). To include
the PostgreSql half for a side-by-side comparison, set `WITANALYTICS_PG_TEST` and
point it at a `postgres:16-alpine` container.

For issue 1 specifically: un-squash the WitDatabase migration history — split
`ExcludedPaths` back out of `InitialCreate` into its own migration — and the
suite reproduces the failure on the first run against a fresh database.
