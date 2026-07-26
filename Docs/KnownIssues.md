# Known issues

Defects found by using WitDatabase as a real application backend, not by unit
testing it. They come from **WitAnalytics** (`dmitrat/WitAnalytics`), which ships
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
