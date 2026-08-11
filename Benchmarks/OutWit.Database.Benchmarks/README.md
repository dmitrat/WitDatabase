# OutWit.Database.Benchmarks

The SQL engine measured against **SQLite** (`Microsoft.Data.Sqlite`) and **LiteDB**, across storage
engines and table sizes.

The published figures are in the [root README](../../README.md#performance). They are not repeated
here: two copies of a number drift, and this project has had to withdraw published performance claims
twice already.

## Run the control first

```bash
dotnet run -c Release -- verify
dotnet run -c Release -- verify Lsm     # or any WitDbEngineMode
```

`verify` runs **every benchmark body once** and compares what the three engines actually return. It
exits non-zero on a disagreement.

**This is not a formality, it is the precondition.** A timing comparison between engines that do not
compute the same thing is not a measurement, and until phase 10 there was not one assertion in 113
benchmark methods - nothing had ever checked that WitDatabase, SQLite and LiteDB agree. When the
check was finally written, it found that a LiteDB index-seek benchmark had been throwing and
reporting `NA` for months, in the class that measures the thing indexes exist for.

Read the three markers in its output:

| | |
|---|---|
| `ok` | all three engines returned the same value |
| `~` | the benchmark returns `void` - nothing to compare (writes; see below) |
| `?` | only one engine implements this shape, so there is nothing to compare it with |

Write benchmarks are checked differently, because affected-row counts can lie: the worst defect ever
found in this repository was `Store=lsm` reporting ten successful `INSERT`s with 0-1 rows actually
present. `WriteVerification` asks the engine what it claims **and** counts what a scan can see - never
`COUNT(*)`, which on this engine is a cached counter and is separate state.

## Running a sweep

```bash
# everything, whole matrix - hours
dotnet run -c Release

# one class
dotnet run -c Release -- --filter "*QueryBenchmarks*"

# the shape the published figures use: default configuration, smallest size of each class
WITDB_BENCH_MODES=Default WITDB_BENCH_SIZES=min \
  dotnet run -c Release -- --filter "*QueryBenchmarks*" --job short --inProcess
```

`WITDB_BENCH_MODES` and `WITDB_BENCH_SIZES` narrow the matrix without editing any class.
`WITDB_BENCH_SIZES` takes `min`, `max` or an explicit list (`1000,5000`). Unset means everything.

**Narrowing exists so that a sweep can be run twice.** The full matrix is five engine modes across
two or three sizes in seven classes - roughly 1,175 cases, hours per pass - which is what makes a
second pass unaffordable and a single pass tempting. **One timing run lies:** in the phase-10 record a
single pass reported a 4.13x regression that a second pass put at 0.83x, and it happened four separate
times. Every figure in the root README is the median of at least two passes, and anything that moved
more than 10% between identical passes was excluded rather than quoted.

If a narrowing matches nothing in a class, that class keeps its own values and says so loudly on
stderr. It used to throw, which killed a whole run over a class that was being filtered out anyway.

## The classes

| class | what it measures |
|---|---|
| `QueryBenchmarks` | full scan, `WHERE`, `ORDER BY`, `LIMIT`, point query by key, projection |
| `InsertBenchmarks` | `INSERT` in a transaction, without one, and `INSERT … RETURNING` |
| `UpdateBenchmarks` | by key, by indexed column, bulk, and `UPDATE … RETURNING` |
| `JoinBenchmarks` | `INNER JOIN` over 2, 3 and 4 tables, `LEFT JOIN`, `JOIN` with `GROUP BY` |
| `AggregateBenchmarks` | `COUNT`, `SUM`, `AVG`, `MIN`/`MAX`, `GROUP BY`, `HAVING` |
| `IndexBenchmarks` | unique and non-unique seeks, range scans, composite index, forced scan |
| `TransactionBenchmarks` | one transaction with N operations, mixed workload, rollback, savepoints |
| `IngestBenchmarks` | sustained batched ingest - the workload an LSM tree exists for |
| `LsmWriteAnatomyBenchmarks` | the LSM write path one property at a time: bare table, key only, +1 index, +3 indexes |
| `IndexSeekAnatomyBenchmarks` | the seek cost against table size, to separate a constant from a curve |

The last three are diagnostic rather than comparative: they exist to attribute a cost, and two of them
run WitDatabase against itself, which is the only comparison no caveat applies to.

## Engine modes

| mode | connection string |
|---|---|
| `Default` | `Data Source=…` - MVCC on, durable commit, B+Tree |
| `Memory` | `Mode=Memory` - no file, to split storage cost from everything above it |
| `BTree` | `Store=btree;Transactions=true;MVCC=false` |
| `Lsm` | `Store=lsm;Transactions=true;MVCC=false;SyncWrites=false` |
| `BTreeParallelAuto` / `LsmParallelAuto` | as above, with automatic parallel writes |

**`Default` is the one that describes a consumer.** It is what every ADO.NET and EF Core caller gets,
and for a long time six of the seven classes did not measure it - they all passed `MVCC=false`, a
configuration nobody runs. Publishing from a tuned mode is how a figure ends up describing a database
that does not exist.

**And there is still no such mode for the LSM store, which is a live gap.** `Default` is B+Tree;
`Lsm` and `LsmParallelAuto` both carry `MVCC=false;SyncWrites=false`. Measured 2026-08-11: with MVCC
on - the default - the LSM store costs **772 us per row against 36.8** for the B+Tree, so every LSM
figure this suite has ever produced describes a configuration a consumer does not get by saying
`Store=lsm`. Adding an `LsmDefault` mode is the first thing the next benchmark session should do.

## Reading a cross-engine number honestly

- **SQLite is native C behind a managed wrapper.** It pays a P/Invoke crossing per call, which is
  most of WitDatabase's margin on small operations and amortises to nothing on scans, where SQLite is
  several times faster. Neither half is an engine result on its own; both are what a .NET caller
  experiences, because there is no other way to reach SQLite from managed code.
- **LiteDB is the memory baseline**, being managed like WitDatabase - but it is a document store with
  no SQL to parse, so a query benchmark is not comparing equal work either.
- **Allocation claims do not generalise.** "WitDatabase allocates ~30% less than LiteDB" was published
  from one workload in one mode; in `Default` it allocates 27% *more* on the same shape and up to 33x
  more on an index seek. Per workload, always.

## Dependencies

BenchmarkDotNet 0.15.8, Microsoft.Data.Sqlite 10.0.10, LiteDB 5.0.21, and
`OutWit.Database.AdoNet` by project reference.

## Related

- [OutWit.Database.AdoNet.Benchmarks](../OutWit.Database.AdoNet.Benchmarks) - the ADO.NET layer
  itself: connections, commands, readers, prepared statements

Three sibling projects - `Comparison`, `Core.Tests` and `EntityFramework` benchmarks - are named in
older documents and in leftover `bin/obj` folders. They were **deleted**, not lost; they are in the
git history if the storage-layer or EF Core measurements are wanted back.
