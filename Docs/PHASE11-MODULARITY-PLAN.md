# Phase 11 - the modular structure

**Opened 2026-08-03**, on `phase11-modularity`, after every phase of the plan closed with 11.2.0.

WitDatabase is built as a construction kit, like WitRPC: a workload picks a store, a transaction model,
a parallel mode, encryption, a journal, a cache. **The combinations have never been enumerated, let
alone run.** This phase enumerates them, runs them, and says which work, which refuse, and which quietly
do something else.

## 1. Why this phase, and what it inherits

Phase 10 found that **every LSM connection-string parameter was inert** - `MemTableSize`, `SyncWrites`,
`EnableWal`, `BlockSize`, `CompactionTrigger`, the block cache. The cause was structural rather than
LSM-specific: `WitDatabaseBuilder.BuildLsmStore` built the store directly and accepted only a ready-made
options object, while the connection-string mapping lived in `ProviderRegistration`, on a path the
builder does not take. **Two construction routes that disagree.** (`PHASE10-PERFORMANCE-PLAN.md` § 18.1.)

That is the phase-7 defect class - *accepted but not enforced* - one level out: accepted at the
**configuration** surface rather than the DDL one. Nothing had checked whether `Cache`, `Journal`,
`PageSize`, `Parallel Mode` and the rest fare any better.

**Acceptance for this phase:**

1. Every combination either works, or is refused **at open** with a legible message. No "accepted and
   ignored".
2. Every connection-string keyword is either **proved to reach the engine**, or removed.
3. What is supported and what is not is written in `WitSQL.md`, where a consumer will read it.

## 2. The instruments

Two, because "does the setting arrive" and "does the engine that received it still answer correctly" are
different questions, and phase 6 / phase 7 both showed that asking one question gets one answer.

### 2.1 Instrument A - the configuration census

`Sources/Providers/OutWit.Database.AdoNet.Tests/Modularity/ConfigurationCensusTests.cs`

For each keyword: build the database **through the real connection-string path** with the baseline value
and with a different value, take a **structural fingerprint** of the built object graph by reflection
(runtime type at every reachable field, value at every scalar; fields only, never properties), and
compare. Identical engines mean the keyword reached nothing.

The verdicts are `REACHES`, `INERT`, `ECHO ONLY` (the only difference is a copy of the keyword's own
text, kept as text) and `REFUSED`.

**Controls, in both directions, asserted rather than printed:**

- **Positive** - `Store`, `MVCC`, `MemTableSize`, all proved to reach. If one reports INERT the
  fingerprint is blind and no INERT verdict in that run may be believed.
- **Negative** - `Nonexistent Keyword=42`, which reaches nothing by construction. If it reports REACHES
  the fingerprint is noisy and no REACHES verdict may be believed.
- **Per-knob noise calibration** - the baseline side is built **twice**, in two directories, and every
  path that already differs between two builds of the same configuration is excluded before the variant
  is compared. That is what keeps temporary paths and handles out of the verdict; the negative control is
  what proves it worked.

Both controls held on the first run and on a repeat, and the two runs produced identical verdicts.

**What the census cannot say** is whether a setting that arrives is then honoured. The isolation level is
the standing example: it is stored on the transactional store, so it REACHES, and it is applied by
nothing. Reaching is necessary and not sufficient.

### 2.2 Instrument B - the combination matrix

`Sources/Providers/OutWit.Database.AdoNet.Tests/Modularity/CombinationMatrixTests.cs`

153 cases: the cross product of store x transaction model x parallel mode x encryption (108), plus
thirteen orthogonal add-ons swept against each store, each asked two questions - does it answer like
every other configuration, and does the data survive a close and reopen.

**The workload is written so its answers do not depend on the configuration.** A configuration without
transactions writes the committed row directly and never writes the rolled-back one, so every
combination must produce byte-identical answers. Rows are compared by **scanning them back**, never by
`COUNT(*)` - on this engine that is a cached counter and has disagreed with the rows before.

**The control is the reference itself**: the default configuration is asserted against **hard-coded
literals**, not against another run of the engine, so an engine-wide regression cannot make the matrix
agree with itself and pass.

**The control earned itself immediately.** Its first run went red - `8:45.50` expected against `8:42.75`
measured - and **the literal was wrong, not the engine**. Eighth time in this project that the instrument
was wrong before its subject.

Runtime: 153 cases in 10.4 s.

## 3. What the census found

Verdicts stable over two runs. `REACHES` for 25 keywords, and:

| Keyword | Verdict | What it means |
|---|---|---|
| `Cache=clock` vs `lru` | **ECHO ONLY** | The chosen key is written into the database header. No cache of that type is ever constructed. |
| `Journal=wal` vs `rollback`, MVCC **on** (the default) | **ECHO ONLY** | Same: the key reaches the header, the journal reaches no store. |
| `PageSize` | **INERT** | From a connection string. Works from the fluent builder. |
| `CacheSize` | **INERT** | Same. |
| `Parallel Mode` `Auto`/`Buffered`/`Latched`/`Optimistic` | **INERT between each other** | Four documented modes with four different descriptions, one behaviour - on both stores. |
| `Max Writers` on B+Tree | **INERT** | Reaches the LSM parallel store; the B+Tree concurrency options have no such field. |
| `Isolation Level` with `MVCC=false` | **INERT** | With MVCC on it reaches a field and is then applied by nothing (phase 6). |

`Parallel Mode=None` vs any other value REACHES - a wrapper appears. It is the four non-`None` values
that are indistinguishable from one another.

**One cause explains `PageSize` and `CacheSize`, and it is bigger than those two.** Every value from a
connection string arrives as a **string**, and `ProviderParameters.Get<T>` is
`if (m_values.TryGetValue(name, out var value) && value is T typed)` - no conversion. `Get<int>("PageSize")`
on the string `"16384"` fails the type test and **returns the default, silently**. The fluent builder
sets the same key as an `int`, so the same code works there. The LSM options escaped only because
`LsmOptions.FromParameters` parses strings by hand - which is what phase 10 built, one parameter set at a
time. Every other `p.Get("pageSize", ...)`, `p.Get("capacity", ...)`, `p.Get("readOnly", false)` in
`ProviderRegistration` has the same hole.

**This is the same defect as phase 10's, in a different coordinate**: not two routes that read different
places, but two routes that supply different *types*.

## 4. What the matrix found

**129 of 153 pass.** Every combination opens, runs the workload and returns identical answers - the
"opens and quietly answers something else" category is **empty**, which is worth stating plainly.

All 24 failures are in the reopen question, in three groups.

### 4.1 `Store=lsm` + `Transactions=false` + a parallel mode loses an acknowledged write

Four cases: `lsm tx=off par=auto` and `lsm tx=off par=buffered`, plain and encrypted. Seven rows survive
the reopen and **the last row written does not** - the write that reported success, then a clean close,
then it is gone.

`lsm tx=off par=none` survives. `lsm tx=mvcc par=*` survives. So it needs the LSM store, no transaction
layer, and a parallel wrapper.

This is the family of the 6.0.0 defect - `Store=lsm` + any parallel mode losing acknowledged writes -
in a configuration that one was not: there is no MVCC commit protocol here to blame, because there is no
transaction layer at all. **Not yet diagnosed.**

### 4.2 `Store=inmemory` with a file `Data Source` leaks the file handle

Eighteen cases. Reopening throws `IOException: the process cannot access the file ... because it is being
used by another process` - the **same** process, after the connection was disposed.

`WitDatabaseBuilder.BuildStoreFromRegistry` builds an `IStorage` for every store, which for a file data
source **opens the file**, and passes it to the provider factory as the `storage` parameter. The
in-memory store's factory ignores every parameter and returns `new StoreInMemory()`. Nothing owns the
storage, nothing disposes it, and the handle is held until finalization.

So the combination is not merely non-persistent, which would be honest: it is **unopenable a second time
in the same process**.

### 4.3 `Journal=wal` leaks the journal file handle

Two cases, one per store, and it is the ECHO ONLY census verdict with teeth.

`BuildTransactionalStoreInternal` calls `BuildJournal()` **before** it branches on MVCC.
`WalTransactionJournal` opens its file in its constructor. With `MVCC=true` - the default - the MVCC
store is built without a journal, and the object just built is dropped: never referenced, never disposed,
its file handle held for the life of the process.

`Journal=rollback` does not fail the same way (it opens lazily), and `Journal=wal;MVCC=false` does not
fail at all - there the journal is wired into `TransactionalStore` and disposed with it.

### 4.4 What the matrix did **not** find, and it is worth saying

**No combination opens, accepts every statement and answers something different.** All 108 cross-product
cases and every add-on produced byte-identical answers to the reference. The dangerous middle category is
empty. Every defect this phase found is in construction or in close, not in the answers.

## 4a. The fixes

Five, each with its red measurement taken before the fix existed.

| Defect | Fix | Proved by |
|---|---|---|
| `Store` decided whether any other keyword arrived | `ConfigureStore` forwards the parameter bag whether or not the store is named (`WithStoreParameters`) | `PageSize` census verdict INERT -> REACHES, both spellings |
| Numeric and boolean keywords silently defaulted | `ProviderParameters.Get<T>` converts text to the requested scalar and **throws** on a present-but-unreadable value | same |
| `Store=inmemory` held the data file open | storage is built through a `Lazy<IStorage>`, so a store that never asks for it never opens it | 18 matrix reopen cases |
| `Journal=wal` held the journal file open | the journal is built only on the branch that uses it, and the combination is now refused (§ 6) | 2 matrix reopen cases |
| `Store=lsm` + `Transactions=false` + a parallel mode lost the last row written | `LsmParallelWriter.Dispose`/`DisposeAsync` hand over the buffers that are still filling **before** closing the queue | 4 matrix reopen cases |

**The data loss, in one paragraph.** `LsmParallelWriter.Dispose` completed the buffer channel - which
drains what is already *queued* - and then disposed the thread-local slots, discarding every entry below
the size threshold. That is the tail of every workload. With MVCC the commit path calls `FlushAllAsync`
and hid it; with `Transactions=false` nothing does. Seven rows survived the reopen and the eighth did
not. The store now flushes before it closes the queue: **a store that accepted a write does not get to
discard it at close.**

Full suite after all five, with the CI filter: **green**, 10,406 tests across eight projects.

## 5. Work order

1. ~~Build both instruments and take the census.~~ **Done.**
2. ~~Fix § 4.3 and § 4.2 - two handle leaks.~~ **Done.**
3. ~~Diagnose § 4.1 - the data loss.~~ **Done, § 4a.**
4. ~~Decide and act on the census's inert settings.~~ **Two of three done, § 6.**
5. ~~Write the supported matrix into `WitSQL.md`.~~ **Done, § 14.10.**
6. ~~The parallel modes (§ 6.3).~~ **Done - removed, § 6.3b.**
7. ~~A database created by one configuration, opened by another (§ 6a).~~ **Done, all three fixed.**
8. ~~The two probes never built - two connections (§ 6c) and durability by configuration (§ 6d).~~
   **Done.**
9. Open: the async builder route (§ 6.5), and the three areas in § 6b that no instrument has touched.

## 6. Decisions

### 6.1 `Cache=clock|lru` - wired, decided 2026-08-03

`StoreBTree` takes an `IPageCache` now, and the builder constructs the one the configuration chose - for
the main store and for each secondary index store, each with its own instance, because a page cache is
bound to one storage. `WithCache(IPageCache)`, which was read by nothing, reaches the main store.
`Cache` moved from ECHO ONLY to REACHES in the census.

### 6.2 `Journal` with MVCC - refused, decided 2026-08-03

A journal is only reachable through the lock-based `TransactionalStore`. `Journal=…` with `MVCC=true` -
the default - or with transactions off is now refused at `Open` with a message naming the way out. That
is the phase's rule applied to itself: a setting that cannot be honoured is an error, not a silence.

### 6.3a Both measurements taken, 2026-08-03 - and they decide it

`MainStoreConcurrencyProbeTests` reuses phase 5's parking seam against a database built the ordinary
way: one writer parked inside a leaf split of the **main** store, a second let in, released, count what
survived. Controls in both directions in every pass - a bare store must be damaged (or the harness is
blind) and parking alone must destroy nothing.

**Measurement 1 - is `Parallel Mode=None` safe?** Five runs, completely stable:

| configuration | outcome |
|---|---|
| bare `StoreBTree` (positive control) | **damaged** 5/5 - 2-9 entries gone, usually with **no exception at all** |
| `BTreeConcurrentStore` (control) | clean 5/5 |
| database, MVCC, no parallel mode | clean 5/5 |
| database, lock-based transactions, no parallel mode | clean 5/5 |
| **database, `Transactions=false`, no parallel mode** | **damaged 5/5** - threw *and* lost a row |
| database, MVCC, parallel mode | clean 5/5 |

So the transaction layer - either one - does serialise the main store, and `Parallel Mode=None` is safe
**as long as transactions are on**. With `Transactions=false` there is nothing between two writers and
one leaf split, and the store is exposed exactly as a secondary index store was before 6.0.0. **A new
defect, and the keyword was load-bearing precisely where it should not have had to be.**

**Measurement 2 - what does serialising cost a single thread?** Five interleaved passes of 20,000
put+get, wrapped against bare, ratios `1.001, 1.070, 1.009, 0.996, 1.000` - **median 1.001**, four of
five within 1%. The 1.070 is the outlier and the same shape as phase 10's "4.13x regression" that a
second pass measured at 0.83.

**Decided: the B+Tree store is serialised unconditionally**, main store and index stores alike, exactly
as 6.0.0 already decided for index stores. It closes the `Transactions=false` hole, it costs nothing,
and it takes correctness off the list of things a connection string can switch off. Three tests that
pinned the old shape were inverted with the reason written into them.

**What is left of `Parallel Mode` is LSM write buffering** - a throughput choice, since `StoreLsm` locks
internally and is safe without it. That is what `WitSQL.md` § 14.10 now says. The four names remain four
spellings; building distinct mechanisms would be a phase, and nothing now depends on it for safety.

### 6.3b The setting is removed, 2026-08-03 - and the third measurement is why the mechanism stays

Dmitry's decision once § 6.3a landed: remove `Parallel Mode`, delete the public API rather than
deprecate it, and **measure the LSM write buffer before deciding whether the mechanism goes with it**.

`LsmWriteBufferingCostProbeTests`, three rounds per shape, interleaved, with the store's own counters
asserted so that a round which buffered nothing cannot pass as a null result:

| | ratio, buffered / direct |
|---|---|
| Straight into the store, one writer | 1.00 - noise, 0.77-1.02 across passes |
| Straight into the store, four contending writers | **0.80** (0.810 / 0.803 / 0.774) |
| Through a database, four writers, autocommit | **1.14** (1.177, 1.136) |
| Through a database, four writers, batches of 1,000 in a transaction | **1.04** |

**The win needs four threads inside the store at once, and a transaction layer will not let that
happen** - § 6.3a measured exactly that, from the other side. So through the engine the buffer only
costs, and a knob that selects it is a knob that makes things worse.

**What was removed:** the `Parallel Mode` and `Max Writers` keywords (refused at `Open`, not ignored),
`WitDbParallelMode`, the ADO.NET properties, EF's `UseParallelWrites`/`MaxWriters`, `ParallelMode`,
`ParallelModeOptions`, `KeyValueStoreFactory`, and the three builder extensions. **What stays:**
`LsmParallelStore`, public, for a caller who drives a store directly - which is the one place the 0.80
is real.

Also recorded on the way past, not chased: through the engine, four writers doing batches of 1,000 in a
transaction took **181 s** against autocommit's **61 s** for the same 100,000 rows. Concurrent MVCC
transactions contend badly, and that is a bigger number than anything this phase set out to find.

### 6.3 The four parallel modes - the reasoning, before the measurements above

Measured, not argued: **which concurrency mechanism you get is decided by the store, not by the keyword.**

| | `Parallel Mode=None` | any other value |
|---|---|---|
| `Store=btree` | bare `StoreBTree` | `BTreeConcurrentStore` - a reader-writer lock |
| `Store=lsm` | bare `StoreLsm` | `LsmParallelStore` - thread-local write buffers, background merge |

So on the B+Tree the honest name is `Latched` and on the LSM it is `Buffered`, and each store has exactly
one strategy. `Auto`, `Buffered`, `Latched` and `Optimistic` are four spellings of "make this store
thread-safe". `Latched` cannot mean what its XML doc says under any of them - the page-latch subsystem
was deleted as dead code in 6.0.0.

**What the options actually give**, which is the question worth answering before choosing:

- `None` -> non-`None` is a **real** difference and the only one: it decides whether the main store is
  serialised at all.
- The other three names differentiate nothing today, and building the differences would be a phase, not
  a fix - `ParallelModeOptions` also carries `FlushIntervalMs`, `LatchTimeout` and `UseOptimisticReads`,
  none of which the builder passes anywhere.
- The sharper question behind it: **is `None` safe for the shape this engine is designed for?**
  Since 6.0.0 secondary index stores are wrapped **unconditionally**, because a second *connection* is
  enough to corrupt a B+Tree leaf split - and the main store is wrapped only when a parallel mode is
  asked for. `StoreBTree` has no locking of its own. That is not a documentation question, and it is not
  answered here: it needs the deterministic parked-collaborator experiment phase 5 built, run against
  the **main** store with `Parallel Mode=None` and two connections.

Until that is measured, `WitSQL.md` § 14.10 states what is true - the mode is decided by the store, the
four names are spellings - rather than repeating the XML docs.

### 6.4 `Max Writers` on the B+Tree

Documented as LSM-only in § 14.10. Not refused: it is a throughput hint, not a promise, and refusing a
harmless hint would break connection strings that name it for a store they later change.

### 6.5 The async builder route disagrees with the sync one - new, not yet acted on

`WitDatabaseBuilder.BuildStoreInternalAsync` builds a `StoreBTree` for **every** configuration that is
not LSM: `Store=inmemory` and any custom registered store provider are ignored on that path. It is the
same two-routes-disagree shape again, in the third place this phase has found it. ADO.NET consumers do
not reach it - `WitDbConnection.OpenAsync` runs the synchronous `Open` - so it is a builder-API defect,
recorded here rather than fixed in the same pass.

## 6a. Instrument C - a database created by one configuration, opened by another

`ConfigurationMismatchTests`, 8 configurations x 8 = 64 pairs plus controls. The matrix reopens with the
**same** connection string; this asks what a consumer meets when a config file drifts. Controls both
ways: every configuration must read its own database back, and an encrypted one must **not** be readable
without its password.

The first classification was too generous and had to be sharpened - it counted any exception as a
legitimate refusal, and `Table 'X' not found` is not a refusal, it is an open that answers wrong. The
outcomes are now **Correct / RefusedAtOpen / OpensAndDataIsGone / Wrong**, and each case additionally
asks whether the **creator** can still read the file afterwards, which separates invisibility from
destruction. **Three defects, none of them previously known:**

1. **The transaction model changes the on-disk layout and nothing says so.** A database written with
   MVCC opens without complaint under `MVCC=false` or `Transactions=false` - and reports
   `Table 'X' not found`. Both directions. The rows are intact: the creator reads them back afterwards.
   So a consumer who flips one keyword sees an empty database, and the obvious next step - create the
   schema - writes over one that was fine.

2. **A larger `PageSize` reinitialises the header instead of reporting the mismatch.**
   `pagesize -> default` refuses cleanly with `Page size mismatch: file has 16384 bytes, storage expects
   4096`. The other direction opens, shows nothing, and **afterwards the original configuration gets that
   same error** - the file now claims a page size it never had. Destruction, not invisibility, and the
   most serious thing in this phase.

3. **A refused open leaves the data file open.** Attribution, not assumption: after a wrong password is
   refused, the creator's own configuration gets `The process cannot access the file ... because it is
   being used by another process`. So a mistyped password locks the database out for the life of the
   process, under a message that names the wrong problem. Same shape as the two handle leaks § 4a fixed -
   something is constructed, the build then fails, nothing disposes what was built.

**All three are fixed, each with its pins going red first - which is the proof.** `StorageFile` refuses
a file too short to hold one page of the size being asked for, so § 6a.2 is now an ordinary refusal at
open; and the storage the store was going to own is disposed when that store's construction throws, so
§ 6a.3 leaves nothing behind. Both pins are inverted and assert the fixed behaviour.

**§ 6a.1 is now FIXED too, 2026-08-03, and the pins went red before they were inverted** - twelve pairs
were pinned as `OpensAndDataIsGone`, and every one of them now refuses at open instead. The design
decision it was
waiting on is `IProviderMetadataSource`: a store that keeps a header may hand back the
`ProviderMetadata` it was opened with, `StoreBTree` implements it over its private `PageManager`, and
`BTreeConcurrentStore` delegates - a separate interface rather than a member of `IKeyValueStore`,
because most stores have no header to answer from. `WitDatabaseBuilder.ValidateStoredConfiguration`
compares it against the configuration now asking to open and throws `ConfigurationMismatchException`,
a type that already existed for this and was called by nothing.

**What is compared is the layout, not the keywords.** The MVCC store writes every value under a
versioned key and nothing else does, so the question is whether that layer is present on both sides:
transactions on *and* MVCC on. The grid measures that `MVCC=false` and `Transactions=false` read each
other's databases correctly, so refusing that pair would refuse something that works. A file whose
metadata section was never written - an empty store provider key - is left alone: this exists to stop a
wrong answer, not to make old databases unopenable.

**The refusal must also let go of the file**, which is why the probe asserts that the creator can still
read its rows afterwards. `Build` now disposes the store on any failure between construction and
handing it to `WitDatabase`, rather than only releasing the database lock - the fourth time this phase
has met "something is constructed, the build then fails, nothing disposes what was built".

**Two collateral findings, both from the same red run:**

- **Three ADO tests had been reusing a database created in December 2025.** `ChangeDatabase*` opened
  `Data Source=mydb.witdb` - a relative path, so the file landed in the test runner's working directory
  and was never deleted. It was written without MVCC, so every run since had been opening a non-MVCC
  database under the MVCC default and seeing nothing; the refusal is what made that visible. They get a
  database of their own now.
- **The crash suite's attribution probe opened a database *underneath* the MVCC layer through
  `WitDatabaseBuilder`**, which is exactly what is now refused. It asks a storage-layer question, so it
  opens `StoreBTree` directly - which is what "underneath the MVCC layer" always meant.

## 6c. Instrument D - the combinations crossed with two connections

`TwoConnectionMatrixTests`, 24 combinations x 2 tests plus a control: the store x transaction model x
encryption cross product and six add-ons, each run twice - once down a single connection and once with
two connections open over the same database at the same time.

The matrix is single-connection, and that is not the shape this engine is designed for: the model is
*one process, one engine per database, many connections*, because the target is ASP.NET Core. 5.0.0
built that shape, and **its defects were configuration-shaped** - a table created by one connection was
`Table not found` to another - but it was never crossed with the configurations.

**The workload asks the two questions in the order that matters:** the second connection reads what the
first wrote *before* it opened, and the first reads what the second wrote *after* it opened. The second
is the state that went stale in phase 5, where eleven tests passed because they all populated their
table before the second connection existed. It also crosses schema with connections (a table created by
one is written to by the other), and checks that closing one connection leaves the other with a working
database.

**Controls both ways.** The single-connection run is the control that separates "this combination
cannot do the work" from "this combination cannot do it twice at once". And two connections to two
*different* databases must share nothing - without that, "the second connection sees the first's rows"
is an assertion no run could fail.

**All 49 cases pass.** Two connections work under every store, every transaction model, with and without
encryption, and with each add-on. Worth stating for one of them: `Store=inmemory` with a file
`Data Source` **is** shared between connections in the same process - it is keyed by the path like any
other - and is still not persistent.

## 6d. Instrument E - durability crossed with configuration

`DurabilityByConfigurationTests`, `Category=Crash`, nine configurations x two runs, through the
out-of-process crash runner phase 4 built. The runner takes `--settings` now, so a scenario can be
played under any connection string; the two new ones write the rows the strongest way the configuration
allows and report **which** way, so a run that fell back to autocommit cannot be read as a commit that
held.

The thirteen crash tests this project had all ran one configuration - a bare `Data Source=` - and
durability is precisely what a configuration decides.

| configuration | clean close | after a kill | |
|---|---|---|---|
| default (MVCC) | 20/20 | **20/20** | asserted |
| `MVCC=false` | 20/20 | **20/20** | asserted |
| `MVCC=false;Journal=wal` | 20/20 | **20/20** | asserted |
| `MVCC=false;Journal=rollback` | 20/20 | **20/20** | asserted |
| `Store=lsm` | 20/20 | **20/20** | asserted |
| `Store=lsm;MVCC=false` | 20/20 | **20/20** | asserted |
| encrypted (`aes-gcm`) | 20/20 | **20/20** | asserted |
| `Synchronous Commit=false` | 20/20 | **0/20** | recorded |
| `Transactions=false` | 20/20 | **0/20** | recorded |

**Every configuration that promises a durable commit keeps one**, and the row count agrees with the
rows in each case - which is the pairing phase 4 learned to check separately.

**The two zeros are the instrument's control in the other direction.** They are not defects: both
configurations disclaim the promise, and a probe that reported survival everywhere would be a probe that
could not see loss. What they add to the record is how *complete* the loss is - not "the last commit is
missing" but nothing at all, including the table: after the kill the reopened database reports `T` not
found. `Transactions=false` is worth saying plainly, because phase 4's "autocommit is durable" is a
statement about the implicit per-statement **transaction**, and with the transaction layer switched off
there is nothing to make durable.

## 6b. Still unexplored

- **`Core.BouncyCastle` and `Core.IndexedDb` have not been touched at all** - their provider
  registrations appear in no census and no matrix.
- **Extensibility itself**: nothing registers a third-party `IStorage`/`IKeyValueStore`/`ICryptoProvider`
  and drives a database through it, which is the construction kit's central claim.
- ~~**The matrix is single-connection.**~~ **Done, § 6c.**
- ~~**Durability has never been crossed with configuration.**~~ **Done, § 6d.**
- ~~**"Works" means "works on eight rows"**~~ **Done, § 7a.5** - 2,000 rows through five configurations,
  with the page splits, overflow chains and compactions measured off the files rather than assumed.
- The five ADO-level keywords the census cannot see structurally: `Enlist`, `Connection Timeout`,
  `Pooling`, `Min`/`Max Pool Size`, `Default Timeout`.

## 7a. After 12.0.0 - the follow-ups, 2026-08-03

The release went out with three items open in § 6.5 and § 6b. Two are now closed and the third turned
into two findings that are handed forward with measurements rather than guesses.

### 7a.1 § 6.5 - the two build routes now agree, and the defect was wider than recorded

`SyncAndAsyncBuildAgreeTests` builds the same configuration with `Build()` and with `BuildAsync()` and
compares a **structural signature** - the runtime types of every store, page cache and storage reachable
in the built graph. Controls both ways: the signature must tell a B+Tree database from an in-memory one,
and one route must agree with itself across two builds.

**Three of seven configurations disagreed**, not the one the plan recorded:

| configuration | `Build()` | `BuildAsync()` |
|---|---|---|
| `Store=inmemory`, file data source | `StoreInMemory` | `StoreBTree` over `StorageFile` - **and it opens the file** |
| a third-party registered store | the registered store | `StoreBTree` |
| `Cache=lru` | `PageCacheLru` | `PageCacheShardedClock` |

So the asynchronous route ignored the **cache** as well as the store - the same defect `Cache=lru` had
on the synchronous route until 12.0.0, one method along.

**The fix:** everything that is not the built-in B+Tree store is built where the synchronous route
builds it, in the provider registry. The B+Tree store keeps a route of its own for one reason - its page
manager reads the header while it is constructed, which a storage that can only work asynchronously
cannot serve - and it now reads its parameters from the same bag the registry factory reads.
`StoreBTree.CreateAsync` gained the overload that takes an `IPageCache`, the asynchronous twin of the
constructor 12.0.0 added. Nine of nine agree; Core's 2,251 tests are unchanged.

### 7a.2 § 6b - extensibility, executed

`ThirdPartyProviderTests`. A third-party `IKeyValueStore`, `IPageCache` and `ICryptoProvider`, each
registered in the provider registry and named in a **connection string**, plus a third-party `IStorage`
handed to the builder - each driving a real database through SQL.

**The control is inside every probe and it is a counter.** Each provider counts the calls it receives
and every test asserts the count is not zero, because "registered and then ignored" is the failure this
phase kept finding and a test that only checked the rows would pass through it. Measured: store 74
writes, cache 447 page requests, crypto 11 encryptions with `row1` absent from the file, storage 11 page
writes. And in the other direction, a provider key registered nowhere is refused at `Open`.

### 7a.3 `Core.BouncyCastle` - the package works, and a connection string alone cannot reach it

`Encryption=chacha20-poly1305` is refused with *"provider is not registered. Available: aes-gcm"* when
the only thing pointing at the package is a reference. The registration hangs off a `[ModuleInitializer]`,
and the CLR runs one when the assembly is **loaded** - which it is not, until something touches a type
in it. The package's own README documents `WithBouncyCastleEncryption(...)`, an extension method on a
type in the assembly, so the documented route loads it as a side effect and works; a consumer who writes
connection strings has no such side effect.

The refusal is legible, so the phase's rule holds. What was wrong is the documentation, and it now says
to call `BouncyCastleProviderRegistration.EnsureRegistered()` once at startup. With that,
ChaCha20-Poly1305 passes everything AES does: the workload, a reopen, no plaintext in the file, and a
refusal on the wrong password and on none.

### 7a.4 `Core.IndexedDb` - the browser story is one statement wide, and it is pinned

The package cannot run on a build machine - `StorageIndexedDb` talks to JavaScript. What it **rests on**
can: a stand-in `IAsyncOnlyStorage` whose every synchronous member throws. Measured, and stable over
three whole-fixture runs:

- the build is asynchronous throughout - the storage is initialised and written asynchronously;
- `CREATE TABLE` survives, and it survives because it writes **nothing**: the storage's write count is
  1 before it and 1 after;
- the **first `INSERT` throws**. Its implicit per-statement transaction commits, the commit flushes, and
  `PageManager.Flush` writes the header through the synchronous `IStorage.WritePage` before calling the
  synchronous `IStorage.Flush`;
- every close ends in the same place, and there is no asynchronous way round it.

**The close half of this is now BUILT - see § 7a.6.** What remains is the write path, and it is a
larger thing than a chain of disposals: the engine has no asynchronous execution at all.
`WitSqlEngine` offers `Execute` and `Query` and nothing else, and the ADO layer's
`ExecuteNonQueryAsync` is `Task.Run` around the synchronous one - a thread-pool hop, which in a
single-threaded browser is worse than useless. Until an asynchronous statement path exists down to
`Transaction.CommitAsync`, a write cannot avoid the synchronous flush its commit performs.

**The chain had five missing links when this was written, and seven when it was built:**
`PageManager` has `FlushAsync` and no `DisposeAsync`, and its flush writes the header synchronously;
`StoreBTree.DisposeAsync` calls that synchronous `Dispose`, under a comment claiming it is safe;
`BTreeConcurrentStore` - which since 12.0.0 wraps **every** B+Tree store - implements no
`IAsyncDisposable`, so an asynchronous disposal degrades at that link; nor does `MvccTransactionalStore`,
the default transaction model; and `WitSqlEngine` is `IDisposable` only, so a consumer has no
asynchronous close to call.

**The instrument was wrong first, for the ninth time in this project.** Its first version closed the
database in a `finally`, so the exception from the close replaced the exception from the workload and
the run reported the *statement* failing where the truth was the *close*. Re-measured with the throwing
cleanup removed, the boundary is exactly where it is stated above. **A cleanup that can throw hides what
the test came to measure.**

### 7a.5 "Works" no longer means "works on eight rows"

`ScaleMatrixTests`. Five configurations, 2,000 rows in one transaction, a secondary index, and every
hundredth row carrying a 4,000-character payload - against an inline limit measured at **960 bytes**, so
those values cannot be anywhere but an overflow chain.

**The workload is not the assertion; the evidence is**, read off the files after the database is closed:

| configuration | 2,000 rows | 8 rows (control) |
|---|---|---|
| `btree` | **116 pages** | 2 |
| `btree` encrypted | 116 pages | 2 |
| `btree`, `MVCC=false` | 78 pages | 2 |
| `lsm` | 6 SSTables, highest id **14** | 1, id 0 |
| `lsm`, `MVCC=false` | 2 SSTables, highest id **3** | 1, id 0 |

A highest file id above the number of files means SSTables were written and then merged away, which is
what a compaction looks like from outside the store. The eight-row control is what makes those numbers
mean something: without it, "the file has many pages" is a statement about the engine's appetite rather
than about the workload.

**Everything answered correctly** - every row back after a reopen, the 4,000-character value byte for
byte, and a secondary index lookup at 2,000 rows. No defect at volume, which is worth stating plainly
after a phase in which every instrument found one.

### 7a.6 The asynchronous close, built - and what the revert test said about its test

The pinned half of § 7a.4 is closed: a database on a storage with **no synchronous operations at all**
can now be closed, through the engine and through the database, under both transaction models. Two
probes were red first and are green now.

**Seven links, two more than the pin had named** - the two extra were found by reading the layer below
rather than by following the stack:

| link | what it did |
|---|---|
| `PageCacheShardedClock` and its shard | `Dispose` flushed every dirty page through the synchronous `WritePage` |
| `PageCacheLru` | the same, in the other implementation - **the third time these two have shared a defect** |
| `PageManager` | no `DisposeAsync` at all; the synchronous one writes the header synchronously |
| `StoreBTree.DisposeAsync` | called that synchronous `Dispose`, under a comment claiming it was safe |
| `MvccKeyValueStore` | closed its inner store synchronously |
| `MvccTransactionalStore` | no `IAsyncDisposable` - and it is the **default** transaction model |
| `BTreeConcurrentStore` | no `IAsyncDisposable`, and since 12.0.0 it wraps **every** B+Tree store, so it broke the asynchronous close of every database |
| `WitSqlEngine` | `IDisposable` only, so a consumer had nothing asynchronous to call |

**A second way to close a database is a second way to lose one**, so `AsynchronousCloseTests` asks
whether the rows come back afterwards, with the synchronous close as its control. Green, three models
plus one, at both the engine and the database level.

**Then its power was measured, and the answer was not the expected one.** The flush was removed from the
asynchronous close - the page manager's, then the engine's, then the page cache's, then the MVCC
store's, and finally all four together - and the fixture stayed **green every time**, including under
`Synchronous Commit=false`, which had been added on the assumption that it would leave the rows
unflushed until the close. It does not: the data is on the media before anything is closed, because
each statement runs in an implicit transaction, and the close path itself flushes in five places.

So the fixture verifies that the new close path does not **lose or corrupt** what was written - the real
risk of a second close path - and it is **not** a test of the flush. That is written into the fixture,
because a green test nobody has tried to break is a claim rather than evidence, and because the
assumption about `Synchronous Commit=false` was wrong: it defers durability against a **process kill**,
which instrument E measures at 0 of 20 rows, not the write itself.

### 7a.7 The planner's range estimate, measured - and the reason it is not fixed here

Phase 10 handed forward *"`RANGE_SELECTIVITY` is a constant"* with no measurement attached. It has one
now. `SelectivityEstimateTests` asks the optimizer what a predicate will return, runs the same predicate
through a real database of 1,000 rows holding the values 1..1000, and compares:

| predicate | estimated | actual | ratio |
|---|---|---|---|
| `Value > 999` | 200 | 1 | **200x too high** |
| `Value > 990` | 200 | 10 | 20x |
| `Value > 800` | 200 | 200 | **1.00** - the case the constant was written for |
| `Value > 500` | 200 | 500 | 0.40 |
| `Value > 0` | 200 | 1000 | **0.20 - five times too low** |
| `Value < 10` | 200 | 9 | 22x |
| `Value < 900` | 200 | 899 | 0.22 |

The controls hold in both directions: a unique-index equality is estimated exactly, and the 20% case
comes out at 1.00, so the harness is not reporting error everywhere.

**And the fix is a storage change rather than an optimizer one, which is the finding under the finding.**
Interpolating between the smallest and largest key in the index is the obvious repair, and that data is
not cheaply available: `ISecondaryIndex` has `GetFirstEntry` and `GetLastEntry`, and the B+Tree
implementation of the latter is `Scan(null, null).LastOrDefault()` - **a full scan**. Calling it per
query would reinstate precisely the defect 11.1.0 removed, where the planner scanned 1,000 rows per
execution and a unique-index seek was 97x slower for it. It is also a latent cost in a **public API**
for anyone who calls it today.

So the work is: a first/last key that descends the tree (a capability on the store, with a scanning
fallback for implementations that cannot do better), then an index-statistics input to the optimizer,
then interpolation. That is a decision about a public interface, so it is written down here with its
measurement rather than taken at the end of a session.

### 7a.8 The MVCC commit read the whole database to find what it had just written

Phase 11 measured, and did not chase, that four writers in transactions took **181 s** against
autocommit's **61 s** for the same 100,000 rows. A transaction being three times slower than no
transaction is the wrong way round, and it was the largest unexplained number the phase produced.

**The mechanism, found with one writer and no contention at all.** `CommitCostProbeTests` commits the
same ten rows against databases of growing size:

| rows already in the store | commit of 10 rows, before | after |
|---|---|---|
| 1,000 | 2.80 ms | 2.11 ms |
| 2,000 | 3.40 ms | 2.15 ms |
| 4,000 | 4.67 ms | 2.20 ms |
| 8,000 | 6.96 ms | 2.14 ms |
| **8x the data** | **2.5x the commit** | **1.0x** |

`MvccKeyValueStore.CommitTransaction` scanned every record in the store to find the versions the
transaction had just written - and `RollbackTransaction` did the same. So the cost of committing ten
rows was the cost of reading everything else, and a hundred commits over a growing store are quadratic.
The store remembers the versioned keys each transaction wrote now, and commit and rollback visit those.
The predicate is unchanged - a record still has to carry the transaction's own id - so only the
candidate set is smaller; a transaction id this store never saw, a record left by an earlier process,
still falls back to the scan, because for that there is nothing else to go on.

**Attributed end to end, both sides on this machine in the same minutes**, four writers x 25,000 rows
through a database:

| | autocommit | batches of 1,000 in a transaction |
|---|---|---|
| commit scans the store | 3.1 s | **50.8 s - 16.3x** |
| commit visits its own writes | 3.4 s | **7.2 s - 2.1x** |

**And the probe was wrong before its subject, for the second time in this session.** Its first version
handed a bare `StoreBTree` to the transactional store; four threads tore it apart inside a leaf split,
which is the correct answer to the question that version was actually asking - `StoreBTree` has no
locking and since 12.0.0 the builder wraps every one. It measures through a database now, as the
original measurement did.

**What is left is a different defect, pinned with its number.** Two times is still the wrong way round
for a batch of a thousand rows, and the flush is not the reason - 0.5 s of a 3.8 s difference, measured
by running the batches with `SynchronousCommit` off. The reason is structural: **a commit rewrites every
version a second time**, so a transactional write costs two writes to the store where an autocommitted
one costs a single write. Removing that means marking the transaction committed once and resolving
visibility through the transaction table on read - a design change rather than a patch, and the next
thing to decide in this area.

### 7a.9 The range estimate now comes from the data, and what that is and is not worth

§ 7a.7 measured the defect and handed the fix forward because it needed a first/last key that descends
the tree. That is built, and with it the estimate:

| predicate, 1,000 rows holding 1..1000 | before | after | actual |
|---|---|---|---|
| `Value > 999` | 200 | **1** | 1 |
| `Value > 990` | 200 | **10** | 10 |
| `Value > 800` | 200 | 200 | 200 |
| `Value > 500` | 200 | **500** | 500 |
| `Value > 0` | 200 | **1000** | 1000 |
| `Value < 10` | 200 | **9** | 9 |
| `Value < 900` | 200 | **899** | 899 |

**What was built, in three pieces.** `BTree` descends to its rightmost leaf as it always could to its
leftmost, and `StoreBTree` exposes both through a new `IKeyRangeSource` - a separate interface, like
`IProviderMetadataSource`, so a store that cannot answer cheaply is not made to pretend. The secondary
index uses it, which is where the second finding is: **`GetLastEntry` was a full scan of the index, and
the query planner already called it for `MIN`/`MAX` optimisation** - so `SELECT MAX(x)` on an indexed
column was advertised as an index operation and was O(n). Measured on 20,000 keys: **0.001 ms by descent
against 10.575 ms by scan, 7489x**. Then `IIndexRangeStatistics` carries "where does this value sit in
the index" to the optimizer, which interpolates on the encoded key bytes - the encoding is
order-preserving, so this needs no per-type ladder.

**Three qualifications, all measured, because the headline overstates it on its own.**

- **On skewed data the new estimate is worse than the constant.** 900 rows holding 1..900 and 100 around
  a million: `Value < 500` is estimated at **1** where the truth is 499. Linear interpolation between
  two keys does exactly that, and the case is in the fixture with its numbers. This is what a histogram
  fixes, and a histogram costs writes.
- **Today the estimate does not decide index against table scan at all.** A scan costs `rows x 1.0`, an
  index range costs `estimated x 0.5`, and the estimate can never exceed the row count - so the index
  wins even for a predicate that returns the whole table. Asserted, not reasoned: the whole-table range
  still chooses the index. The estimate ranks indexes against each other; an accurate one is a
  **precondition** for a cost model that could choose, not an improvement by itself.
- **The instrument was wrong twice on the way.** Its shuffle overflowed `int` and produced a negative
  index, so every case failed on the helper; and it called the optimizer **without** the statistics it
  had just been given, so the first "after" measurement was identical to the "before" one - measuring
  the path that had not changed.

## 7. Ledger

47 suppressed entries (33 `[Ignore(…)]` + 14 `Ignore =`) plus 2 `[Explicit]`, counted with the commands
on this branch. Unchanged so far by this phase.
