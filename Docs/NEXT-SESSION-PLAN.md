# Work plan — next session

Written at the end of the session that produced [AUDIT-2026-07.md](AUDIT-2026-07.md) and shipped
2.0.0. Starting point: `main` at the 2.0.0 merge, whole suite green on net9.0 and net10.0
(10,296 tests, 0 failures), all seven packages published.

Three workstreams, independent of each other. **A** is a bounded piece of work with a known design.
**B** is a triage backlog. **C** is an investigation with no fix committed to up front.

---

## A. `BETWEEN` operator precedence — the last item from the audit's week-1 list

**Status:** the only remaining item from §3 of the audit. Two `[Ignore]`d tests already state the
intended behaviour:
[WitSqlEnginePrecedenceTests.cs](../Sources/Engine/OutWit.Database.Tests/Engine/WitSqlEnginePrecedenceTests.cs)
— `BetweenDoesNotSwallowTheFollowingConjunctTest` and
`NotBetweenDoesNotSwallowTheFollowingConjunctTest`. Remove the `[Ignore]` when it lands.

### The defect

Measured, still true on `main`:

```
Age BETWEEN 1 AND 10 AND Flag = 1
  →  Between(Age, lower = (1 AND 10), upper = (Flag = 1))
```

`WHERE Age BETWEEN 18 AND 65 AND Active = TRUE` therefore returns nothing, silently.

### Why the `LIKE` fix does not apply

Same root cause — ANTLR compiles an *interior* recursive reference (one that is neither first nor
last in its alternative) as `expression(0)`, full precedence, so it consumes everything after it.
`LIKE` was fixable positionally: splitting the optional `ESCAPE` block into its own alternative put
the pattern back in the trailing position, where ANTLR bounds it. `BETWEEN`'s `AND` keyword sits
structurally **in the middle** of its alternative, so no reordering can move the lower bound out of
the interior position.

### The change

Lift the boolean layer out of `expression` in
[WitSqlParser.g4](../Sources/Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4), the way
Presto/Trino and the reference SQL grammars do:

```
searchCondition : searchCondition OR searchCondition
                | searchCondition AND searchCondition
                | NOT searchCondition
                | predicate ;

predicate       : valueExpression comparisonOp valueExpression
                | valueExpression NOT? BETWEEN valueExpression AND valueExpression
                | valueExpression NOT? LIKE valueExpression (ESCAPE valueExpression)?
                | valueExpression NOT? IN ( … )
                | valueExpression IS NOT? NULL
                | … ;

valueExpression : /* arithmetic, concat, collate, functions, literals */ ;
```

With `BETWEEN`'s operands at the `valueExpression` layer, the interior reference can no longer reach
`AND`, because `AND` lives one layer up.

### Cost and blast radius

Roughly a week, and larger than it looks:

- every `WHERE` / `HAVING` / `ON` / `CHECK` / partial-index reference changes from `expression` to
  `searchCondition`;
- the parse-tree shape changes, so `WitSqlVisitor.Expressions.cs` needs reworking — the visitor
  currently switches on labelled alternatives of one flat rule;
- once `LIKE` is inside `predicate`, the two-alternative split done in `fde365d` can and should be
  collapsed back into one, since the positional workaround stops being necessary;
- `WitSqlExpressionSerializer` must still round-trip, including parenthesisation.

### Acceptance

- The two `[Ignore]`d tests pass with the attribute removed.
- All 12 tests in `WitSqlEnginePrecedenceTests` pass, including the ones that pin what must *not*
  change (`GLOB`, `IN`, `AND`-binds-tighter-than-`OR`, unary minus).
- Parser tests stay at 711 passing; engine tests at 1848.
- Add the shapes the audit lists under §4.2 that nobody has executed yet: `NOT BETWEEN … AND` with a
  trailing `OR`, `BETWEEN` inside a `CASE`, `BETWEEN` with a subquery bound.

---

## B. The 104 unverified audit findings

### What these are, precisely

The audit ran 16 dimensions and produced **272 findings**, of which **198** were rated
blocker/critical/major. Adversarial verification was capped at the five highest-severity findings per
dimension, so **94 were verified and 104 were not**. §4 of the audit report is the verified subset —
a floor, not a ceiling.

**Every one of the 104 is rated `major`.** No blocker or critical claim is unverified: those all fell
inside the top-five cut. That bounds the risk here — this is a backlog of "probably real, would
matter to a user, nobody attacked the claim", not of potential catastrophes.

### How to work them

Do **not** fix them in order. The audit's own record shows why: of the claims that *were* verified,
several changed materially under scrutiny —

- the B+Tree split was rated **blocker** and turned out to be **latent** (unreachable at the shipped
  `MaxInlineSize`);
- `FreeOverflow` "leaks a pin per chain link" was actually one pin per *chain*, which changed how the
  regression test had to be built;
- `Math.Max`/`Math.Min` collapsing to an aggregate did not reproduce at all;
- "at-rest encryption is cryptographically void" overstated it — it fails closed and leaks no
  plaintext; the defect is the key schedule.

So: **verify first, then fix.** For each finding, the question is not "how do I fix this" but "does a
test prove it". The cheapest form is the one used throughout this session — write the test that
should fail, run it, and only then look at the code.

Suggested batching, highest-signal first:

1. **`core-concurrency` (11)** — highest concentration of "this cannot be right" claims, and the
   hardest to verify by reading. Several are about disposal and cancellation paths where a wrong
   claim is cheap to disprove with a targeted test.
2. **`engine-dml` (7) + `engine-query` (7) + `engine-schema-ddl` (6)** — all directly executable as
   SQL, so verification is fast and unambiguous. Best value per hour.
3. **`dropin-gaps` (10)** — these decide whether "drop-in" is an honest claim; several are capability
   statements that need confirming against the real EF Core behaviour, not just the code.
4. **`cross-cutting` (12)** — mixed bag; contains the two credential-leak claims, which should be
   checked early regardless of batch order.
5. Everything else.

### The 104, by dimension

Severity is the reporting agent's own rating, unverified. Paths are relative to `Sources/`.

### Cross-cutting quality  <sub>`cross-cutting` — 12 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | The engine never throws a DbException: WitDbException is dead code and WitDbCommand does not wrap engine exceptions | `Providers/OutWit.Database.AdoNet/WitDbException.cs:119` |
| major | Disposal paths swallow write failures and skip cleanup on exception, leaking file handles and losing the final flush | `Engine/OutWit.Database/Engine/WitSqlEngine.cs:302` |
| major | Connection-string password is written into the EF Core log through LogFragment and PopulateDebugInfo | `Providers/OutWit.Database.EntityFramework/Infrastructure/WitDbContextOptionsExtension.cs:246` |
| major | ConnectionPool is unreachable from the provider and permanently leaks a semaphore permit on every borrow | `Providers/OutWit.Database.AdoNet/Pool/ConnectionPool.cs:234` |
| major | EF Core database-first scaffolding is SQLite code that WitSQL cannot execute | `Providers/OutWit.Database.EntityFramework/Design/Internal/WitDatabaseModelFactory.cs:92` |
| major | EF translates DateTime.Now, DateTime.Today and DateTimeOffset.Now to NOW(), which the engine defines as UTC | `Providers/OutWit.Database.EntityFramework/Query/Translators/WitMemberTranslator.cs:133` |
| major | Migration SQL literals are generated with the current culture: decimal and time separators corrupt the emitted SQL | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:809` |
| major | Three migration operations are emitted as SQL comments and idempotent scripts are generated without guards | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:312` |
| major | Reopening an encrypted MVCC database silently downgrades it to non-MVCC, whose on-disk key format differs | `Core/OutWit.Database.Core/Builder/WitDatabase.cs:310` |
| major | BulkOptions.SetOutputIdentity is documented as reading identities back but instead inserts explicit zero keys | `Providers/OutWit.Database.EntityFramework/Extensions/WitDbBulkExtensions.cs:555` |
| major | LSM compaction swallows File.Delete failures, and SSTableReader's FileShare mode makes those failures likely on Windows | `Core/OutWit.Database.Core/Stores/StoreLsm.cs:521` |
| major | The IndexedDB/Blazor WASM story cannot work: 0-byte async engine, Task.Run-wrapped ADO.NET, sync-over-async storage, and a README sample that does not compile | `Engine/OutWit.Database/Engine/WitSqlEngine.Async.cs:1` |

### Core: concurrency and locking  <sub>`core-concurrency` — 11 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | DeadlockDetector is never fed any wait edge — row-lock deadlocks are undetectable and both transactions time out | `Core/OutWit.Database.Core/Transactions/MvccTransaction.cs:228` |
| major | DatabaseLock.AcquireReadLockAsync leaks m_readerCount on cancellation, permanently breaking reader/writer exclusion | `Core/OutWit.Database.Core/Concurrency/DatabaseLock.cs:153` |
| major | RowLockHandle.Dispose() is an empty method; combined with the grant/timeout race a row lock can be held forever by a finished transaction | `Core/OutWit.Database.Core/Concurrency/RowLockHandle.cs:40` |
| major | RowLockManager completes TaskCompletionSource under m_syncLock without RunContinuationsAsynchronously | `Core/OutWit.Database.Core/Concurrency/RowLockManager.cs:110` |
| major | LsmParallelStore.Get/Scan do not wait for the background merge — writes are invisible to the caller that made them | `Core/OutWit.Database.Core/Builder/LsmParallelStore.cs:83` |
| major | LsmParallelWriter.FlushAllAsync drains and disposes other threads' live thread-local buffers | `Core/OutWit.Database.Core/LSM/LsmParallelWriter.cs:217` |
| major | Page caches dispose CachedPage (returning its pooled buffer) while an async write of that page is in flight | `Core/OutWit.Database.Core/Cache/PageCacheShardedClock.cs:160` |
| major | ConnectionPool never reclaims a permit — ReturnConnection has no caller, so the pool is exhausted after MaxPoolSize borrows | `Providers/OutWit.Database.AdoNet/Pool/ConnectionPool.cs:234` |
| major | StorageFile mixes locked synchronous FileStream I/O with unlocked async I/O on the same handle | `Core/OutWit.Database.Core/Storage/StorageFile.cs:199` |
| major | EnableFileLocking defaults to true but the builder selects the in-process-only LockManager overload, so no file lock is ever created | `Core/OutWit.Database.Core/Builder/WitDatabaseBuilder.cs:561` |
| major | PageLatchManager.Cleanup can dispose a latch another thread is acquiring or holding, and Release silently no-ops on a removed latch | `Core/OutWit.Database.Core/Tree/PageLatchManager.cs:228` |

### Drop-in capability gaps  <sub>`dropin-gaps` — 10 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | `BeginTransaction(IsolationLevel.X)` runs at ReadCommitted and leaks the requested level onto the *next* transaction | `Providers/OutWit.Database.AdoNet/WitDbConnection.cs:164` |
| major | Schemas are unsupported at every layer, and the one schema name the validator accepts (`public`) produces unresolvable SQL | `Providers/OutWit.Database.EntityFramework/Metadata/WitModelValidator.cs:56` |
| major | `AlterColumn` silently emits nothing for a column-type change — model and database diverge with no error | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:182` |
| major | AddPrimaryKey / DropPrimaryKey / RenameIndex emit SQL comments — the operation is silently skipped | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:320` |
| major | Filtered indexes (`HasFilter`), `IncludeProperties` and descending indexes are silently dropped by the migrations generator | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:239` |
| major | EF-generated CROSS APPLY / OUTER APPLY cannot be parsed — filtered/limited collection includes and correlated Take fail at runtime | `Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4:157` |
| major | `ExecuteUpdate`/`ExecuteDelete` support only single-table statements — OpenIddict pruning already fails downstream | `Providers/OutWit.Database.EntityFramework/Extensions/WitDbServiceCollectionExtensions.cs:37` |
| major | Savepoints are not wired to the ADO.NET contract, so EF cannot roll a failed SaveChanges back to a savepoint | `Providers/OutWit.Database.AdoNet/WitDbTransaction.cs:104` |
| major | Ambient transactions / TransactionScope are unsupported — `EnlistTransaction` is not implemented | `Providers/OutWit.Database.AdoNet/WitDbConnection.cs:154` |
| major | User-defined functions and stored procedures do not exist anywhere in the stack, while the dialect spec documents them as features | `Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4:35` |

### Test-suite gaps  <sub>`tests-and-gaps` — 8 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | StatementExecutor tests — the only coverage of the 50 KB StatementExecutor.Update.cs — mock IDatabase and assert Received(n), so read-your-own-writes defects are structurally invisible | `Engine/OutWit.Database.Tests/Statements/StatementExecutorUpdateTests.cs:418` |
| major | Migration integration coverage tests only MigrateAsync; the sync Database.Migrate() path that reproduces KnownIssues #1b has zero coverage, and no test round-trips `dotnet ef migrations add` | `Providers/OutWit.Database.EntityFramework.Tests/MigrationTests/MigrateAsyncIntegrationTests.cs:54` |
| major | The suite's only reference-model oracle is one-sided: deleted-key reads are unasserted, 1000 of ~110k keys are verified, the seed is fixed, and WAL is off | `Core/OutWit.Database.Core.Tests/LSM/LsmTreeStressTests.cs:428` |
| major | No coverage measurement and no mutation testing, despite coverlet.collector being referenced in all seven test projects | `.github/workflows/ci.yml:56` |
| major | EF Core's provider specification test suite is not referenced — the canonical proof of "drop-in" is absent, while an unused SQLite reference makes a differential oracle nearly free | `Providers/OutWit.Database.EntityFramework.Tests/OutWit.Database.EntityFramework.Tests.csproj:24` |
| major | No page-level corruption or parser fuzzing: the single corruption test flips one hard-coded byte in a WAL file, behind an `if` that can silently skip the mutation | `Core/OutWit.Database.Core.Tests/Wal/WriteAheadLogTests.cs:284` |
| major | Five [Ignore]d ADO.NET tests silence an unfixed parsing defect and a drop-in limitation that is absent from KnownIssues.md, with no negative test asserting a clean failure | `Providers/OutWit.Database.AdoNet.Tests/Parallel/WitDbConnectionParallelAccessTests.cs:79` |
| major | No SQL-literal text round-trip property test: only 2 of 9 LiteralType values are round-tripped, with structural-only assertions | `Engine/OutWit.Database.Parser.Tests/SerializerTests.cs:236` |

### Engine: DML and indexes  <sub>`engine-dml` — 7 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | Foreign keys that reference their own table are excluded from all cascade handling | `Engine/OutWit.Database/Statements/StatementExecutor.Validation.cs:91` |
| major | ON UPDATE CASCADE / SET NULL / SET DEFAULT is never applied | `Engine/OutWit.Database/Statements/StatementExecutor.Validation.cs:163` |
| major | UPDATE of an autoincrement primary key desynchronises the PK from the internal rowid, making the row unreachable by PK | `Engine/OutWit.Database/Statements/StatementExecutor.Update.cs:891` |
| major | Narrowing numeric writes silently truncate/wrap, and unparseable text is written as 0 | `Engine/OutWit.Database/Types/WitTypeConverter.cs:576` |
| major | Declared VARCHAR(n) length and DECIMAL(p,s) precision/scale are recorded but never enforced | `Engine/OutWit.Database/Definitions/DefinitionColumn.cs:148` |
| major | Statements are not atomic: a constraint failure part-way through a multi-row DML leaves earlier rows written | `Engine/OutWit.Database/Statements/StatementExecutor.Update.cs:1076` |
| major | Recursive triggers have no depth limit and terminate the process with a StackOverflowException | `Engine/OutWit.Database/Statements/StatementExecutor.Triggers.cs:121` |

### Engine: query execution and optimizer  <sub>`engine-query` — 7 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | LIMIT is applied before DISTINCT, so `SELECT DISTINCT ... LIMIT n` can return fewer than n distinct rows | `Engine/OutWit.Database/Query/QueryPlanner.cs:545` |
| major | Default window frame is the whole partition instead of UNBOUNDED PRECEDING..CURRENT ROW, so `SUM(x) OVER (ORDER BY y)` returns the partition total, not a running total | `Engine/OutWit.Database/Iterators/IteratorWindow.Frame.cs:24` |
| major | `ORDER BY ... NULLS FIRST \| NULLS LAST` is parsed and then silently ignored by the sort iterator | `Engine/OutWit.Database/Iterators/IteratorSort.cs:45` |
| major | LIKE is compiled to a .NET regex without Singleline/CultureInvariant, so `%` and `_` cannot cross a newline, `$` tolerates a trailing newline, and matching is culture- and case-insensitive | `Engine/OutWit.Database/Expressions/ExpressionEvaluator.Conditional.cs:155` |
| major | Most scalar functions do not propagate NULL — LENGTH(NULL)=0, UPPER(NULL)='', YEAR(NULL)=1, ROUND(NULL)=0 | `Engine/OutWit.Database/Expressions/ExpressionEvaluator.Functions.cs:58` |
| major | Equals/GetHashCode contract is violated for cross-type numerics, so hash join, GROUP BY, DISTINCT and UNION disagree with `=` | `Engine/OutWit.Database/Values/WitSqlValue.Comparison.cs:68` |
| major | Index selection matches predicates by column name only, ignoring the table qualifier, so a predicate on another table can drive an index seek on the scanned table | `Engine/OutWit.Database/Optimizers/OptimizerQuery.cs:272` |

### Parser and grammar  <sub>`parser` — 7 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | Serializers emit unquoted identifiers using an incomplete reserved-word list, so round-tripped schema objects fail to re-parse | `Engine/OutWit.Database.Parser/Serializers/WitSqlExpressionSerializer.cs:441` |
| major | `INSERT INTO t DEFAULT VALUES` is not in the grammar although the EF provider emits it | `Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4:194` |
| major | MERGE assigns the source alias to TargetAlias when the target alias is omitted | `Engine/OutWit.Database.Parser/Visitor/WitSqlVisitor.DML.cs:373` |
| major | No typed, prefixed or hexadecimal literal forms — including the hex literals the spec itself documents | `Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4:527` |
| major | MySQL-style `LIMIT offset, count` binds the two operands the wrong way round | `Engine/OutWit.Database.Parser/Visitor/WitSqlVisitor.DML.cs:80` |
| major | Documented trigger bodies are unusable: `SET NEW.col = ...` is a syntax error and SIGNAL bodies throw NotSupportedException | `Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4:80` |
| major | Integer literals above long.MaxValue escape as a raw OverflowException, and long.MinValue cannot be written at all | `Engine/OutWit.Database.Parser/Visitor/WitSqlVisitor.Expressions.cs:277` |

### Engine: schema catalog and DDL  <sub>`engine-schema-ddl` — 6 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | Named constraints declared in CREATE TABLE lose their names, so ALTER TABLE DROP CONSTRAINT can never remove them (breaks EF Core DropForeignKey / DropCheckConstraint migrations) | `Engine/OutWit.Database/Statements/StatementExecutor.Ddl.Tables.cs:128` |
| major | ALTER TABLE ADD COLUMN silently discards UNIQUE, PRIMARY KEY, CHECK and REFERENCES column constraints | `Engine/OutWit.Database/Statements/StatementExecutor.Ddl.Tables.cs:283` |
| major | DROP COLUMN leaves the dropped column referenced by PRIMARY KEY / UNIQUE / FK / index metadata, making the table un-insertable | `Engine/OutWit.Database/Schema/SchemaCatalog.Columns.cs:41` |
| major | Self-referencing foreign keys never cascade: ON DELETE CASCADE / RESTRICT is skipped for self-references | `Engine/OutWit.Database/Statements/StatementExecutor.Validation.cs:89` |
| major | Cascade matching ignores fk.ForeignColumns and compares child FK values positionally against the parent's PRIMARY KEY | `Engine/OutWit.Database/Statements/StatementExecutor.Validation.cs:277` |
| major | dotnet ef dbcontext scaffold cannot work: WitDatabaseModelFactory queries sqlite_master and SQLite PRAGMAs that the engine does not implement | `Providers/OutWit.Database.EntityFramework/Design/Internal/WitDatabaseModelFactory.cs:92` |

### Core: MVCC and isolation  <sub>`core-mvcc` — 6 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | READ COMMITTED point reads and range scans use different snapshots, so one transaction sees mutually inconsistent data | `Core/OutWit.Database.Core/Transactions/MvccTransaction.cs:158` |
| major | SERIALIZABLE does not prevent phantoms or write skew because range reads are never added to the read set | `Core/OutWit.Database.Core/Transactions/MvccTransaction.cs:381` |
| major | ADO.NET/EF Core isolation level is silently ignored and leaks into the following transaction | `Providers/OutWit.Database.AdoNet/WitDbConnection.cs:164` |
| major | Garbage collection never reclaims deleted keys or metadata versions, so the file grows without bound | `Core/OutWit.Database.Core/Stores/MvccKeyValueStore.cs:546` |
| major | Every commit and every rollback scans the entire database, making bulk EF SaveChanges quadratic | `Core/OutWit.Database.Core/Stores/MvccKeyValueStore.cs:400` |
| major | The persisted max timestamp can lag the data on disk, so after a crash committed rows become invisible to every transactional read | `Core/OutWit.Database.Core/Stores/MvccKeyValueStore.cs:749` |

### Core: WAL, recovery, durability  <sub>`core-durability` — 6 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | Recovery truncates the WAL after a partial replay, so one bad record permanently destroys every committed transaction behind it — with no error reported | `Core/OutWit.Database.Core/Transactions/TransactionalStore.cs:403` |
| major | Auto-increment / rowid counters are written after the commit fsync and never flushed, so after a crash the next INSERT reuses a live rowid and silently overwrites an existing row | `Engine/OutWit.Database/Engine/WitSqlEngine.Transactions.cs:56` |
| major | Autocommit DML is never fsync'd: there is no Flush call anywhere in the ADO.NET or EF Core provider, and pooled connections are not disposed on Close() | `Engine/OutWit.Database/Engine/WitSqlEngine.Dml.Operations.cs:257` |
| major | Savepoint rollback is invisible to the journal, so WAL replay resurrects writes that were rolled back before commit | `Core/OutWit.Database.Core/Transactions/Transaction.cs:310` |
| major | RollbackJournal recovery has no checksum and no length verification, so a torn tail is applied as a truncated or fabricated before-image | `Core/OutWit.Database.Core/Transactions/RollbackJournal.cs:262` |
| major | Journal=rollback with a bare relative Data Source throws ArgumentException when the connection is opened | `Core/OutWit.Database.Core/Transactions/RollbackJournal.cs:51` |

### EF query translation  <sub>`ef-translation` — 5 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | `StartsWith`/`EndsWith` build LIKE patterns without escaping wildcards in the search term | `Providers/OutWit.Database.EntityFramework/Query/Translators/WitStringMethodTranslator.cs:128` |
| major | Engine `LIKE` is case-insensitive and newline-blind, so `StartsWith` and `Contains` disagree with each other and with every real backend | `Engine/OutWit.Database/Expressions/ExpressionEvaluator.Conditional.cs:158` |
| major | Translators emit functions and casts the engine does not implement (MILLISECOND, TOTAL_SECONDS, LOG base, unsigned/short CASTs, fractional DATEADD) | `Providers/OutWit.Database.EntityFramework/Query/Translators/WitMemberTranslator.cs:110` |
| major | JSON columns (`ToJson`) and primitive collections are unsupported: `VisitJsonScalar` is not overridden and `FindMapping` has no collection path | `Providers/OutWit.Database.EntityFramework/Query/WitQuerySqlGenerator.cs:11` |
| major | `CROSS APPLY`/`OUTER APPLY` and `VALUES` table sources are neither overridden nor supported by the grammar | `Providers/OutWit.Database.EntityFramework/Query/WitQuerySqlGenerator.cs:85` |

### Core: LSM engine  <sub>`core-lsm` — 4 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | A failed flush leaves m_immutableMemTable populated forever; the next successful flush overwrites the reference and truncates the WAL, losing the data permanently | `Core/OutWit.Database.Core/Stores/StoreLsm.cs:550` |
| major | The SSTable is never fsynced but the WAL is truncated immediately after, so a power loss discards a whole flushed memtable | `Core/OutWit.Database.Core/LSM/SSTableBuilder.cs:184` |
| major | Compaction has no manifest: the live SSTable set is inferred from the directory listing, so a crash between publishing the output and deleting the inputs resurrects deleted rows | `Core/OutWit.Database.Core/Stores/StoreLsm.cs:519` |
| major | LsmParallelWriter.Dispose discards unsubmitted thread-local buffers instead of flushing them | `Core/OutWit.Database.Core/LSM/LsmParallelWriter.cs:497` |

### EF migrations (KnownIssues #1)  <sub>`blocker-migrations` — 4 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | BuildCreateOperations silently drops HasData seed rows and EnsureSchema, and skips Sort() so generated InitialCreate migrations are in the wrong dependency order | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsModelDiffer.cs:71` |
| major | EnsureSchemaOperation is not handled, so a model that WitModelValidator explicitly allows (HasDefaultSchema("public")) fails migration with NotSupportedException; schema is also dropped from every emitted identifier | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:38` |
| major | AddColumn/ColumnDefinition ignore the model and the type mapping source, so maxLength / precision / scale are silently lost from ALTER TABLE and CREATE TABLE | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:102` |
| major | SchemaCatalog.AddColumn does not reject a duplicate column name, so a replayed ALTER TABLE ADD COLUMN appends a second identical column and widens every row again | `Engine/OutWit.Database/Schema/SchemaCatalog.Columns.cs:17` |

### EF provider runtime  <sub>`ef-runtime` — 4 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | dotnet ef dbcontext scaffold cannot work: WitDatabaseModelFactory queries SQLite catalogs (sqlite_master, PRAGMA) | `Providers/OutWit.Database.EntityFramework/Design/Internal/WitDatabaseModelFactory.cs:92` |
| major | Bulk extensions skip shadow properties and bypass value converters, writing structurally wrong rows | `Providers/OutWit.Database.EntityFramework/Extensions/WitDbBulkExtensions.cs:463` |
| major | BulkOptions.SetOutputIdentity does the opposite of its documentation: it sends default PK values instead of reading generated ones | `Providers/OutWit.Database.EntityFramework/Extensions/WitDbBulkExtensions.cs:469` |
| major | WitModelRuntimeInitializer hardcodes designTime:false, so the design-time model is given a runtime relational model | `Providers/OutWit.Database.EntityFramework/Infrastructure/WitModelRuntimeInitializer.cs:94` |

### Literal round trip  <sub>`literal-roundtrip` — 3 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | All REAL_LITERALs are parsed as double, so full-precision decimal literals silently lose digits on the way into DECIMAL columns and make `=` match the wrong rows | `Engine/OutWit.Database.Parser/Visitor/WitSqlVisitor.Expressions.cs:284` |
| major | A `char` CLR property is mapped to StringTypeMapping, so any inlined char constant throws InvalidCastException before SQL is produced | `Providers/OutWit.Database.EntityFramework/Storage/WitTypeMappingSource.cs:150` |
| major | Schema-qualified identifiers do not round-trip: DDL drops the schema while EF's query/update generators keep it, so the one schema value WitModelValidator permits ("public") makes every table unreachable | `Providers/OutWit.Database.EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:39` |

### Core: encryption, cache, storage, providers  <sub>`core-crypto-cache-storage` — 2 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | Page caches protect the same mutable state with two different locks for their sync and async APIs | `Core/OutWit.Database.Core/Cache/PageCacheShardedClock.cs:36` |
| major | Zeroed or truncated pages bypass AEAD authentication, and no AAD/version binding allows silent page rollback | `Core/OutWit.Database.Core/Storage/StorageEncrypted.cs:78` |

### ADO.NET provider  <sub>`adonet` — 2 unverified</sub>

| Sev | Finding | Where |
|---|---|---|
| major | Connection pool can never reclaim a connection: the return path is unreachable, so every borrow leaks a pool permit | `Providers/OutWit.Database.AdoNet/Pool/ConnectionPool.cs:234` |
| major | Nothing tracks an open reader: closing the connection disposes the storage under a live streaming iterator | `Providers/OutWit.Database.AdoNet/WitDbCommand.cs:131` |

---

## C. Performance investigation

Unlike A and B this is an **investigation**, not a fix list. The goal is to find out where the time
and the allocations actually go, and only then decide what to change. There are concrete leads
already, all measured during the audit session.

### The competitor is LiteDB, not SQLite

This framing matters more than any individual measurement, and the README currently gets it wrong by
using SQLite as the baseline.

**SQLite is native C.** Its numbers are a reference point, not a target: matching a twenty-year-old C
engine from managed code is probably not a solvable problem. Where WitDatabase does come close — or
ahead — part of the credit belongs to P/Invoke overhead in the C# wrapper rather than to the engine,
and that should be said out loud when it is claimed.

**LiteDB is the real competitor.** It is pure .NET, like WitDatabase, so it is beatable on the same
terms. It is *not* relational and has no EF Core provider, which is exactly the gap WitDatabase
exists to fill. So the goal is precise:

> **Faster than LiteDB and lighter than LiteDB, with full EF Core support.**
> Approaching or beating SQLite anywhere is a bonus, and worth reporting when it happens.

### Before any number: know what is being measured

Both comparisons are distorted on trivial operations, and in opposite directions. Neither distortion
is anyone's fault; both make a small-insert benchmark close to worthless as a verdict.

**SQLite pays P/Invoke on every call.** For an operation that is a few microseconds of real work,
that per-call marshalling overhead is a large fraction of the measurement. It amortises to nothing on
a complex query, where one call does substantial work inside the native engine. So *beating SQLite on
small inserts is not a win* — it mostly measures the C# wrapper's call cost. SQLite's real speed
shows up on complex queries, and that is where a comparison against it means something.

**LiteDB is a document store.** A trivial insert there is a document write: no SQL to parse, no
relational bookkeeping, no schema to honour. It *should* be very fast at that, and a relational
engine losing to it on that one operation says little about either. Chasing parity on trivial
inserts is chasing the wrong number.

Both effects point the same way: **the insert/transaction benchmarks are the least discriminating
workload in the suite**, and they are the only ones that have been run. The target on them is
*adequate*, not spectacular.

### Where that goal actually stands — on the least informative workload

Everything below is `TransactionBenchmarks` (Ryzen 9 5950X, .NET 10, ShortRun): a single transaction
with N trivial inserts. Per the caveats above, treat it as a sanity check, not a scorecard.
**LiteDB as the baseline:**

| configuration | N | WitDatabase | LiteDB | vs LiteDB | allocated Wit / Lite |
|---|---|---|---|---|---|
| B+Tree, `MVCC=false` | 100 | 2.43 ms | 0.81 ms | **3.0x slower** | 736 / 827 KB |
| B+Tree, `MVCC=false` | 500 | 4.30 ms | 1.98 ms | **2.2x slower** | 3621 / 5181 KB |
| Default (MVCC, durable) | 100 | 3.17 ms | 0.80 ms | **4.0x slower** | 929 / 827 KB |
| Default (MVCC, durable) | 500 | 5.30 ms | 2.21 ms | **2.4x slower** | 4509 / 5181 KB |
| LSM, `MVCC=false` | 100 | 12.28 ms | 0.73 ms | **16.8x slower** | 762 / 827 KB |
| LSM, `MVCC=false` | 500 | 52.91 ms | 2.33 ms | **22.7x slower** | 3726 / 5181 KB |

What this does and does not say:

- **Memory — ahead, and this one does generalise.** At 500 inserts WitDatabase allocates ~30% less
  than LiteDB in every mode. Allocation is not distorted by P/Invoke or by document-vs-relational
  framing the way latency is, so this is a real result. Only the MVCC path at 100 inserts is worse
  (929 KB against 827 KB); MVCC versioning is the obvious suspect. Against SQLite's 42 KB / 208 KB
  both managed engines look profligate — that is what a native engine with no managed object graph
  buys, and it is not the target.
- **Speed — 2.2-3.0x behind LiteDB on the B+Tree engine.** Worth knowing, not worth a crusade. This
  is a document store's home ground, and part of the gap is SQL parsing and relational bookkeeping
  LiteDB simply does not do. "Adequate" is the bar here; if profiling turns up something cheap, take
  it, but do not optimise the engine around this workload.
- **The SQLite column on these rows should be ignored.** WitDatabase looking 1.3-2.5x "faster" than
  SQLite on trivial inserts is largely the wrapper's per-call overhead, not the engine.
- **LSM is the one real signal here.** 17-28x slower than LiteDB, 7.9x slower than SQLite, and
  **non-linear in N** — 12 ms at 100 inserts, 53 ms at 500. Non-linearity is a defect signature, not
  a workload artifact: no amount of "different engine categories" explains super-linear growth.
  Something in that path is quadratic or flushing per operation. Chase this one.

An honest scorecard also notes what LiteDB does **not** do: it is not relational, has no SQL engine,
no EF Core provider, no MVCC snapshot isolation. Some of the gap is the cost of features it does not
have — an explanation, not an excuse, but it does mean the trivial-insert gap is the least
interesting place to spend effort.

### The workloads that would actually settle it are already written and have never been run

This is the important gap. The suite already contains, all comparing against both LiteDB and SQLite:

| class | benchmarks | why it discriminates |
|---|---|---|
| `AggregateBenchmarks` | 24 | GROUP BY / SUM / COUNT — real engine work, P/Invoke amortised |
| `QueryBenchmarks` | 18 | WHERE, ORDER BY, projections — the SQL path |
| `JoinBenchmarks` | 18 | joins are where a document store has to work hardest |
| `IndexBenchmarks` | 18 | index selection and seek, the thing indexes exist for |
| `UpdateBenchmarks` | 12 | read-modify-write, index maintenance |
| `InsertBenchmarks` | 9 | the trivial case, for completeness |
| `TransactionBenchmarks` | 14 | **the only class that has been run** |

Seventy-eight benchmarks measuring the workloads that characterise a relational engine sit unrun,
while every number in this document comes from the one class measuring the workload that
characterises it least. **Run those first.** They are where SQLite's overhead amortises into
irrelevance and a comparison becomes fair, and where LiteDB has to do work it was not designed for.
Any conclusion about "the performance story" before that is premature.

**Space is not reclaimed.** Five rounds of `DELETE FROM T` plus refilling the same 2,000 rows grew
the file from 1,564 KB to 10,788 KB — **6.9x**, with no `VACUUM` to recover it. The audit's finding
that delete never merges, rebalances or frees pages is the likely cause, and it compounds: every
round leaves the previous round's pages stranded.

**Commit is O(store size).**
[MvccKeyValueStore.CommitTransaction](../Sources/Core/OutWit.Database.Core/Stores/MvccKeyValueStore.cs)
scans **every record in the store** to find the ones belonging to the committing transaction, and
rewrites each one. That is per commit. Since `0a3b876` it also runs under the commit lock, so it
serialises every writer behind a full-store scan. It should iterate the transaction's own
`m_changes` instead — this is the single most obviously wrong thing in the write path, and the fix
looks small.

**Reads and sorts fully materialise.** `IteratorSort`, `IteratorGroupBy`, `IteratorHashJoin` and the
`StatementExecutor.Select` fast path all build complete result sets in memory, with no spill and no
row or byte budget. PostgreSql and SQL Server spill; here a large `ORDER BY` is an OOM. That is also
WitAnalytics' query shape.

**`RecoverMaxTimestamp`** falls back to an O(n) full scan when its cached value is absent — on every
open of a legacy file.

### What the investigation has to do first

**Fix the benchmark suite before trusting any number from it.**

1. Three benchmark projects — `Comparison.Benchmarks`, `Core.Tests.Benchmarks`,
   `EntityFramework.Benchmarks` — exist only as `bin`/`obj` and **have never been tracked by git**.
   Most of the historical numbers came from them and cannot be reproduced. Either commit them or
   accept that they are gone and rebuild what is needed.
2. Every mode in `BuildConnectionString` except the `Default` added in this session passes
   `MVCC=false`, and the LSM ones also `SyncWrites=false` — configurations no ADO.NET or EF Core
   consumer runs. The other benchmark classes still need `WitDbEngineMode.Default` added to their
   `[Params]`, the way `TransactionBenchmarks` now has it.
3. The README table currently states measured transaction numbers and explicitly withdraws the
   INSERT/UPDATE/DELETE/SELECT rows. Those come back only when there is a committed benchmark that
   measures them.
4. **Re-baseline the benchmarks on LiteDB.** Both the suite's `Baseline = true` attribute and the
   README table use SQLite, which reports the wrong ratio for the thing being optimised — every
   number currently reads as a win against a native engine while hiding a 2-3x loss to the actual
   competitor. Move the baseline to LiteDB and keep SQLite as an additional column.

### Then: where does the time go

Profile rather than guess, and measure the right workload before drawing any conclusion. In order:

1. **Run the 78 unrun benchmarks** — aggregates, queries, joins, indexes, updates. Everything below
   is provisional until those numbers exist, because they are the ones where the comparison is fair
   in both directions.
2. **Why is LSM non-linear in N?** 12 ms at 100 inserts and 53 ms at 500. This is the one conclusion
   from the insert benchmarks that survives the methodology caveats, because super-linear growth
   cannot be explained by engine category or call overhead. Suspects: memtable rotation, per-commit
   flush behaviour, `LsmParallelWriter`'s buffering, the full-store scan in `CommitTransaction`.
3. **What does the commit path cost, and is the O(n) scan the reason?**
   `MvccKeyValueStore.CommitTransaction` scans the entire store per commit and now does so under the
   commit lock. Fix it to walk the transaction's own `m_changes`, then re-measure — this plausibly
   explains a large share of both the LSM non-linearity and the MVCC overhead below.
4. **What does MVCC cost?** 3.17 ms against B+Tree's 2.43 ms at 100 inserts, 929 KB against 736 KB —
   ~30% slower, ~25% heavier, and it is what every ADO.NET and EF Core consumer gets by default. It
   is also the only configuration allocating more than LiteDB. Versioning itself, or the commit path?
5. **Where do the allocations come from?** `MemoryDiagnoser` is already enabled; get the profile per
   operation. `MvccRecord.Serialize`, the `byte[]` key building throughout
   (`SchemaCatalog.CreateRowKey` allocates on every row access), and `WitSqlValue` boxing through
   `m_objectValue` are the first places to look. The target is LiteDB, mostly already beaten — the
   remaining work is the MVCC path.
6. **What does the commit lock added in `0a3b876` cost under concurrent writers?** It was correct to
   add — it closed a snapshot-isolation violation — but its cost has not been measured, and it wraps
   the O(n) scan from step 3. Measure before and after fixing that scan.
7. **What does durable commit actually cost?** `SynchronousCommit` defaults on since 2.0.0, and the
   comparison against `WithAsynchronousCommit()` has never been run.
8. **Is the page cache doing its job?** No hit/miss counters exist anywhere — the audit notes zero
   metrics of any kind in ~57k LOC. Add them before drawing conclusions about caching.

Explicitly *not* a priority: closing the 2.2-3.0x trivial-insert gap to LiteDB. Take a cheap win if
profiling hands one over, but do not shape the engine around a document store's best case.

### Constraints on any fix that comes out of it

- No change may reintroduce the partial-commit window closed in `0a3b876`. The deterministic
  `MvccCommitAtomicityTests` must stay green — it fails in 11 ms if that regresses.
- Durability stays on by default. If a change trades correctness for throughput it needs an explicit
  opt-in, as `WithAsynchronousCommit()` is.
- Any published number must name the configuration it was measured in. The reason the old README
  table had to be withdrawn is that it did not.
- A claim of beating SQLite must say which workload and, where it is plausibly the reason, that
  P/Invoke overhead in the managed wrapper is part of the margin. Overstating that is how the
  previous performance table lost its credibility.

---

## Ordering

A, B and C are independent; pick by what the project needs.

If the goal is **an honest "drop-in" claim**, do A (it is a silently-wrong-results defect) and then
B's `dropin-gaps` batch.

If the goal is **confidence in the audit**, do B — the 104 are the difference between "we found 90
real problems" and "we know what is wrong with this database".

If the goal is **the performance story**, do C — but start by running the 78 benchmarks that already
exist and have never been run. Every performance number anyone currently has, including all of the
ones in this document, comes from trivial inserts: the workload where SQLite is penalised by
per-call marshalling and LiteDB is on its home ground, so neither comparison means much. The one
finding that survives that caveat is LSM's non-linear growth in N, and it is worth chasing on its
own. Beyond that, the honest position today is **not "we are behind" but "we have not measured the
thing that matters"** — with one real result to keep: ~30% less allocation than LiteDB.

One thing that is not in any of the three, and is worth doing whenever there is an hour: **run EF
Core's own provider specification suite** (`Microsoft.EntityFrameworkCore.Specification.Tests`). It
is the canonical way to prove drop-in compatibility, it is not referenced today, and it would likely
surface more than the whole of workstream B.
