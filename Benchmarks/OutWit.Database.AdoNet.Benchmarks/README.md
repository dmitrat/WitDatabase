# OutWit.Database.AdoNet.Benchmarks

The ADO.NET layer itself - `WitDbConnection`, `WitDbCommand`, `WitDbDataReader` - against
`Microsoft.Data.Sqlite` and LiteDB.

Where [OutWit.Database.Benchmarks](../OutWit.Database.Benchmarks) measures what the engine does with
a query, this measures what the provider costs on the way there: opening a connection, creating and
reusing a command, walking a reader.

## The classes

| class | what it measures |
|---|---|
| `ConnectionBenchmarks` | open and close once, 100 times, open + query + close, one connection reused for 100 queries |
| `CommandBenchmarks` | `ExecuteNonQuery`, `ExecuteScalar`, `ExecuteReader`, parameterised update |
| `DataReaderBenchmarks` | row iteration and typed getters at 100 / 1,000 / 5,000 rows |
| `PreparedStatementBenchmarks` | a prepared command reused against a fresh command per call, at 100 and 500 operations |

Every class takes the provider as a parameter (`WitDb`, `SQLite`, `LiteDB`), so each row of a report
is the same operation on the three of them.

## Running

```bash
cd Benchmarks/OutWit.Database.AdoNet.Benchmarks
dotnet run -c Release

dotnet run -c Release -- --filter "*ConnectionBenchmarks*"
dotnet run -c Release -- --job short --inProcess
```

Reports land in `BenchmarkDotNet.Artifacts/results/`.

## Two things to know before quoting a number from here

**There is no equivalence check in this project.** Its sibling has one - `dotnet run -- verify`, which
runs every benchmark body once and compares what the three engines return - and this one does not.
Nothing here asserts that the LiteDB row and the WitDatabase row did the same work, or any work: a
reader benchmark that iterates zero rows benchmarks as very fast. That gap is exactly what the sibling
suite was found to have in phase 10, where a LiteDB benchmark had been throwing and reporting `NA` for
months without anyone noticing. **Treat these figures as unverified until that control exists here
too.**

**One timing run lies.** Take every sweep at least twice and report the spread rather than an average;
in the phase-10 record a single pass reported a 4.13x regression that a second pass put at 0.83x.

## Reading a cross-engine number honestly

- **SQLite is native C behind a managed wrapper**, so it pays a P/Invoke crossing per call. On this
  layer specifically - open, prepare, step - that crossing is most of what is being measured, which
  cuts in WitDatabase's favour and is not an engine result.
- **LiteDB is the memory baseline**, being managed too - but it has no SQL to parse and no ADO.NET
  layer of its own, so "the same operation" is an approximation on every row.

## Dependencies

BenchmarkDotNet 0.15.8, Microsoft.Data.Sqlite 10.0.10, LiteDB 5.0.21, and
`OutWit.Database.AdoNet` by project reference.
