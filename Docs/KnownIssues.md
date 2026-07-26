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

| # | Area | Severity | One line |
|---|------|----------|----------|
| [1](#1-alter-table-add-column-is-unusable-schema-cannot-evolve) | EF migrations + engine DDL | **Blocker** | A schema can be created but never changed |
| [2](#2-inline-date-literals-are-rejected-by-the-parser) | EF query translation | Major | `WitSqlParsingException` on an inlined `DateOnly` |
| [3](#3-intstring-conversion-inside-a-query-is-not-translated) | EF query translation | Minor | `group.Key.ToString()` does not translate |

---

## 1. `ALTER TABLE ADD COLUMN` is unusable — schema cannot evolve

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
