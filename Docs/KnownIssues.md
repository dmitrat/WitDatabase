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
| [10](#10-a-table-rebuild-through-studio-left-two-files-unreadable) | Storage | **Blocker** | **open — not reproduced headlessly** | A rebuilt database cannot be opened again |

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

## 10. A table rebuild through Studio left two files unreadable

> **OPEN.** Reproduced twice in the shipping application; **not reproduced in
> sixteen controlled runs outside it**. Studio's rebuild is disarmed because of
> this — it plans the work and hands the script to the query editor rather than
> running it.

Create a database through Studio's Create dialog, run a schema script, rebuild a
table through the designer, leave through **File > Exit**, reopen: the file cannot
be opened, by Studio or by anything else.

```
System.IO.InvalidDataException: Page 9 is not an overflow page
   at PageManagerOverflow.GetOverflowInfo(UInt32 firstPage)
   at PageManagerOverflow.ReadOverflow(UInt32 firstPage)
   at BTree.CollectPageEntries(...)
   at MvccKeyValueStore.GetRecordAsOf(...)
   at SchemaCatalog.GetSchemaRecord(...)
   at SchemaCatalog.LoadSchema()
```

So a schema record's overflow chain points at a page that is no longer an
overflow page — freed and reused while something still referenced it. The second
file failed identically at page 7.

**What has been ruled out.** Sixteen runs, each reopening the file afterwards, all
correct: the rebuild alone; without the trigger; without the index; without
either; with an extra `ADD COLUMN` before it; the same statements typed by hand;
over 2000 rows; with one and with four readers scanning the table throughout;
with a second database open in the process; at page sizes 512, 1024, 4096 and
8192; through the engine directly and through the ADO.NET provider; and with the
catalogue being read by four connections while the rebuild ran. The control
**without** a rebuild — same creation path, same script, same clean exit —
reopens correctly, which is what implicates the rebuild rather than anything
around it.

The instrument was checked rather than assumed: `SharedDatabase.Release` disposes
the database when the last lease goes and `Acquire` builds a new one with a fresh
`SchemaCatalog`, so those sixteen reopens really did re-read the file.

**Evidence kept.** Both damaged files are held from the session that produced
them; they are the fastest way in for whoever picks this up — the chain can be
walked from the page manager to find which record points at the freed page.

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
