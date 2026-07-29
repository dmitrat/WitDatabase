# Changelog

## 3.0.0

Closes phase 3, the grammar. **Major rather than minor because this changes answers the previous
releases gave** — and in two cases the old answer was silently destructive rather than merely wrong.

The headline is `BETWEEN`. `WHERE Age BETWEEN 18 AND 65 AND Active = TRUE` used to return **nothing**,
and `WHERE Age NOT BETWEEN 1 AND 20 AND Active = 0` used to return **everything** — so
`DELETE … WHERE x NOT BETWEEN a AND b AND …` removed exactly the rows the `WHERE` clause was written
to protect. If you worked around either, remove the workaround.

### Behaviour changes

- **`BETWEEN` no longer swallows the conjunct that follows it.** The boolean layer is lifted out of
  the flat `expression` rule into `searchCondition` / `predicate` / `valueExpression`, so `BETWEEN`'s
  bounds sit one layer below `AND` and cannot reach it. The old parse produced
  `Between(Age, lower = (1 AND 10), upper = (Flag = 1))`.

  The defect is narrower than it was recorded as, and the correction is worth knowing: it fired only
  when `BETWEEN` was followed by `AND`. A trailing `OR`, a `BETWEEN` inside a `CASE`, and
  parenthesised subquery bounds were always correct.

- **`SELECT 0x1F` returns 31.** It used to return **0**: the lexer split the literal into the integer
  `0` and the identifier `x1F`, so the statement succeeded under an accidental alias. `Flags & 0x0F`
  did not parse at all. Hexadecimal literals are now 64-bit two's complement — `0xFFFFFFFFFFFFFFFF`
  is `-1`, not `18446744073709551615` — and a literal wider than 64 bits is refused rather than
  truncated.

- **`INSERT … DEFAULT VALUES` is accepted.** One row built from column defaults, auto-increment and
  `ROWVERSION`. `NOT NULL` is still enforced, so a table with a non-nullable defaultless column still
  refuses the insert.

- **A query needing `CROSS`/`OUTER APPLY` is refused at translation time.** The EF Core provider
  inherited `VisitCrossApply`/`VisitOuterApply` and emitted SQL its own parser could not read, so a
  correlated `Take` built a clean model and then died at execution with a syntax error naming a
  construct you never wrote. It now fails early with a message that names the LINQ shape and a way
  out. **This is a stopgap, not a settled limit** — the engine has no lateral joins yet.

- **`NOT EXISTS` produces the same AST by a single route.** The grammar carried the optional `NOT` in
  two places, which ANTLR resolved silently by alternative order. No visible change; recorded because
  the parse tree shape differs.

### Fixed

- An aggregate inside `BETWEEN` or `IN` in a `HAVING` clause is still refused — **not** fixed here,
  and now recorded with a failing test. `HAVING COUNT(*) > 1` works; `HAVING COUNT(*) BETWEEN 1 AND 5`
  raises. Pre-existing, confirmed against the parent commit.

- The expression serializer still replaces every subquery with the literal text `SELECT ...` — **not**
  fixed here, and now recorded with failing tests. It matters because the DDL path persists schema
  through it: **a view whose body contains a subquery is created successfully and then throws a parse
  error on every query against it.** Partial-index filters and `CHECK` constraints are affected the
  same way.

### Under the hood

- The grammar parses roughly **twice as fast** (193-entry corpus, 104 µs → ~53 µs per parse), and has
  **no ambiguous parses** where it previously had seven.
- The `LIKE` two-alternative workaround is collapsed back into one alternative with an optional
  `ESCAPE`; the `CASE` visitor no longer infers simple-vs-searched by counting expressions.
- New regression instruments: a 193-entry grammar corpus checked for ambiguity and serializer
  round-tripping, and a differential oracle that compares **answers** against SQLite rather than only
  whether SQL is accepted.

### Documentation

`WitSQL.md` now carries per-section status notes where it described unshipped behaviour: §22
user-defined functions and §23 stored procedures are **not implemented and still planned**, and §2.8
`CREATE TRIGGER` is **partly** implemented — reading `OLD`/`NEW` and `SIGNAL` work, assigning to `NEW`
does not parse.


## 2.4.0

Closes phase 2. Every one of the 2026-07 audit's 29 EF Core findings is now either fixed or restated
with what it actually is - and **nine of them were misattributed**, almost one in three. Each
correction came from running the same model on EF Core's SQLite provider rather than from reading
the code, which is what the oracle added in 2.3.0 exists for.

Minor rather than patch for the same reason as the last three releases: most of these change an
answer the previous release gave. The ones most likely to be noticed are that `DateTime.Now` returns
local time, that a `char` property works at all, that migration literals no longer depend on the
developer's locale, and that a bulk insert of more than one row with `SetOutputIdentity` no longer
fails outright.

### Behaviour changes

- **The schema name is dropped from DDL and DML alike.** `CREATE TABLE` emitted a bare `"T"` while
  queries and updates emitted `"public"."T"`, so the DDL and the DML disagreed about the table's
  name — and `public` is the one schema the model validator accepts, which made it the one value
  that broke. WitDatabase has no schemas, so the name is now ignored everywhere, as EF Core's SQLite
  provider does.

- **The bulk extensions write shadow properties, apply value converters, and `SetOutputIdentity`
  does what it says.** Three defects in one path, all of which came from reading values with
  reflection over `PropertyInfo`:

  - A shadow property has no `PropertyInfo`, so it was filtered out and never written — silently.
    They are not exotic: EF Core creates one for any relationship whose foreign key has no CLR
    property.
  - A property with a value converter had its raw CLR value sent straight through, so the
    conversion the model declares never ran and the value layer refused the type outright.
  - `SetOutputIdentity` added the generated key to the *insert* column list, so every row carried an
    explicit zero and the second collided with the first — **the option made any bulk insert of
    more than one row fail.** It now reads the generated keys back, which is what it documents.

  A store-generated column is written when the caller supplies values for it and left to the store
  otherwise, so an explicitly assigned key still reaches the table.

- **A prepared statement reports the row id its INSERT generated.** The engine's own execute path
  published `LastInsertRowId` after running and the prepared path did not, so the key was
  unreachable through a prepared statement and every caller asking for it got zero. Found while
  fixing `SetOutputIdentity`, which is the first caller to need it.

- **Migration literals no longer follow the machine's locale.** A decimal default of `1.5` was
  written as `DEFAULT 1,5` on a comma-separator machine — a migration generated by one developer
  that is corrupt SQL for everyone else. Less obvious and fixed with it: `:` and `/` inside a
  custom date format are the *culture's* separators rather than literals, so even the explicit
  `"yyyy-MM-dd HH:mm:ss"` shifted with the culture. Everything is pinned to the invariant culture,
  and `float`/`double` use a round-trip format.

- **`ADD COLUMN` keeps the size the model declares.** `MaxLength = 16` became `TEXT` and
  `Precision/Scale = 18,4` became `DECIMAL`, so the model's own constraints were dropped on the way
  to the DDL with nothing said. They now emit `VARCHAR(16)` and `DECIMAL(18,4)`.

- **A `char` property works.** It was mapped to the string mapping, whose converter cannot take a
  `char`, so **any** `SaveChanges` on an entity with one failed — the property was unusable
  outright, not merely in inlined constants. Mapped properly now, and the value layer accepts a
  `char` as the one-character string it is.

- **`DateTime.Now`, `DateTime.Today` and `DateTimeOffset.Now` return local time.** All three
  translated to `NOW()`, which the engine defines as UTC, so the answer was wrong by exactly the
  machine's offset and said nothing about it. They now use `LOCALTIMESTAMP`; `UtcNow` still uses
  `NOW()`. EF Core's SQLite provider keeps them apart the same way.

## 2.3.0

An EF Core conformance release. It opens phase 2 of
[Docs/NEXT-SESSION-PLAN.md](Docs/NEXT-SESSION-PLAN.md) by referencing EF Core's own provider
specification suite - the canonical proof of drop-in compatibility, which this provider had never
been run against - and fixes what that surfaced.

**The headline is a measurement rather than a fix.** Nine conformance suites are wired, and
WitDatabase matches EF Core's SQLite provider **exactly** on every one: same pass count, same
failing tests, across roughly 3,600 tests. 3,146 of them now run in CI on every build.

Minor rather than patch for the same reason as 2.1.0 and 2.2.0: several of these change an answer
the previous release gave.

**Everything below was found either by the suite or by the SQLite oracle beside it.** Six of the
findings the 2026-07 audit had recorded turned out to be misattributed, and each correction came
from running the same model on SQLite rather than from reading the code - the entries say so where
it matters.

### Behaviour changes

- **Dropping an index now empties its storage.** Dropping removed the index from the manager and
  disposed it, which only closes the backing store - on a file-backed database the entries stayed on
  disk under the index's name, and the next index created with that name adopted them. A table
  dropped and recreated then **rejected rows it did not contain**, reporting a `UNIQUE` violation
  against keys belonging to a table that no longer existed. Affects primary keys, single and
  composite, and explicitly declared unique indexes alike. An in-memory database was never affected:
  it builds a fresh store per index.

- **Deleting a database now deletes all of it.** `EnsureDeleted` removed the data file and reported
  success while leaving the index directory (`<file>_indexes`) in place, so a database recreated at
  the same path inherited every index of the one that had been deleted - with the same symptom as
  above. The naming rule for the sidecar files now lives in one place,
  `OutWit.Database.Core.Utils.DatabaseFiles`, so what creates them and what deletes them cannot
  drift apart.

- **Temporal literals are emitted as plain strings, not ANSI typed literals.** EF Core's own
  mappings produce `TIMESTAMP '1970-01-01 …'`, a form WitSQL has no grammar for, so **any query
  comparing against a constant date failed to parse before it reached the engine** —
  `no viable alternative at input '>TIMESTAMP'`. `DATE`, `TIME` and `DATETIMEOFFSET` had the same
  shape. All four now emit a quoted string, as EF Core's SQLite provider does.

- **`MILLISECOND`, `TOTAL_SECONDS` and casts to the narrower integer types now work.** The
  translators emitted them and the engine had no implementation, so `DateTime.Millisecond`,
  `TimeSpan.TotalSeconds` and a cast to `short` each ended in `NotSupportedException` at run time.
  `TOTAL_MINUTES`, `TOTAL_HOURS`, `TOTAL_DAYS`, `TOTAL_MILLISECONDS` and `TINYINT` came with them.

- **`StartsWith` and `EndsWith` no longer treat the search term's own wildcards as wildcards.** The
  term was spliced into the LIKE pattern raw and with no `ESCAPE` clause, so `StartsWith("a_")`
  matched every row beginning with `a` followed by anything — four seeded rows instead of the one
  that literally starts with `a_`. That is a wrong answer, not a slow one. A constant term is now
  escaped when the query is built; anything else is escaped by the engine with `REPLACE`, since its
  value is not known until the query runs. Matches what EF Core's SQLite provider emits.

- **Index filters and descending columns now reach the SQL.** `HasFilter` was dropped, so a
  filtered UNIQUE index became a full one — which enforces a **stricter** constraint than the model
  declares, rejecting rows the application is entitled to insert. `IsDescending` was dropped too.
  Both now emit exactly what EF Core's SQLite provider emits.

- **Migration operations WitDatabase cannot carry out now stop the migration instead of emitting a
  comment.** Adding or dropping a primary key on an existing table, renaming an index and changing a
  column's type each produced a SQL comment — or, for the type change, nothing at all. A comment is a
  valid script that changes nothing, so the migration was recorded as applied while the database kept
  its old schema and the model silently disagreed with it from then on. All four now throw
  `NotSupportedException` naming the table, the change and the way round it, as EF Core's SQLite
  provider does for the same operations.

- **`EnsureSchema` is ignored rather than refused.** It threw `NotSupportedException`, which failed
  migrations EF Core emits as a matter of course. WitDatabase has one schema, so there is nothing to
  create; SQLite ignores the operation in the same way.

- **Disposing an LSM store now waits for all of its background work.** `Dispose` waited for
  compaction and *then* flushed what was left in the memtable — and that flush scheduled a fresh
  compaction which nobody waited for. The next store opened on the same directory met an SSTable
  that was still being written (`SSTable file is too small`) or a file the departing compaction
  still held open. Nothing is scheduled once disposal has begun; compacting on the way out bought
  nothing in any case.

  Not found by the audit. `RapidOpenCloseTest` had been failing intermittently on CI and 9 runs in
  10 on Windows.

- **A composite key containing a store-generated property is now reported when the model is built.**
  Nothing can fill such a key - value generation is tied to the row counter, which can only stand
  behind a key of one column - but the model was accepted in silence, the emitted DDL declared the
  column `NOT NULL` with nothing to fill it, and the first insert that relied on generation failed
  with a `NOT NULL` violation naming a column the caller had never written to. The model build now
  says so, naming the entity, the key and the way out.

  It is a warning and not an error: such a model works whenever the caller supplies the values, and
  EF Core's SQLite provider accepts it too. Most likely to be met through an owned collection, whose
  key is the owner's key plus a generated ordinal unless configured with `HasKey`.

### Added

- `OutWit.Database.EntityFramework.Specification.Tests` - the EF Core conformance harness. Its
  `WitComplianceTest` records the baseline of specification suites not yet implemented - **317**,
  down from 325 as suites are wired - and fails if a future EF Core adds one that nobody has looked
  at. Individual conformance suites are
  tagged `Category=Conformance` and excluded from CI while they are red.

- Nine conformance suites wired, each paired with its oracle. **WitDatabase matches SQLite exactly
  on every one of them** — same pass count, same failing tests, across roughly 3,600 tests.
  `Load` (3,137 tests), `NullKeys` and `NotificationEntities` pass outright and run in CI;
  `FieldMapping`, `StoreGenerated`, `WithConstructors`, `CompositeKeyEndToEnd` and `PropertyValues`
  are at parity, failing only where SQLite fails; `Find` remains red.

- A differential oracle in the same project: the same suites run against SQLite, tagged
  `Category=Oracle`. A conformance suite failing on WitDatabase says nothing on its own - some of
  EF Core's specification models ask for capabilities no file-backed provider has. Only a test that
  passes on SQLite and fails here is a WitDatabase defect.

- `OutWit.Database.Core.Utils.DatabaseFiles` - the files a file-backed database owns (data file,
  index directory, journal) and a `Delete` that removes all of them.

### Known and recorded, not fixed here

Twelve of the audit's EF findings remain, each with a test that turns green when it is fixed: JSON
columns (they fail at model build), literal round trips, the migrations blockers, and a
schema-qualified table whose `CREATE TABLE` drops the schema that the query and update generators
keep. `Docs/NEXT-SESSION-PLAN.md` has the ledger; the marker count across the repository stands at
**85**, from 100 when the release opened.

## 2.2.0

A referential-integrity release. Everything here comes out of the verified backlog
([Docs/NEXT-SESSION-PLAN.md](Docs/NEXT-SESSION-PLAN.md), phase 1), and these are the defects that
**corrupted data** rather than returning a wrong answer - a cascade that deleted the wrong row, a
constraint that never fired, a column write that wrapped.

Minor rather than patch for the same reason as 2.1.0: most of these change an answer the previous
release gave. Every one is a correction, and a consumer can still see different behaviour after
upgrading.

### Behaviour changes

- **Cascades now match on the columns a foreign key actually references.** Matching compared the
  child's key values positionally against the parent's PRIMARY KEY, ignoring `ForeignColumns`. With
  a foreign key pointing anywhere other than the primary key - a `UNIQUE` column is enough - it went
  wrong in both directions at once: a child whose referenced row still existed **was deleted**, and
  a child whose referenced row was gone **survived as an orphan**.

  A key whose column count does not line up with what it references is now skipped rather than
  matched positionally.

- **Self-referencing foreign keys cascade.** They were skipped outright, so `ON DELETE CASCADE` left
  the child behind and `ON DELETE RESTRICT` raised nothing at all - the safe-looking declaration was
  the one that orphaned rows.

  Cascading is recursive, so this makes a reference cycle reachable; a guard tracks the rows whose
  cascade is in flight and stops one exactly, rather than capping depth and silently truncating a
  deep but legitimate tree. A row referencing only itself can still be deleted: it is excluded from
  its own child set, as in every other database.

- **`ON UPDATE` actions run.** Nothing ever reached them: cascading was only ever invoked from
  DELETE. `CASCADE`, `SET NULL`, `SET DEFAULT`, `RESTRICT` and `NO ACTION` were all parsed, stored,
  and silently ignored, so changing a referenced key left children pointing at a value that no
  longer existed. `ON UPDATE CASCADE` rewrites the child's key rather than deleting the child.

- **An integer column refuses a value it cannot hold.** The conversions were unchecked C# casts:
  `100000` into a `SMALLINT` wrapped silently, and text that is not a number became `0`. Both were
  wrong answers rather than errors, while WitSQL.md documents the exact range of each type.

  If you have been writing out-of-range values, they were being wrapped and you will now get an
  error instead. Rows already stored are untouched.

- **A range scan and a point read agree.** `Get` chose its timestamp by isolation level while `Scan`
  was hard-wired to the snapshot, so under READ COMMITTED one transaction could read the same key as
  `2` by key and `1` by scan. That level permits seeing another transaction's commit; it does not
  permit two reads in one transaction to disagree. Stricter levels are unaffected - a rescan under
  REPEATABLE READ still shows the snapshot.

### Fixed

- **`DROP COLUMN` no longer leaves a table nothing can write to.** Only the column list was
  rewritten, so the primary key and foreign keys still named a column that had gone and the next
  INSERT died with `Column '…' not found` - from a DDL statement that reported success. Foreign keys
  built on the dropped column now go with it. Dropping a column the **primary key** is built from is
  refused rather than performed, as SQLite does: silently rewriting a table's identity is not a
  decision `DROP COLUMN` should make on its own.

- **`ALTER TABLE … ADD COLUMN` rejects a duplicate column name** instead of appending it again.
  Replaying a migration left the catalog holding the same column twice.

### Known and recorded, not fixed here

- **A multi-row statement is still not atomic.** A failure part-way leaves the earlier rows written.
  The fix is an implicit per-statement transaction - pre-validating every row would break
  intra-statement uniqueness - which is the same mechanism autocommit durability needs, so it gets
  its own change rather than a partial one here.

- **Declared sizes are still unenforced, and for a reason the audit did not state.** `VARCHAR(n)`
  and `DECIMAL(p,s)` are not merely unchecked - they are **never recorded**: after
  `CREATE TABLE T (S VARCHAR(5))`, the column's `MaxLength` is null. Enforcement cannot be added
  until the DDL path captures them, and `INFORMATION_SCHEMA` under-reports the schema for the same
  reason.

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
