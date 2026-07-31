# Phase 6 — the ADO.NET and EF Core contract

Working record. Phase 5 closed on 2026-07-31 and shipped as 6.0.0; this is the phase that follows it.

**The premise, from `NEXT-SESSION-PLAN.md`:** *where "the application must not notice" fails first — not
in the engine, but in the surface the application actually holds.* Every finding in this area shares one
shape: the provider works when you hold `WitDbConnection` and does not when you hold `DbConnection`,
which is what every framework built on the contract holds.

---

## 1. The area, counted

| Where | Markers |
|---|---|
| `AdoNet.Tests/AuditVerification/AdoNetFindingsTests.cs` | 3 |
| `AdoNet.Tests/AuditVerification/DropInGapsAdoNetTests.cs` | 4 |
| `AdoNet.Tests/AuditVerification/CrossCuttingAdoNetTests.cs` | 1 + **2** `TestCase` properties on `DbException` |
| `AdoNet.Tests/Connection/ReadOnlyConnectionTests.cs` | 1 — `Mode=ReadWrite` creates a database it was told to open |
| `AdoNet.Tests/AuditVerification/ConnectionPoolFindingTests.cs` | 1 — **unreachable, do not touch** |
| `EntityFramework.Tests/AuditVerification/EfTranslationFindingsTests.cs` | 1 |

Counted with the § 1 commands from the phase-5 plan, which are the only form that sees a marker on a
continuation line.

---

## 2. Instrument A — the contract census

The phase's method was already written down: *enumerate, member by member, what the base types promise,
and for each: overridden, shadowed, or absent.* `AdoNet.Tests/AuditVerification/ContractCensusProbeTests`
is that enumeration, by reflection, over the seven types a consumer can hold.

**It does not test behaviour, deliberately.** It asks what the *declaration* is, because the failure mode
this phase exists for is a declaration:

> A member declared `public void Save(string)` instead of `public override void Save(string)` passes
> every test written against the concrete type and throws `NotSupportedException` for a consumer holding
> the base type. Reflection sees the difference; a behavioural test only sees it if it remembered to hold
> the base type — and the 2026-07 audit's harness did not.

**The census as it stood on the first run:**

| Type | Overridden | **Shadowed** | Inherited |
|---|---|---|---|
| `WitDbConnection` | 14 | — | 7 |
| `WitDbCommand` | 12 | — | 1 |
| **`WitDbTransaction`** | 6 | **6** | 2 |
| `WitDbDataReader` | 40 | — | 9 |
| **`WitDbParameter`** | 10 | **2** | — |
| `WitDbParameterCollection` | 15 | — | 3 |
| `WitDbProviderFactory` | 8 | — | 6 |

**The instrument was wrong before its subject, as usual** — `DbDataReader` declares both `this[int]` and
`this[string]`, so looking a property up by name alone threw `AmbiguousMatchException`. Matching on the
index parameters as well fixed it. Eighth time in this project that the harness failed first.

---

## 3. What the census found

### 3.1 The audit had named three of six — `WitDbTransaction`

The recorded finding named `Save`, `Rollback(string)` and `Release(string)`. The census found **six**:
`SaveAsync`, `RollbackAsync(string, …)` and `ReleaseAsync(string, …)` were shadowed too, and nothing had
said so. `SupportsSavepoints` was left inherited, so it answered `false` while all six members existed
and worked.

**Fixed:** all six carry `override`, and `SupportsSavepoints` returns `true`.

**The consequence is not cosmetic.** EF Core asks `SupportsSavepoints` *before* using a savepoint to
retry a failed `SaveChanges`, so the provider was declining a recovery path it had fully implemented. The
three EF suites — 553, 3,146 and the provider's own — are green with savepoints now advertised, which is
the check that matters: advertising a capability makes EF start using it.

### 3.2 `WitDbParameter.Precision` and `Scale` — **in no audit**

Both are virtual on `DbParameter` and both were declared without `override`. Measured through the
contract:

```
set through DbParameter: Precision=5, Scale=2  ->  the provider sees Precision=0, Scale=0
```

`DbCommand.CreateParameter()` returns a `DbParameter`, so this is the ordinary path, not an exotic one.
The value is dropped silently. **Fixed**, and the probe's pin inverted to 5 and 2.

This one connects to an item carried since phase 1: *DDL never captures declared sizes*, so
`MaxLength`/`Precision`/`Scale` are null after `CREATE TABLE T (V DECIMAL(5,2))`. The parameter half is
now wired even though the DDL half is still open — worth knowing, because it means a fix in phase 7 will
not need this one repeating.

---

## 4. What is next in this phase

**The `Inherited` column is the rest of the work**, and it is not automatically defective — a provider is
not required to override everything, and most base implementations are correct. Three are already known
to be wrong here and each has a marker waiting:

- **`DbConnection.EnlistTransaction`** — inherited, and the base throws. An abandoned `TransactionScope`
  therefore leaves the write committed, which is the silent-data-loss half of this phase.
- **The requested isolation level is dropped** through ADO.NET — `WitSqlEngine.Execute` builds a fresh
  execution context per call, so the level left pending never reaches the transaction.
- **A reader keeps streaming after `Close()`** — `IsClosed` is False and rows keep arriving.

Plus `Mode=ReadWrite` silently creating a database it was told to open, handed over by phase 5, and the
two `DbException` `TestCase` markers.

**And one more of the same family, found while looking for a timeout knob:** the connection string
declares `Default Timeout` and **nothing reads it** — `WitDbConnectionStringBuilder.DefaultTimeout` has no
consumer anywhere in the provider. That is the shape phase 5 fixed twice already (`Read Only`, then
`Mode`): a keyword parsed and dropped. `DbConnection.ConnectionTimeout` is inherited too, so it reports
the base class's 15 seconds, which is not a number this provider means.

**Acceptance for the phase, unchanged:** no member reachable through a base type behaves differently from
the concrete one — `ProbeNoContractMemberIsShadowedTest` is that criterion as a single assertion, and it
is green as of § 3 — and `TransactionScope` either works or refuses at enlist time, never committing
silently outside the scope.

---

## 4a. Exclusivity, decided rather than inherited — and the restart window closed

Handed over from phase 5 § 3a-bis, and **decided by Dmitry 2026-07-31**: exclusivity is the *goal*, not a
limitation being tolerated. *"Это файловая база… если нужно будет обращение из разных мест, можно будет
сделать сервис-обёртку, к которому можно обращаться через API — с разными сессиями мы работать умеем."*

That is the right shape and worth recording as an architectural answer rather than a preference:
**multi-process access is a service boundary, not a storage feature.** The engine already does the hard
half — many connections and many sessions in one process, each with its own transaction, over one shared
engine — so a wrapper in front of it is a transport concern.

The comparison supports it. **LiteDB's default is the same model**: `Connection=Direct` opens the datafile
exclusively and no second process can open it; its `Shared` alternative closes the file between
operations and is the part with a long tail of locking bug reports. **SQLite** starts from the opposite
end — multi-process is the design centre — but offers exactly this as `PRAGMA locking_mode=EXCLUSIVE`
(which in WAL mode is also the only way to run without shared memory), and stops guaranteeing anything on
network filesystems, where locking primitives are unreliable enough to corrupt a database.

### The one operational cost, and it is fixed here

**The restart window.** A host restart overlaps the outgoing process with the incoming one. The guard made
exactly one attempt, so the incoming process died at startup with `DatabaseAlreadyOpenException` while the
outgoing one was still flushing. SQLite survives this through `busy_timeout`; LiteDB Direct does not.

`Build` now retries with backoff for **five seconds** by default (`WithOpenTimeout`, zero restores the
single attempt). Three tests, each measuring a different half:

| Test | Measured |
|---|---|
| A second engine opening while the first closes | **opens**, and reads what the first wrote |
| A database that stays open | refused after 1203 ms — the wait happened, and the limit still holds |
| `OpenTimeout = 0` | refused after 0 ms |

Reverting the wait turns **2** red.

**Found while wiring it, and fixed:** the waiting acquire path caught only `IOException`, while the
single-attempt path has always also caught `UnauthorizedAccessException` — which is how Unix reports a
denied advisory lock in some configurations. So on those configurations the retry loop would have escaped
instead of waiting. Same defect class as the rest of § 3a: the two platforms report the same refusal
differently.

### Still open, named rather than done

`FileLocking=false` still admits two engines on Linux (phase 5 § 3a-bis). Now that exclusivity is the
stated intent, that is a hole in the intent rather than a documented trade-off — keep it and warn, refuse
it for stores whose own files do not enforce exclusivity, or remove it. Not decided.

---

## 5. Found in passing, not investigated

**The same defect class one layer down, and the compiler has been reporting it all along.** Building the
solution emits `warning CS0114` for several `WitSqlVisitor` members — `VisitSetTransactionStatement`,
`VisitExplainStatement`, `VisitCteDefinition` and others hide the generated base visitor's methods rather
than overriding them. ANTLR dispatches through the base type, so a hidden visitor method is a method that
never runs.

Not investigated, not claimed as a defect: some may be unreachable, and the generated visitor's default
may be the intended behaviour. **Named here because the shape is exactly the one this phase is about**,
and because a compiler warning that nobody reads is the cheapest possible instrument going unused.
