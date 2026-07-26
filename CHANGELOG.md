# Changelog

## 2.0.0

A correctness release. It comes out of a full audit of the engine and both providers
([Docs/AUDIT-2026-07.md](Docs/AUDIT-2026-07.md)) and fixes both halves of the schema-evolution
blocker in [Docs/KnownIssues.md](Docs/KnownIssues.md), three silent data-corruption paths, and
several cases where a query returned the wrong rows without any error.

Almost everything here changes behaviour that the previous release's 3,700 tests agreed with. That
is the point: the tests asserted on generated SQL strings, on `COUNT(*)`, and on clean shutdown,
none of which could see these defects. Every fix below ships with regression tests that execute SQL
and read rows back.

### Breaking

- **Default table names now come from the `DbSet` property, not the entity type.** The EF Core
  provider never registered an `IProviderConventionSetBuilder`, so EF fell back to the *core*
  builder and the whole relational convention set — including `TableNameFromDbSetConvention` — was
  absent. The same model produced `Website` here and `Websites` on SQL Server, PostgreSql and
  SQLite. Migrations were therefore not portable between providers, which is what made a
  hand-written `AddColumn(table: "Websites")` fail with `Table 'Websites' not found`: the table
  genuinely did not exist.

  Existing `.witdb` files carry the old singular names. Either recreate them, or pin the old name
  per entity with `ToTable("Website")`.

  This also restores `RelationalValueGenerationConvention` (identity and computed columns),
  `RelationalDbFunctionConvention` (`HasDbFunction`, i.e. user-defined functions could not have
  worked at all), `SharedTableConvention` and `StoreGenerationConvention`.

- **A commit now flushes to storage before it returns.** MVCC is the default transactional mode
  behind the ADO.NET and EF Core providers, and its commit path returned without flushing anything,
  so a successful `COMMIT` was lost by a process kill with no journal to replay it from. Durable
  commit costs throughput; `WithAsynchronousCommit()` is the explicit opt-out for a disposable test
  database or a re-runnable bulk import.

- **A numeric literal with a decimal point and no exponent is now exact (DECIMAL), not `double`.**
  This is what SQL, PostgreSql and SQL Server do. `12345678901234.5678` inserted into a
  `DECIMAL(28,10)` column previously read back as `12345678901234.6`.

- **A caller-supplied storage is now encrypted.** `WithStorage()` — and therefore
  `WithIndexedDbStorage()`, the documented Blazor WASM path — bypassed the encryptor while the
  header still recorded `ProviderFeatures.Encryption`. If the storage's page size cannot hold the
  per-page nonce and tag, the build now fails with the size it needs instead of silently writing
  plaintext.

- **An unreadable schema record now throws `WitSchemaCorruptException`** instead of yielding an
  empty catalog. Previously any deserialization failure produced `Table 'X' not found` for every
  statement, and the next DDL statement overwrote the record — turning a recoverable file into a
  permanently lost schema, silently.

- **A database file from a newer major format version is rejected** rather than parsed as though its
  layout were the current one. `FORMAT_VERSION` was written and never compared.

### Fixed — silent data corruption

- `DROP COLUMN` re-serialized surviving rows against the *pre*-drop column list, so every column
  after the dropped one was written under its neighbour's type. Dropping the middle column of
  `(Id INT, Name VARCHAR, Age INT)` rewrote `Age = 42` as `2`.
- `DROP TABLE` deleted neither the rows nor the indexes. A table recreated under the same name
  silently served the dropped table's contents.
- Row keys were built from the caller-supplied identifier while the catalog resolves names
  case-insensitively, so `INSERT INTO users` after `CREATE TABLE Users` wrote into a key space
  nothing could read, and `TRUNCATE TABLE users` deleted no rows while resetting the rowid counter —
  so subsequent inserts overwrote live rows.
- A rejected `INSERT` left the row in the store (invisible to `COUNT(*)`, which reads the catalog's
  counter); a rejected `UPDATE` left the new values. Both now compensate.

### Fixed — wrong results

- **No three-valued logic.** `NULL < 5`, `NULL <> 5` and even `NULL = NULL` evaluated to TRUE, so
  rows with a NULL column leaked through ordinary `WHERE` filters — `Where(u => u.Age < 18)`
  returned people with no recorded age. `AND`/`OR` now follow the SQL truth tables too
  (`NULL AND FALSE` is FALSE; `NULL OR FALSE` is NULL).
- **`LIKE` swallowed the rest of the predicate.** `WHERE Name LIKE 'a%' AND Age > 18` parsed as
  `Name LIKE ('a%' AND Age > 18)` and matched nothing; `DELETE … WHERE Name NOT LIKE 'p' AND Id = 5`
  deleted every row in the table.
- **Prefix `NOT` bound tighter than every comparison**, so `NOT Age > 18` meant `(NOT Age) > 18`.
- **`.Skip(n)` without `.Take(n)` returned nothing.** The provider emitted SQLite's `LIMIT -1`
  placeholder and the engine took it literally. A negative limit now means unbounded, `OFFSET`
  without `LIMIT` is accepted by the grammar, and the provider emits the standard form.

### Fixed — other

- `dotnet ef migrations add` produced an empty migration while still updating the model snapshot, so
  the model and the database diverged permanently and silently. `WitModelRuntimeInitializer` handed
  the migrations differ a read-optimized model; `WitMigrationsModelDiffer` swallowed the resulting
  exception. Both are removed — EF Core's stock implementations are correct once the convention set
  builder above is registered. `Down()` is no longer empty either.
- `CreateTable` operations lost `maxLength` and every other facet, and dropped unique and check
  constraints, because the custom differ rebuilt them by hand.
- Integer literals above `long.MaxValue` and `-9223372036854775808` threw a raw `OverflowException`
  out of the parser, making `UBIGINT`'s upper half unreachable.
- `FreeOverflow` released the next page in the chain rather than the one it had pinned, leaking a
  pin on the head of every freed chain.

### Packaging

- `Microsoft.EntityFrameworkCore.Design` and `Antlr4BuildTasks` are no longer public dependencies.
  Consumers were restoring Roslyn, MSBuild and a net472 build host into `bin/`, and on .NET 10 the
  vulnerable `System.Security.Cryptography.Xml` 9.0.0 with it.
- CI now fails on a known-vulnerable package in any shipped project, and on a build-time dependency
  reappearing in a nuspec.

### Known issues

- `BETWEEN` still swallows a following `AND` conjunct: `Age BETWEEN 1 AND 10 AND Flag = 1` parses as
  `Between(Age, (1 AND 10), Flag = 1)`. Same root cause as `LIKE`, but its `AND` sits structurally in
  the middle of the grammar alternative, so it needs the boolean layer split out of `expression`.
  Two `[Ignore]`d tests state the intended behaviour.
- Two `DbContext`s cannot open one `.witdb` concurrently (`FileShare.None`), which rules out the
  ASP.NET Core scoped-`DbContext` shape.
- `UseWitDbInMemory()` cannot persist across EF's per-operation connection close.
- At-rest encryption fails closed and does not leak plaintext, but its key schedule needs a rebuild:
  the salt is derived from the password, so it adds no entropy.
- The remaining items are listed in [Docs/AUDIT-2026-07.md](Docs/AUDIT-2026-07.md) §3.

## 1.1.0 and earlier

See the git history.
