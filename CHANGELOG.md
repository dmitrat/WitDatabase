# Changelog

## 2.1.0

The first release out of the verification pass over the 2026-07 audit's backlog. Every one of the
104 findings that audit raised but never attacked now has a verdict backed by a running test
([Docs/NEXT-SESSION-PLAN.md](Docs/NEXT-SESSION-PLAN.md), workstream B); ten of the confirmed defects
are fixed here.

Roughly four claims in five survived scrutiny. The number worth knowing is the other one: twenty
needed restating, and about half of those *understated* the defect. Each fix below closes a test
that had been left `[Ignore]`d with the behaviour actually observed, so the fix and its proof arrive
together.

### Security

- **The connection-string password no longer reaches the log.** `LogFragment` appended the
  connection string verbatim and `PopulateDebugInfo` copied it into the debug dictionary, and EF
  Core writes `LogFragment` at Information level the first time a context is used — so an
  encryption password landed in ordinary application logs:

  ```
  Using WitDatabase 'Data Source=app.witdb;Password=hunter2'}
  ```

  Only the `Password` value is replaced; the `Data Source` and the other parameters stay, because a
  log line that says nothing is its own kind of failure. It **fails closed**: a connection string
  that cannot be parsed is withheld entirely rather than logged as it stands. The service-provider
  cache key is unaffected — it still sees the real string, so two connection strings differing only
  by password continue to get different providers.

### Behaviour changes

Every item here changed an answer the previous release gave. That is the point of the release, but
it means results can differ after upgrading.

- **`LAST_VALUE` and `NTH_VALUE` now follow the standard frame.** An `OVER` clause with an
  `ORDER BY` and no frame clause defaults to `RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW`;
  the engine treated it as the whole partition, so `SUM(x) OVER (ORDER BY y)` returned the partition
  total instead of a running total.

  With the correct default, `LAST_VALUE` returns the **current row's** value, not the partition's
  last. This is the best-known gotcha in window functions and what PostgreSQL, SQL Server, Oracle
  and MySQL all do. To get the partition's last value, name the frame:

  ```sql
  LAST_VALUE(Name) OVER (PARTITION BY Department ORDER BY Salary DESC
                         ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING)
  ```

  Known gap, deliberately left open: the default frame is typed `RANGE`, but peers — rows with equal
  `ORDER BY` values — are not yet grouped as `RANGE` requires. It affects ties only.

- **Scalar functions propagate NULL.** `LENGTH(NULL)` was `0`, `UPPER(NULL)` was `''`,
  `YEAR(NULL)` was `1`, `ROUND(NULL)` was `0` — wrong answers rather than missing ones, and they
  propagated into comparisons and aggregates unnoticed. SQL scalar functions are strict, so the rule
  is now general: a NULL argument yields NULL. Exempt are the functions that exist to inspect or
  replace a NULL (`COALESCE`, `NULLIF`, `IFNULL`, `NVL`, `TYPEOF`) and the JSON constructors and
  inspectors, because JSON has a null of its own — `JSON_ARRAY(1, NULL, 'hello')` must still build
  `[1,null,"hello"]` and `JSON_TYPE(NULL)` must still answer `"null"`.

- **`LIKE` matches across newlines, ignores the ambient culture, and no longer tolerates a trailing
  one.** The pattern compiled to a .NET regex with only `IgnoreCase`, which meant `%` and `_` could
  not cross a newline, `LIKE 'abc'` accepted a string ending in a newline (because .NET's `$` also
  matches immediately before a final one), and `'I' LIKE 'i'` gave different answers under the
  invariant culture and under `tr-TR`. Now `Singleline`, `CultureInvariant`, and `\A`/`\z` anchors.

  `IgnoreCase` is deliberately unchanged. WitSQL.md neither documents nor rules out LIKE's case
  behaviour, and altering it would silently change results for every consumer — a semantics
  decision, not a defect fix.

- **`SELECT DISTINCT … LIMIT n` returns n distinct rows.** `LIMIT` ran before `DISTINCT`, so the
  limit truncated the rows the duplicates were drawn from: with four distinct values,
  `SELECT DISTINCT Category FROM T LIMIT 3` returned one row.

- **`ORDER BY … NULLS FIRST | NULLS LAST` is honoured.** It was parsed and then discarded by the
  sort comparator. The null order is resolved *before* `ASC`/`DESC` is applied, because the two are
  orthogonal: reversing the direction must not move the NULLs.

- **MySQL-style `LIMIT offset, count` binds its operands in order.** They were bound backwards, so
  `LIMIT 10, 5` meant "skip 5, take 10" — over 20 rows it returned 6..15 instead of 11..15. The
  comma form now has its own branch; it cannot share one with `LIMIT count OFFSET offset`, because
  the same positions mean opposite things in the two forms.

- **`MERGE` no longer gives the target the source's alias.** Both aliases are optional, so an index
  into the parsed alias list does not identify them: with only the source aliased,
  `USING Source AS s` was read as the target's alias and every unqualified reference resolved to the
  wrong table.

### Fixed

- **`ALTER TABLE … ADD COLUMN` rejects a duplicate column name** instead of appending it again.
  Replaying a migration — a partially applied one, or a script run twice — left the catalog holding
  the same column twice and widened every row a second time, with nothing reported.

### Tests

The verification harness ships with the release, under `AuditVerification/` in each test project.
A confirmed-but-unfixed defect is a test asserting the **correct** behaviour, marked `[Ignore]` with
the behaviour observed, so it turns green the day the defect is fixed; refuted and latent findings
stay as passing pins. 100 such specifications remain.

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

### Fixed — snapshot isolation

- **A snapshot could observe a transaction's writes partially applied.** Commit timestamps and
  snapshot timestamps came from the same counter, and the commit timestamp was allocated *before* any
  version was installed — so a reader could take a snapshot above a commit that had only partly
  landed, and see some of its keys updated and others not. Snapshots now read a published watermark
  and a commit installs everything before publishing it, so a transaction is visible entirely or not
  at all. The same change closes a lost update where two writers could both pass conflict validation
  and both install.

  This defect predates 1.1.0; it was found because its stress test fails about one run in five, on
  the released code as well.

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
- In the MVCC path: the tombstone written when a version is superseded is not transaction-gated and
  rollback does not revert it, so a failure part-way through a commit still destroys the previous
  value; pruning the committed-transaction map can make committed data invisible.
- The remaining items are listed in [Docs/AUDIT-2026-07.md](Docs/AUDIT-2026-07.md) §3.

## 1.1.0 and earlier

See the git history.
