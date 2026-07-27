# Work plan — next session

Written at the end of the session that produced [AUDIT-2026-07.md](AUDIT-2026-07.md) and shipped
2.0.0. Starting point: `main` at the 2.0.0 merge, whole suite green on net9.0 and net10.0
(10,296 tests, 0 failures), all seven packages published.

> **Correction, 2026-07-27.** "0 failures" does not hold. On a Ryzen 9 5950X,
> `OutWit.Database.Tests` fails `InsertExplicitPkWithIndexTest` **3 runs out of 3** on unmodified
> `main` at `a668f73` — confirmed against a baseline with no local changes present. It passes when
> run alone. The test asserts a **wall-clock ratio**
> ([Level3_ConstraintValidationTests.cs:286](../Sources/Engine/OutWit.Database.Tests/Performance/Level3_ConstraintValidationTests.cs#L286)):
> `Assert.That(ratio, Is.LessThan(12))` over `Stopwatch` timings of 500- and 2000-row inserts. Under
> the parallel load of the full suite it measures the machine, not the engine. **17 assertions of
> this shape** live under `Sources/Engine/OutWit.Database.Tests/Performance/`. They belong in the
> benchmark suite discussed in workstream C, not among the unit tests.

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

## B. The 104 unverified audit findings — **complete, 2026-07-27**

### Fixed so far

Verification is finished; fixing has started. Branch `fix/audit-confirmed-defects`, on top of the
verification branch. Ten confirmed defects are closed, each by removing the `[Ignore]` from the test
that proved it — the marker count is the remaining work, and it stands at **100 of 106**.

| Fixed | Finding | Note |
|---|---|---|
| `9510710` | Connection-string password reaching the EF Core log | The one whose cost does not scale with frequency. Redaction keeps the `Data Source` — a log line that says nothing is its own failure — and **fails closed**: an unparseable string is withheld whole. |
| `f71a897` | MySQL `LIMIT offset, count` bound the operands backwards | The comma form now has its own branch; it cannot share one with `LIMIT count OFFSET offset`, because the same positions mean opposite things. |
| `f71a897` | MERGE gave the target the source's alias | Split by position relative to `USING`. An index into `context.alias()` cannot identify them when both are optional. |
| `6198ff8` | `SchemaCatalog.AddColumn` accepted a duplicate | Left the catalog holding `[Id, A, A]` on a replayed migration. |
| `6ebc621` | `NULLS FIRST/LAST` ignored | Resolved **before** `ASC/DESC`, since the two are orthogonal; a test pins that specifically. |
| `6ebc621` | `LIMIT` applied before `DISTINCT` | `IteratorDistinct` is streaming and yields first occurrences, so an upstream `ORDER BY` still survives; the change costs the early-out, not the ordering. |
| `6ebc621` | `LIKE` regex flags — three defects | `Singleline`, `\A`/`\z` instead of `^`/`$`, `CultureInvariant`. **`IgnoreCase` deliberately untouched**: WitSQL.md does not fix LIKE's case behaviour, and changing it would silently alter every consumer's results — a semantics decision, not a defect fix. |
| `e899ad6` | Scalar functions swallowed NULL | A general strict guard with an explicit exemption list. The JSON half of that list came from the **suite**, not from reasoning: a first version broke `JSON_ARRAY(1, NULL, 'hello')` and `JSON_TYPE(NULL)`. A guard this broad only earns its keep behind a full run. |
| `bc03319` | Ordered windows defaulted to the whole partition | **Behavioural change, and the point rather than a side effect**: `LAST_VALUE` now returns the current row, as in every real backend. Two tests that asserted the old answer now assert the standard one, plus a new test showing the explicit frame that gets the partition's last value. |

**One gap deliberately left open and pinned**: the synthesized default frame is typed `RANGE`, but
`CURRENT ROW` still maps to the current index whatever the frame type, so peers — rows with equal
`ORDER BY` values — are not grouped as `RANGE` requires. It affects ties only, and is narrower than
the defect it came from. `WindowRangeFrameGroupsPeersTest` states it.

### What these are, precisely

The audit ran 16 dimensions and produced **272 findings**, of which **198** were rated
blocker/critical/major. Adversarial verification was capped at the five highest-severity findings per
dimension, so **94 were verified and 104 were not**. §4 of the audit report is the verified subset —
a floor, not a ceiling.

**Every one of the 104 is rated `major`.** No blocker or critical claim is unverified: those all fell
inside the top-five cut. That bounds the risk here — this is a backlog of "probably real, would
matter to a user, nobody attacked the claim", not of potential catastrophes.

### Progress

**2026-07-27 — all 104 settled. Workstream B is complete.** Every dimension has been worked through
by execution or, for the `tests-and-gaps` claims about the build itself, by measurement. The
harnesses live in `AuditVerification/` folders under the Engine, Parser, Core, AdoNet and
EntityFramework test projects: 101 entries examined directly, plus the duplicates settled by the
same evidence.

**Tally: 71 confirmed, 7 confirmed in part or with a correction, 9 confirmed in mechanism or
measurement only, 2 latent, 1 not reproduced, 4 already fixed, 2 not reproducible with the current
surface, 8 duplicates.** Every verdict below carries the behaviour actually observed rather than the
claim that was made.

> **One verdict has already been corrected by a second machine, and it is the cautionary tale of
> this whole pass.** `LsmParallelWriter.FlushAllAsync` was written up as *not reproduced* because
> its test passed locally. Both CI runs then failed it, losing exactly the entries written after the
> foreign flush. The fixture's own opening note says a passing stress run proves only that the race
> did not happen that time — and the verdict ignored it anyway. **Treat every "not reproduced" on a
> concurrency claim as provisional until a second machine agrees**, and prefer `[Ignore]` over an
> active test for anything timing-dependent, or CI inherits the flake.

### What the pass says about the audit

**Roughly four claims in five survive intact.** That is a good hit rate for findings nobody had
attacked — and it is not the interesting number. The interesting number is that **twenty needed
restating, and about half of those understated the defect**: `ON DELETE RESTRICT` on a self-reference
silently deletes the parent; a recursive trigger kills the host process rather than the query;
`SetOutputIdentity` does not mis-report identities but breaks the insert outright; a page-cache
eviction writes another borrower's bytes to disk; an unflushed restart hides **every** committed row.

Four were **already fixed** during the audit session itself — the finding list was written against
pre-fix code and never updated. Two of those were found only by checking history, and one names a
file that no longer exists.

**One finding deserves to be lifted out of the list and acted on first**: the connection-string
password is written into the EF Core log in plaintext, at Information level, the first time a context
is used. It is the only item whose cost does not scale with how often the defect fires.

**And one entry in `tests-and-gaps` explains most of the rest.** The `StatementExecutor` tests mock
`IDatabase` and assert `Received(n)`. A suite that checks call counts against a mock cannot notice a
wrong value — which is precisely the class of defect confirmed over and over here. Closing that, plus
referencing EF Core's specification suite (the SQLite dependency it needs is already paid for), would
do more for confidence than fixing any individual finding in this document.

**Where a claim could not be reproduced, the reason is recorded rather than papered over.** Three
entries are "mechanism confirmed, consequence not reproduced" — the defective code is exactly as
described and something else currently prevents the damage. Those are the ones to re-check after any
refactor, because the protection is incidental rather than designed.

**Two of the three "already fixed" entries were found by checking history, not by re-reading the
claim** — and one of them names a file that no longer exists. When a verification test passes,
`git log -L` on the cited function, and `git log --diff-filter=D` on the cited file, separate "the
claim was wrong" from "someone fixed this during the audit session".

**One finding deserves to be lifted out of the list: the connection-string password is written into
the EF Core log in plaintext**, at Information level, the first time a context is used. It is the
only item in the audit whose cost does not scale with how often the defect fires — one log line, one
shipped secret. Nothing else here is in that category.

What the batch says about the audit's accuracy is the argument for continuing it. The claims are
largely sound — roughly four in five survive intact — but **eight needed restating, and five of
those understated the defect**:

- `ON DELETE RESTRICT` on a self-reference raises nothing and deletes the parent, so the *safe*
  declaration is the one that corrupts;
- the recursive-trigger claim is not a hung query but a **dead host process**;
- the positional-cascade defect deletes the wrong child *and* orphans the right one, on ordinary
  schema — a foreign key to a `UNIQUE` column is all it takes;
- scaffolding does not degrade, it fails on its first query;
- the page-latch defect double-grants a page **and** makes the rightful holder throw on release,
  from a background thread, which kills the process.

- a filtered UNIQUE index does not merely lose its filter, it becomes a **stricter** constraint that
  rejects rows the application is entitled to insert;
- `TransactionScope` does not fail loudly, it silently **keeps** the write from a scope that was
  never completed.

Four were overstated: the index table-qualifier defect and the `DatabaseLock` cancellation leak are
both **unreachable**, DROP COLUMN breaks 2 of the 4 metadata kinds it was said to break, and the
isolation level is silently *dropped* through ADO.NET rather than leaked onto the next transaction.
One was wrong about the mechanism (the autoincrement row is reachable by the *wrong* key), and two
do not reproduce at all.

Reading alone would have carried every one of those errors forward in both directions.

Two verdicts are worth their own category, because collapsing them either way would lose
information. `StorageFile`'s sync/async mixing and the missing file lock are both **real in the
code and invisible in behaviour** — the first because a page-sized write bypasses the stream buffer
anyway, the second because `FileShare.None` already gives the OS-level exclusion the absent lock was
supposed to provide. Neither should be reported as a working feature; neither is currently hurting
anyone.

**Two traps that made a test pass on a live defect**, both worth carrying into the remaining
dimensions:

- *Asserting a relation instead of a value.* The MERGE test first asserted only
  `TargetAlias != SourceAlias`, and passed — the two do differ, in the wrong direction. Check each
  value against what it should hold.
- *Handing off on the same thread.* `ReaderWriterLockSlim.IsWriteLockHeld` is thread-affine, so
  holding a latch on the test's own thread made `PageLatchManager.Cleanup` look correct. The defect
  only appeared once the latch was acquired on a second thread.
- *Sharing a model with a defect that fires at model build.* One `ToJson` mapping in a shared
  `DbContext` made every test in the `ef-translation` fixture fail in setup with an unrelated error,
  so the whole first run's verdicts were worthless. Isolate anything that can fail before the query
  does.

And one verdict that is easy to get wrong in the other direction: **a passing test does not
distinguish "the claim was wrong" from "the claim was right and was fixed during the audit
session"**. Run `git log -L <range>:<file>` on the cited function before writing "not reproduced" —
doing so turned two entries into *already fixed*, both by commit `9556bd2`.

Convention used, following the existing `WitSqlEnginePrecedenceTests` BETWEEN tests: a confirmed
finding gets a test asserting the **correct** behaviour, marked `[Ignore]` with the observed
behaviour, so it is an executable specification that turns green the day the defect is fixed.
Refuted, latent and partially-refuted findings keep **passing** tests as regression pins. The one
exception is the recursive-trigger test, marked `[Explicit]`: `[Ignore]` would not be enough,
because running it takes the whole test host down.

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

### Cross-cutting quality  <sub>`cross-cutting` — 12, verified 2026-07-27</sub>

Verified in
[CrossCuttingEfTests.cs](../Sources/Providers/OutWit.Database.EntityFramework.Tests/AuditVerification/CrossCuttingEfTests.cs),
[CrossCuttingCoreTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/CrossCuttingCoreTests.cs)
and
[CrossCuttingAdoNetTests.cs](../Sources/Providers/OutWit.Database.AdoNet.Tests/AuditVerification/CrossCuttingAdoNetTests.cs).

**The credential leak is the one to act on first** — not because it is the most likely to fire, but
because it is the only finding in the audit whose cost does not scale with how often it is hit. One
log line is enough.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | Connection-string password is written into the EF Core log through LogFragment and PopulateDebugInfo | both surfaces leak, verbatim: `Using WitDatabase 'Data Source=…;Password=hunter2-should-never-be-logged'}` and `WitDb:ConnectionString=…;Password=hunter2-should-never-be-logged`. EF Core writes `LogFragment` at **Information** level the first time a context is used, so the encryption password lands in ordinary application logs | `EntityFramework/…/WitDbContextOptionsExtension.cs:246` |
| **confirmed** | The engine never throws a DbException | a missing table gives `InvalidOperationException: Table 'NoSuchTable' not found`, bad SQL gives `WitSqlParsingException`, a duplicate key gives `InvalidOperationException: UNIQUE constraint failed`. None derive from `DbException`. `WitDbException` does and has a `FromException` factory — nothing calls it, and `WitDbCommand` contains **no `catch` at all**. Every framework that handles database failure generically (EF Core execution strategies, Polly, ASP.NET diagnostics) keys off `DbException` and will not see these | `AdoNet/WitDbException.cs:119` |
| **confirmed**, worse | BulkOptions.SetOutputIdentity inserts explicit zero keys instead of reading identities back | it does not merely mis-document — enabling it **breaks the insert**: `InvalidOperationException: UNIQUE constraint failed: GeneratedRows.Id (duplicate value: 0)`. The option adds the identity property to the insert column list, so every row is sent an explicit zero key and the second collides with the first. Any bulk insert of more than one row with a generated key fails | `EntityFramework/…/WitDbBulkExtensions.cs:555` |
| **confirmed**, worse | Reopening an encrypted MVCC database silently downgrades it to non-MVCC | `SupportsMvcc` is False after the reopen **and the data is gone** — a row written before it comes back `null`. The unencrypted `Open()` reads the header and restores MVCC when `detection.HasMvcc`; the encrypted overload cannot read the header, so it unconditionally calls `WithTransactions()`. This is not a capability downgrade, it is silent data loss | `Core/Builder/WitDatabase.cs:310` |
| **confirmed**, measured | EF translates DateTime.Now / Today / DateTimeOffset.Now to NOW(), which the engine defines as UTC | the server returned `05:09:58` where local time was `08:09:58+03:00` — off by exactly the machine's UTC offset, **180 minutes** | `EntityFramework/…/WitMemberTranslator.cs:133` |
| **confirmed** | Migration SQL literals are generated with the current culture | under `de-DE` the generator emitted `ALTER TABLE "T" ADD COLUMN "Price" DECIMAL(18,2) NOT NULL DEFAULT 1,5;`. A migration generated on a comma-locale developer machine is corrupt SQL | `EntityFramework/…/WitMigrationsSqlGenerator.cs:809` |
| **half confirmed, half not verified** | Three migration operations are emitted as SQL comments **and** idempotent scripts are generated without guards | the comment half is confirmed in the `dropin-gaps` table above, verbatim output included. The idempotent half is **not verified**: a `DbContext` declared inline in a test assembly has no migration classes, so `GenerateScript(…, Idempotent)` has nothing to guard and any assertion over it is vacuous — a first attempt passed only because the DDL happened to contain `IF NOT EXISTS`, which is not a migration guard. Needs the `dotnet ef migrations add` round-trip the audit lists under `tests-and-gaps` | `EntityFramework/…/WitMigrationsSqlGenerator.cs:312` |
| **confirmed by inspection**, consequence not reproduced | Disposal paths swallow write failures and skip cleanup on exception | the swallow is literal: `try { m_database.Flush(); } catch { }` with the comment "Best effort - don't fail dispose on flush errors", so a failed final write is invisible. But the "skips cleanup, leaking file handles" half does **not** hold for the flush — the catch guarantees `m_database.Dispose()` still runs. It holds for a different line: `m_currentTransaction?.Dispose()` sits *before* the try, unguarded, so a throwing transaction dispose skips the store dispose entirely. Right conclusion, wrong line | `Engine/WitSqlEngine.cs:302` |
| **confirmed by inspection**, consequence not reproduced | LSM compaction swallows File.Delete failures, and SSTableReader's FileShare mode makes them likely on Windows | literally `try { File.Delete(file); } catch { }`, and `SSTableReader` opens with `FileShare.Read`, which on Windows refuses a delete while any reader holds the file — so the failure the swallow hides is the likely case, not the exotic one. Combined with the separate `core-lsm` finding that compaction has no manifest and infers the live set from the directory listing, an undeleted input resurrects rows | `Core/Stores/StoreLsm.cs:521` |
| **partly confirmed** | The IndexedDB/Blazor WASM story cannot work | `WitSqlEngine.Async.cs` is confirmed **0 bytes**, and `WitDbConnection`'s async transaction methods are `Task.Run` wrappers around the synchronous ones — two of the four sub-claims. The README sample and the sync-over-async storage claim are not verified | `Engine/WitSqlEngine.Async.cs:1` |
| **duplicate** | ConnectionPool is unreachable from the provider and leaks a semaphore permit on every borrow | settled under `core-concurrency` — confirmed, but latent because the provider does not use the pool | `AdoNet/Pool/ConnectionPool.cs:234` |
| **duplicate** | EF Core database-first scaffolding is SQLite code that WitSQL cannot execute | settled under `engine-schema-ddl` — `sqlite_master` does not exist and `PRAGMA` does not parse | `EntityFramework/…/WitDatabaseModelFactory.cs:92` |

### Core: concurrency and locking  <sub>`core-concurrency` — 11, all verified 2026-07-27</sub>

Verified in
[CoreConcurrencyFindingsTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/CoreConcurrencyFindingsTests.cs)
and, for the pool, in
[ConnectionPoolFindingTests.cs](../Sources/Providers/OutWit.Database.AdoNet.Tests/AuditVerification/ConnectionPoolFindingTests.cs).

**Every test here is deterministic.** That was the point of the batch: a stress run that stays green
proves only that the race did not happen this time, so each test either drives the threads into the
exact interleaving the finding describes — a storage double that parks inside `WritePageAsync`, a
latch acquired on a second thread — or replaces the race with a direct observation of the state the
race would corrupt.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | DeadlockDetector is never fed any wait edge | both transactions failed with `TimeoutException` after the full 2 s timeout; neither raised `DeadlockException`. In the code the detector is read into a local that is never used, and the deadlock check is an **empty `if` body** whose comment reads "full implementation would track all holders" | `Transactions/MvccTransaction.cs:228` |
| **latent** | DatabaseLock.AcquireReadLockAsync leaks m_readerCount on cancellation | the `catch` blocks genuinely do not restore `m_readerCount`, so the code is one refactor from leaking — but the window cannot be entered. Every writer path takes `m_readerGate` **before** `m_writeSemaphore`, so a reader can never find the semaphore held while the gate is open, which is the only state in which the increment is followed by a blocking wait. 200 cancelled acquisitions leave the count at 0 | `Concurrency/DatabaseLock.cs:153` |
| **confirmed** | RowLockHandle.Dispose() is an empty method | `IsLocked` still reports true after disposal, and a second transaction cannot take the row | `Concurrency/RowLockHandle.cs:40` |
| **confirmed**, measured | RowLockManager completes TaskCompletionSource under m_syncLock without RunContinuationsAsynchronously | `ReleaseAllLocks` took **1007 ms** for a waiter whose continuation sleeps 1000 ms. The releasing thread runs the woken transaction's code to completion while holding the manager's lock | `Concurrency/RowLockManager.cs:110` |
| **confirmed** | LsmParallelStore.Get/Scan do not wait for the background merge | `Get` returned **null** for a key written moments earlier on the same thread; `Scan` returned **0 rows** | `Builder/LsmParallelStore.cs:83` |
| **confirmed** <sub>(corrected — first recorded as "not reproduced")</sub> | LsmParallelWriter.FlushAllAsync drains and disposes other threads' live buffers | **the development machine said no and CI said yes.** All 20 keys survived locally, so this was first written up as not reproduced. Both PR runs on CI then failed it, losing the tail of the producer's second batch — `k18,k19` on one run, `k17,k18,k19` on the other. Those are precisely the entries written *after* the foreign `FlushAllAsync`, so the flush really does take a buffer another thread is still using. Now `[Ignore]`d rather than left active, because as a running test it is timing-dependent and fails intermittently | `LSM/LsmParallelWriter.cs:217` |
| **confirmed**, worse | Page caches dispose CachedPage while an async write of that page is in flight | **this one corrupts data outright.** With the write parked inside the storage double, the page that reached storage was filled with `0xFF` — the content of the next borrower of the recycled pooled array — instead of the `0xAB` the caller wrote. The path matters: `Evict()` correctly refuses with "Cannot evict pinned page"; **`Clear()`** disposes every `CachedPage` unconditionally | `Cache/PageCacheShardedClock.cs:160` |
| **confirmed**, latent for the provider | ConnectionPool never reclaims a permit | with `MaxPoolSize = 1`, a second borrow after the first connection was disposed had still not completed **5012 ms** later. `GetConnection` hands back `pooledConn.InnerConnection`, so the caller never holds the `PooledConnection` that `ReturnConnection` needs. But no type outside the `Pool` namespace holds a `ConnectionPool`, so the provider does not use the pool — a defect in public API surface, not on a live path | `AdoNet/Pool/ConnectionPool.cs:234` |
| **mechanism confirmed, consequence not reproduced** | StorageFile mixes locked synchronous FileStream I/O with unlocked async I/O on the same handle | the mechanism is exactly as described — the sync path seeks and reads through the buffered `FileStream` under `m_lock`, the async path calls `RandomAccess` on `m_stream.SafeFileHandle` with **no lock at all** — but neither cross-path probe shows a user-visible effect: a page written sync is visible to an async read and vice versa, because a write of exactly `pageSize` bypasses the stream's buffer | `Storage/StorageFile.cs:199` |
| **mechanism confirmed, consequence prevented elsewhere** | EnableFileLocking defaults to true but the builder selects the in-process-only LockManager overload | true at the code level: `new LockManager(Options.LockTimeout)` sets `m_fileLock = null; m_useFileLocking = false`, while `ProviderFeatures.FileLocking` is still advertised. But a second opener **is** refused — `StorageFile` opens with `FileShare.None`, so the OS provides the exclusion the missing lock would have. The user-facing gap is the failure *mode*, a hard `IOException` instead of the lock timeout the option implies, not unguarded concurrent access | `Builder/WitDatabaseBuilder.cs:561` |
| **confirmed**, worse | PageLatchManager.Cleanup can dispose a latch another thread holds, and Release silently no-ops | both halves reproduce, and the mechanism is sharper than written: `Cleanup` tests `latch.IsWriteLockHeld`, which is **thread-affine** — a latch held by another thread looks completely idle to the cleanup thread. The second exclusive acquire **is granted** while the page is held, and the original holder's release then throws `SynchronizationLockException: The write lock is being released without being held`, because it lands on the replacement latch. That exception is raised on a background thread, so unhandled it **terminates the process** — it crashed the test host before the test wrapped its Dispose | `Tree/PageLatchManager.cs:228` |

### Drop-in capability gaps  <sub>`dropin-gaps` — 10, all verified 2026-07-27</sub>

Verified across three projects, because "drop-in" is a claim about three different contracts:
[DropInGapsEngineTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests.cs)
(grammar and isolation),
[DropInGapsAdoNetTests.cs](../Sources/Providers/OutWit.Database.AdoNet.Tests/AuditVerification/DropInGapsAdoNetTests.cs)
(ADO.NET contract) and
[DropInGapsMigrationsTests.cs](../Sources/Providers/OutWit.Database.EntityFramework.Tests/AuditVerification/DropInGapsMigrationsTests.cs)
(migrations).

**9 of the 10 reproduce.** Two methodological notes are worth keeping, because both changed what the
tests had to look like:

- The ADO.NET tests are written against the **base types** — `DbTransaction`, `DbConnection` — not
  against WitDatabase's own classes. That is not pedantry: `WitDbTransaction` declares `Save`,
  `Rollback(string)` and `Release(string)` as `public void` rather than `override`, so the methods
  work perfectly when called on the concrete type and throw when called through the contract. **This
  is precisely why the provider's own test suite does not catch it and EF Core does.** Any future
  drop-in test that instantiates the concrete type is testing the wrong thing.
- The migrations generator's failure mode is emitting a **SQL comment** where it cannot emit a
  statement. A comment is a valid script that changes nothing, so the migration is recorded as
  applied while the database keeps its old schema. Asserting "SQL was produced" would pass; the
  tests assert the output is not comment-only.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed**, half restated | `BeginTransaction(IsolationLevel.X)` runs at ReadCommitted and leaks the requested level onto the *next* transaction | first half confirmed, and the mechanism is now exact. `StatementExecutor.Transactions.cs` says in a comment "Use SET TRANSACTION ISOLATION LEVEL **before** BEGIN TRANSACTION if needed" — and `WitDbConnection.BeginDbTransaction` emits them in the opposite order, so the level is still sitting unapplied in `PendingIsolationLevel` after `BEGIN`. Second half **restated**: the leak is real *within one execution context*, but `WitSqlEngine.Execute` builds a **fresh `ContextExecution` per call**, so through ADO.NET the requested level is silently **dropped**, not carried forward | `AdoNet/WitDbConnection.cs:164` |
| **confirmed** | Schemas are unsupported at every layer; the one name the validator accepts (`public`) produces unresolvable SQL | `EnsureSchemaOperation` throws `NotSupportedException`, and a schema-qualified `CreateTable` emits `CREATE TABLE IF NOT EXISTS "T"` with the schema dropped — while EF's query and update generators keep it, so the DDL cannot match the DML | `EntityFramework/Metadata/WitModelValidator.cs:56` |
| **confirmed**, worse | `AlterColumn` silently emits nothing for a column-type change | emits **nothing at all** — not even the explanatory comment its sibling operations produce. The migration is recorded as applied and the column keeps its old type | `.../WitMigrationsSqlGenerator.cs:182` |
| **confirmed** | AddPrimaryKey / DropPrimaryKey / RenameIndex emit SQL comments | verbatim: `-- WitDatabase limitation: Cannot add PRIMARY KEY to existing table. Columns: Id`, `-- WitDatabase limitation: Cannot drop PRIMARY KEY from existing table. Table: T`, `-- Rename index: IX_Old -> IX_New` | `.../WitMigrationsSqlGenerator.cs:320` |
| **confirmed**, worse | Filtered indexes, `IncludeProperties` and descending indexes are silently dropped | "dropped" understates it for the filtered case. A **filtered UNIQUE** index came out as `CREATE UNIQUE INDEX ... ON "T" ("Value")` with no `WHERE`, which enforces a **stricter** constraint than the model declares — rows the application is entitled to insert are rejected. Descending direction is dropped likewise | `.../WitMigrationsSqlGenerator.cs:239` |
| **confirmed** | EF-generated CROSS APPLY / OUTER APPLY cannot be parsed | `CROSS APPLY`, `OUTER APPLY` and a `VALUES` table source all fail to parse. These fail at **runtime**, not at model build | `Parser/Grammars/WitSqlParser.g4:157` |
| **not reproduced** | `ExecuteUpdate`/`ExecuteDelete` support only single-table statements | four shapes all succeed: `ExecuteDelete` and `ExecuteUpdate` with a predicate over a navigation, `ExecuteDelete` over an explicit `Join`, and `ExecuteDelete` with a `NOT IN` over a filtered projection of another table — the pruning shape the finding names. If OpenIddict pruning does fail, it fails for some other reason, and the reproducing query is still needed | `.../WitDbServiceCollectionExtensions.cs:37` |
| **confirmed** | Savepoints are not wired to the ADO.NET contract | `DbTransaction.SupportsSavepoints` is **False**, and `Save` through the base type throws `NotSupportedException`. EF Core checks exactly that property before using a savepoint to recover a failed `SaveChanges` | `AdoNet/WitDbTransaction.cs:104` |
| **confirmed**, worse | Ambient transactions / TransactionScope are unsupported | `EnlistTransaction` throws `NotSupportedException`, as claimed — but the damaging part is what happens when nobody calls it: a write inside a `TransactionScope` that is **never completed survives**. Code relying on `TransactionScope` for atomicity is silently wrong rather than loudly unsupported | `AdoNet/WitDbConnection.cs:154` |
| **confirmed** | User-defined functions and stored procedures do not exist, while the spec documents them | neither `CREATE FUNCTION` nor `CREATE PROCEDURE` parses. WitSQL.md gives both full sections with syntax — §22 and §23. These are the two gaps Dmitry names as remaining before true drop-in status | `Parser/Grammars/WitSqlParser.g4:35` |

### Test-suite gaps  <sub>`tests-and-gaps` — 8, all verified 2026-07-27</sub>

Verified in
[TestsAndGapsFindingsTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/TestsAndGapsFindingsTests.cs).
This dimension is unlike the rest: the claims are about the **test suite and the build**, not about
what the engine does, so they are settled by measuring the repository. The tests still assert the
*desired* state, so each turns green the day its gap is closed.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | EF Core's provider specification suite is not referenced, while an unused SQLite reference makes a differential oracle nearly free | **the single highest-value entry in the backlog.** The csproj references `Microsoft.EntityFrameworkCore.Sqlite` on both TFMs and **not** `Specification.Tests` — so the dependency that would make a differential oracle nearly free is already paid for, and the conformance suite that decides the drop-in claim is absent | `EntityFramework.Tests.csproj:24` |
| **confirmed** | No coverage measurement and no mutation testing, despite coverlet.collector in all seven test projects | measured: coverlet.collector is referenced by **all 7** test projects, and `ci.yml` contains **zero** occurrences of a collect flag, of `stryker`, or of `mutation`. Given that nine behaviours changed during the audit without a single test failing, mutation testing is the gap that would have caught them | `.github/workflows/ci.yml:56` |
| **confirmed** | StatementExecutor tests mock IDatabase and assert Received(n), so read-your-own-writes defects are structurally invisible | seven occurrences of `Substitute.For<IDatabase>` / `Received(` in that one file. **This is the finding that explains the others**: a suite asserting call counts against a mock cannot notice a wrong value, which is exactly the class of defect this verification pass kept confirming | `StatementExecutorUpdateTests.cs:418` |
| **confirmed** | The single corruption test flips one hard-coded byte behind an `if` that can silently skip the mutation | `bytes[25] ^= 0xFF` sits inside `if (bytes.Length > 30)`. A shorter file skips the mutation and the test passes having verified nothing | `Core.Tests/Wal/WriteAheadLogTests.cs:284` |
| **confirmed**, different arithmetic | No SQL-literal round-trip property test: only 2 of 9 LiteralType values are round-tripped | measured: **6 of the 10** members are never mentioned by the serializer tests — `Real`, `Blob`, `CurrentTimestamp`, `CurrentDate`, `CurrentTime`, `Decimal`. The enum has since gained `Decimal` (commit `9556bd2`) and 4 members are exercised rather than 2. Same gap, different numbers | `Parser.Tests/SerializerTests.cs:236` |
| **confirmed** | Five [Ignore]d ADO.NET tests silence an unfixed defect with no negative test asserting a clean failure | exactly **5** `[Ignore]` attributes in that file | `AdoNet.Tests/Parallel/WitDbConnectionParallelAccessTests.cs:79` |
| **partly wrong** | The sync `Database.Migrate()` path has **zero** coverage, and no test round-trips `dotnet ef migrations add` | the sync path **is** covered: `context.Database.Migrate()` appears twice, in `MigrationTests/SchemaEvolutionRegressionTests.cs` (lines 58 and 80). The second half holds — nothing round-trips `dotnet ef migrations add`, because no real `Migration` subclass exists anywhere in the test projects | `MigrateAsyncIntegrationTests.cs:54` |
| **partly wrong** | The LSM reference-model oracle is one-sided … and WAL is off | WAL is **enabled** in two of the four stress configurations (`WAL+Cache+SyncCompact`, `WAL+Cache+BgCompact`); only `NoWAL+Cache` turns it off. The rest holds and was measured: the seed is fixed (`new Random(42)`) and verification covers `expected.Take(1000)` keys | `Core.Tests/LSM/LsmTreeStressTests.cs:428` |

### Engine: DML and indexes  <sub>`engine-dml` — 7, all verified 2026-07-27</sub>

Verified by execution in
[EngineDmlFindingsTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/EngineDmlFindingsTests.cs).
**All seven reproduced.** Two are worse than the audit stated and one is narrower — see the notes.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | Foreign keys that reference their own table are excluded from all cascade handling | `ON DELETE CASCADE` leaves the child row behind. Worse, `ON DELETE RESTRICT` **raises nothing** — the parent is deleted and the child is left dangling, so the safe declaration is the one that corrupts | `StatementExecutor.Validation.cs:91` |
| **confirmed** | ON UPDATE CASCADE / SET NULL / SET DEFAULT is never applied | child key stays at the old value under both CASCADE and SET NULL, producing a genuine **orphan row** — referential integrity is not maintained on the UPDATE path at all | `StatementExecutor.Validation.cs:163` |
| **confirmed**, restated | UPDATE of an autoincrement primary key desynchronises the PK from the internal rowid | the audit says "unreachable by PK"; it is reachable by the **wrong** key. After `UPDATE T SET Id = 100 WHERE Id = 1`: `SELECT Id` returns **100**, `WHERE Id = 100` returns **nothing**, and `WHERE Id = 1` returns **one row that projects Id = 100** — a row contradicting the predicate that found it. Bounded on one side: the uniqueness check reads the stored column, so a duplicate insert of 100 is still rejected. It is a lookup defect, not a uniqueness hole | `StatementExecutor.Update.cs:891` |
| **confirmed** | Narrowing numeric writes silently truncate/wrap, and unparseable text is written as 0 | no exception for 100000→`SMALLINT`, 999→`TINYINT`, 9999999999999→`INT`, or `'not a number'`→`INT`. WitSQL.md documents each type's exact range, so these are outside the declared contract | `Types/WitTypeConverter.cs:576` |
| **confirmed** | Declared VARCHAR(n) length and DECIMAL(p,s) precision/scale are recorded but never enforced | a 12-character string is accepted into `VARCHAR(5)`; `123456.78` into `DECIMAL(5,2)`; and `DECIMAL(10,2)` stores and returns `1.23456` unrounded, so the column does not round-trip at the precision the schema promises | `Definitions/DefinitionColumn.cs:148` |
| **confirmed** | Statements are not atomic: a constraint failure part-way through a multi-row DML leaves earlier rows written | a 3-row INSERT failing on the third leaves **2 rows** committed; a 5-row UPDATE failing partway leaves row 1 mutated from 1 to **30**. The UPDATE case is the worse one — it damages data that already existed | `StatementExecutor.Update.cs:1076` |
| **confirmed**, conclusively | Recursive triggers have no depth limit and terminate the process with a StackOverflowException | run alone, the test host dies: `Test host process crashed : Stack overflow.` There is no depth counter anywhere in the file. `StackOverflowException` is uncatchable in .NET, so a self-referencing trigger takes the **host application** down, not just the query | `StatementExecutor.Triggers.cs:121` |

### Engine: query execution and optimizer  <sub>`engine-query` — 7, all verified 2026-07-27</sub>

Verified by execution in
[EngineQueryFindingsTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/EngineQueryFindingsTests.cs).
Confirmed tests carry `[Ignore]` with the behaviour observed; the passing ones stay active as pins.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | LIMIT is applied before DISTINCT, so `SELECT DISTINCT ... LIMIT n` can return fewer than n distinct rows | returned **1** row where 3 distinct values were available | `Query/QueryPlanner.cs:545` |
| **confirmed** | Default window frame is the whole partition instead of UNBOUNDED PRECEDING..CURRENT ROW | `SUM(x) OVER (ORDER BY Id)` returned **450, 450, 450** instead of 100, 250, 450 | `Iterators/IteratorWindow.Frame.cs:24` |
| **confirmed** | `ORDER BY ... NULLS FIRST \| NULLS LAST` is parsed and then silently ignored | NULLs sort first either way. `NULLS FIRST` therefore *appears* to work — it agrees with the default by coincidence, so only `NULLS LAST` exposes it | `Iterators/IteratorSort.cs:45` |
| **confirmed** | LIKE is compiled to a .NET regex without Singleline/CultureInvariant | three distinct defects, each reproduced: `'a\nb' LIKE 'a%b'` and `LIKE 'a_b'` both match **nothing**; `'abc\n' LIKE 'abc'` **matches**; `'I' LIKE 'i'` matches under invariant culture and **not** under `tr-TR` | `Expressions/ExpressionEvaluator.Conditional.cs:155` |
| **confirmed** | Most scalar functions do not propagate NULL | **9 of 11** probed expressions return a zero-value. `ABS(NULL)` and `NULL \|\| 'x'` are correct, so "most" is accurate but not universal | `Expressions/ExpressionEvaluator.Functions.cs:58` |
| **confirmed** | Equals/GetHashCode contract is violated for cross-type numerics | Integer/Decimal, Integer/Real and Real/Decimal all report `Equals` true with three different hash codes; `SELECT 1 UNION SELECT 1.0` returns **2 rows** while `1 = 1.0` is true | `Values/WitSqlValue.Comparison.cs:68` |
| **latent** | Index selection matches predicates by column name only, ignoring the table qualifier | the defective code is exactly as described — `FindMatchingPredicate` compares `ColumnName` alone and never reads the `TableAlias` captured beside it — but nothing reaches it. `QueryPlanner` declares an `OptimizerQuery` field and **never calls it**, so SELECT never uses this optimizer; the only callers are UPDATE/DELETE, and both bypass it as soon as a second table appears; `ExtractPredicatesRecursive` does not descend into subqueries; and `DmlOptimizer` only consults it past 50 rows. Four probes seeded past that floor all pass | `Optimizers/OptimizerQuery.cs:272` |

### Parser and grammar  <sub>`parser` — 7, all verified 2026-07-27</sub>

Verified in
[ParserFindingsTests.cs](../Sources/Engine/OutWit.Database.Parser.Tests/AuditVerification/ParserFindingsTests.cs)
(parse level) and
[ParserFindingsEngineTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/ParserFindingsEngineTests.cs)
(the claims that are about what a parsed statement *does*). **6 of 7 reproduce.**

One methodological note from this batch, because it nearly cost a real defect: the MERGE test first
asserted only that `TargetAlias != SourceAlias`, **and it passed** — the two do differ, in the wrong
direction. Asserting a relation between two values is not the same as asserting each value.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | Serializers emit unquoted identifiers using an incomplete reserved-word list | the set holds **68** entries; WitSQL.md's documented reserved list is roughly twice that. `Using`, `With`, `Row`, `Column`, `Cross`, `Interval` and `Partition` are all emitted unquoted and then fail to re-parse. `Order` and `Group`, used as controls, round-trip correctly | `Parser/Serializers/WitSqlExpressionSerializer.cs:441` |
| **confirmed** | `INSERT INTO t DEFAULT VALUES` is not in the grammar | `WitSqlParsingException` with 2 errors | `Parser/Grammars/WitSqlParser.g4:194` |
| **confirmed** | MERGE assigns the source alias to TargetAlias when the target alias is omitted | the aliases are **exactly swapped**: `TargetAlias = "s"` (the source's) and `SourceAlias = null` | `Parser/Visitor/WitSqlVisitor.DML.cs:373` |
| **confirmed**, worse | No typed, prefixed or hexadecimal literal forms | in a `WHERE`, `Flags & 0x0F` is a clean syntax error — `mismatched input 'x0F'`. In a select list it is **worse than an error**: `SELECT 0x1F` parses silently as the integer `0` with the column alias `x1F`. A query written against the spec's own bitwise examples returns 0 instead of failing | `Parser/Grammars/WitSqlParser.g4:527` |
| **confirmed** | MySQL-style `LIMIT offset, count` binds the operands the wrong way round | `LIMIT 10, 5` yields `offset = 5`, `count = 10`. Executed against 20 rows it returns **6..15 instead of 11..15** — a silently wrong page | `Parser/Visitor/WitSqlVisitor.DML.cs:80` |
| **confirmed**, mechanism restated | Documented trigger bodies are unusable | `SET NEW.col = ...` is a syntax error, as claimed. The SIGNAL half is right about the symptom and wrong about the stage: SIGNAL **parses** fine and fails later with `NotSupportedException: Statement serialization not supported: WitSqlStatementSignal` — the break is in statement *serialization*, not in the grammar or in execution | `Parser/Grammars/WitSqlParser.g4:80` |
| **already fixed** <sub>(reclassified)</sub> | Integer literals above long.MaxValue escape as a raw OverflowException, and long.MinValue cannot be written | nothing is thrown: `99999999999999999999` is promoted to `Decimal` and keeps its value exactly, and `-9223372036854775808` parses for the same reason. First recorded as "not reproduced"; the `literal-roundtrip` batch then found the cause — commit **`9556bd2`**, in the 2.0.0 merge, fixed exactly this alongside the decimal-literal defect. The tests pin the fixed behaviour | `Parser/Visitor/WitSqlVisitor.Expressions.cs:277` |

### Engine: schema catalog and DDL  <sub>`engine-schema-ddl` — 6, all verified 2026-07-27</sub>

Verified by execution in
[EngineSchemaDdlFindingsTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/EngineSchemaDdlFindingsTests.cs).
All reproduce; one is narrower than written, one is a duplicate, and one settles two entries in
other dimensions as well.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | Named constraints declared in CREATE TABLE lose their names, so ALTER TABLE DROP CONSTRAINT can never remove them | reproduced for CHECK, FOREIGN KEY and UNIQUE alike: `InvalidOperationException: Constraint 'CK_V' not found on table 'T'`. Constraints added *later* via `ALTER TABLE ADD CONSTRAINT` do work — which is why the existing `WitSqlEngineAlterTableConstraintTests` never caught this. In the engine's favour: it fails **loudly**, so an EF `DropForeignKey` migration throws rather than silently leaving the constraint in place | `StatementExecutor.Ddl.Tables.cs:128` |
| **confirmed** | ALTER TABLE ADD COLUMN silently discards UNIQUE, PRIMARY KEY, CHECK and REFERENCES column constraints | every violating INSERT accepted without exception — duplicate into a `UNIQUE` column, `-5` into `CHECK (Age >= 0)`, `999` into a `REFERENCES P(Id)` column. Unlike the row above this one is **silent**: the column reads as constrained in the DDL the user wrote and is unconstrained in the database | `StatementExecutor.Ddl.Tables.cs:283` |
| **confirmed in part** | DROP COLUMN leaves the dropped column referenced by PRIMARY KEY / UNIQUE / FK / index metadata, making the table un-insertable | only **2 of the 4** named metadata kinds are affected. Index ✅ and UNIQUE ✅ are cleaned up correctly, as is dropping the column an FK points *at* ✅. Broken: **FOREIGN KEY** and **PRIMARY KEY**, both accepting the drop and then failing the next INSERT with `KeyNotFoundException: Column '…' not found`. Not the DROP COLUMN defect fixed in 2.0.0 — that one re-serialised rows against the pre-drop column list; this is the metadata half | `Schema/SchemaCatalog.Columns.cs:41` |
| **duplicate** | Self-referencing foreign keys never cascade | same defect as the `engine-dml` entry at line 91, already confirmed — and there the worse half is that `ON DELETE RESTRICT` raises nothing | `StatementExecutor.Validation.cs:89` |
| **confirmed**, worse than stated | Cascade matching ignores fk.ForeignColumns and compares child FK values positionally against the parent's PRIMARY KEY | **the most damaging finding in the batch.** It goes wrong in *both* directions on the same fixture: a child whose referenced row still exists **is deleted** (silent data loss), and a child whose referenced row is gone **survives** (silent orphan). Reproduced again with a composite FK listing the parent's PK columns in reverse order. This needs no contrived schema — only a foreign key pointing at a `UNIQUE` column rather than the PK | `StatementExecutor.Validation.cs:277` |
| **confirmed**, ×3 | dotnet ef dbcontext scaffold cannot work: WitDatabaseModelFactory queries sqlite_master and SQLite PRAGMAs the engine does not implement | `SELECT name FROM sqlite_master` → `InvalidOperationException: Table 'sqlite_master' not found`; `PRAGMA` does not even parse, it is not in the grammar's statement set. Scaffolding fails on its **first** query, so database-first is inoperative rather than incomplete. The identical finding is listed under `cross-cutting` and `ef-runtime`, so this evidence settles all three entries | `EntityFramework/Design/Internal/WitDatabaseModelFactory.cs:92` |

### Core: MVCC and isolation  <sub>`core-mvcc` — 6, all verified 2026-07-27</sub>

Verified in
[CoreMvccFindingsTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/CoreMvccFindingsTests.cs).

Two techniques worth reusing for the remaining core dimensions. The commit-cost claim is settled by
**counting, not timing** — a counting inner `IKeyValueStore` turns "scans the whole database" into a
deterministic number, avoiding the stopwatch-assertion mistake the suite's own `Performance/` tests
already make. And the crash claim needs **no process kill**: an in-memory inner store plays the part
of the durable media and simply outlives the `MvccKeyValueStore` that never got to flush.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | READ COMMITTED point reads and range scans use different snapshots | exactly as described: the same transaction read `a` as **'2' by key and '1' by scan**. READ COMMITTED permits seeing another transaction's commit; it does not permit two reads in one transaction to disagree about the same key | `Core/Transactions/MvccTransaction.cs:158` |
| **half confirmed** | SERIALIZABLE does not prevent phantoms or write skew | **write skew: confirmed** — both transactions committed and nobody was left on call. **Phantoms: not reproduced** — a row inserted after the reader began does not appear in its rescan, because the snapshot already hides it. So what this actually provides is *snapshot isolation*: phantoms stopped, write skew allowed. That gap is precisely the difference SERIALIZABLE exists to close, but the finding's wording claims more than is true | `Core/Transactions/MvccTransaction.cs:381` |
| **duplicate** | ADO.NET/EF Core isolation level is silently ignored and leaks into the following transaction | settled under `dropin-gaps` — and restated there: through ADO.NET the level is silently **dropped**, not leaked, because `WitSqlEngine.Execute` builds a fresh execution context per call | `AdoNet/WitDbConnection.cs:164` |
| **confirmed** | Garbage collection never reclaims deleted keys or metadata versions | 50 inner records before `RunNow()` and **50 after** — 50 keys written, all 50 deleted, no live transaction protecting any of them, nothing reclaimed | `Core/Stores/MvccKeyValueStore.cs:546` |
| **confirmed**, with a number | Every commit and rollback scans the entire database | committing **one** row over a 500-row store enumerated **502 entries across 7 scans**. The cost of a commit follows the size of the database, so bulk `SaveChanges` really is quadratic | `Core/Stores/MvccKeyValueStore.cs:400` |
| **confirmed**, and total | The persisted max timestamp can lag the data, so after a crash committed rows become invisible | **0 of 10** committed rows were visible after an unflushed restart. The watermark is written only on `Flush` and `Dispose`, so recovery reads a timestamp that hides everything committed since the last flush — the data is on the media and unreachable | `Core/Stores/MvccKeyValueStore.cs:749` |

### Core: WAL, recovery, durability  <sub>`core-durability` — 6, verified 2026-07-27</sub>

Verified in
[CoreDurabilityFindingsTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/CoreDurabilityFindingsTests.cs).
Recovery is simulated the same way as in the LSM and MVCC batches: the journal file is the durable
media and a fresh store is opened over it — no process kill, and the interleaving is exact.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed**, and quantified | Recovery truncates the WAL after a partial replay, destroying every committed transaction behind a bad record, with no error reported | after corrupting 16 bytes in the middle of the log, **2 of 5** committed transactions were recovered and **no error was reported**. Three committed transactions vanished in silence — that is the half that matters, since a database may lose data to corruption but must say so | `Core/Transactions/TransactionalStore.cs:403` |
| **confirmed** | Savepoint rollback is invisible to the journal, so WAL replay resurrects writes rolled back before commit | the discarded write came back during replay. The rollback removed it from the store and left its record in the journal, so recovery reapplied a write the transaction had already thrown away | `Core/Transactions/Transaction.cs:310` |
| **confirmed** | Journal=rollback with a bare relative Data Source throws ArgumentException | `ArgumentException: The value cannot be an empty string. (Parameter 'path')`. `Path.GetDirectoryName("relative.witdb")` returns the **empty string**, not null, so the `?? basePath` fallback never fires and `CreateDirectory("")` throws | `Core/Transactions/RollbackJournal.cs:51` |
| **confirmed by measurement** | Autocommit DML is never fsync'd: no Flush call anywhere in the ADO.NET or EF Core provider | the factual half is exactly true and was checked directly — `grep -rn "\.Flush(" --include=*.cs` over both provider projects returns **zero** hits. Showing the resulting loss needs a real power cut | `Engine/WitSqlEngine.Dml.Operations.cs:257` |
| **not verified** | RollbackJournal recovery has no checksum or length verification, so a torn tail is applied as a fabricated before-image | not reached in this batch | `Core/Transactions/RollbackJournal.cs:262` |
| **not reproducible** with the current surface | Auto-increment / rowid counters are written after the commit fsync and never flushed, so after a crash the next INSERT reuses a live rowid | the media-outlives-the-wrapper trick works at the store layer, but the rowid counters live in the engine's schema, and a file-backed engine opens its storage with `FileShare.None` — a second engine cannot be opened over the same file without disposing the first, and disposing is exactly what flushes the counters. Needs a second process or an injected failure point | `Engine/WitSqlEngine.Transactions.cs:56` |

### EF query translation  <sub>`ef-translation` — 5, all verified 2026-07-27</sub>

Verified end-to-end, as LINQ queries rather than assertions over generated SQL, in
[EfTranslationFindingsTests.cs](../Sources/Providers/OutWit.Database.EntityFramework.Tests/AuditVerification/EfTranslationFindingsTests.cs).

**A methodological trap caught in this batch, and it invalidated a whole first run.** The `ToJson`
mapping fails at **model build**, not at query time, so putting the JSON entity in the same
`DbContext` as everything else made *all ten* tests fail in setup with an unrelated error. Every
verdict would have been wrong in the same direction. The JSON and primitive-collection entities now
live in their own contexts. **Where a defect fires matters as much as whether it fires** — a
model-build failure contaminates every test sharing that model.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | `StartsWith`/`EndsWith` build LIKE patterns without escaping wildcards in the search term | the term is spliced in unescaped with no `ESCAPE` clause. `StartsWith("a_")` returned **all four** seeded rows — `a%b`, `a_c`, `axb`, `azc` — instead of the one that literally starts with `a_` | `EntityFramework/…/WitStringMethodTranslator.cs:128` |
| **confirmed**, measured | Engine `LIKE` is case-insensitive, so `StartsWith` and `Contains` disagree with each other | on the same row `UPPERcase`: `StartsWith("upper")` matched **1**, `Contains("upper")` matched **0**. `StartsWith` becomes `LIKE` (case-insensitive in the engine); `Contains` becomes `INSTR` (ordinal). Two string predicates over the same data, opposite answers | `Engine/…/ExpressionEvaluator.Conditional.cs:158` |
| **confirmed**, 4 of 5 | Translators emit functions and casts the engine does not implement | `NotSupportedException: Function not supported: MILLISECOND`; the same for `TOTAL_SECONDS`; `NotSupportedException: CAST to SMALLINT not supported`; and fractional `DATEADD` does not even reach the engine — the generated SQL fails to parse with `no viable alternative at input '>TIMESTAMP'`. The fifth, `Math.Log(x, base)`, **works** | `EntityFramework/…/WitMemberTranslator.cs:110` |
| **half confirmed** | JSON columns (`ToJson`) and primitive collections are unsupported | JSON columns: confirmed — `InvalidOperationException: The store type 'null' specified for JSON column 'Detail' … is not supported by the current provider`, raised at model build. Primitive collections: **not reproduced** — a `List<int>` round-trips correctly | `EntityFramework/Query/WitQuerySqlGenerator.cs:11` |
| **duplicate** | `CROSS APPLY`/`OUTER APPLY` and `VALUES` table sources are unsupported | settled under `dropin-gaps` — all three shapes fail to parse | `EntityFramework/Query/WitQuerySqlGenerator.cs:85` |

### Core: LSM engine  <sub>`core-lsm` — 4, all verified 2026-07-27</sub>

Verified in
[CoreLsmFindingsTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/CoreLsmFindingsTests.cs).
The crash needs no process kill: the directory *is* the durable media, so restoring a file the
compaction should have deleted reproduces "crashed between publishing the output and deleting the
inputs" exactly.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed**, and total | LsmParallelWriter.Dispose discards unsubmitted thread-local buffers | **all five** entries lost — `k0..k4` all missing after `Dispose()` followed by `store.Flush()`. The ordinary `using` shape throws away everything the caller wrote that had not yet crossed the buffer threshold | `Core/LSM/LsmParallelWriter.cs:497` |
| **mechanism confirmed, consequence not reproduced** | Compaction has no manifest, so a crash between publishing the output and deleting the inputs resurrects deleted rows | the live set really is `Directory.GetFiles(m_directory, "sst_*.sst")`, and a surviving input **is** readmitted — but it loses. `Recover()` sorts by filename and the compaction output carries a higher id, so it counts as newest; and the output **retains the tombstone**, which was verified rather than assumed (`Get(k0)` returned null after compaction, when the output was the only file left). A resurrection would need the output to drop the tombstone or to sort behind a survivor. Test kept active as the pin for both properties | `Core/Stores/StoreLsm.cs:519` |
| **mechanism confirmed, consequence not reproduced** | The SSTable is never fsynced but the WAL is truncated immediately after | true as written: finalisation ends at `m_writer.Flush()`, which only pushes the `BinaryWriter` buffer into the `FileStream`. There is **no `flushToDisk` anywhere under `Core/LSM/`** — grep returns zero hits — so the SSTable is still in the OS page cache when the WAL holding the same data is truncated. Showing the loss needs a real power cut; a clean process kill is not enough, because the OS writes its cache back | `Core/LSM/SSTableBuilder.cs:184` |
| **mechanism only** | A failed flush leaves m_immutableMemTable populated forever, and the next flush loses the data | reproducing it needs an injected I/O failure part-way through a flush, and the current `StoreLsm` surface offers no way to arrange one. Recorded as unverified rather than guessed at | `Core/Stores/StoreLsm.cs:550` |

### EF migrations (KnownIssues #1)  <sub>`blocker-migrations` — 4, all verified 2026-07-27</sub>

Verified in
[BlockerMigrationsFindingsTests.cs](../Sources/Providers/OutWit.Database.EntityFramework.Tests/AuditVerification/BlockerMigrationsFindingsTests.cs)
and
[SchemaCatalogFindingTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/SchemaCatalogFindingTests.cs).

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **already fixed** | BuildCreateOperations drops HasData seed rows and EnsureSchema, and skips Sort() | the class named here **no longer exists**: `WitMigrationsModelDiffer.cs` was deleted in commit **`b686dd3`**, the convention-set-builder fix, in the 2.0.0 merge — so EF Core's own differ does the work. Verified rather than assumed: `HasData` rows reach the create script, and a referenced table is created before the table that references it. Both tests stay active as the pins for that removal | *(deleted)* `WitMigrationsModelDiffer.cs:71` |
| **duplicate** | EnsureSchemaOperation is not handled, and schema is dropped from every emitted identifier | settled twice already: `EnsureSchemaOperation` throws `NotSupportedException` (`dropin-gaps`), and `EnsureCreated` on a `HasDefaultSchema("public")` model therefore fails outright (`literal-roundtrip`) | `EntityFramework/…/WitMigrationsSqlGenerator.cs:38` |
| **confirmed** | AddColumn/ColumnDefinition drop maxLength, precision and scale | `MaxLength = 16` produced `ALTER TABLE "T" ADD COLUMN "Code" TEXT;` and `Precision = 18, Scale = 4` produced `ALTER TABLE "T" ADD COLUMN "Amount" DECIMAL NOT NULL;`. **This compounds with a confirmed `engine-dml` finding**: declared `VARCHAR(n)` and `DECIMAL(p,s)` are never enforced anyway, so even correct DDL would not be honoured — two independent defects covering for each other | `EntityFramework/…/WitMigrationsSqlGenerator.cs:102` |
| **confirmed** | SchemaCatalog.AddColumn does not reject a duplicate column name | the second `ALTER TABLE T ADD COLUMN A INT` is accepted silently and the catalog ends up holding **`Id, A, A`**. Migrations are replayed routinely — a partially applied migration, a script run twice — so this is not a hypothetical path | `Engine/Schema/SchemaCatalog.Columns.cs:17` |

### EF provider runtime  <sub>`ef-runtime` — 4, all verified 2026-07-27</sub>

Verified in
[EfRuntimeFindingsTests.cs](../Sources/Providers/OutWit.Database.EntityFramework.Tests/AuditVerification/EfRuntimeFindingsTests.cs).
Three of the four are settled elsewhere; only the bulk-extensions entry is new.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **duplicate** | dotnet ef dbcontext scaffold cannot work | settled under `engine-schema-ddl` — `sqlite_master` does not exist and `PRAGMA` does not parse | `EntityFramework/…/WitDatabaseModelFactory.cs:92` |
| **confirmed**, both halves | Bulk extensions skip shadow properties and bypass value converters | **shadow properties, silently**: a shadow value set through the change tracker reads back as `null` — `GetInsertColumns` filters with `.Where(p => !p.IsShadowProperty())`, so the column is never written and nothing reports it. Shadow properties are not exotic; EF creates one for any relationship whose FK has no CLR property. **Value converters, loudly**: `ArgumentException: Cannot convert State to WitSqlValue` — the raw CLR value reaches the value layer unconverted. A `SaveChanges` control test passes, which places the defect in the bulk path rather than the mapping | `EntityFramework/…/WitDbBulkExtensions.cs:463` |
| **duplicate** | BulkOptions.SetOutputIdentity sends default PK values instead of reading generated ones | settled under `cross-cutting` — and worse than stated: enabling it makes the insert fail with a duplicate-zero-key violation | `EntityFramework/…/WitDbBulkExtensions.cs:469` |
| **already fixed** | WitModelRuntimeInitializer hardcodes designTime:false | the file **no longer exists** — deleted in commit `b686dd3`, the convention-set-builder fix, in the 2.0.0 merge. It was one of the two workarounds that fix made unnecessary | *(deleted)* `WitModelRuntimeInitializer.cs:94` |

### Literal round trip  <sub>`literal-roundtrip` — 3, all verified 2026-07-27</sub>

Verified in
[LiteralRoundTripFindingsTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/LiteralRoundTripFindingsTests.cs)
and
[LiteralRoundTripEfTests.cs](../Sources/Providers/OutWit.Database.EntityFramework.Tests/AuditVerification/LiteralRoundTripEfTests.cs).

**This batch turned up a verdict category the earlier ones were missing: _already fixed_.** A test
that passes does not distinguish "the claim was wrong" from "the claim was right and someone fixed
it during the audit session". Checking `git log -L` on the cited function separates them — and it
reclassified an entry in the `parser` table too. Worth doing before writing "not reproduced" again.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **already fixed** | All REAL_LITERALs are parsed as double, so decimal literals lose digits and `=` matches the wrong rows | both probes pass: a 20-digit literal reaches a `DECIMAL(28,20)` column intact, and `=` matches only the row that holds it. The reason is in the history, not the claim — commit **`9556bd2`**, *"fix(parser): numeric literals are exact, and out-of-range integers no longer throw from the parser"* (2026-07-26), is part of the 2.0.0 merge. `ParseNumericLiteral` now tries `decimal` first and falls back to `double` only for exponent form. The finding list was written against pre-fix code and never updated | `Parser/Visitor/WitSqlVisitor.Expressions.cs:284` |
| **confirmed**, broader | A `char` CLR property is mapped to StringTypeMapping, so any inlined char constant throws | broader than written: `InvalidOperationException: No coercion operator is defined between types 'System.Char' and 'System.String'` is raised by a **plain `SaveChanges`**, not only by an inlined constant. A `char` property is unusable outright | `EntityFramework/Storage/WitTypeMappingSource.cs:150` |
| **confirmed**, fails earlier | Schema-qualified identifiers do not round-trip, making every table unreachable | right about the outcome, wrong about the stage: `EnsureCreated` itself throws `NotSupportedException` for `EnsureSchemaOperation`, so the table is never created and there is no DDL/DML mismatch left to reach. The DDL half was confirmed separately under `dropin-gaps` | `EntityFramework/Migrations/WitMigrationsSqlGenerator.cs:39` |

### Core: encryption, cache, storage, providers  <sub>`core-crypto-cache-storage` — 2, all verified 2026-07-27</sub>

Verified in
[CryptoCacheStorageFindingsTests.cs](../Sources/Core/OutWit.Database.Core.Tests/AuditVerification/CryptoCacheStorageFindingsTests.cs),
using an in-memory inner `IStorage` so the ciphertext can be tampered with directly rather than
through the file.

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **confirmed** | Page caches protect the same mutable state with two different locks for their sync and async APIs | each shard holds **both** a `Lock m_lock` and a `SemaphoreSlim m_asyncLock` over the same `m_pages` / `m_pageIndex` / `m_count` / `m_clockHand`. With an async flush provably still in flight — parked inside the storage double — a synchronous `CreatePage` **proceeded after 0 ms**. There is no mutual exclusion between the two APIs at all | `Core/Cache/PageCacheShardedClock.cs:36` |
| **confirmed**, narrower than written | Zeroed or truncated pages bypass AEAD authentication, and no AAD/version binding allows silent page rollback | both halves reproduce, but the second is **overstated**. Zeroed page: no exception — `ReadPage` tests `IsAllZeros` *before* decrypting and returns early, so a wiped sector is indistinguishable from a page that was never written, and authentication is skipped for exactly the shape it exists to catch. Rollback: an older ciphertext of the **same** page re-authenticated and read back as `0x11`, so no version or counter is bound. **But AAD binding does exist** — a control test moving page 1's ciphertext onto page 2 is correctly rejected, so cross-page substitution is caught. "No AAD/version binding" should read "no *version* binding" | `Core/Storage/StorageEncrypted.cs:78` |

### ADO.NET provider  <sub>`adonet` — 2, all verified 2026-07-27</sub>

Verified in
[AdoNetFindingsTests.cs](../Sources/Providers/OutWit.Database.AdoNet.Tests/AuditVerification/AdoNetFindingsTests.cs).

| Verdict | Finding | Observed | Where |
|---|---|---|---|
| **duplicate** | Connection pool can never reclaim a connection | settled under `core-concurrency` — the permit leak is confirmed (a second borrow at `MaxPoolSize = 1` had not completed 5012 ms later), but the pool is unreachable from the provider | `AdoNet/Pool/ConnectionPool.cs:234` |
| **confirmed**, with a calibration | Nothing tracks an open reader: closing the connection disposes the storage under a live streaming iterator | `reader.IsClosed` is **False** after `connection.Close()`, and the reader **kept streaming 4 more rows** — on a real file-backed database, not only on `:memory:`. The streaming half of the claim is right: `WitSqlResult` wraps an `IEnumerable<WitSqlRow>` with a cursor-style `Read()`, so it really is pulling from the engine's iterator after `Close()` disposed it. **But the rows that came back were correct**, so what was observed is undefined behaviour that happened to work, not data corruption — the awkward kind, silent and timing-dependent rather than reliably fatal | `AdoNet/WitDbCommand.cs:131` |

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
a complex query, where one call does substantial work inside the native engine.

Two different claims have to be kept apart here, because only one of them is unsupported:

- *"Our storage engine is faster than SQLite's"* — **not supported** by a small-insert benchmark.
  That number is substantially the wrapper's call cost, not the engine, and SQLite's real speed shows
  up on complex queries. Do not make this claim from this workload.
- *"From .NET, on workloads made of many small operations, WitDatabase is faster than SQLite
  end-to-end"* — **supported, real, and worth saying.** A .NET consumer cannot reach SQLite except
  through that wrapper, so the overhead is not an artifact to be subtracted; it is part of what they
  actually pay. Being in-process managed code with no marshalling boundary is a genuine structural
  advantage, not a measurement error.

That second claim matters for this project specifically. Small-operation-dominated workloads are
exactly what the OutWit consumers do: WitAnalytics ingests events one at a time, WitIdentity writes
per request, and an EF Core `SaveChanges` is typically a handful of rows. In those scenarios the
advantage is the user's real experience, and it should be measured deliberately rather than fall out
of a benchmark aimed at something else — a workload of many *individual* small operations, not one
transaction containing N of them, would show it far more clearly than anything in the suite today.
Report it with the mechanism named, and the claim stays honest.

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
- **1.3-2.5x faster than SQLite here is real for the user, but is not an engine result.** The gap is
  largely the wrapper's per-call overhead — so it proves nothing about the storage engine and will
  shrink toward zero as queries get more substantial. But a .NET consumer cannot reach SQLite except
  through that wrapper, so on workloads made of many small operations this *is* what they experience,
  and it is worth stating with the mechanism named. Do not delete this column; do not promote it into
  a claim about the engine either.
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
9. **Measure the no-marshalling advantage on purpose.** The per-call gap against SQLite is a real
   structural property of being managed in-process, and it is worth a benchmark aimed *at* it instead
   of one that stumbles into it. The right shape is many **individual** small operations — N separate
   auto-committed inserts, N single-row lookups by key, N small `SaveChanges` calls — not one
   transaction containing N of them, which amortises the very overhead being measured. That is also
   the shape the OutWit consumers actually run: WitAnalytics ingesting events one at a time,
   WitIdentity writing per request. Report it as a deployment result with the mechanism named, never
   as an engine-speed claim.

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
per-call marshalling and LiteDB is on its home ground, so neither tells you much *about the engines*.
Beyond that, the honest position today is **not "we are behind" but "we have not measured the thing
that matters"** — with three results worth keeping. LSM's non-linear growth in N is a defect
signature and worth chasing on its own. ~30% less allocation than LiteDB is real, because allocation
is not distorted by call overhead. And being faster than SQLite on small operations, while it says
nothing about the storage engine, is still what a .NET consumer actually gets — there is no way to
use SQLite from managed code without paying for the crossing, and on workloads made of many small
operations that is the user's real experience, not a measurement error to be apologised for.

One thing that is not in any of the three, and is worth doing whenever there is an hour: **run EF
Core's own provider specification suite** (`Microsoft.EntityFrameworkCore.Specification.Tests`). It
is the canonical way to prove drop-in compatibility, it is not referenced today, and it would likely
surface more than the whole of workstream B.
