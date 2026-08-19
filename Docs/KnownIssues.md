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
| [2](#2-inline-date-literals-are-rejected-by-the-parser) | EF query translation + grammar | Major | **FIXED** | `WitSqlParsingException` on an inlined `DateOnly` — and then, worse, no rows at all |
| [3](#3-intstring-conversion-inside-a-query-is-not-translated) | EF query translation + query planner | Minor | **FIXED** | `group.Key.ToString()` does not translate — and behind it, no grouped query could be ordered by an expression |

A full audit of the engine and both providers was carried out in 2026-07. It is a
working paper rather than documentation and is not published here; its §0 - the
execution-verified part - supersedes the analysis below wherever the two disagree.

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

> **FIXED, 2026-08-09 — and the interim workaround was worse than the defect.**
>
> **The grammar has typed temporal literals now**, and the rule is that **the word in front decides
> the type**, spelled the way the type is spelled in DDL:
>
> | literal | value |
> |---|---|
> | `DATE '2026-07-01'` | `DateOnly` |
> | `TIME '13:45:30'` | `TimeOnly` |
> | `TIMESTAMP '2026-07-01 13:45:30.1234567'`, or `DATETIME '…'` | `DateTime` |
> | `DATETIMEOFFSET '2026-07-01 13:45:30 +03:00'` | `DateTimeOffset` |
>
> `TIMESTAMP` carrying an offset is **refused by name** rather than truncated — the message says to
> write `DATETIMEOFFSET`. PostgreSQL accepts that shape and silently discards the offset, which is one
> row meaning two different instants in two databases.
>
> **What had happened in between, and it is the part worth reading.** Rather than wait for the
> grammar, the EF provider was given four custom mappings that emit a plain quoted string. That
> parses — and **measured 2026-08-09, it answers with nothing**: a row written by this provider could
> not be found by the very text this provider writes, because text is not converted to a temporal
> column's type before a comparison.
>
> ```
>   1 rows   a DATETIME found by a typed literal
>   0 rows   a DATETIME found by the very text it was written with
>   0 rows   a DATETIMEOFFSET found by the very text it was written with
>   1 rows   CONTROL: the row is there at all
> ```
>
> A loud parse error had been traded for a silently empty result set — which is exactly what the
> analysis below warned would happen, written before the workaround was made. `DateOnly` was the
> exception and is why it went unnoticed: an ISO date is ten characters with nothing after it, so the
> quoted form and the typed form happen to agree.
>
> **Where the fix lives:** the grammar (`WitSqlParser.g4`), the visitor that turns a literal into a
> typed value, the serializer that writes one back, `AsLiteral` so a column DEFAULT keeps its type
> instead of becoming text, the four EF mappings, and Studio's dump — which used to write
> `'yyyy-MM-dd HH:mm:ss'` and lost the fraction of a second as well as the type.
>
> **Regression tests:** `ExpressionParserTests` (the keyword decides the type; DATE and TIME are still
> function names), `AuditVerification/TypedTemporalLiteralTests` in the engine (including two cases
> that **pin** the text-versus-temporal comparison as it is, so a future change to it has to go red on
> purpose), and `Integration/InlinedTemporalConstantTests` in the EF provider — four of whose eight
> cases go red again if the mappings are put back to quoted strings.

> **The original analysis, kept for the record and no longer current.** It was root-caused
> here and fixed on 2026-08-09 - see the banner at the top of this entry, and issue 20 for the
> comparison rule that was the other half of it. This is not a
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

> **FIXED, 2026-08-09 — and the missing translator was hiding a planner defect behind it.**
>
> `WitConvertMethodTranslator` turns `x.ToString()` and `Convert.ToString(x)` on a primitive into a
> SQL `CAST`, so the conversion happens in the database. Only where it really is a conversion:
> `ToString()` taking a format or a culture is left alone — a `CAST` would ignore the format and
> answer with something else — and the temporal types are left alone too, because `DateTime.ToString()`
> renders in the **current culture** and a query whose result depends on where it ran is a defect, not
> a feature.
>
> **What was underneath.** With the translator in place the issue's own query still failed, now inside
> the engine. EF emits
>
> ```sql
> SELECT CAST("e"."DeviceType" AS TEXT) AS "Key", COUNT(*) AS "Count"
> FROM "Events" AS "e" GROUP BY "e"."DeviceType"
> ORDER BY CAST("e"."DeviceType" AS TEXT)
> ```
>
> and a grouped row carries **only the SELECT list**, so the `ORDER BY` was evaluated against a row
> with no source columns in it: `Column 'DeviceType' not found`. The planner knew exactly two shapes,
> an aggregate call and a column whose name matches a select alias, and let everything else through
> unchanged — so **no grouped query could be ordered by an expression at all**, cast or arithmetic,
> whether or not EF was involved. It now resolves an `ORDER BY` expression that is the same expression
> as a select item to that item's position, compared as the canonical text the serializer writes
> (the AST's own equality includes the line and column a node was parsed at, and the two occurrences
> of one expression are by definition in two different places).
>
> **Named remainder, refused loudly rather than answered wrongly:** ordering by a grouping column that
> is **not** in the SELECT list is still refused. Standard SQL allows it; this engine would have to
> make a grouped row carry its key, which is a change to what a grouped row is. Pinned by
> `GroupedOrderByExpressionTests.OrderingByAGroupingColumnThatIsNotSelectedIsStillRefusedTest`.
>
> **Regression tests:** `Integration/ConvertToStringTranslationTests` in the provider (five cases,
> three of which were red on the untranslated provider with *"Translation of method 'int.ToString'
> failed"*) and `AuditVerification/GroupedOrderByExpressionTests` in the engine (six, four of which go
> red again if the resolution is removed).

> **The original analysis, kept for the record and no longer current** - it was fixed on
> 2026-08-09, see the banner at the top of this entry. **The engine was not at fault** — all four conversion forms
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

### The cause, narrowed to one sentence and proved by running it

**A DDL statement in autocommit never reaches a `Commit`, and a `Commit` is the only
thing that flushes.** Splitting the same workload says it without any reading:

| workload, MVCC, ended with `Environment.Exit(0)` | header vs file | reopen |
|---|---|---|
| 400 inserts, no DDL | 52 = 52 | opens |
| 240 DDL statements in autocommit | **1 against 591** | opens **empty** - everything lost |
| **the same** 240 DDL statements inside an explicit transaction | **200 = 200** | opens, 2 tables |
| both, as the casualties were made | **52 against 635** | **BROKEN** |

DML is safe because the engine wraps every statement in an implicit transaction whose
commit flushes (`MvccTransaction.Commit` when `SynchronousCommit`, which is the
default; `Transaction.Commit` always). DDL gets no such wrapper:
`SchemaCatalog.PutSchemaRecord` finds no ambient transaction and calls `m_store.Put`,
and **neither** `TransactionalStore.Put` nor `MvccTransactionalStore.Put` flushes.

The third row is the fix pointing at itself: the identical statements, changed only
by being inside a transaction, leave the file whole.

Why it is MVCC that shows the damage: without versions the file stops growing, so a
stale header keeps counting the pages that exist. MVCC keeps every version, the file
grows past the header's last value, and the two are then at different vintages.

Two things worth knowing before choosing a fix. `PageManager.Flush` writes the header
**first**, then the cache, then storage - the opposite of the crash-safe order. And a
page reaches the disk on its own whenever the cache **evicts** it (`EvictSlot` writes
if `IsDirty`), with nothing ordering that write against the header.

### FIXED, 2026-08-07

**`SchemaCatalog.MakeDurable`** - an autocommit schema write is made durable where
it is written, so no schema writer added later can forget it. That is the same
reasoning the ambient-transaction routing already used, one line down.

**A flush and not a transaction of our own**, though the measurement that found the
cause used one. The commit's only contribution here is the flush at the end of it,
and opening a transaction would cost more than it buys: on the non-MVCC store a
transaction takes the database write lock for its lifetime, so the documented
out-of-contract caller - one that opens a transaction on one execution flow and runs
DDL on another - would move from a `LockRecursionException` to a **deadlock**.

**And `PageManager.Flush` writes the header LAST**, after the pages it counts are
durable. It used to write it first, which is the unsafe order: a header that
promises pages the file does not hold is unreadable, while one that is merely older
is not. The storage flush *between* the two is the half that is easy to omit -
without it the ordering lives in the source and not on the disk.

Pinned by `DdlAfterAKillTests` (the crash runner, a real process, killed - a
`CREATE TABLE` that returned is gone without the fix, and the database will not open
either) and `FlushWritesTheHeaderLastTests` (the order asserted on the storage,
because both orders end with the same bytes). Both measured red first.

**Two existing tests had been passing on this defect**, which is its own finding:
`AttributionAreTheUncommittedRowsOnTheMediaTest` counted every reachable record and
read zero - an answer about the schema wearing an answer about the rows - and the
async-only-storage pin recorded the boundary as sitting between `CREATE TABLE` and
the first `INSERT`, when the create only "survived" because it wrote nothing at all.

**Still open, and MEASURED 2026-08-09 - the paragraph that used to stand here was
right about one outcome of three.** It said only that a process dying in the MIDDLE
of a statement can leave a header of one vintage beside pages of another, that the
window is "identical for DML", and that closing it needs a journal.

What a kill in the middle of a statement actually leaves, measured through the
ADO.NET path with a real `TerminateProcess` and a guard that DISCARDS any run whose
kill arrived after the statement returned:

| configuration | subject | after the kill |
|---|---|---|
| **MVCC - the default** | `UPDATE` over 20,000 rows | **the database will not open**, 5 of 5 runs, same page every time |
| `MVCC=false`, no journal | the same | **opens, and the statement is HALF APPLIED** - 9,081 / 14,860 / 16,156 of 20,000 |
| `MVCC=false;Journal=wal` or `rollback` | the same | clean: the statement left nothing |
| **every configuration** | `CREATE INDEX` | opens; the index is in the catalogue and EMPTY, and the planner uses it - see issue 14 |

So three corrections. The failure under the default configuration is the
**unopenable** one, not the quiet one. The atomicity half - a statement runs in an
implicit transaction, so one that did not return must leave NOTHING behind - was not
recorded at all, and it is broken on the transactional store with no journal. And a
journal is **half** a remedy: it closes the DML half and cannot touch the index half,
because an index is a separate file the journal does not cover.

### The mechanism, attributed by counting rather than by reading

The same `UPDATE` inside an EXPLICIT transaction left nothing behind in 6 of 6 runs
at the same depth. That difference is now measured, and it needs no kill to see it -
count what reaches the storage while the statement is still running:

| store | how the statement runs | pages on the media BEFORE it ends | at the commit |
|---|---|---|---|
| `MVCC=false` | autocommit | **2,222** | - |
| `MVCC=false` | inside a transaction | **0** | 2,222 |
| MVCC (the default) | autocommit | **10,245** | - |
| MVCC | inside a transaction | **0** | 10,245 |

**A statement in autocommit writes through to the media as it runs; the same
statement inside an explicit transaction writes nothing until the commit.** The rule
is the same on both stores, so this is not about MVCC and not about a journal - it is
what an implicit per-statement transaction is.

That single fact explains every outcome above. Under MVCC ten thousand pages land
while the statement runs and the header is not among them, so the two are at
different vintages and the file will not open. Without MVCC two thousand land and
nothing can take them back, so the statement is half applied - unless a journal holds
the before-images, which is why `wal` and `rollback` come back clean. And an explicit
transaction is atomic against a kill in every configuration, because nothing has
reached the media to be atomic about.

**The consequence a user can act on today:** a large write wrapped in an explicit
transaction survives an abrupt end as all-or-nothing; the same write left to
autocommit does not.

**What a fix would have to choose between**, and none of these is free: make the
implicit transaction buffer the way an explicit one does, which costs memory in
proportion to the statement (this one would hold ~10,000 pages); give the paged store
a journal that can be replayed at open and let it be combined with MVCC, which it
currently refuses; or document the limit as a limit.

Evidence and the probes: `@Evidence/item8`, outside every working tree - `Mid` for
the kill and `Reach` for the counts above.

---

## Found by WitDatabase Studio, 2026-08-08

Both came out of the same measurement: executing a dump back into an empty database
for the first time. The dump's own two defects — every index written as `CREATE
UNIQUE INDEX`, and a trigger written as its bare body — were Studio's and are fixed
there. These two are the engine's.

---

## 11. A restored dump refuses the next generated key — and the cause was the MVCC key encoding

> **FIXED.** `Core/Stores/MvccKeyValueStore.cs`. Regression tests:
> `Core.Tests/AuditVerification/MvccPrefixKeyTests` (5 cases, 4 red against the
> unfixed store), `Engine/AuditVerification/RowIdCounterPrefixTests` (4 cases,
> written as pins and inverted when the fix landed) and
> `Studio.Tests/Services/DatabaseMigrationTests.AMigratedDatabaseCanTakeANewRowAsync`.

### The cause: the MVCC key encoding is not prefix-free

A version lives at `[key][8-byte inverted timestamp]`, and every version of one key
is found by scanning `[key]00·8 … [key]FF·8`. **That range does not contain only
that key's versions.** For `Orders` it also contains every version of
`OrdersAudit`, because `'A'` is 0x41 and the range runs to 0xFF — and 0x41 sorts
*before* a typical inverted timestamp, so the foreign record is usually the **first**
thing such a scan sees.

`MarkPreviousVersionDeleted` walks that scan and marks the first live record it
finds as deleted. So writing `$schema:_rowid:Orders` marked
`$schema:_rowid:OrdersAudit` deleted, and on the next open `OrdersAudit`'s counter
was absent, read as zero, and the next generated key collided with row 1.

**It was never about dumps.** A dump only makes it certain, because it writes every
table's counter in one run. Any database holding a table whose name begins with
another table's name was one insert away from it.

**And losing a counter was the mild half.** `MvccPrefixKeyTests` also pins the other
direction: a read of a key that does not exist returned the value of a longer key
that begins with it — a wrong answer rather than a missing one.

### The fix

A length test at each single-key version scan (`GetRecordAsOf`, `GetVersionCount`,
`GetAllVersions`, `MarkPreviousVersionDeleted`): inside the scanned range the range
already guarantees the leading bytes, so a versioned key belongs to `key` exactly
when it is `key.Length + 8` bytes long. Range scans over a key *range* are
unaffected — there a longer key is a legitimately different key, which is why they
extract the original key and deduplicate.

**The census after the change is empty**: Core 2315, engine 2358 (the four
`Performance` timing failures on this machine are pre-existing and were verified by
name), ADO.NET 1025, EF 544, Studio 730. The EF specification suite's 1198 failures
were measured **with the fix reverted** and are identical — pre-existing, and CI
runs a filtered subset of it.

### How it was found, because the shape recurs

Studio's dump could not be executed back into a database. **Fifteen controlled cases
built up from nothing failed to reproduce it** — they all used names like `A`/`B`
and `Alpha`/`Beta`, which cannot. Bisecting *down* from the fixture that did
reproduce it took four steps: `DELETE FROM Orders` made the copy healthy,
`Customers` survived where `OrdersAudit` did not, and the two names are `Orders` and
`OrdersAudit`. **A control set built from invented names cannot find a defect that
lives in the relationship between real ones.**

### What it looked like from the outside

A database rebuilt from a dump — which is also what a password change is, since
that is a migration — **could not take a new row in any table whose rows carried
their keys**. The first insert that asked for a generated key was refused with:

```
UNIQUE constraint failed: OrdersAudit.Id (duplicate value: 1).
The table's key counter is behind its rows; the insert was refused rather than
overwriting one.
```

That refusal is issue 5's fix behaving correctly — it refuses to overwrite row 1
rather than doing it silently, which is the better of the two failures, and it is
why this cost nobody any data. What was wrong is the state underneath it. And
**nothing in the migration report said so**: every table's rows were counted on
both sides, they matched, and the transfer reported itself complete.

---

## 12. `UPDATE OF` is accepted and ignored

> **FIXED 2026-08-09**, both halves in one piece of work. Covered by
> `AuditVerification/UpdateOfColumnsTests` (15 cases) and, in Studio, by
> `DatabaseDumpTests`, `TableRebuildTests` and `SchemaDialogTests`.

`CREATE TRIGGER T AFTER UPDATE OF Watched ON Source …` names the column the trigger
watches. The parser read the list, `DefinitionTrigger.UpdateColumns` stored it, and
**nothing on the firing path ever consulted it**: measured, the trigger fired on an
update of a column it did not name. The phase-7 class again — accepted, and then
ignored.

**It was tied to the dump, which is how it surfaced.** `INFORMATION_SCHEMA.TRIGGERS`
publishes no column for the list, so Studio's `CREATE TRIGGER` — assembled from the
catalogue — widened `UPDATE OF Watched` to every column. That lost nothing while the
engine ignored the clause as well, and **fixing the firing path alone would have turned
it into a silent fidelity loss in every dump**. So both halves landed together.

### What was decided, before any code

- **Where the catalogue publishes the list:** a new
  `INFORMATION_SCHEMA.TRIGGERED_UPDATE_COLUMNS`, which is where ISO/IEC 9075-11 puts it
  and the shape PostgreSQL publishes — one row per watched column, and **no rows at all**
  for a trigger that watches every column. `TRIGGERS` keeps the shape every other
  database has; a column of our own invention in a standard view would be a second place
  holding one fact.
- **When the clause fires:** the statement must **name** a watched column in its `SET`
  clause. Not "the value changed" — that is what SQLite and PostgreSQL do, and it keeps
  the answer a property of the statement rather than of the data. `SET Watched = Watched`
  fires; one row of a multi-row `UPDATE` cannot fire while its neighbour does not.
  `AssigningTheSameValueStillFiresTest` is that decision, and it is the case that tells
  the two readings apart — `modifiedColumns`, the wrong answer, is already computed a few
  lines away in three of the four paths.

### How it was verified

`UPDATE` has **four execution paths** and each fires triggers itself, so there is a case
per statement shape rather than one case with four assertions. The routing was measured
by instrumenting the four paths, not read off the code — and it turned up a fact worth
keeping: with a `BEFORE` trigger present, a statement naming a column that trigger does
not watch now takes the **fast** path, because the guard asks whether the trigger is
*reached* rather than whether it *exists*.

Power was measured the other way on the same day. With the filter returned to `true` —
the defect restored — **8 cases went red**: all four paths, both other timings, the
several-columns case and the replacement of the old pin. Narrowing the comparison to
`Ordinal` reddened the case-insensitivity case and nothing else. The old pin,
`UpdateOfIsAcceptedAndIgnoredTest`, **inverted on the first run of the fix** (expected 1,
got 0) and is replaced by `UpdateOfIsHonouredTest`.

**The census after the change is empty**: Core 2315, ADO.NET 1025, EF 544, Parser 797,
IndexedDb 153, Studio 803, engine 2373 passing (the four `Performance` timing failures on
this machine are pre-existing and were verified by name).

### The Studio defect this uncovered, which was not about triggers

The new "a replaced trigger still watches its column" case **could not fail**, and
finding out why took a probe: `SchemaChangeSet.ApplyAsync` ran `InPlaceStatements`, and a
trigger replacement is categorised `DropCreate`. Its `DROP` and `CREATE` were left out,
the report came back **empty**, an empty report **is complete**, and the trigger editor
said the trigger had been replaced, closed, and had changed nothing. Every earlier case
asserted the trigger COUNT afterwards, which one untouched trigger satisfies exactly as
well as one replaced. Fixed by running everything that carries statements; pinned by
`SchemaDialogTests.ReplacingATriggerActuallyReplacesItAsync`, which was run red against
the unfixed code first.

---

## 13. Three statements update a row and fire no trigger

> **FIXED 2026-08-09.** The three pins inverted. `AuditVerification/TriggerlessWritePathsTests`
> is seven cases now: the three paths, one per decision below, and a control that an INSERT
> which does NOT conflict still fires no UPDATE trigger. Previously pinned by — three cases, each
> with a control that proves the row really was updated and the trigger really is live.

`MERGE … WHEN MATCHED THEN UPDATE`, `INSERT … ON CONFLICT DO UPDATE` and a foreign key's
`ON UPDATE CASCADE` all rewrite a row **without firing any UPDATE trigger**. Measured
2026-08-09: in each case the value changes and the log stays empty.

Found while fixing issue 12, by grepping for the SHAPE rather than the site —
`Database.UpdateRow` is called from six places in `StatementExecutor` and only the four
in `Update.cs` fire triggers. The other three are `Merge.cs`, the `ON CONFLICT` branch of
`Insert.cs`, and the referential cascade in `Validation.cs`.

**What it costs:** an audit trigger is the commonest reason to write a trigger at all,
and it silently misses every row these three statements change. Nothing reports it.

### The three decisions, settled with Dmitry before any code

- **A cascade fires BEFORE and AFTER on the child, and a cancellation is an ERROR.** So is an
  `INSTEAD OF` trigger standing in for the write. Skipping the row would leave the child
  pointing at a key that no longer exists — the trigger would be handed the power to break
  referential integrity silently — so the statement is refused and names the trigger.
  PostgreSQL allows the skip and lets the constraint break; this engine does not.
- **An `INSTEAD OF` trigger DOES stand in** for the matched half of a `MERGE` and for
  `DO UPDATE`, because both are updates and one rule beats two exceptions.
- **The columns a cascade "names"**, for `UPDATE OF`, are the foreign key's own — exactly the
  ones it rewrites. A trigger on the FK column fires; one on another column does not.

**One case is named after what it measures rather than what it was written for.** A trigger
body cannot cancel from SQL — `ContextTrigger.Cancel` is set by the executor, not by anything
a body can write — so the explicit refusal is reachable only from a host that drives the
executor directly. The case exercises the neighbouring path, a body that fails, which lands in
the same place for the user, and says so.

---

## 14. A `CREATE INDEX` that failed left an index the planner used and the file could not answer

> **FIXED 2026-08-09.** `Engine/WitSqlEngine.Ddl.Indexes.cs` and
> `Core/Indexes/IndexManager.cs`. Regression tests:
> `Engine/Durability/HalfBuiltIndexTests` (five cases, three of them controls) and
> `Core.Tests/Indexes/IndexManagerDropTests` (measured both ways). One remainder is
> **pinned rather than fixed** - see the end.

A `CREATE INDEX` writes its catalogue entry first - and since issue 10 that write is
flushed where it is made - and only then fills the index from the table. So between
the two there is an index that is registered, empty, and **used by the query
planner**.

**Reaching it needs no crash at all.** Measured 2026-08-09 with 2,000 rows,
`CacheSize=8` and a thousand distinct values, the build itself fails - "Cache is full
and all pages are pinned" - and afterwards:

- the catalogue still names `IX_T_V`, and that entry is durable;
- `IX_T_V.idx` is one empty page;
- `SELECT Id FROM T WHERE V = 7` answers **0** rows where 2 of them match, with the
  database opening and the query succeeding.

`EXPLAIN` says why the answer is nothing rather than everything:
`SEARCH TABLE T USING INDEX IX_T_V (=)`. **A wrong answer with no error anywhere** -
worse than the loud halves of the same window, where the database refuses to open or
a statement is visibly half applied.

### Two causes, both in the failure path

- the `catch` read **every** `InvalidOperationException` as a unique violation, so an
  exhausted page cache was reported to the user as "UNIQUE constraint failed" - and
  every other way a build can end was not cleaned up at all;
- the cleanup ran `m_database.DropIndex` **first**, and that can throw for the same
  reason the build did, leaving `m_schema.DropIndex` on the next line unreached. The
  catalogue entry is the half that persists, so it is the half that must be removed
  whatever else happens.

`UniqueIndexViolationException` (deriving from `InvalidOperationException`, so nothing
that catches the base type stops working) is what lets the two be told apart. Exactly
one existing test depended on the old exact type, and it now asserts both.

### Also fixed: a drop that could not release what it dropped

`IndexManager.DropIndex` empties the backing store before releasing the index -
emptying matters because a persistent store keeps its entries under the index's own
name. `ClearBackingStore` says in its own comment that a drop must not fail because
the store could not be emptied, and it named two exception types; a third walked past
it and `index.Dispose()`, the line after, was never reached. The dispose is in a
`finally` now.

### Pinned, not fixed

That is **not enough to release the file**: the dispose CHAIN flushes, and on this
failure the flush throws in its turn, so a failed build still holds its `.idx` file
for the life of the process. `HalfBuiltIndexTests.AFailedIndexBuildStillHoldsItsFileTest`
pins it and says what the fix should invert. It is recoverable in practice because
nothing names that file any more - the database reopens.

### Named remainders, both measured

- **A killed `CREATE INDEX` still leaves the same state.** A kill runs no cleanup, so
  the ordering - catalogue first, content after - is what would have to change.
  Measured 8 of 8 across four configurations. At the default page cache the damage
  usually repairs itself instead, because the index file does not exist yet at the
  moment of the kill and `EnsurePhysicalIndexesExist` rebuilds a **missing** index.
- **An index that exists and holds SOME entries is trusted.** `BuildIndexFromExistingData`
  skips building when `Count > 0`, and `EnsurePhysicalIndexesExist` rebuilds only a
  missing index - its comment says an empty one is left alone deliberately, and the
  comment above it promises a lazy rebuild that does not happen. So a build that
  stopped part way and then got past the cleanup would be adopted as complete.

---

## 15. A column the query GROUPS BY cannot be reached from `ORDER BY` or `HAVING`

> **FIXED, 2026-08-10.** `Engine/Query/GroupingKeyReachabilityTests` is the fix's
> fixture - the pins are inverted, not deleted - and the older pin in
> `GroupedOrderByExpressionTests` went red on the first run exactly as its own text
> said it would, and now asserts the order.

A grouped row was built out of the SELECT list and nothing else, so a clause naming
anything else was evaluated against a row that does not have it:

```sql
SELECT COUNT(*) FROM T GROUP BY Kind ORDER BY Kind     -- Column 'Kind' not found
SELECT COUNT(*) FROM T GROUP BY Kind HAVING Kind > 'a' -- Column 'Kind' not found
```

Both are ordinary SQL and **PostgreSQL, SQL Server and SQLite all accept them.**
Adding `Kind` to the select list makes each work, which is the control.

### Two things the earlier record got wrong, both measured 2026-08-09

- **It is not "refused".** Nothing checks it and nothing says so. From `ORDER BY` the
  failure arrives as .NET's own **`Failed to compare two elements in the array`** -
  the sort could not compare two rows - with `Column 'Kind' not found` one level in.
  A consumer sees a sentence about arrays.
- **It was recorded as an `ORDER BY` limitation and `HAVING` has the identical
  hole**, which matters more: filtering groups by the column you grouped by is an
  everyday shape, while ordering by it is often a convenience.

### The fix: the grouped row carries its keys, and drops them again

`IteratorGroupBy.BuildResultRow` builds exactly the select list it is given, and the
planner's `ResolveAggregateExpression` can only rewrite an `ORDER BY` item to a
**column index in that list**. `PassesHavingFilter` evaluates against the same
projected row, so one mechanism serves both clauses:

- `QueryPlanner.BuildGroupedSelectList` appends every `GROUP BY` expression that is
  not selected already, so the grouped row can answer for it;
- the carried columns keep their **natural names**, which is what lets an expression
  *over* a grouping column - `ORDER BY UPPER(Kind)` - resolve by ordinary evaluation
  rather than by the planner recursing into every node type;
- `ResolveCarriedGroupingKeys` rewrites a carried expression appearing in `HAVING` to
  a reference to the column it is carried in. This is needed only when the KEY is an
  expression (`GROUP BY UPPER(Kind) HAVING UPPER(Kind) > 'A'`); a plain grouping
  column is served by the carried name alone. Measured: with the rewrite removed,
  exactly that one case goes red;
- `IteratorHideGroupingKeys` drops the carried columns after the sort and **before**
  `LIMIT` and `DISTINCT`, so both count and compare the columns the query asked for.
  With the trim removed, four cases go red, `DISTINCT` among them.

**Nothing is carried when the query has neither clause to serve**, so the commonest
grouped query keeps exactly the plan it had; the case that asserts this is the
control on the fix's cost, and it is the only one that goes red if the carrying is
made unconditional. When keys *are* carried the plan shows `HIDE GROUPING KEYS`.

Only expressions the serializer can identify are carried: it renders any subquery as
the literal `SELECT ...`, so two different ones cannot be told apart, and a key
carried under the wrong identity would order by the wrong column. Those keep the old
behaviour rather than gain a wrong answer.

### What did not change, deliberately

A column that is neither grouped by nor aggregated is still unreachable from both
clauses, which is what all three target databases do with it. That is asserted as a
control: without it, "the grouping column is reachable" would be equally true of a
planner that answers with an arbitrary row's value.

The refusal is still the sort's own `Failed to compare two elements in the array`,
which says nothing useful. It is now reached only by a query that all three target
databases also refuse, so it is a poor message rather than a wrong answer.

### Both directions measured

With the carrying removed altogether, 15 of the fixture's 18 cases go red and the
three controls stay green. Census: engine 2442, Core 2323, ADO.NET 1025, EF 568,
Parser 808, IndexedDb 153, Studio 818 - all green; the EF specification suite's 1198
local failures were compared **by test name** before and after and the two sets are
identical.

---

## 16. `ORDER BY <position>` was accepted and did nothing

> **FIXED, 2026-08-10**, in the commit after the one that found it.
> `Engine/Query/OrderByOrdinalTests` is the fix's fixture - the pins are inverted.

```sql
SELECT Kind FROM T ORDER BY 1               -- rows came back in insertion order
SELECT Kind, Amount FROM T ORDER BY 2 DESC  -- likewise; the DESC was ignored too
SELECT Kind FROM T ORDER BY 99              -- accepted, not refused
```

The parser makes the integer an ordinary literal, nothing turned it into a position,
and `IteratorSort` evaluated it once per row: every row answered the same number,
every comparison was equal, and the sort was a no-op. **The answer was exactly what
the same query without any `ORDER BY` answers**, which is what the pins asserted -
"not sorted" would also be satisfied by a sort that is merely wrong.

PostgreSQL, SQL Server and SQLite all implement the positional form, so a query
written for any of them was quietly answered in the wrong order here. **That is worse
in kind than 15 was**, which at least failed loudly. It affected every query, not
only grouped ones.

The record of issue 15 named `ORDER BY 1` as one of the shapes that "already works
and must keep working". It never worked - which is why a record's *controls* are
claims to re-measure exactly as its findings are.

### The fix: two resolutions, because the clause runs in two places

- **Over a grouped, windowed or `VALUES` result the row already IS the output**, so a
  position becomes a reference to that column.
- **For an ordinary query the sort runs BEFORE the projection** - deliberately, so it
  can reach the source's own column names - so the row in front of it is the source's
  and column one of that is not the first selected column. There a position becomes
  the **N-th select item's own expression**, which is what the user would otherwise
  have had to write. Measured: using the first rule everywhere reddens 13 cases.
- **`SELECT *` is the shape whose output columns are not its select list.** A position
  there counts the source's columns minus the internal ones, so the `_rowid` every
  scanned row carries is not position four.

Out of range is refused, with the range in the message. A **carried grouping key**
from issue 15 is not reachable by position - it is not a column the query returns -
and one case holds that seam: counting the grouped row instead of the select list
reddens `SELECT COUNT(*) FROM G GROUP BY Kind ORDER BY 2` and nothing else.

### What is a position, measured against SQLite rather than assumed

Three of the corners are not guessable, so they were run through SQLite first:

| shape | SQLite | now |
|---|---|---|
| `ORDER BY 1 + 1` | a constant - sorts nothing | same |
| `ORDER BY '1'` | a constant - sorts nothing | same |
| `ORDER BY -1` | **a position**, refused as out of range | same |

The sign is read as part of the position for exactly that reason: the parser gives
`-1` as a unary negation over a literal, so without it that form would stay the
silent no-op this issue is about.

`SELECT *, Amount * 2 FROM T ORDER BY 4` is the one shape where this engine and
SQLite differ, and the cause is issue 17: SQLite expands the star, so position four
is the expression, while here a star sharing its select list is not expanded at all.
The refusal names that reason rather than sorting by the NULL the star becomes, and
its case goes red when 17 is fixed.

---

## 17. A grouped query answered with columns no group could answer for

> **FIXED, 2026-08-10.** `Engine/Query/GroupedQueryColumnRulesTests` is the fix's
> fixture; it replaces `SelectStarOverAGroupedQueryTests`, which pinned only the star.

The entry began as "`SELECT *` is only expanded when it is the only select item", and
measuring it found the star was the least likely way into a much larger hole:

```sql
SELECT * FROM T GROUP BY Kind      -- was: one row per group, ONE column, always NULL
SELECT *, Amount * 2 FROM T        -- was: two columns, the first NULL on every row
SELECT Kind, Amount FROM T GROUP BY Kind  -- was: Amount from an ARBITRARY row of the group
SELECT Kind, COUNT(*) FROM T       -- was: one row, Kind from the first row of the table
```

The last two need no star at all and are far likelier to be written. **The row and
group counts were right in every case**, which is what made the answers look like
data.

### Two changes, and only one of them was a decision

- **A star is expanded into the columns it stands for.** All three reference
  databases do this, so there was nothing to choose. The lone `SELECT *` of an
  ordinary query is deliberately left alone - the projection already answers that one
  directly, and expanding it would give the commonest query in the language a
  different plan for nothing.
- **Every column that is neither in `GROUP BY` nor inside an aggregate is refused**,
  in the SELECT list, in `ORDER BY` and in `HAVING` alike - PostgreSQL's and SQL
  Server's rule. **Dmitry's decision, taken with the cost measured first:** adopting
  it turned **one** test red across the engine, ADO.NET, EF, Studio and the 8,145-case
  EF specification suite, and that one was the pin recording the defect.

**The two had to land together.** The refusal alone would have let
`SELECT * FROM T GROUP BY Id, Kind, Amount` through - it is legal under the rule - to
the same NULLs it always gave. A rule that blesses a wrong answer is worse than no
rule.

### The strict form, deliberately

PostgreSQL also accepts a column functionally dependent on a grouped PRIMARY KEY -
`SELECT * FROM T GROUP BY Id` - and SQL Server does not. The stricter reading is
implemented because widening it later cannot break a query that works today, while
narrowing it could. A case pins that choice and says what to do if it is ever
revisited.

An output ALIAS is not a source column: `ORDER BY` and `HAVING` may still name one,
as they always could and as every reference database allows. Measured - without that
arm, four working cases go red. A qualified name and a bare one are the same column,
because a check that refuses more than it understands turns a working query into an
error, which is the one outcome worse than the defect it replaces.

### Both halves measured

With the refusal removed, 8 of the fixture's cases go red and every control stays
green; with the star expansion removed, 5, three of them cases the refusal alone
would have left answering NULLs.

---

## 18. `ORDER BY` and `LIMIT` over a `UNION` applied to the first arm only

> **FIXED, 2026-08-10.** `Engine/Query/OrderByOverASetOperationTests` is the fix's
> fixture - the pins are inverted. Found while fixing 16 and **pre-existing and
> independent of it**: the same thing happened when the clause named the column.

```sql
SELECT Kind FROM T WHERE Amount > 25
UNION ALL SELECT Kind FROM T WHERE Amount < 25
ORDER BY Kind;   -- was: c d a b, the first arm sorted and the second left where it was
                 -- SQLite answers a b c d, and so does this now
```

`QueryPlanner.Plan` applied `ORDER BY`, `LIMIT` and `DISTINCT` inside the
aggregate/non-aggregate branch and only then called `ApplySetOperations`, so each of
them was wrapped **by** the union rather than wrapping it. The parser was never at
fault: it hangs the clauses on the outer statement, which is where SQL puts them.

**The `LIMIT` half lost rows rather than misplacing them.** Measured: `LIMIT 1` over
a two-arm union answered **three** rows - the first arm cut to one, the second
returned whole.

### The fix

A trailing `ORDER BY`, `LIMIT` and `OFFSET` belong to the whole set expression -
there is no way to attach one to an arm without parentheses - so the arm is planned
without them and `Plan` applies them after the arms are combined.

- **`DISTINCT` is deliberately NOT deferred.** `SELECT DISTINCT a FROM t UNION ALL
  SELECT b FROM u` de-duplicates the FIRST ARM, which is where SQL puts it and what
  this engine already did. A case pins that, and deferring it along with the other
  two - the easy mistake - reddens exactly that one case and nothing else.
- **An aggregate arm must not carry a grouping key for a clause that is not its own.**
  Issue 15 makes a grouped arm carry its grouping expressions when it has an
  `ORDER BY`; if the union's clause counted, the arm's schema would widen and the set
  operation compares the two schemas. The arm is planned as if it had no `ORDER BY`
  at all.

### A shape that now fails, and did not before

`ORDER BY` over a set operation may only name a **result column or a position**, as
PostgreSQL restricts it: after a union there is no source row left to evaluate an
expression against. Previously `… UNION ALL … ORDER BY Amount` did not fail - the
clause was applied to the first arm, whose source row still had `Amount`, so half the
answer was quietly ordered by something the caller could not see.

The refusal names the column and lists the ones there are. Without it the failure is
.NET's own *"Failed to compare two elements in the array"*, which is the message
issue 15 existed to get rid of.

### Why nobody had seen it

Two arms whose values do not interleave answer correctly whatever the plan does -
sorting each and concatenating gives the same list as sorting the whole. A case that
can fail needs the second arm to hold values that must come *before* the first arm's,
and that control is in the fixture beside the cases.

### Both directions measured

With the old clause order restored, 9 of the fixture's 13 cases go red and four stay
green: the two controls that cannot fail, the derived-table form, and an out-of-range
position, which is out of range for one arm as well.

---

## 19. Every second migration altered every sized column, both ways

> **FIXED, 2026-08-10.** Reported from WitAnalytics against 12.3.0 and **reproduced on
> 12.5.0**, so it had not been fixed in between. Pinned by
> `Storage/StoreTypeNameFacetsTests` in the EF provider's suite; evidence, including
> the generated migrations, in `@Evidence/differ`.

Generate a migration, add one property, generate a second: the second carried one
spurious `AlterColumn` **per sized column**, in both directions, and EF printed
*"An operation was scaffolded that may result in the loss of data"*.

```csharp
migrationBuilder.AlterColumn<string>(name: "Name", table: "Products",
    type: "VARCHAR(100)", maxLength: 100, nullable: false,
    oldClrType: typeof(string), oldType: "TEXT", oldMaxLength: 100);
```

**The `Down()` half is the dangerous one**: it narrowed each column back to `TEXT`.

### The cause

A model snapshot writes **both** `HasMaxLength(100)` and `HasColumnType("VARCHAR(100)")`.
The two resolved to different store types:

| how the column is described | resolved to |
|---|---|
| `HasMaxLength(100)` - the live model | `VARCHAR(100)` |
| `HasColumnType("VARCHAR(100)")` - the snapshot | **`TEXT`** |

`WitTypeMappingSource.FindMapping` cut the size off the name to look it up
(`GetBaseTypeName`) and then returned the shared unsized mapping, throwing the size
away. EF's differ compares the resolved types, so every sized column looked altered.

A store type name that carries facets now resolves to a mapping carrying those facets,
built the same way the CLR path builds it - so the two spellings of one column are one
mapping. A name whose facets cannot be read (`VARCHAR(MAX)`) or that has no use for
them (`INT(11)`) falls back to the plain mapping, as before.

### What the report did not say

**`DECIMAL(p,s)` and `VARBINARY(n)` had the identical fault** - `oldType: "DECIMAL"`
against `DECIMAL(18,2)` is in the same generated migration. The report named `VARCHAR`
because that is what the reporter's model had.

### The instrument that could not reach it

An in-process differ over two models built in one process reported **no operations at
all**, before and after the fix: both sides collapsed to the same answer, so the
comparison was quiet whether the defect was present or not. The reproduction that works
is the reported one - a scratch project referencing the provider, `dotnet ef migrations
add` twice, and read the file. The shipped regression test therefore pins the
**mechanism** at the mapping layer, where it is exact, and says why.

---

## 20. Text compared with a typed column was compared as two renderings

> **FIXED, 2026-08-10.** Pinned by `Engine/Query/TextComparedWithATypedColumnTests`.
> The other half of issue 2: the grammar and the provider were fixed then, the
> comparison rule was not. Evidence in `@Evidence/coercion`.

Every comparison between a text value and a value of another type fell through to an
**ordinal comparison of the two renderings**. That is not a near-miss - it gives wrong
answers, and the two worst are wrong in opposite directions on the same row:

| written | answered | should be |
|---|---|---|
| `N > '9'` with `N = 42` | no rows | the row |
| `N < '9'` with `N = 42` | the row | no rows |
| `S = '2026-07-01 13:45:30'` | no rows | the row |
| `S > '2026-07-01 13:45:30'` on that very instant | the row | no rows |

A `DateTime` renders as `2026-07-01T13:45:30.0000000` and nobody writes that; the `T`
sorts after a space, which is why the last line answers "greater" for an equal moment.

### It was recorded as a temporal-literal problem and it is not one

`DATE`, `TIME`, `GUID` and `BOOLEAN` happened to work, because their rendering **is**
the way a person writes them. So the defect was visible only where the rendering and
the writing disagree - `DATETIME`, `DATETIMEOFFSET`, and **every number**. An integer
column compared with a string parameter is the commonest shape there is, and it was
answering wrongly.

### The rule now

Text meeting a value of another type is **read as that type**, which is what PostgreSQL
and SQL Server do. Invariant culture throughout, so a stored value is not read
differently on a machine whose locale writes dates the other way round.

Text that is not a value of that type at all - `D = 'not a date'` - keeps the old
behaviour and answers "not equal" rather than being refused. **A comparison is not the
place to refuse**: a caller filtering on user input needs an answer, not an exception.
That case has a control, and so does text against text, which stays ordinal - on a
`VARCHAR` column `'42'` really does sort before `'9'`.

### Census

Empty. Engine 2524, Core 2323, Parser 808, ADO.NET 1025, EF 590, IndexedDb 153, Studio
818, and the EF specification suite's 1198 failures identical by name - a change to the
comparison primitive that moved nothing, which also says how little of this the suites
were covering. With the coercion removed, 7 of the fixture's 34 cases go red and every
control stays green.

---

## 21. The isolation level was accepted and applied to nothing

> **FIXED, 2026-08-10.** `Modularity/IsolationLevelIsAppliedTests` is the fix's fixture -
> the pin is inverted. Evidence in `@Evidence/isolation`.

A transaction opened at `Serializable`, `RepeatableRead` or `Snapshot` saw a row
another connection committed after it began - which is the one thing each of those
three levels exists to prevent. Every level answered identically, on a scan and on a
single-key seek.

### Neither the store nor the transaction was at fault

Both are correct, and measuring them is what made the attribution possible. At the Core
level, one MVCC database, a reader at `Serializable` and another transaction
committing: the reader stays at 1 row while `ReadCommitted` goes to 2. `MvccTransaction`
honours every level it is given.

**It was never given one.** `SET TRANSACTION ISOLATION LEVEL` recorded the level on the
**execution context**, and `WitSqlEngine.Execute` builds a fresh context per call - so
the level could survive to `BEGIN TRANSACTION` only when both statements arrived in one
batch. A driver that sends them separately always opened at the default.

And the ADO layer sent them separately **in the wrong order**: `BEGIN TRANSACTION`
first, then `SET TRANSACTION ISOLATION LEVEL`, which in SQL applies to the *next*
transaction. Either fault alone is enough; both were fixed, and each is measured -
restoring the order alone leaves the case red, and it was restoring the order alone
that first *refuted* the obvious explanation.

### Where the answer already was

Two `[Ignore]`d markers in `DropInGapsEngineTests`, written **2026-07-27**, carried the
whole diagnosis - one naming `WitDbConnection.cs:164` and the ordering, the other
saying in as many words that *"WitSqlEngine.Execute builds a fresh ContextExecution per
call, so through ADO.NET the level is silently DROPPED"*. Both are live tests now,
asserting the SQL rule they were describing.

**Four green tests were pinning the defect**, and all four asserted on
`context.PendingIsolationLevel` - the plumbing - using a single shared context, which is
the one arrangement in which the plumbing worked. They assert the transaction's actual
level now.

### Census

Engine 2525, Core 2323, Parser 808, ADO.NET 1025, EF 590, IndexedDb 153, Studio 818 -
green; the EF specification suite's 1198 failures identical by name. The `[Ignore]`
ledger is one lighter.

---

## Found by writing the documentation site, 2026-08-15

The three below are not defects to be fixed by surprise: each is behaviour the engine has, that a
document claimed otherwise about, and that an application has to be built around. All three were
measured on 2026-08-15, while checking what the documentation says against what the engine does; the
run itself is a working paper and is not published here, but every number below is quoted from it.

---

## 22. Write skew is permitted at every isolation level, `Serializable` included

> **NOT A DEFECT TO FIX QUIETLY.** `Concurrency/WhatEachIsolationLevelPreventsTests` pins it, in
> both directions. Preventing it needs predicate locking or serializable snapshot isolation, which
> this engine does not have; if a future change starts refusing it, those cases go red and the
> decision is visible rather than accidental.

Two transactions read the same rows, each writes a **different** row, both commit, and an invariant
that held for each of them separately is gone. Measured with the standard example: two doctors on
call, each transaction takes a different one off, both commit, and the ward ends with none.

```
SKEW a sees on call: 1 | 2        SKEW b sees on call: 1 | 2
SKEW a commits: ok                SKEW b commits: ok
SKEW on call afterwards: <no rows>
```

Nothing detects it, because neither transaction touched what the other wrote. **What `Serializable`
does prevent, also measured:** a transaction that reads a range, another that inserts into that
range and commits, and then a write from the first - the first is refused with a serialization
failure. And two transactions writing the same row: the second is refused.

**What this means for an application.** An invariant across ROWS - "at least one of these is true",
"these must sum to zero" - is not enforced by choosing a higher level. Enforce it by writing a row
both transactions touch, so the conflict becomes visible, or serialise the operation outside the
database.

**And a documentation defect behind it:** `WitIsolationLevel.RepeatableRead` promised that read
locks are held for the duration of the transaction. Every level above `ReadCommitted` here is
optimistic - the conflict is found at COMMIT and raised as an exception, so a caller who expected to
block gets an error and has to retry. Corrected 2026-08-15.

---

## 23. Partial indexes are never chosen by the planner

> Measured 2026-08-15. `OptimizerQuery.EvaluateIndex` returns null for any index whose definition
> carries a `WHERE`, with the comment *"For now, skip filtered indexes in automatic selection"*.

`CREATE INDEX ... WHERE ...` is accepted, the index is built, and it costs every write that touches
its table. No query will use it: index selection skips it before cost is considered.

The comment adds *"they can still be used with explicit hints"*, which is a claim nobody has
verified - it should not be repeated in documentation until somebody checks that a hint reaches this
path.

**Until it is implemented, a partial index is a cost with no benefit.** A full index on the same
column is the answer.

---

## 24. No page cache counts hits or misses, so there is no hit rate to report

> Measured twice, 2026-08-02 and 2026-08-09, and stated in `IPageCacheOccupancySource`'s own
> remarks.

Neither `PageCacheLru` nor `PageCacheShardedClock` counts a hit or a miss. **The hit rate is absent
from the engine rather than merely unexposed**, so no amount of plumbing publishes one. Only the LSM
`BlockCache` counts, and that is a different cache.

What the engine does publish is **occupancy**: how many pages the cache is holding and how many of
those are dirty, taken as a reading at the moment it is asked. Studio's Database tab shows it.

A monitoring page or dashboard that plots "cache hit ratio" for this engine is plotting something
that does not exist.

---

## Found by clicking through Studio before 3.1.1, 2026-08-19

Both of the engine entries below were reproduced against a bare `WitSqlEngine` over an in-memory
database, with no Studio in the picture. The third is a gap in Studio itself, recorded here
because a person meeting it will look for it here.

---

## 25. A join condition written the other way round is refused

> Measured 2026-08-19 on engine 14.0.0. **Root cause identified**, fix not written.

In `A JOIN B ON x = y`, the column of the LEFT input has to be written FIRST. Written the other
way, the query fails at execution with a `KeyNotFoundException`:

```sql
SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId   -- 3 rows
SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON o.CustomerId = c.Id   -- Column 'CustomerId' not found
SELECT Customers.Country FROM Customers JOIN Orders
     ON Orders.CustomerId = Customers.Id                                          -- the same, without aliases
```

**In a chain of joins, "the left input" is everything joined so far**, which is the shape most
likely to be met by accident:

```sql
SELECT c.Country FROM Customers c JOIN Orders o ON c.Id = o.CustomerId
                                  JOIN Items i ON i.OrderId = o.Id                -- Column 'OrderId' not found
```

**Root cause.** `Optimizers/OptimizerJoinCondition.TryExtractEquiJoinKey` builds the key pair as
`LeftKey = binary.Left, RightKey = binary.Right` - it takes **the written order of the equality**
for the order of the join inputs. It checks that the two column references carry different table
qualifiers and never checks WHICH input each belongs to, so `IteratorHashJoin.ComputeHashKey`
evaluates the right table’s column against rows of the left one and the evaluator throws.

**Where a fix belongs:** `Query/QueryPlanner.Sources.cs`, `CreateJoinIterator` - it already holds
both input iterators and therefore both schemas, so each pair can be oriented before the iterator
is built. Unqualified columns are already sent to the residual condition, so only the qualified
case needs it.

**What was measured, and what it means for a workaround:**

| written as | result |
|---|---|
| `INNER JOIN ... ON left.x = right.y` | works |
| `INNER JOIN ... ON right.y = left.x` | **fails** |
| `LEFT JOIN ... ON right.y = left.x` | works |
| `FROM a, b WHERE right.y = left.x` | works |

So: **write the left side’s column first**, or put the condition in `WHERE` with a comma join.
The failure is in the hash-join path, and the planner chose a hash join even for two-row tables.

**Why the suite never saw it:** every JOIN case in the engine writes the equality in the same
order.

---

## 26. `EXPLAIN` gives the right input’s child the wrong parent

> Measured 2026-08-19 on engine 14.0.0.

For `SELECT c.Country, o.Total FROM Customers c JOIN Orders o ON c.Id = o.CustomerId LIMIT 3`,
`EXPLAIN` answers:

```
id parent detail
0  -1     LIMIT
1   0     PROJECT
2   1     HASH INNER JOIN
3   2     ALIAS c
4   3     SCAN TABLE Customers
5   2     ALIAS o
6   3     SCAN TABLE Orders     <- parent should be 5
```

`SCAN TABLE Orders` is reported as a child of `ALIAS c`. Anything that draws the plan as a tree -
Studio’s Plan panel does - draws the wrong tree, faithfully. **The renderer is not the defect.**

---

## 27. Studio: a function or a procedure can only be refreshed

> Studio 3.1.1. A gap rather than a wrong answer.

The tree’s context menu offers a routine exactly one item, `Refresh`. The engine has
`DROP FUNCTION` and `DROP PROCEDURE`, and the catalogue already carries the routine’s body - the
inspector on the right shows it - so both *View definition* and *Drop* are possible and neither is
offered. Every other kind of object in the tree was given its own menu in 3.1.0; routines were not
included.

Until they are: drop a routine by running the statement in a query tab.

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
