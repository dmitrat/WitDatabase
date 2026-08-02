# Phase 9d — the routine subsystem: design note

Opened 2026-08-01. **Acceptance for 9d is this note, agreed before implementation** — the six
questions of `PHASE9-UNBUILT-CAPABILITY-PLAN.md` § 5, answered. Nothing here is built yet.

The requirement, in Dmitry's words, is to **embed functions and procedures within the design, not
bolt them on**. So the note opens with what the design actually is, measured rather than read.

---

## 0. The audit, by execution

Every number below was produced by running the engine at head (`c23b983`, tag `v10.0.0`), not by
reading it. The probes are archived outside the repository; each finding names what it measured.

| # | Question asked of the engine | Measured |
|---|---|---|
| M1 | Can a nested body read another table while the outer write lock is held? | **Yes** |
| M1b | Can it read the table being written, and does it see the uncommitted row? | **Yes to both** — an `AFTER INSERT` body counting the table reads `1` |
| M2 | DDL in a nested body, with the declaration refusal bypassed through the catalog API | `LockRecursionException` — **and table `Z` exists afterwards**, and is usable, while the outer row is rolled back |
| M3 | A chain of three triggers, each firing the next | **Works** |
| M3b | Self-recursion, bounded by a `WHEN` | 200 ✓ · 400 ✓ · **600 kills the host process with a stack overflow** |
| M4 | A subquery in a `CHECK`, a computed column, an index expression | **All three accepted and evaluated** — the `CHECK` refuses a violating row, the computed column reads `43` |
| M5 | A body that throws part-way | The whole statement rolls back — 1 row, not 2 |
| M6 | `CREATE FUNCTION` / `CREATE PROCEDURE` / `CALL` / `EXEC` / `DROP FUNCTION` | **All refused by the parser** |
| M8 | `INFORMATION_SCHEMA.ROUTINES` and `.PARAMETERS` at run time | `Unknown INFORMATION_SCHEMA view` — a clean refusal, not an empty result |
| M9 | A `COMMIT` inside a nested body, during a three-row insert whose last row fails | **No error at all, and 2 of 3 rows left behind** |
| M10 | The price of a subquery on the row path, interleaved and repeated | **+33 µs/row**, 1.06× a literal `CHECK` |
| P1/Q1/Q2 | Each DDL kind inside an explicit transaction | **All five throw, and all five keep the change** |
| P2 | The same nested DDL with transactions switched off | **Succeeds** |
| P3 | `SELECT MyFunc(2)`, in a `WHERE`, in `VALUES` | **All parse today.** Only `FROM MyFunc(2)` is refused |
| P4 | Thirteen names a routine grammar would want, as column names | **All thirteen work today** |
| P5/Q3 | A function that does not exist, named in a `CHECK` / computed column / index expression / view | **All four accepted at declaration.** `CHECK` and view throw at use; a **computed column silently yields NULL** |

### The one measurement that was wrong before its subject

M10's first version ran the literal `CHECK` first and the subquery `CHECK` second, and reported the
subquery as **cheaper — 0.67×**. That is an order effect, not a result. Interleaved over six rounds
with the first discarded, the answer is **1.06×**. Had the first number been believed, this note
would have argued that a subquery on the row path is free. *Thirteenth time an instrument here was
wrong before its subject.*

### What the measurements say that the records did not

The July audit already proposed a routine design (§ 6 items 9 and 10 of `AUDIT-2026-07.md`), and its
core is right and is adopted below. Three of its statements are now stale, and one of its
prerequisites is already paid:

- *"stored in `SchemaCatalog` as serialized SQL exactly like a computed column"* — **phase 8 replaced
  that.** The catalog stores trees; a rendering is produced on demand and is never asked a question.
- *"the body stored as original source text (not re-serialized)"* — **superseded.** A trigger body is
  a `IReadOnlyList<WitSqlStatement>` since 9.0.0.
- *"That interpreter must first be fixed — it splits the body on `';'`"* — **done.** There is nothing
  left to split.
- *"Prerequisite: fix the serializer's lossiness or persisted function bodies will corrupt the same
  way views do"* — **paid by phase 8.**

And one it did not have: **no bound on execution nesting**, measured at 400 good / 600 fatal.

---

## 1. What is a body

**A procedure's body is a statement list. A function's body is a single expression.** Two different
answers, and the split is the load-bearing decision of this note.

A procedure body is `IReadOnlyList<WitSqlStatement>`, exactly as `DefinitionTrigger.Statements` is
since 9.0.0. The measurements say this machinery works: a nested body reads (M1), reads what the
outer statement has not committed (M1b), chains three deep (M3), and rolls back with its caller
(M5). Phase 8's answer is the answer, and it is adopted because it was re-measured, not because it
was written down.

A function body is a `WitSqlExpression`. `CREATE FUNCTION F(N INT) RETURNS INT AS BEGIN RETURN N * 2;
END` is parsed for the expression `N * 2` and nothing else is stored. This is not a shortcut; it is
what makes question 5 answerable:

- Invoking a function becomes **substitution inside `ExpressionEvaluator`**, not re-entry into
  `StatementExecutor`. The engine is not re-entered per row — the expression tree is walked, which is
  what already happens for a computed column.
- It costs **no new statement union tag** for the body. A `RETURN` statement node would be one more
  permanent format commitment for a construct with exactly one legal position.
- The single reachable nondeterminism (a subquery in the body) becomes a property of the tree that
  can be decided at declaration time — see § 5.

The cost, stated: PostgreSQL's `LANGUAGE SQL` functions may be `SELECT`-bodied and may return a
table. That is refused here, and § 7 pins it.

---

## 2. Re-entrancy against the write lock

**The largest risk in 9d, and the measurements move it from a design problem to two named ones.**

### What actually holds the lock

[`WitSqlEngine.ExecuteAtomically`](../Sources/Engine/OutWit.Database/Engine/WitSqlEngine.cs#L357)
wraps every data-modifying statement in a transaction, and
[`TransactionalStore.BeginTransaction`](../Sources/Core/OutWit.Database.Core/Transactions/TransactionalStore.cs#L102)
takes the write lock for that transaction's whole lifetime. `DatabaseLock` refuses same-thread
re-entry by design. So a nested body runs **inside** the caller's transaction, and:

- nested **DML** is fine and is atomic with the caller — it goes through `m_currentTransaction` and
  takes no new lock (M1, M1b, M3, M5);
- nested **DDL** throws, because `SchemaCatalog` is built over the `TransactionalStore` itself and
  every schema write is an auto-commit `Put` that asks for the write lock again (M2, M12).

### The nested-DDL failure is not a routine problem, and it is already recorded

Measured today at head: `BEGIN TRANSACTION; CREATE TABLE Z (Id INT); COMMIT` throws
`LockRecursionException` **and leaves `Z` in the catalog, usable, permanently** — while telling the
caller the statement failed. `CREATE INDEX`, `CREATE VIEW`, `DROP TABLE`, `CREATE SEQUENCE` all do
the same; `ALTER TABLE` throws on the *read* lock instead. With transactions switched off the same
nested DDL succeeds (P2), which identifies the ambient transaction's write lock as the entire
mechanism — the nesting is incidental.

This is **`AUDIT-2026-07.md` finding 19 / § 4.8**, open since July, with the fix already diagnosed:
thread `ITransaction?` into `SaveSchema`/`SaveViews`/`SaveTriggers`/the sequence writers, and reload
the catalog on rollback. Re-verified rather than re-discovered — and the re-verification adds the
half the record did not state plainly: **the change survives the error**, so the caller is told "no"
about something that is permanently "yes".

**Consequence for 9d, and it is a design decision rather than a discovery:** a routine body may not
contain DDL, refused at declaration, exactly as a trigger body is. Not because DDL in a procedure is
wrong — PostgreSQL and SQL Server both allow it — but because the engine cannot do DDL inside *any*
transaction today. The day finding 19 is fixed, routines get DDL bodies for free. A marked test
records that, and turns green when it becomes true.

### Transaction control is worse than DDL, because it is silent

`COMMIT` in a nested body is refused by nothing. Measured (M9): a three-row `INSERT` whose third row
violates a key, with a body that commits, **left two rows behind and raised only the key violation**.
The body committed the statement's own transaction; the statement then continued outside any
transaction, and `ExecuteAtomically`'s rollback had nothing to roll back. This is precisely the class
`ExecuteAtomically` was written to close, re-opened from inside.

**A routine body may not contain transaction control.** Refused at declaration, and the refusal must
name the statement — the same rule as the trigger's, with a stronger reason: DDL fails loudly, this
does not fail at all.

### Nesting has no bound, and the failure is not catchable

Self-recursion through a trigger: **200 levels pass, 400 pass, 600 kills the host process with a
stack overflow.** `StackOverflowException` cannot be caught in .NET; the process dies. This is a
**live defect on the trigger path today**, independent of 9d, and it is the reason a routine
subsystem cannot be added without it: a procedure that calls itself is the ordinary way to write one.

**Design:** a nesting counter on `ContextExecution`, incremented on every nested execution — trigger
firing, procedure call, and any future construct that re-enters `StatementExecutor` — with a
configurable ceiling and a **catchable** `InvalidOperationException` naming the routine and the depth
when it is crossed. Default 32, which is SQL Server's nesting limit and a value PostgreSQL's
`max_stack_depth` approximates. The counter must live on the context, not on the executor: a nested
`Execute` today constructs nothing new, so the executor is the wrong place to hold depth.

This lands **first**, before any routine work, because it is a defect and not a feature. The revert
test for it writes itself: with the counter removed, the fixture kills the run.

### What does not need designing

Reads. A nested body reads other tables and the table being written, and sees the outer statement's
uncommitted rows (M1, M1b). That is the correct semantics and it already holds.

One thing to carry into implementation rather than assume: `AUDIT-2026-07.md` records that
`AcquireWriteLockAsync` never records the owning thread, so **the recursion guard is off for async
writers** — the same nested DDL becomes a 30-second timeout instead of an exception on the async ADO
path. Not measured in this pass. It does not change any decision here, and it must be measured before
the depth cap is called complete, because a depth cap that only works on the sync path is the kind of
instrument this project has been wrong with before.

---

## 3. What a routine may contain

Question 3 comes after question 2 because question 2 is what decides it.

**A function body:** one expression. No statements, no `SELECT`, no control flow, no recursion (§ 5).

**A procedure body:** `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, **DDL**, and `CALL` of another
procedure. **Agreed 2026-08-01**, after the audit fixes changed what the answer could be.

### The measurement that decided it

The first version of this section refused DDL, because DDL inside any transaction threw and kept the
change. § 9 closed that. So the question was re-asked by measurement rather than by argument — DDL
run from inside a body that is itself running **in a loop over rows**:

| | |
|---|---|
| DDL on an unrelated object (`CREATE TABLE Z`) | **Works.** Z created, the writing table intact |
| `ALTER TABLE T ADD COLUMN` from a trigger on `T` | **Works.** Both rows present |
| A multi-row `INSERT` whose DDL fails on the second row | **Fails loudly and rolls back cleanly** |
| `DROP TABLE T` from a trigger on `T` | **Reports success. T is gone**, with the row just written. Nothing raised |
| *Control:* the same `ALTER` between statements in a transaction | Works, and backfills the `DEFAULT` |

So the hazard is not "DDL in a routine". It is **DDL against the object a statement is currently
iterating** — and that situation exists only when the routine was reached from a trigger body. A
procedure invoked by `CALL` at the top level is a statement, not a row loop.

### The rule, in one sentence

**A procedure body may contain DML and DDL; a trigger body may not contain `CALL`.**

That puts the restriction exactly where the measurement puts the hazard. It needs no transitive
analysis over the call graph — which is the alternative, and which would have to re-run every time a
procedure is redefined — and it is one refusal at declaration, the same shape as the trigger's
existing one.

### Still refused, and why

| Refused | Why, measured |
|---|---|
| `BEGIN` / `COMMIT` / `ROLLBACK` / `SAVEPOINT`, in any body | Stopped by **nothing** at runtime: a nested `COMMIT` commits the firing statement's transaction, so the rest of it runs outside one. A three-row `INSERT` left two rows behind and raised only the key violation. DDL fails loudly; this does not fail |
| `CALL`, in a **trigger** body | The rule above |
| `CREATE`/`DROP FUNCTION`/`PROCEDURE`, in any body | Self-modification during execution |
| Control flow — `IF`, `WHILE`, `DECLARE`, `SET` | A **new class of failure with no answer here**: a `WHILE` is iteration, not nesting, so the depth cap of § 2 does not see it, and a loop that does not terminate holds its transaction's write lock while every other session waits for the lock timeout. Excluded until there is a guard for it, which is a piece of work of its own |
| `OUT` parameters, more than one result set | `WitDbDataReader.NextResult` is hard-coded `false` (verified at head) |

### And the trigger's own DDL refusal was re-justified rather than left standing

`RefuseNonDmlBody` refused DDL in a trigger body *because of the lock recursion this phase fixed*. A
guard resting on a repaired defect is a guard the next reader deletes, correctly, on the evidence in
front of them. Its reason is now the `DROP TABLE` measurement above, and that measurement is pinned
by `TriggerBodyFidelityTests.DdlInsideATriggerDestroysWhatTheStatementIsWritingTest` — reached
through the catalog API, because the declaration refusal is the only thing preventing it.

---

## 4. SQL-bodied only

No external code, no assembly loading, no CLR registration, no `LANGUAGE` other than SQL. This is not
a limitation to be lifted later; it is what a file database embedded in another process should refuse
on principle.

Concretely:

- `LANGUAGE SQL` is **accepted and is the only accepted value**. Any other language name is refused
  loudly at declaration. An accepted-and-ignored `LANGUAGE plpgsql` would be exactly the
  "accepted, not enforced" class phase 7 closed across the DDL surface.
- No `EXTERNAL NAME`, no `AS 'assembly.Type.Method'`, no dollar-quoted body containing a foreign
  language.
- PostgreSQL's dollar-quoting (`$$ … $$`) is a lexer construct the engine does not have (measured:
  `token recognition error at: '$$'`). It is **not** added. The corpus already fixes the spelling
  WitDatabase must accept, and it is the `BEGIN … ; END` form.

---

## 5. Determinism, and where a function may appear

### What the row path already does

A subquery in a `CHECK` and in a computed column is **already accepted and already evaluated per
row** (M4). So "an expression on the row path may reach the query machinery" is not a new exposure
this phase introduces — it is the state of the engine, and it costs **+33 µs/row** over a literal, or
1.06× an insert statement. Both framings belong: 6% of a statement is nothing, and 33 µs per row is a
great deal in a loop that does not pay a statement's overhead per row (a bulk path, an index build).

Because a function body is an expression (§ 1), calling one adds **no execution nesting at all** —
there is no `StatementExecutor` re-entry, so § 2's counter is not consumed and § 2's worst form does
not arise. That is the whole reason for the expression-body decision, and it is the answer to
question 5: a function may appear anywhere an expression may — select list, `WHERE`, `CHECK`,
computed column, `DEFAULT`, view — subject to the two rules below.

### Rule one: an index expression demands a deterministic function

An index expression containing a subquery is **accepted today** (M4, M11), and it is a nondeterminism
hole: the key is computed once at write time, and the expression's value can change afterwards
without the index knowing. **What was measured is the acceptance and the write, not a wrong answer**
— the probe changed the looked-up row after indexing and the query still answered from a scan, so
the divergence is available rather than demonstrated. Stated that way on purpose: the same
distinction is what `IsFiltered` turned into a silently wrong result once the optimiser believed it.

A function is **deterministic** when its body contains no subquery and no nondeterministic built-in
(`NOW`, `CURRENT_TIMESTAMP`, `RANDOM`, `NEWGUID`, `LAST_INSERT_ROWID`, `CHANGES`, …). Because the
body is a tree, this is decided at declaration and stored on the definition — **decided from the
tree, never from a rendering of it**, which is phase 8's standing rule.

A nondeterministic function in an index expression is refused. Whether to extend the same refusal to
the *existing* subquery-in-an-index-expression hole is a separate call: it is pre-existing, it is not
9d's, and closing it changes behaviour for schemas that already exist. **Recorded as a finding with a
marked test, not fixed inside 9d.**

### Rule two: a function named in schema must exist, and must keep existing

Measured (P5, Q3): a `CHECK`, a computed column, an index expression and a view **all accept a
function that does not exist**. At use time the `CHECK` and the view throw `NotSupportedException`,
and the computed column **silently returns NULL** — a wrong answer rather than an error, which is the
worse of the two failures and is recorded as a finding of its own.

So:

- a schema object naming an unknown function is **refused at declaration**;
- `DROP FUNCTION` is refused while a schema object depends on it (`RESTRICT`), with `CASCADE` not
  offered.

The reason is already on the books in the worst possible form: `RENAME COLUMN` and `DROP COLUMN`
leave expressions naming the old column, after which **the table cannot be written to at all**.
A dangling function reference is the same class, and it is cheaper to refuse the drop than to
discover the table is dead.

### Recursion

A function may not call itself, directly or through a cycle — refused at declaration by a walk over
the call graph. An expression-bodied function has no terminating construct, so recursion in one is
always unbounded, and unbounded here means the process dies (§ 2). Cheap to check, and it removes the
row-path stack overflow entirely rather than capping it.

---

## 6. Catalog and reporting

### Storage

`DefinitionFunction` and `DefinitionProcedure`, `[MemoryPackable]`, held and persisted exactly as
`DefinitionTrigger` is: a dictionary on `SchemaCatalog`, its own store key, a `SaveFunctions()` /
`SaveProcedures()` beside `SaveTriggers()`, loaded from `LoadSchema`.

**Two keys, not one.** A single mixed routine list would need a union tag of its own and would force
every consumer to discriminate; the two definitions have different bodies (an expression against a
statement list) and different rules, and nothing reads them together.

Both store trees and no text, per phase 8: the body is a `WitSqlExpression` or an
`IReadOnlyList<WitSqlStatement>`, and `ROUTINE_DEFINITION` is rendered on demand for
`INFORMATION_SCHEMA`. `CatalogCoherenceTests.NothingWritesTheLegacyTextFieldsTest` extends to cover
them, so the two-copy class cannot return through a new door.

### Union tags

Five statement types are appended to `WitSqlStatement`'s union, **26 through 30**, and never
renumbered — the file format depends on it, and `AstMemoryPackContractTests` pins the set
exhaustively and must be extended in the same commit:

| Tag | Type |
|---|---|
| 26 | `WitSqlStatementCreateFunction` |
| 27 | `WitSqlStatementDropFunction` |
| 28 | `WitSqlStatementCreateProcedure` |
| 29 | `WitSqlStatementDropProcedure` |
| 30 | `WitSqlStatementCall` |

No tag for the function body: it is a `WitSqlExpression` that already round-trips.

### INFORMATION_SCHEMA

`ROUTINES` and `PARAMETERS` are added to the planner's view switch, which today refuses them cleanly
(M8 — a clean refusal, so nothing is currently reading an empty result and believing it).

`ROUTINES`: `ROUTINE_CATALOG`, `ROUTINE_SCHEMA`, `ROUTINE_NAME`, `ROUTINE_TYPE`
(`FUNCTION`/`PROCEDURE`), `DATA_TYPE` (the return type, null for a procedure), `ROUTINE_BODY`
(`SQL`), `ROUTINE_DEFINITION` (rendered), `IS_DETERMINISTIC`, `SQL_DATA_ACCESS`.

`PARAMETERS`: `SPECIFIC_NAME`, `ORDINAL_POSITION`, `PARAMETER_MODE` (`IN` only — see § 7),
`PARAMETER_NAME`, `DATA_TYPE`.

`IS_DETERMINISTIC` answers **from the body tree**, never from `ROUTINE_DEFINITION`. `IsFiltered`
reading the rendered text is what made a partial index report itself as complete, and the optimiser
believed it.

---

## 7. The drop-in surface, and what is out of scope

### Grammar: less than expected

Measured (P3): **calling a scalar function needs no grammar change at all.** `SELECT MyFunc(2)`,
`WHERE MyFunc(Id) > 1` and `VALUES (MyFunc(1))` all parse today and reach the evaluator, which
refuses with `Function not supported: MYFUNC`. Only `FROM MyFunc(2)` — a table-valued function — is
refused, and that is out of scope.

New productions: `CREATE FUNCTION`, `DROP FUNCTION`, `CREATE PROCEDURE`, `DROP PROCEDURE`, and
`CALL`/`EXEC`/`EXECUTE`.

**The `TOP` lesson applies directly.** All thirteen names a routine grammar would want — `Function`,
`Procedure`, `Call`, `Returns`, `Return`, `Language`, `Body`, `Exec`, `Execute`, `Declare`, `Out`,
`Inout`, `Deterministic` — **are usable as column names today** (P4). Every new token goes into
`nonReservedKeyword`, and `KeywordAsIdentifierCorpusTests` is the net: it enumerates the generated
lexer's own vocabulary, so it catches a token added tomorrow without anyone remembering it.

The spellings that must be accepted are already fixed by the oracle corpus, which is what makes them
a measurement rather than a preference:

```
CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END
CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T; END
```

### ADO.NET: a procedure nobody can call is not a drop-in

`WitDbCommand.CommandType` **throws `NotSupportedException` for anything but `Text`** (measured at
head). `CommandType.StoredProcedure` is how every ADO.NET caller invokes one, so 9d includes
accepting it and translating `CommandText` (a routine name) plus the command's parameters into a
`CALL`. Without this the subsystem exists and no ordinary consumer can reach it.

`WitDbDataReader.NextResult` is hard-coded `false` at head (verified, not recalled). A procedure
returning **one** result set works within that; multiple result sets do not, and are out of scope.

### Out of scope, deliberately, each with a marked test

| Excluded | Why |
|---|---|
| Table-valued functions (`FROM F(2)`) | Grammar *and* planner work; the two targets' spellings diverge |
| Control flow in a body (`IF`, `WHILE`, `DECLARE`, `SET`) | That is a procedural language, not a routine subsystem |
| `OUT` / `INOUT` parameters | Needs a result protocol ADO does not have here |
| Multiple result sets | `NextResult` is `false` |
| Transaction control in a body | § 2 — silent statement tearing |
| DDL in a body | § 2 — blocked on audit finding 19 |
| Dollar-quoted bodies, `LANGUAGE plpgsql`, external code | § 4 |

`UnbuiltCapabilityCorpusTests` pins `CREATE FUNCTION` and `CREATE PROCEDURE` as absent today and will
fail the moment they work — by design. It inverts as each lands, and the excluded rows above get
their own pins so the boundary is measured rather than remembered.

---

## 8. Order of work

Dependencies, not value — the same rule phases 5–10 used.

1. ~~**The nesting depth cap.**~~ **Done** — see § 9. The async path was checked rather than
   assumed: the engine has no separate async execution path at all, so there was nothing for the
   limit to miss.
2. ~~**Catalog + `INFORMATION_SCHEMA.ROUTINES`/`PARAMETERS`**, with nothing to put in them yet.~~
   **Done.** `DefinitionFunction` holds an expression body, `DefinitionProcedure` a statement list,
   `DefinitionRoutineParameter` is shared; two store records rather than one, so neither needs a
   union tag and nothing has to discriminate. Both views answer from the definitions, and
   `IS_DETERMINISTIC` from the tree rather than from the rendering. The reopen tests were proved red
   by disabling the load, and the tree comparison was proved able to say no.
3. ~~**Grammar + AST + union tags 26–30**, with the keyword corpus as the net.~~ **Done.** Six new
   tokens, all six in `nonReservedKeyword`, and the keyword corpus stayed green — none of them took
   a name away. The contract test caught the five new union tags on its first run, which is what it
   is for. `UnbuiltCapabilityCorpusTests` failed as designed and now pins the honest half-built
   state: the syntax parses, execution is still refused.
4. ~~**Scalar functions** — declaration, determinism at declaration, evaluator substitution,
   dependency refusal on `DROP`.~~ **Done.** Invocation is substitution against a parameter row,
   which shadows the caller's row; determinism is decided from the body and folds in the functions
   it calls; a self-call, an unbound name, a foreign `LANGUAGE` and a duplicate parameter are all
   refused at declaration; and `DROP FUNCTION` is `RESTRICT` over the stored expressions rather than
   over a dependency list, because a list is a second copy of a fact.
5. ~~**Procedures** — statement-list body, the refusal set of § 3, `CALL`, depth-counted.~~ **Done.**
   Arguments bind through the evaluator's existing named-parameter fallback, so a body statement
   needs no new resolution path. The last statement's result is the call's result. Recursion is
   allowed and bounded at 32 - the opposite of a function, and for the reason that matters: every
   body statement passes `Execute`, which counts, while a function is evaluated inside an expression
   and never does. Two defects were found by the fixture's own tests, both recorded in the commit.
6. **`CommandType.StoredProcedure`** on the ADO surface.
7. **Corpus inversion** — `UnbuiltCapabilityCorpusTests` and the oracle report both move these two
   from absent to built.

The audit's estimate for the pair was **1 week** for functions and **2–3 weeks** for procedures; the
scope here is narrower than the one it priced (no `OUT` parameters, no multiple result sets), and
three of its prerequisites were paid by phase 8.

---

## 9. Findings this audit produced — all five fixed

Dmitry's call, taken after the note was read: **fix what the audit found first, then build the
subsystem.** All five are closed, each with a test that was red before the change, and none needed a
suppression marker — **the ledger is unchanged at 33 `[Ignore(…)]` + 14 = 47**, counted by command.

1. **No bound on execution nesting** — 600 levels of trigger self-recursion killed the process with
   an uncatchable stack overflow. Counted in `StatementExecutor.Execute`, the one door every nested
   statement passes through, on `ContextExecution`, which is what resets per submitted statement.
   Limit 32, not configurable. `ExecutionNestingFindingsTests`, whose 5000-level case **takes the
   whole run down when the limit is reverted** — verified, not asserted.
2. **A computed column that cannot be evaluated answered NULL** — three iterators each ended their
   per-row evaluation with a bare `catch` returning NULL. Now one shared evaluation that names the
   table and column and carries the cause. `ComputedColumnFailureFindingsTests`, five of seven red
   against the reverted code.
3. **An unresolved function name was accepted at declaration** in a `CHECK`, a computed column, a
   `DEFAULT` and an index expression. Refused now, by name, asked of the whole DDL statement rather
   than clause by clause. Views are deliberately out — stated in the code, not omitted.
4. **An index expression could contain a subquery**, and equally `RANDOM()` or `NOW()`. Refused at
   declaration by `ExpressionDeterminism`, which is the same predicate § 5 needs before a
   user-defined function may appear in an index key.
5. **DDL inside a transaction threw and kept the change** — `AUDIT-2026-07.md` finding 19, closed.
   Schema records go through the caller's open transaction, held **ambient and per execution flow**
   because the catalog is shared between sessions and MVCC lets two of them be in transactions at
   once; rollback reloads the whole catalog; the eleven DDL row scans go through `ScanStore`.

### And one the instruments found on their own

**`TOBOOLEAN` was in the grammar and not in the engine.** Every other `TO…` conversion works;
`SELECT TOBOOLEAN(1)` reached the evaluator and was refused. Found by `KnownFunctionCorpusTests` on
its first green run, because that corpus asks the question of **every function token the lexer
defines** rather than of the ones somebody thought to try. `CAST(1)` — the no-target-type form the
grammar also admits — is pinned as a deliberate exception with its reason.

That corpus also had to be proved before it could be believed, and it failed twice on the way:
first it found **zero** functions, because this grammar builds keywords from case-insensitive
fragments so the lexer records no literal for any of them; then it found **everything**, because
`functionName` admits `IDENTIFIER`, so `SELECT WS(1)` parses and the lexer's whitespace rule was
being reported as an unknown function. Its "found no function tokens at all" guard caught the first;
the second is the dialect oracle's mistake exactly — one probe measuring two things and the result
attributed to the wrong one.

### Suites

```
Engine 2119 / 0    Core 2278 / 0    Parser 774 / 0    AdoNet 788 / 0    EntityFramework 554 / 0
EF Specification: 1198 failed / 6937 passed - identical to this branch before the fixes, and to the
                  pre-existing figure on main
```

---

## 10. What needs a decision before implementation

1. **The function body is an expression, not a statement list.** This is the load-bearing choice: it
   keeps functions off the execution-nesting path entirely, at the cost of PostgreSQL's `SELECT`-bodied
   and table-returning forms. Agreed?
2. **A procedure is restricted to the same statements as a trigger, plus `CALL`.** Less than the plan
   expected, and § 2 is why. Accept, or hold 9d until audit finding 19 is fixed and procedures can
   have DDL bodies honestly?
3. ~~**The depth cap default of 32.**~~ Built at 32, deliberately not configurable — raising it
   trades a catchable error for the crash it exists to prevent.
4. ~~**Findings 2–4 — markers or fixes?**~~ **Fixed, all five**, on Dmitry's instruction. § 9 has the
   result.

**Both are now agreed, 2026-08-01, and § 1 and § 3 carry the answers:**

1. **A function's body is an expression**, a procedure's a statement list — taken on the balance of
   capability against complexity. It is the choice that keeps a function off the execution-nesting
   path entirely, and its cost is stated where it is made: PostgreSQL's `SELECT`-bodied and
   table-returning forms are out.
2. **A procedure body may contain DML and DDL; a trigger body may not contain `CALL`.** Decided
   against the row-loop measurement in § 3 rather than against the earlier argument, whose premise
   the audit fixes had removed.

One claim in the earlier version of this section was **written without measuring and was wrong**: it
said the ALTER family would still scan rows outside the transaction. Measured afterwards,
`RENAME TABLE`, `DROP COLUMN`, `ALTER COLUMN TYPE` and `ADD COLUMN` each keep a row inserted in the
same transaction — routing the eleven scans through `ScanStore` closed `AUDIT-2026-07.md` finding 35
as well as finding 19. Left on the record because it is the reason § 3 could be answered the way it
was.

**Nothing else blocks implementation.** The order of work in § 8 stands, starting at the catalog.
