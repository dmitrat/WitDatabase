# Changelog

## 12.1.0

Phase 11's follow-ups. A **minor**: behaviour is fixed and the public API grew, and no answer, no file
format and no existing contract changed. The two build routes now agree, the construction kit's
central claim is executed rather than asserted, a database can be closed without synchronous I/O,
and two operations that read the whole database to answer a small question no longer do.

### Fixed

- **`BuildAsync` ignored the store and the cache the configuration chose.** It built a `StoreBTree` for
  every configuration that was not LSM, so on that route `Store=inmemory` opened the data file it exists
  in order not to touch, a third-party store registered in the provider registry was ignored outright,
  and `Cache=lru` selected a cache that was never constructed. Everything but the built-in B+Tree store
  is now built where the synchronous route builds it - in the provider registry - and the B+Tree store,
  which keeps a route of its own because its page manager reads the header while it is constructed, reads
  its parameters from the same bag the registry factory reads. Measured by building each configuration
  both ways and comparing the object graphs.

### Fixed

- **`SELECT MAX(x)` on an indexed column read the whole index.** The planner optimises `MIN`/`MAX`
  through `ISecondaryIndex.GetFirstEntry`/`GetLastEntry`, and the second of those was
  `Scan(null, null).LastOrDefault()` - so an operation advertised as an index lookup was O(n). The
  B+Tree descends to its rightmost leaf now, as it always could to its leftmost: **0.001 ms against
  10.575 ms on 20,000 keys.**

- **The query optimizer estimated every range predicate at 20% of the table.** It now asks the index
  what range of values it holds and interpolates. Measured on 1,000 rows holding 1..1000, the estimate
  for `Value > 999` goes from 200 to **1** (one row is the truth) and for `Value > 0` from 200 to
  **1000**. Two things this does not do, both measured and recorded rather than left to be assumed: on
  heavily skewed data a linear interpolation is *worse* than the constant, which is what a histogram
  would fix; and with the present cost model the estimate does not decide index against table scan at
  all, since an index range is priced below a scan for any estimate - so this is a precondition for a
  cost model that can choose, not a plan change on its own.

- **An MVCC commit read the whole database to find what it had just written.**
  `MvccKeyValueStore.CommitTransaction` scanned every record in the store looking for the versions the
  transaction had written, and `RollbackTransaction` did the same - so committing ten rows cost the
  reading of everything else, and a hundred commits over a growing store were quadratic. The store keeps
  the versioned keys each open transaction wrote and visits those instead; the rule that decides whether
  a record belongs to the transaction is unchanged, and an id this process never saw still falls back to
  the scan so that a record left by an earlier one stays recoverable.

  Measured with one writer and no contention, committing the same ten rows: **2.80 ms against 1,000 rows
  and 6.96 ms against 8,000 before - eight times the data for two and a half times the commit - and
  2.11 ms against 2.14 ms after, which is 1.0x.** End to end, four writers x 25,000 rows through a
  database: batches of 1,000 in a transaction went from **50.8 s to 7.2 s**, against 3.1 s for the same
  writes on autocommit. What remains - a transaction still costing about twice a plain write - is the
  commit rewriting every version a second time, which is recorded rather than fixed.

### Added

- **A database can now be closed without a synchronous storage call.** `WitSqlEngine`,
  `MvccTransactionalStore`, `MvccKeyValueStore`, `BTreeConcurrentStore`, `PageManager` and both page
  caches gained `DisposeAsync`, and `StoreBTree.DisposeAsync` stopped calling the synchronous one. Until
  this, an asynchronous close degraded to a synchronous flush at the first link that had none - and
  since 12.0.0 that was `BTreeConcurrentStore`, which wraps every B+Tree store, so it affected every
  database. It matters for a storage that has no synchronous operations at all, which is what
  `OutWit.Database.Core.IndexedDb` is; the same package still cannot be **written** to, because there is
  no asynchronous statement path.

- **`StoreBTree.CreateAsync(IStorage, IPageCache, bool, ProviderMetadata?, CancellationToken)`** - the
  asynchronous twin of the constructor that made the `Cache` provider key mean something in 12.0.0.

### Verified

- **The configurations now run at a size that reaches the structures.** Every combination the matrix
  covers had only ever been run on eight rows, which fit in one leaf. 2,000 rows through five
  configurations, with a 4,000-character payload against a 960-byte inline limit, produce measured page
  splits (116 pages against 2 for eight rows), overflow chains and LSM compactions (SSTables written and
  merged away) - and every answer, including the large value and a secondary index lookup, is correct
  after a reopen. No defect at volume.

### Documentation

- **A provider from another package needs its assembly loaded before a connection string can name it.**
  `Encryption=chacha20-poly1305` is refused unless something has touched a type in
  `OutWit.Database.Core.BouncyCastle`, because the registration hangs off a module initializer and the
  runtime loads an assembly lazily. `WitSQL.md` para 14.10 now says to call
  `BouncyCastleProviderRegistration.EnsureRegistered()` at startup; the fluent route was never affected.

- **A storage with no synchronous operations can host a database that cannot be written to.** Measured
  with a stand-in for `OutWit.Database.Core.IndexedDb`: the build is asynchronous throughout,
  `CREATE TABLE` succeeds because it writes nothing, and the first `INSERT` throws - the commit's flush
  writes the header through the synchronous `IStorage.WritePage`, and so does every close. Documented in
  `WitSQL.md` para 14.10 as unfinished rather than supported.

## 12.0.0

A **major**, and the reason is the last section: `Parallel Mode` and `Max Writers` are gone from the
connection string, from `WitDbConnectionStringBuilder`, and from Entity Framework's
`UseParallelWrites` / `MaxWriters`. A connection string that still carries one is refused at `Open`.


Phase 11, the modular structure: **the combinations this construction kit offers have been enumerated,
built and run for the first time.** Five instruments - a reflection census that asks whether a
connection-string keyword reaches the engine at all; a 153-case matrix that runs the same workload
through every legal combination and compares the answers; an 8x8 grid that creates a database with one
configuration and opens it with another; the same combinations with two connections open at once; and
durability crossed with configuration, one process kill each.

The second reason it is a major is the transaction-model check below: an application that opened an
MVCC database with `MVCC=false` used to get an empty-looking database and now gets an error.

**Reassuring, and worth stating:** no combination opens, accepts every statement and answers something
different. Every defect found is in construction or in close.

### Fixed

- **`Store` decided whether any other keyword arrived.** The ADO.NET layer forwarded the entire
  pass-through parameter set only when `Store=` appeared in the connection string, so
  `Data Source=db;PageSize=16384` silently used the default page size while
  `Data Source=db;Store=btree;PageSize=16384` - which asks for the same engine, `btree` being the
  default - honoured it. Measured both ways.

- **Numeric and boolean keywords fell back to their defaults in silence.** Every value from a connection
  string arrives as text, and `ProviderParameters.Get<T>` tested the type without converting, so
  `Get<int>("16384")` returned the default. It converts now, and a present value that cannot be read as
  the requested type is an error at `Open` rather than a silent default.

- **`Store=inmemory` with a file `Data Source` held the file open.** The builder opened storage for every
  store and handed it to the factory; the in-memory store ignores it, nothing owned it, and the database
  could not be opened a second time in the same process. Storage is deferred now and never opens for a
  store that does not ask for it.

- **`Journal=wal` held the journal file open.** The journal was constructed before the builder chose
  between the MVCC store, which takes no journal, and the lock-based one, which does - so with the
  default `MVCC=true` a write-ahead log was built, dropped, and its handle held for the life of the
  process.

- **`Store=lsm` with `Transactions=false` and a parallel mode lost the last row written.** The parallel
  writer's `Dispose` completed its buffer channel - draining only what was already queued - and then
  discarded the thread-local buffers, which is where every entry below the size threshold sits. Seven
  rows survived a clean close and reopen; the eighth did not. With MVCC the commit path's `FlushAllAsync`
  hid it. The writer now hands over the buffers that are still filling before it closes the queue.

- **Switching `MVCC` or `Transactions` made an existing database look empty.** A database created with
  the default `MVCC=true` opened without complaint under `MVCC=false` or `Transactions=false` and then
  reported every table as missing - and the other way round. The rows were never lost: the
  configuration that wrote them read them back afterwards. The danger was the next step, because a
  consumer meeting an apparently empty database creates the schema, over one that was intact. The
  database header has always recorded which transaction model wrote it; that record is now compared at
  `Open` and a mismatch is refused with a message naming the setting. `MVCC=false` and
  `Transactions=false` write the same layout as each other and still open each other's databases.

- **A larger `PageSize` reinitialised the header of an existing database.** `StorageFile` counted
  pages as `length / pageSize`, so a 4,096-byte database opened with `PageSize=16384` counted zero
  pages, the page manager took that for a new database and overwrote the header - after which the
  configuration that created the file could not open it either. A non-empty file too short to hold one
  page is refused now.

- **A refused open held the data file for the life of the process.** A wrong password or a wrong page
  size left the storage that had already been built undisposed, so the next attempt - with the right
  password - met "the process cannot access the file", a message naming the wrong problem entirely.
  Anything that fails between building the store and handing it over now disposes it.

- **The B+Tree store was left unserialised when no parallel mode was asked for.** Secondary index
  stores have been wrapped unconditionally since 6.0.0, because a second connection is enough to walk
  into a leaf split someone else is halfway through; the main store was left conditional on
  `Parallel Mode`. With `Transactions=false` and no mode there was nothing at all between two writers
  and one split: measured, a writer threw and a row was lost in **five runs out of five**. With
  transactions the layer above happens to serialise it, which is a property of that layer rather than a
  guarantee the store may lean on. The B+Tree store is now serialised whenever it is built, which costs
  a single thread nothing - median **1.001x** over five interleaved passes of 20,000 operations.

### Changed

- **`Cache=clock|lru` selects a cache.** `StoreBTree` takes an `IPageCache`, and the builder constructs
  the one the configuration chose, for the main store and for each secondary index store.
  `WithCache(IPageCache)` - which was read by nothing - now reaches the main store. Before this the
  chosen key was written into the database header while a `PageCacheShardedClock` was built regardless,
  so a file could claim a cache it had never had.

- **`Journal=…` with `MVCC=true` or with transactions off is refused at `Open`**, with a message naming
  the way out. Nothing would have used it.

- **`IProviderMetadataSource`, a new interface**, lets a store hand back the provider metadata the
  database it opened was created with. `StoreBTree` implements it and `BTreeConcurrentStore` delegates;
  stores with no header to answer from - the LSM and in-memory ones - simply do not implement it. This
  is what the transaction-model check reads.

- **`Docs/WitSQL.md` para 14.10** now also states what may and may not be changed on an existing
  database, and carries a measured durability table: a committed transaction survives a process kill
  under every transaction model, both stores, both journals and encryption - and is lost, along with
  the whole database, under `Synchronous Commit=false` and `Transactions=false`, both of which
  document that they trade it.

- **`Docs/WitSQL.md` para 14.10** states which combinations are supported, which are refused and why -
  including that `Auto`, `Buffered`, `Latched` and `Optimistic` are four spellings of "make this store
  thread-safe", because the concurrency mechanism is decided by the store and not by the keyword.

### Removed

- **`Parallel Mode` and `Max Writers`.** They chose a concurrency wrapper, and concurrency is not a
  choice: the B+Tree store has no locking of its own and is serialised whenever it is built, while the
  LSM and in-memory stores lock internally. What the setting still selected was the LSM store's write
  buffer, and that was **measured before it was removed**:

  | | ratio, buffered / direct |
  |---|---|
  | Straight into the store, one writer | 1.00 (noise, 0.77-1.02 across passes) |
  | Straight into the store, four contending writers | **0.80** |
  | Through a database, four writers, autocommit | **1.14** |
  | Through a database, four writers, batches of 1,000 in a transaction | **1.04** |

  The win needs four threads inside the store at once, and a transaction layer serialises writers
  before that can happen - so through the engine the buffer only costs. `LsmParallelStore` remains
  public for a caller who drives a store directly, which is where the win is real.

  Removed with them: `ParallelMode`, `ParallelModeOptions`, `KeyValueStoreFactory`,
  `WithParallelWrites`, `WithoutParallelWrites`, `WithMaxWriters`, `WitDbParallelMode`, and EF's
  `UseParallelWrites` and `MaxWriters`.

## 11.2.0

Phase 10 closed: **four defects in the LSM storage engine**, which was 12-20x slower than the B+Tree
on writes - the workload an LSM tree is chosen for. A minor rather than a major because no answer, no
file format and no API changed; a minor rather than a patch because `IKeyValueStore` gained a member
and `Flush()` on the LSM store now means something narrower.

`Store=lsm` is now a defensible choice, and `Docs/WitSQL.md` para 14.9 states exactly where it wins
and where it does not - because measurement showed the boundary is narrow enough that "LSM is
write-optimised" is not a true sentence on its own.

### Fixed

- **The write-ahead log bypassed the OS write cache.** It was opened with `FileOptions.WriteThrough`,
  so every append went to the device; combined with a `SeekToEnd()` that flushed the buffer on each
  entry, the 4 KB buffer never accumulated anything. In LSM mode each secondary index has its own
  store and therefore its own log, so three indexes meant four write-through streams per row. It also
  contradicted its own option - `SyncWrites` documents itself as "if false, relies on OS buffering"
  and defaults to false, while the handle made OS buffering impossible.

- **Every LSM connection-string parameter was inert.** `MemTableSize`, `SyncWrites`, `EnableWal`,
  `BlockSize`, `CompactionTrigger` and the block-cache settings were all dropped: the builder
  constructed the store directly and asked only for a ready-made options object, while the parser
  that turns connection-string keys into options was reachable only through the provider registry.
  Measured with 5 MB written in one transaction, `MemTableSize=1024` produced **1** SSTable before
  and **5,556** after.

- **Every commit wrote an SSTable.** `Transaction.Commit` calls `Flush()`, and `Flush()` emptied the
  MemTable whenever it held anything at all - so `MemTableSizeLimit` was unreachable and each commit
  paid to create a file, write its blocks, bloom filter and index, fsync it and leave the compactor
  more work. A transaction cost the same whether it held one row or a hundred, which is the signature
  of paying for something other than the work.

- **Secondary index stores ignored configuration**, receiving plain defaults regardless of the
  connection string.

### Added

- **`IKeyValueStore.Checkpoint()`** - forces a store's accumulated in-memory state out to its main
  on-disk structure. It defaults to `Flush()`, so existing implementations need no change.

  `Flush()` now means **make durable** and is what a commit calls; `Checkpoint()` means **force the
  accumulated state out now** and is what a size threshold, maintenance or a caller wanting an
  on-disk sorted file calls. Every database separates these - PostgreSQL commits by syncing the WAL
  and moves buffers on `CHECKPOINT`, SQLite has `PRAGMA synchronous` and `PRAGMA wal_checkpoint`,
  RocksDB has `SyncWAL()` and `Flush()`. Durability is an operation on the log; reorganising the data
  structure is a separate decision.

  **If you called `Flush()` on a store to get an SSTable on disk, call `Checkpoint()`.** Durability
  is unaffected either way, and is verified by the 13 out-of-process crash tests.

### Performance

Ryzen 9 5950X, .NET 10, medians of repeated rounds. Full record and method in
`Docs/PHASE10-PERFORMANCE-PLAN.md`.

| | before | after |
|---|---|---|
| 50 transactions x 1 row | 14.67 ms/tx | **2.11-3.00 ms/tx** |
| 50 transactions x 100 rows | 13.32 ms/tx | **4.47-5.03 ms/tx** |
| 1,000 rows, no index | 118 ms | **33 ms** |
| 1,000 rows, PK + 3 indexes | 534 ms | **44 ms** |

Against the B+Tree store, LSM went from **12-20x behind on writes to parity**, and at the storage
layer on sustained ingest it is now **10-13% faster** at 500,000 and 1,000,000 rows - while doing 19
flushes and 5 compactions, so the work is happening rather than being deferred out of the
measurement.

### Known, and the reason para 14.9 is narrow

**The advantage does not survive secondary indexes.** At 500,000 rows through SQL: parity with the
B+Tree without indexes (15.10 against 15.36 µs/row), and **1.6x slower with three** (23.16 against
36.86). Index maintenance costs 2.7 µs per row per index on the B+Tree and 7.2 on LSM, because each
secondary index still gets its own LSM store with its own log, MemTable and compaction schedule.
Sharing one log across index key spaces - as RocksDB does with column families - is the next piece of
work and is a design change rather than a defect fix.

Also unchanged: LSM autocommit is still several times the B+Tree's and undiagnosed, and the query
planner still has no selectivity statistics, so a range over a low-cardinality index can cost more
than the scan it replaces.

## 11.1.0

Phase 10, first fix: **the query planner stopped scanning 1,000 rows to decide whether to use an
index.** A minor rather than a patch because it changes how plans are costed, and a minor rather
than a major because it changes no answer, no API and no file format - an application that worked on
11.0.0 cannot fail on 11.1.0 without changing a line.

The whole change is one function. `QueryPlanner.EstimateTableRowCount` opened a table scan and read
up to **1,000 rows on every query execution**, purely to estimate a row count for the cost model. It
now reads the catalog's O(1) per-table counter - the same one that makes `SELECT COUNT(*)` flat in
table size.

### Fixed

- **Any `SELECT` with a `WHERE` against a table carrying an index paid a 1,000-row scan before it
  ran.** Measured at ~1,317 KB and ~0.49 ms per query execution, flat in table size because the old
  scan was capped at 1,000 rows - so the cost of *choosing* an index was about **200x the lookup it
  saved**.

  The estimate bought nothing even at that price. The cost model is homogeneous in it - a table scan
  is costed at `N x 1.0` and an index range at `N x 0.2 x 0.5` - so `N` cancels out of the comparison
  and the same plan is chosen whatever the estimate says. The old code also returned `count * 10`
  once it hit its cap, reporting **every table of 1,000 rows or more as exactly 10,000** regardless
  of its real size.

### Performance

Measured on a Ryzen 9 5950X, .NET 10, BenchmarkDotNet ShortRun, in the default configuration
(MVCC on, durable commit, B+Tree) - the one every ADO.NET and EF Core consumer gets. Every figure is
the result of two passes, and the full record with its spread is in
`Docs/PHASE10-PERFORMANCE-PLAN.md`.

| operation | 11.0.0 | 11.1.0 | |
|---|---|---|---|
| Index seek on a UNIQUE index, x100 | 48.42 ms / 125,332 KB | **0.500 ms / 970 KB** | **97x faster, 129x less allocated** |
| Index seek, non-unique, x20 | 19.01 ms | 8.40 ms | 2.3x |
| Composite index query | 9.89 ms | 4.52 ms | 2.2x |
| Index range scan (`> threshold`) | 1.571 ms | 0.839 ms | 1.9x |
| Index range scan (`BETWEEN`) | 1.397 ms | 0.832 ms | 1.7x |
| `SELECT ... WHERE Age > 30` | 1.637 ms | 1.114 ms | 1.5x |
| Full scan with no usable index | 7.53 ms | 5.94 ms | 1.27x |
| aggregates, joins, projections | | | 5-8% |
| inserts, updates, transactions | | | unchanged |

The write paths are untouched - the planner is not on them. The unindexed scan gains because it
still has a `WHERE` over a table that has indexes, so it paid the estimate without ever using one.

Against **LiteDB**, the pure-.NET engine this project measures itself against, the unique-index seek
went from **23.4x slower to 4x faster**; the non-unique seek, both range scans, filtered `SELECT` and
full scan all moved from 1.2-2.1x slower to parity. Two operations remain more than 3x behind, both
inserts: autocommit at 21.4x - the durable-commit cost chosen deliberately in 4.0.0, on which
WitDatabase is 3.3x *faster* than SQLite - and `INSERT ... RETURNING` at 3.1x.

### Known and unchanged

- **The planner still has no selectivity statistics.** `RANGE_SELECTIVITY` is a fixed 0.2, so an
  applicable index always wins a range comparison whatever the predicate really selects. A range
  selecting ~75% of a **low-cardinality** non-unique index costs 2.5x the scan it replaced (499 ms
  against 199 ms at 100,000 rows). SQLite shows the same 13.7x jump on the same shape: visiting rows
  in index-key order when the key is uncorrelated with physical row order is random access into the
  table. Fixing it needs distinct-value statistics the engine does not keep.
- **`Store=lsm` is 12-20x slower than the B+Tree store on writes**, at every size, and is *not*
  non-linear in N as previously recorded - per-row cost falls as the table grows. First diagnosis in
  `Docs/PHASE10-PERFORMANCE-PLAN.md` para 14: the primary key is free, and secondary index
  maintenance dominates at roughly 48 us per row per index. Until that is understood, LSM should not
  be presented as the write-optimised option.

## 11.0.0

Phase 9d: **user-defined functions and stored procedures**, and the five defects the area's audit
found before any of them was written. **Major**, because the AST format gained five union tags and
the catalog gained two records — 10.0.0 cannot open a database that uses either, while 11.0.0 reads
everything 10.0.0 wrote.

The subsystem was designed before it was built, against measurements rather than against the code,
and three of the design's six answers changed because of what the measurements said. The working
record is `Docs/PHASE9D-ROUTINE-SUBSYSTEM-DESIGN.md`.

### Added

- **`CREATE FUNCTION`** — a scalar function whose body is **one expression** over its parameters,
  callable anywhere an expression may appear: a select list, a `WHERE`, a `CHECK`, a computed
  column, a `DEFAULT`, an index key, a view, a trigger body.

  ```sql
  CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END;
  SELECT Doubled(Price) FROM Orders WHERE Doubled(Price) > 100;
  ```

  An expression body rather than a statement list is the decision the rest of the subsystem rests
  on: calling a function is a substitution inside the expression evaluator, so it runs no statements,
  opens no transaction and consumes no nesting budget — which is what makes it safe to reach from a
  path evaluated per row.

- **`CREATE PROCEDURE` and `CALL`** — a body of statements, invoked as one unit of work. The last
  statement's result is the call's result, so a body ending in a `SELECT` returns rows.

  ```sql
  CREATE PROCEDURE ArchiveOrder(OrderId INT) AS BEGIN
      INSERT INTO OrdersArchive SELECT * FROM Orders WHERE Id = OrderId;
      DELETE FROM Orders WHERE Id = OrderId;
  END;

  CALL ArchiveOrder(42);
  ```

- **`CommandType.StoredProcedure`** on the ADO.NET provider. Without it a procedure could exist in
  the database and not be reachable from ordinary consumer code.

- **`INFORMATION_SCHEMA.ROUTINES` and `.PARAMETERS`**, which is what scaffolding reads.

### Fixed

- **Nested execution had no bound.** A trigger writing to its own table recursed until the stack ran
  out: 400 levels passed and 600 **killed the host process**, because `StackOverflowException` cannot
  be caught. Statements now nest at most 32 deep and the limit is a catchable error.

- **DDL inside a transaction threw and kept the change anyway.** `CREATE TABLE`, `CREATE INDEX`,
  `CREATE VIEW`, `CREATE SEQUENCE`, `CREATE TRIGGER` and `DROP TABLE` all raised
  `LockRecursionException` inside an open transaction — *and the catalog kept the change*, so the
  caller was told the statement failed about something permanent. Every migration tool wraps DDL in a
  transaction. Schema records now go through the caller's transaction, and a rollback discards them.

- **The `ALTER` family could not see rows written in its own transaction.** `RENAME TABLE`,
  `DROP COLUMN`, `ALTER COLUMN TYPE` and `ADD COLUMN` scanned past them, so a migration that wrote
  and then reshaped lost what it had written.

- **A computed column that could not be evaluated answered `NULL`.** Three read paths each turned
  every failure into a legal value — and `NULL` is the one answer a caller cannot tell from a real
  one. It now names the table, the column and the cause. This is how a column left dangling by
  `DROP COLUMN` or `RENAME COLUMN` stayed quiet.

- **An unknown function name was accepted in schema.** A `CHECK`, a computed column, a `DEFAULT` or
  an index expression naming a function the engine does not have was accepted at declaration and
  failed later. Refused when it is written now.

- **An index could be built on an expression whose value moves** — a subquery, `RANDOM()`, `NOW()`.
  An index key is computed once when the row is written and never recomputed.

- **`TOBOOLEAN` was in the grammar and not in the engine.** Every other `TO…` conversion worked.

### Notes

- A routine body is **SQL only**. No external code, no assembly loading, no `LANGUAGE` other than
  `SQL`.
- A routine body may **not** control transactions. Committing inside one commits the statement that
  called it, and nothing reports that.
- A **trigger** body may not `CALL` a procedure. A procedure may contain DDL precisely because a
  `CALL` is a statement rather than a loop over rows.
- Not included, and listed in the design note § 7: table-valued functions, control flow in a body
  (`IF`/`WHILE`), `OUT` parameters, and multiple result sets.
- The package READMEs said `.NET 9` and showed `Version="1.0.0"` in their install snippets. Both were
  stale; both are corrected.

## 10.0.0

Phase 9 up to the routine subsystem: two defects that the feature work sat on, and four capabilities
both drop-in targets have. **Major**, and for two reasons - a `SELECT *` over a derived table returns
a different result shape, and schema that uses the new table source cannot be read by 9.0.0.

The phase opened by re-measuring its own list, which had been assembled a month earlier and was wrong
in three of ten places: JSON columns work end to end, database-first scaffolding works, and the
`HAVING COUNT(*) BETWEEN` entry is a defect rather than a missing feature. The tenth time in this
project that a record about the past turned out false when re-run.

**The instrument the phase needed was a dialect oracle**, because every other one here compares
against SQLite - which lacks most of the list itself and so cannot answer whether the drop-in target
has a capability. Run against PostgreSQL 17 and SQL Server 2022, it earned itself on its first
report: a **derived column list is rejected by SQLite and supported by both targets**. Against the
SQLite oracle that would have read as parity, and the decision would have gone the wrong way.

### Breaking

- **`SELECT *` over a derived table returns each column once.** It used to return every column twice,
  qualified and bare - `(SELECT Id, TId FROM S) AS X` yielded `X.Id, X.TId, Id, TId`. Code reading
  such a result by ordinal was reading a row twice as wide as it asked for; code reading it by name
  is unaffected.

- **A database using `LATERAL` / `APPLY` in a view cannot be opened by 9.0.0.** The new table source
  has a new union tag, and an older engine does not know it. Databases that do not use one are
  unaffected, and 10.0.0 reads everything 9.0.0 wrote.

- **`COUNT(*)` outside an aggregate query reports a caller error.** The message used to be
  `COUNT(*) should be handled by aggregation iterator`, an internal invariant.

### Added

- **`TOP n`** - SQL Server's spelling of a row limit, mapped onto the existing `LIMIT` rather than
  carried as a second concept. `TOP` stays usable as a column name.

- **`VALUES` as a query** - `SELECT * FROM (VALUES (1), (2)) AS V`, and anywhere else a query goes.
  Columns are named `column1`, `column2` after PostgreSQL; ragged rows are refused rather than padded.

- **A derived column list** - `(SELECT Id, Name FROM T) AS V (Key, Label)`, renaming positionally.
  A list whose width does not match the subquery is refused, as both targets refuse it.

- **A correlated subquery in `FROM`** - `LATERAL`, `CROSS APPLY` and `OUTER APPLY`. One capability
  with two spellings; both targets have it. The subquery is planned per outer row, because its plan
  depends on the outer values.

### Fixed

- **An aggregate resolves the same way wherever it appears.** `HAVING COUNT(*) > 1` worked and
  `HAVING COUNT(*) BETWEEN 1 AND 5` raised; so did `SUM`/`MIN` inside `BETWEEN`, and the same
  aggregate inside `IN`. All three reference engines accept every one of those. The capability was
  torn across three places - detection, `HAVING` evaluation and the select-list projection - and a
  fourth surfaced after those were fixed: the group's rows were kept only when a `HAVING` clause
  existed, so an aggregate that needed them was computed over an empty list and returned NULL.

### Ledger

**33 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 47**, plus 2 `[Explicit]`, down from 35 + 14 = 49
at 9.0.0.

## 9.0.0

Closes phase 8, serializer round-trip. **Major, and the breaking half is the file format:** the catalog
stores schema as parse trees rather than as SQL text, so a database written by 9.0.0 **cannot be opened
by 8.x**. Upgrading is one-way.

The theme: **the catalog's storage format was SQL text, and the code that produced it was incomplete.**
That made an ordinary-looking serializer into the write half of a persistence codec whose read half is
the parser - and a gap between them is not a formatting nuisance, it is schema corruption on disk. The
gaps had two grades. The known one was *created then broken*: a view whose body contained a subquery
was created successfully and then raised a parse error on every query against it, because the subquery
had been written down as the literal text `SELECT ...`. The one nobody had seen was worse.

**A view over `SELECT … UNION SELECT …` was stored as its first branch alone.** It was created without
complaint, queried without any error at all, and answered from half its rows, for ever. The same
happened to `WITH`, to `OFFSET` without `LIMIT`, and to nine other clauses. The round-trip harness could
not see it: it compared two *serializations*, and a dropped clause is idempotent, so both passes agreed
and the entry counted as clean.

### Breaking

- **The catalog file format changed, and the change is one-way.** 9.0.0 reads a database written by
  8.x; 8.x cannot read one written by 9.0.0 - it fails with `property count is 3 but binary's header
  marked as 4`. Take a copy before upgrading if a rollback has to stay possible.

- **`IDatabase.CreateView` no longer takes the body as a string.** It takes the parsed `SELECT`. The
  text is not stored at all now.

- **`DefinitionView.SelectSql`, `DefinitionColumn.DefaultValue` / `CheckExpression` /
  `ComputedExpression`, `DefinitionIndex.WhereExpression` / `ExpressionColumns`,
  `DefinitionTable.CheckExpressions`, `DefinitionNamedConstraint.CheckExpression` and
  `DefinitionTrigger.Body` / `WhenCondition` are legacy.** They are read for a database written before
  9.0.0 and never written. Code reading them to learn what a column or index declares must call
  `ResolveDefault()`, `ResolveCheck()`, `ResolveComputed()`, `ResolveWhere()` or `ResolveQuery()`; code
  wanting the SQL to show a human must call `DisplayDefault()`, `DisplayCheck()`, `DisplayWhere()` or
  `DisplayQuery()`.

- **A trigger body may contain only `SELECT`, `INSERT`, `UPDATE`, `DELETE` and `MERGE`.** Anything else
  is refused when the trigger is declared. The grammar admitted any statement and the engine could not
  run most of them: DDL inside a trigger deadlocks against the write lock held by the statement that
  fired it, and it failed **part-way**, leaving a table created by a trigger that then threw.

- **`INFORMATION_SCHEMA` withholds a definition it cannot render faithfully** rather than reporting an
  approximation of it. `VIEW_DEFINITION`, `CHECK_EXPRESSION`, `COLUMN_DEFAULT`, `FILTER_CONDITION` and
  `ACTION_STATEMENT` are null in that case. Reporting `SELECT Id FROM A` as the definition of a view
  over `A UNION B` is a false statement about a database, and someone would copy it.

### Fixed

- **A view keeps its whole body.** `UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`, `WITH`, `OFFSET` without
  `LIMIT`, window frames, `NULLS LAST` and subqueries in any position all survive. Several of these
  used to make the view answer *incorrectly and silently*; the rest made it unqueryable.

- **A trigger keeps its whole body.** `ON CONFLICT` and `INSERT OR IGNORE` inside a trigger used to be
  stored as a plain `INSERT`, so the conflict handling vanished and the trigger threw on a duplicate
  instead of ignoring it. The body was also **split on `;`** rather than parsed, so a semicolon inside a
  string literal cut a statement in half.

- **Partial-index filters and `CHECK` conditions keep their subqueries.**

- **Index maintenance is decided by reading the expression, not by searching its text.** A write used to
  decide whether it had to maintain a filtered or expression index by asking whether the *rendered text*
  contained the column's name - which said yes for a column named `Age` and a filter mentioning `Agent`.

- **The reserved-word list is derived from the grammar** instead of being a hand-kept copy of it. The
  copy held 68 words where the grammar reserves 170: **102 keywords did not survive being written out as
  an identifier and read back**, among them `USING`, `WITH`, `ROW`, `COLUMN`, `CROSS`, `INTERVAL` and
  `PARTITION`. It also held `KEY`, which the grammar had deliberately released.

- **`ModelBase.Is` on the AST**: a `CROSS JOIN` threw `NullReferenceException` when compared or cloned;
  an `INSERT … SELECT` never compared equal to itself; `VALUES (1,2),(3)` compared equal to
  `VALUES (1),(2,3)`; and a `BLOB` literal never compared equal to itself.

- **The schema is stored once.** An earlier form of this change kept the text beside the tree, and
  `ALTER COLUMN SET DEFAULT` and `DROP DEFAULT` then updated the text and left the tree - reporting
  success, changing the catalog, and changing nothing about what was inserted. Both work; nothing stores
  a second copy of a fact any more.

### Performance

The engine no longer parses stored schema on the row path. A partial index used to run the full ANTLR
parser **once per inserted row and twice per updated row** (37-70 µs per parse). The end-to-end effect
is single-digit percent and smaller than run-to-run noise - the mechanism is removed with certainty, the
magnitude is modest, and it is stated that way rather than claimed as a headline.

Schema records are about **3.4x larger** (38 -> 128 bytes for a small view). Schema is small and written
rarely; the trade is correctness for bytes, and it is not a compactness win.

### Ledger

**35 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 49 suppressed entries**, plus 2 `[Explicit]`,
unchanged from 8.0.0. Two markers closed - the view body's subquery and the partial index's filter - and
two opened for defects the phase's audit found and **measured against 8.0.0 as pre-existing**:
`RENAME COLUMN` leaves the column's `CHECK` naming the old column, and `DROP COLUMN` leaves a table
`CHECK` naming the dropped one; in both cases the table cannot be written to afterwards.

## 8.0.0

Closes phase 7, schema and DDL fidelity. **Major, and the breaking half is data:** constraints the DDL
has always accepted are now enforced, so values that used to go in are refused, and decimals are stored
at the scale their column declares.

The theme: **a declaration can fail three independent ways** - it can fail to reach the catalog, fail to
be reported by `INFORMATION_SCHEMA`, and fail to be enforced - and this release found all three
diverging. The instrument that found them asks all three of every declaration, which is why the results
were legible rather than a list.

The headline: **declared sizes were being dropped in four separate layers.** `VARCHAR(5)` was parsed
correctly and then lost by the EF type mapping source, by the migrations generator's `CREATE TABLE`
path, by the DDL executor, and by validation - each independently. Fixing any one alone changed nothing
observable, which is how a whole class of declaration escaped a 104-finding audit.

### Breaking

- **Declared lengths are enforced.** A string longer than its `VARCHAR(n)` or `CHAR(n)` is **refused**
  rather than stored, on `INSERT` and on `UPDATE`. Code that has been writing over-long values into a
  sized column will start failing. Refused rather than truncated on purpose: silently losing the end of
  a value is the one outcome nobody can want.

- **Decimals are stored at their declared scale.** `123.456` into a `DECIMAL(5,2)` column is now stored
  as `123.46`, as PostgreSQL stores it. The value in the database changes; it is not an error.

- **Numeric overflow is refused.** A value whose integer part does not fit in `precision - scale` digits
  is rejected. Rounding cannot save it, and it used to be accepted.

- **`ALTER TABLE ADD COLUMN … PRIMARY KEY` is refused** instead of silently recording half of it. A
  primary key is a property of the table - it needs the key list rewritten and every existing row
  checked - and adding a column only appends one. `ALTER TABLE ADD CONSTRAINT` has always refused it for
  the same reason.

- **`ALTER TABLE ADD COLUMN` now enforces `UNIQUE`, `CHECK` and `REFERENCES`.** It used to understand
  only `NOT NULL` and `DEFAULT` and drop the rest in silence, so a column added with a constraint
  arrived without one. Inserts that violate those constraints will now be refused.

- **EF store types carry the size.** A property with `HasMaxLength(5)` now maps to `VARCHAR(5)` rather
  than `TEXT`, so generated migrations, created schemas and scaffolded models all differ from 7.0.0.

### Fixed

- **A name given to a constraint inside `CREATE TABLE` reaches the catalog.** It never did, so
  `INFORMATION_SCHEMA.TABLE_CONSTRAINTS` could not list it and `ALTER TABLE DROP CONSTRAINT` could not
  find it - while the constraint itself was enforced the whole time. A constraint that worked, could not
  be listed, and could not be removed.

- **`DROP CONSTRAINT` removes the enforcement, not only the name.** Dropping used to be accepted and
  change nothing.

- **`INFORMATION_SCHEMA` describes the declared sizes**, which follows from recording them - the views
  had always had the columns and always reported them empty.

- **Database-first scaffolding works.** The model factory was issuing SQLite's own catalog queries -
  `SELECT name FROM sqlite_master` and four `PRAGMA`s - which this engine has never had and whose
  grammar does not contain the word `PRAGMA`, so `dotnet ef dbcontext scaffold` failed on its first
  query. It reads `INFORMATION_SCHEMA` now. Two further defects came out of that rewrite: the primary
  key was being inferred from which columns came back auto-generated, and the store type would have lost
  its size again.

- **`UPDATE` enforces declared sizes.** Its fast path has its own validation entry point, which the new
  enforcement reached nowhere - so a hundred characters could be written into a `VARCHAR(5)` by updating
  a row that already existed.

### Ledger

**35 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 49 suppressed entries**, plus 2 `[Explicit]`, down
from 37 + 14 = 51 at 7.0.0. Every marker in the schema and DDL area is closed.

## 7.0.0

Closes phase 6, the ADO.NET and EF Core contract. **Major, and the breaking half is the point:** the
provider now behaves like a provider where it used to behave like itself.

The theme of the whole phase is one shape. Everything worked when you held `WitDbConnection` and did not
when you held `DbConnection` - which is what EF Core holds, what Dapper holds, and what every framework
built on the contract holds. The instrument was a **reflection census** of the contract surface: for
every public virtual member the base types declare, is it overridden, *shadowed*, or inherited?
**Shadowed is the dangerous middle** - it passes every test written against the concrete type and throws
for everyone else.

### Breaking

- **Database failures are now `DbException`.** A missing table, a constraint violation and a syntax error
  used to arrive as `InvalidOperationException` and `WitSqlParsingException`, so every framework that
  handles database failures generically - EF Core execution strategies, Polly retry policies, ASP.NET
  diagnostics - saw none of them. Code that catches `InvalidOperationException` around command execution
  must catch `DbException` instead.

  The provider's own guards for **API misuse** - no connection, no command text, a transaction already in
  progress - are still `InvalidOperationException`, which is what ADO.NET means by them.

- **`Mode=ReadWrite` and `Mode=ReadOnly` no longer create a database that is not there.** They mean *open
  an existing one*, and all four values of `Mode` used to behave identically because the only question
  asked was whether the mode was `Memory` - so a mistyped path produced an empty database instead of an
  error. Use `Mode=ReadWriteCreate`, the default, to create.

- **Savepoints and ambient transactions are now advertised, so EF Core starts using them.**
  `DbTransaction.SupportsSavepoints` answers `true` (it answered `false` while all six savepoint members
  existed and worked), and a connection opened inside a `TransactionScope` enlists in it. Both were
  no-ops before; both now take part in EF Core's own recovery paths.

- **A reader is closed when its connection closes.** It used to keep returning rows - correctly - out of
  storage that had been disposed underneath it.

### Added

- **Ambient transaction support.** A connection opened inside a `TransactionScope` enlists as the single
  resource manager of that transaction; an abandoned scope rolls the work back. Enlistment happens at
  `Open` and only there, exactly as in SqlClient, and `Enlist=false` turns it off. **A second database in
  the same scope is refused by name** rather than joining and committing on its own: this engine has no
  durable two-phase prepare, and says so instead of pretending.

- **`Connection Timeout`**, and `DbConnection.ConnectionTimeout` reporting it. Opening waits briefly for
  another engine to release the database - five seconds by default - because a host restart overlaps the
  outgoing process with the incoming one, and refusing on the first attempt turned that window into a
  startup failure. SQLite covers the same window with `busy_timeout`.

### Fixed

- **Six savepoint members were shadowed rather than overridden**, so they worked on `WitDbTransaction`
  and threw through `DbTransaction`. The recorded finding had named three of the six; the async trio was
  shadowed too and nothing had said so.

- **`DbParameter.Precision` and `Scale` were dropped.** Set to 5 and 2 through the base type - which is
  what `CreateParameter()` returns - the provider saw 0 and 0.

- **`DbCommandBuilder.QuoteIdentifier` threw**, while the builder had been configured with its quote
  characters since the day it was written. It now applies them, doubling a quote that appears inside the
  identifier.

- **`Default Timeout` was parsed and read by nothing.** It now sets a new command's `CommandTimeout`,
  which is what ADO.NET means by the keyword.

### Known and recorded

- **The requested isolation level is reported and applied by nothing.** Measured: a transaction opened at
  `Serializable` or `RepeatableRead` sees a row another connection committed after it began. The level is
  sent to the engine; honouring it needs MVCC to pin a read snapshot at transaction start, which is its
  own piece of work and is not in this release. `ReadCommitted` is unaffected - it is allowed to see the
  row.

- **`FileLocking=false` admits a second engine on Linux**, where .NET maps the write-ahead log's share
  mode to a shared advisory lock. The switch documents itself as disabling the exclusivity guard; on that
  platform it does so silently, and the two engines then diverge. Use it only for the case it exists for:
  a single engine on a filesystem whose locking cannot be trusted.

### Ledger

**43 `[Ignore(...)]` + 14 `[TestCase(... Ignore =)]` = 57 suppressed entries**, plus 2 `[Explicit]`, down
from 52 + 14 = 66 at 6.0.0. Six markers closed and two opened - both the isolation level, one defect
wearing two `TestCase`s.

## 6.0.0

Closes phase 5, concurrency, apart from one named experiment. **Major, because two public types were
deleted** — and because the heaviest fix in it is a supported configuration that acknowledged writes and
then lost them.

The headline: **`Store=lsm` with any parallel mode lost acknowledged writes, on default settings.** Ten
`INSERT`s, every one reporting success, left 0 or 1 rows — and they were still missing after a clean
close and reopen, so they had never been written at all. The cause was not in the LSM store:
`MvccKeyValueStore.CommitTransaction` **scans the store to find the versions it has just installed** and
rewrites them as committed, and over a buffering parallel store that scan read past its own write, found
nothing to commit, and left every version uncommitted for ever. **Read-your-own-writes is not a
convenience in this engine; the commit protocol is built on it.**

### Breaking

- **`PageLatch` and `PageLatchManager` are removed.** 551 lines that nothing constructed — the compiler
  confirmed it, which is stronger than a search. `BTreeConcurrentStore` serialises with one store-wide
  lock, so per-page latching bought nothing under the concurrency model this phase settled.

- **A scan through `BTreeConcurrentStore` is no longer a snapshot.** It used to materialise the whole
  range under one read lock; it now streams in chunks, and writes landing between chunks are visible to
  the rest of the scan — which is what the unwrapped store has always done, so concurrent mode behaves
  like the default mode rather than differently from it. The reason for the change is that materialising
  charged the caller for everything it did not ask for: an open-ended index range whose consumer took
  five entries cost **108 ms and 25.5 MB** on a 200,000-entry index, against 3.1 ms and nothing for the
  unwrapped store. It is 82 KB now.

- **Secondary index stores are serialised**, so `ISecondaryIndexFactory.ProviderKey` for a file-backed
  B+Tree index reports `btree-concurrent` rather than `btree`. Nothing persists that value; index files
  on disk are unchanged and open as they always did.

### Fixed

- **`Store=lsm` + any parallel mode lost acknowledged writes** (above). `Get` and `Scan` now flush the
  calling thread's own buffer and wait for the merge, which is exactly read-your-own-writes. A reader
  with nothing buffered pays nothing — an empty buffer returns without touching the channel.

- **Concurrent connections corrupted a secondary index.** The builder wrapped the main store for
  concurrent access and handed every index a bare `StoreBTree` with no locking at all, so two
  connections inserting rows walked into the same B+Tree leaf split. Measured over ten runs of a
  deterministic experiment: nine threw out of `BTreeNode.CollectLeafEntries`, and once nothing threw and
  three entries were simply gone — two of them belonging to the writer that had already finished. Index
  stores are now serialised, and not conditionally on a parallel mode, because a second connection is
  enough.

- **`Flush()` did not flush what the write threshold had already queued.** A buffer reaches the LSM
  merge queue with no completion attached whenever `Put` crosses its size threshold, and
  `FlushAllAsync` waited only for the buffers it handed over itself — so `LsmParallelStore.Flush` wrote
  an SSTable while the entries behind it were still in the channel. Measured single-threaded:
  **10,000 of 10,000** entries still in flight when the flush returned. The flush now queues an empty
  buffer last and waits for that too, which the channel's FIFO order turns into "everything ahead of it
  has been applied".

- **`LsmParallelWriter.FlushAllAsync` took buffers their owners were still writing into**, and reset
  only the calling thread's slot, leaving every other owner holding a buffer that had already been
  merged and disposed. Measured: runs of eight and nine consecutive entries lost, and a flush dying
  inside `List.ToList`. Buffers now change hands under the same gate their owner appends under.

- **`BTreeConcurrentStore` held its lock across an await** in all four asynchronous entry points.
  `ReaderWriterLockSlim` is thread-affine, so a continuation resuming on another thread threw
  `SynchronizationLockException` out of the release — and left the lock held by a thread that had moved
  on, deadlocking every later reader and writer.

- **Row locks were never released, and ran waiters' continuations inline.** `ReleaseAllLocks` took
  1023 ms with a one-second continuation attached to a waiter — measured on the releasing thread, which
  is where that work should never have run.

- **The MVCC deadlock detector was complete and was never fed a wait edge.** `SELECT … FOR UPDATE` now
  reports `DeadlockException` naming the other participants, instead of both sides waiting out the lock
  timeout and each getting a `TimeoutException`.

- **`Clear()` on the page cache recycled a pooled buffer while a write was still using it** — in *both*
  cache implementations. The second was found by grepping for the shape rather than for the name.

- **`TransactionWaitQueue` completed waiters on the canceller's thread** (1004 ms, same measurement).

- **The batch merge applied every `Put` before every `Delete`**, reordering operations within a batch —
  which is what MVCC does on commit, so a delete could be applied to a store state that never existed.

- **Closing a database refused a concurrent open.** `SharedDatabase.Release` removed its registry entry
  inside the lock and disposed outside it, and disposal is what releases the exclusive file lock — so a
  concurrent `Acquire` built a second engine and hit a lock the first had not let go of. Found by CI
  after 15 consecutive green runs locally.

### Ledger

**52 `[Ignore(…)]` + 14 `[TestCase(… Ignore =)]` = 66 suppressed entries**, plus 2 `[Explicit]`, down
from 66 + 14 = 80 at 5.0.0. The concurrency area is closed: `CoreConcurrencyFindingsTests` holds no
markers at all, and what remains there is an unreachable `ConnectionPool` permit leak and a reclassified
`MVCC=false` divergence — neither an open defect.

## 5.0.0

Closes the first half of phase 5, concurrency and concurrent access. **Major, because it changes two
answers the previous releases gave — and in opposite directions.**

The headline: **several connections to one database in one process now work.** That is the shape an
ASP.NET Core service has — one host, a scoped `DbContext` per request — and until this release it did
not work at all. Every connection built its own engine, a database admits one engine, so the second
concurrent connection simply failed.

The counterpart: **a second *process* is now refused deliberately and identically everywhere.** It used
to depend on the platform and the store, and on Linux an LSM database refused nobody — two engines would
open it and then quietly diverge.

The audit that preceded the fixes had to establish what the concurrency model even was, because nothing
in the project stated it. It is now written down in `WitSQL.md` § 15.0:

> **One process. One engine per database. Many connections. One writer at a time.**

### Behaviour changes

- **Several connections to one database, in one process, share one engine.** They see each other's
  committed work — rows *and* `COUNT(*)`, including tables created after a connection opened. The engine
  is created by the first connection and disposed when the last one closes. Connections are cheap
  handles now; the expensive thing was always the engine.

- **A second process is refused with `DatabaseAlreadyOpenException`** instead of a raw `IOException`
  carrying an operating-system sharing message. Enforcement is an exclusive `.lock` sidecar held for the
  engine's lifetime, which behaves the same on Windows and Linux and does not depend on which files a
  given configuration happens to create.

  **This is the breaking half.** Before 5.0.0 exclusivity was a side effect of file-sharing modes: a
  B+Tree database refused a second engine everywhere, an LSM database only on Windows and only with the
  write-ahead log enabled, and an LSM database with the log disabled refused nobody at all. Code that
  opened one LSM database from two processes on Linux will now get an exception. That configuration was
  unsafe — the two engines diverged, one seeing a row the other could not.

  The operating system releases the lock when a process exits, so a crash does **not** leave a database
  permanently unopenable. That is now tested across a real process boundary, not asserted in prose.

- **`Read Only=true` and `Mode=ReadOnly` are honoured.** Both were parsed and dropped: a write through a
  read-only connection succeeded. A read-only connection now refuses everything that could change data
  or schema, including the bulk API, and permits `SELECT`, `EXPLAIN` and transaction control. It is a
  property of the **connection**, not of the file, so a read-only connection and a writing one can
  address the same database at once — which is what it is for.

- **`FileLocking=false` no longer disables in-process write serialisation.** The flag decided whether a
  lock manager existed *at all*, and both transactional stores treat "no lock manager" as "no locking" —
  so a setting that reads *"do not coordinate across processes"* removed the mutual exclusion between two
  threads writing the same store. Write serialisation is no longer optional; the flag now controls only
  the cross-process guard, which is the job its name always described.

- **`CREATE TABLE T (Key TEXT)` parses.** `KEY` was a lexer token with no way in as an identifier. The
  failure had been recorded for months against `Parallel Mode=Buffered`, in a concurrency fixture.
  Parallel mode was never the cause and is a supported configuration.

### Known and unchanged

- **Concurrent transactions in different connections need MVCC**, which is the provider default. With
  `MVCC=false` a transaction holds a database-wide write lock and a second session's `BEGIN` reports a
  lock-recursion error.
- **Two `Data Source=:memory:` connections are still two databases**, as in SQLite without
  `Cache=Shared`. Sharing them would be an opt-in feature, not a silent change.
- **`Mode=ReadWrite` still creates a database that is not there**, instead of failing as its name
  promises. Recorded with a failing test; it is a database-level change and did not belong in this one.
- **118 keywords cannot be used as bare column names** where SQLite accepts them — measured against the
  oracle, mostly type names such as `Text`, `Int` and `Decimal`. Recorded and pinned by name; the fix
  belongs with the DDL work.

## 4.0.0

Closes phase 4, durability and crash recovery. **Major, because it changes answers the previous
releases gave — and in several cases the old answer was silence.**

The headline is that a crash no longer costs you data the database told you it had accepted. Before
this release, a process that died took every autocommit write with it **including the tables those
writes had created**, because autocommit opened no transaction and therefore never committed and
never flushed. A statement is now a unit of work.

Thirteen defects, six of which were in no audit and were found only because two instruments were
built first: an out-of-process crash runner, and a modelled power cut at the storage seam.

### Behaviour changes

- **A statement either happens completely or not at all.** A multi-row `INSERT` that failed on the
  third row used to leave the first two; an `UPDATE` that failed on a later row left the earlier ones
  changed. A data-modifying statement executed outside an explicit transaction now runs inside an
  implicit one. Pre-validating the rows would have been the wrong fix — intra-statement uniqueness
  depends on the earlier rows already being present.

- **Autocommit writes are durable.** They follow from the same change: a statement commits, and a
  commit flushes. **This costs about 1.5× on the write path** — 1000 autocommit inserts measured at
  0.26 s against 0.17 s before — which is the price of the D in ACID and what PostgreSQL, SQL Server
  and SQLite all charge. Use an explicit transaction around a batch to pay it once.

- **A damaged write-ahead log is reported instead of being truncated past.** Recovery used to stop at
  the first record that failed verification, return what it had managed as though the log ended there,
  and then truncate — so one bad record destroyed every committed transaction behind it, silently.
  Measured: 2 of 5 committed transactions recovered, no error. It now raises `WalReplayException`
  carrying how many entries were replayed, how many the header knew about, and the offset — and it
  leaves the log intact rather than checkpointing over the evidence. **A database whose log is damaged
  mid-way now fails to open rather than opening with fewer transactions than it has.** A torn tail —
  the half-written record an ordinary crash leaves — is still recovered from as before, told apart by
  the log's own entry counter. The same silence existed in the LSM write-ahead log and is fixed there
  too.

- **A write rolled back to a savepoint stays rolled back.** `Put` and `Delete` write to the journal
  when they are called, while the store is not touched until commit, so rolling back to a savepoint
  left the journal holding writes the transaction had discarded and recovery replayed them.

- **Row counts and row-id counters commit with the rows they describe.** After a crash, `SELECT`
  returned every row while `SELECT COUNT(*)` returned **0**, and the row-id counter came back at zero
  so the next insert took an identity that was already in use — and so did every insert after it.

- **A key beginning with `$` written inside an MVCC transaction is visible after commit.** The MVCC
  store skipped every such key when marking a transaction's records committed, though it owns exactly
  one of them. The transaction reported success and the value was gone.

- **An SSTable is on the media before the log holding the same data is truncated.** The LSM path never
  asked for durability: finalisation ended at a buffer flush, and the write-ahead log was dropped
  immediately afterwards. `Flush()` therefore *reduced* durability — it replaced a log the caller may
  have synced with a table that was never synced.

- **A crash while writing an SSTable leaves nothing behind.** Tables were written straight to the name
  recovery looks for, so a half-written one was loaded as the newest table in the store and the next
  open failed outright with `Invalid SSTable magic`. Tables are now written under a name the store
  ignores and renamed into place, which is atomic. A table that *is* damaged is still reported rather
  than skipped.

- **Compaction of an encrypted store works.** The compactor was never given the store's encryptor, so
  compacting an encrypted store failed outright — and had the reads succeeded, every row would have
  been rewritten in clear text.

- **A scan keeps working when compaction replaces the tables under it.** Readers are closed when the
  last holder lets go rather than when compaction disposes them; a scan in flight used to read from a
  closed file.

- **The rows of a failed memtable flush stay readable.** The next flush overwrote the only pointer
  still holding them, so a running process went on answering reads without rows it had accepted. They
  were never lost for good — the log still had them — but only a restart would have shown that.

- **A rollback journal accepts a bare relative path.** `Journal=rollback` with `Data Source=x.witdb`
  threw `ArgumentException`; and for a path at a filesystem root it created a *directory* named after
  the journal file.

### Under the hood

- Two new instruments, both carrying their own controls: `Tools/OutWit.Database.CrashRunner`, which
  runs a scenario in its own process so it can be killed, and a modelled power cut over `IStorage`
  that promotes writes to the media only on flush. Crash tests run in CI — they are deterministic
  assertions about this code, not timing measurements.
- `LsmOptions.SstableFileFactory` is a seam for the SSTable output file. It settled three findings the
  audit had recorded as unreachable.

## 3.0.1

**A release-process fix, not a code change.** No behaviour differs from 3.0.0; use this instead.

3.0.0 shipped incompletely. A version bump to `OutWit.Database.csproj` was reverted by accident
while undoing an unrelated change to the same file, so that package was packed as **2.4.0**. nuget.org
answered `Conflict - already exists`, `--skip-duplicate` swallowed it, and the publish workflow
reported **success** having published nothing. The next package in the chain,
`OutWit.Database.AdoNet 3.0.0`, was then published for real carrying a dependency on the stale
`OutWit.Database 2.4.0` — pairing it with `OutWit.Database.Core 3.0.0`, which it does not match.

Packages on nuget.org are immutable, so 3.0.1 republishes all seven at a consistent version.
**`OutWit.Database.AdoNet 3.0.0` should not be used**; `OutWit.Database 3.0.0` never existed.

The publish workflow now refuses to push when the packed version does not match the release tag on
`HEAD`, and fails loudly when `--skip-duplicate` means nothing was actually published.


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
