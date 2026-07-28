# Phase 3 — the grammar, all of it at once

Written 2026-07-28, before a single rule was changed. Starting point: `main` at `7e28dfa` (the
v2.4.0 merge), tree clean. Phases 0–2 closed, 2.1.0–2.4.0 published.

**Ledger, counted rather than trusted:** `grep -rho "\[Ignore" --include=*.cs Sources/ | wc -l` →
**74**, plus **3** `[Explicit]`. Matches the number carried forward from phase 2.

**Measured baseline, this machine, before any change:**

| Suite | Result |
|---|---|
| `OutWit.Database.Parser.Tests` (net10.0 only — the project is single-target) | **723 passed, 10 skipped, 733 total** |
| `OutWit.Database.Tests`, `Category!=Performance` (net9.0 and net10.0, identical) | **1904 passed, 28 skipped, 1932 total** |

> The acceptance criteria in [NEXT-SESSION-PLAN.md](NEXT-SESSION-PLAN.md) §A say "parser tests stay at
> 711 passing; engine tests at 1848". **Both numbers are stale** — the measured figures are **723** and
> **1904**. Corrected here rather than carried forward, and these are the numbers PR 2 must hold.

---

## 1. What the defect actually is

ANTLR eliminates left recursion by compiling each alternative's recursive references with a
precedence argument. A reference that is **leftmost** or **rightmost** in its alternative is bound to
the rule's own precedence, so it stops at the right place. A reference that is **interior** — neither
first nor last — is compiled as `expression(0)`: full precedence, consuming everything that follows.

`BETWEEN`'s lower bound is interior, and the token following it is `AND`, which is itself an operator
of the same rule. So:

```
Age BETWEEN 1 AND 10 AND Flag = 1
  →  Between(Age, lower = (1 AND 10), upper = (Flag = 1))
```

`WHERE Age BETWEEN 18 AND 65 AND Active = TRUE` returns nothing, silently.

**Why the `LIKE` fix does not generalise.** `LIKE` was repaired positionally: splitting the optional
`ESCAPE` block into its own alternative
([WitSqlParser.g4:496-503](../Sources/Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4#L496-L503))
moved the pattern into the trailing position, where ANTLR bounds it. `BETWEEN`'s `AND` keyword sits
**structurally** in the middle. No reordering can move the lower bound out of the interior position,
because the alternative has three operands and the middle one is defined to be in the middle.

This is why the fix has to be structural, and why the whole phase is gated behind it.

---

## 2. The restructure

Lift the boolean layer out of `expression` into three rules, the way the reference SQL grammars and
Presto/Trino do:

```antlr
searchCondition
    : searchCondition OR searchCondition
    | searchCondition AND searchCondition
    | NOT searchCondition
    | predicate
    ;

predicate
    : valueExpression comparisonOp valueExpression
    | valueExpression NOT? BETWEEN valueExpression AND valueExpression
    | valueExpression NOT? LIKE valueExpression (ESCAPE valueExpression)?
    | valueExpression NOT? GLOB valueExpression
    | valueExpression NOT? IN LPAREN (valueExpression (COMMA valueExpression)* | queryExpression) RPAREN
    | valueExpression IS NOT? NULL
    | valueExpression comparisonOp (ANY | SOME | ALL) LPAREN queryExpression RPAREN
    | NOT? EXISTS LPAREN queryExpression RPAREN
    | valueExpression
    ;

valueExpression
    : /* literals, columnRef, functionCall, parameter, arithmetic, bitwise, concat, collate,
         CASE, CAST, CONVERT, IIF, scalar subquery */
    | LPAREN searchCondition RPAREN
    ;
```

`BETWEEN`'s interior reference is now a `valueExpression`, which **cannot reach `AND`** — `AND` lives
one layer up. The precedence bug is removed structurally rather than worked around.

### 2.1 The change is far smaller than the plan assumed

[NEXT-SESSION-PLAN.md](NEXT-SESSION-PLAN.md) §A predicts that "every `WHERE`/`HAVING`/`ON`/`CHECK`/
partial-index reference changes from `expression` to `searchCondition`" — 23 sites in the grammar and
29 `.expression()` call sites in the visitor.

**It does not have to.** Keep a rule named `expression` as the entry point:

```antlr
expression : searchCondition ;
```

Then **all 23 grammar sites and all 29 visitor call sites keep compiling unchanged**, and every one of
them — `WHERE`, `HAVING`, join `ON`, `CHECK`, computed columns, trigger `WHEN`, partial-index `WHERE`,
`DEFAULT`, `MERGE … ON` — automatically gets the full boolean layer, because `expression` *is*
`searchCondition`. The diff collapses to: replace the body of one rule, add three rules, and rewrite
`VisitExpression` into three dispatchers.

Per the standing rule that a recorded conclusion is a claim to re-check, I am **calibrating this in
both directions**: the mechanical blast radius is smaller than recorded, but §2.4 introduces an
ambiguity risk the original estimate never mentioned, and that is the real hazard of this phase.

### 2.2 The AST does not change — this is what protects the suite

`Sources/Engine/OutWit.Database.Parser/Expressions/` holds one flat `WitSqlExpression` hierarchy with
**no boolean/value distinction**. So `VisitSearchCondition`, `VisitPredicate` and `VisitValueExpression`
all return `WitSqlExpression`, and the visitor's *output contract* is byte-identical.

Confirmed by measurement: **no production code outside `Visitor/` references an ANTLR parse-tree
context.** `grep -rln "WitSqlParser\." --include=*.cs Sources/` returns only `obj/` build output and
three test files whose matches are comment strings citing the `.g4` path.

So the engine, the statement executor, the ADO.NET layer and the EF Core provider are all downstream
of an unchanged contract, and the ~10,000 existing tests keep their full value as a regression net.
The visitor is rewritten; nothing it feeds is.

### 2.3 `LPAREN searchCondition RPAREN` is mandatory, not optional polish

`valueExpression` must be able to re-enter `searchCondition` through parentheses. Two independent
reasons, both load-bearing:

1. **The serializer parenthesises unconditionally.**
   [WitSqlExpressionSerializer.cs:154-162](../Sources/Engine/OutWit.Database.Parser/Serializers/WitSqlExpressionSerializer.cs#L154-L162)
   emits `({left} {op} {right})` for *every* binary node. So `a AND b` round-trips as `(a AND b)`, and
   `WHERE (a AND b) AND c` must still parse. Without the re-entry, every round-trip through the
   serializer stops parsing — and `SerialisedIdentifierReParsesTest` already pins round-tripping as a
   requirement.
2. **This dialect genuinely uses booleans as values.** There is a `BOOLEAN` type, `TRUE`/`FALSE`
   literals, `DEFAULT TRUE`, `SELECT a > b AS Cmp`. A strict two-sort grammar would reject working SQL.

Together with `predicate : valueExpression` (a bare value used as a condition), the two layers stay
mutually reachable. **That is deliberate**: it keeps the grammar exactly as permissive as it is today,
which is the property that makes the existing suite a valid regression net.

**The whole restructure must be behaviour-preserving except for `BETWEEN`.** Any other change in what
parses is a regression until proven otherwise.

### 2.4 The new risk: ambiguity

Mutual reachability means `(x)` is derivable two ways — as a parenthesised value and as a
parenthesised search condition that reduces to a value. ANTLR resolves by alternative order and will
still parse, but it may build a different tree shape, and an ambiguity that resolves silently today
can resolve differently after any later edit.

Mitigation, and it is executable rather than a promise: run the entire corpus of test SQL through the
parser with `DiagnosticErrorListener` and `PredictionMode.LL_EXACT_AMBIG_DETECTION`, and **fail the
test on any ambiguity report**. This lands in PR 1, *before* the grammar changes, so it also records
whether the current grammar is already ambiguous.

Second-order risk: deeper nesting costs parse time, and phase 5 will measure it. PR 1 records a parse
throughput number so the restructure's cost is attributable rather than discovered later.

### 2.5 Two cleanups the restructure earns

- **Collapse the `LIKE` split.** The two-alternative workaround from `fde365d` exists only because
  `LIKE`'s pattern was interior. Inside `predicate` it no longer is, so
  `LIKE valueExpression (ESCAPE valueExpression)?` becomes one alternative again. The grammar comment
  at [WitSqlParser.g4:496](../Sources/Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4#L496)
  says as much. **Per rule 3, that comment is a claim, not a closed case** — the five `LIKE`/`GLOB`
  precedence tests must be re-run red-to-green across the collapse, not assumed.
- **Delete the `CASE` counting heuristic.**
  [WitSqlVisitor.Expressions.cs:540-592](../Sources/Engine/OutWit.Database.Parser/Visitor/WitSqlVisitor.Expressions.cs#L540-L592)
  distinguishes simple from searched `CASE` by *counting* how many expressions the context holds.
  Layering makes the two forms structurally distinct:

  ```antlr
  | CASE valueExpression (WHEN valueExpression THEN expression)+ (ELSE expression)? END  # simpleCase
  | CASE (WHEN searchCondition THEN expression)+ (ELSE expression)? END                  # searchedCase
  ```

  The heuristic goes away, and with it a class of bug nobody has looked for.

---

## 3. The other six scope items — and an honest split

The phase was scoped as seven items. Three are grammar. Three are grammar plus engine work. One is a
new subsystem. Calling them all "grammar" would understate the last four.

| # | Item | What it actually costs | SQLite oracle says |
|---|---|---|---|
| 1 | `BETWEEN` precedence | grammar + visitor | supported |
| 2 | `INSERT … DEFAULT VALUES` | grammar (small) **+ executor**: insert a row from column defaults | **supported** |
| 3 | Hex literals | lexer + visitor | **supported** (`0x…`) |
| 4 | `VALUES` table source | grammar **+ executor** (materialise a row set) **+ derived column list** | partly — see §3.2 |
| 5 | `CROSS`/`OUTER APPLY` | grammar **+ lateral join execution** — re-evaluate the right side per left row | **not supported** — see §3.2 |
| 6 | User-defined functions | grammar + catalog + evaluator integration + persistence — **a feature** | **not supported** |
| 7 | Stored procedures | grammar + a procedural interpreter (variables, control flow, `CALL`) — **a subsystem** | **not supported** |

### 3.1 Three ignored test cases are asserting SQL-Server-isms

This is the phase-2 pattern repeating, and it is exactly what rule 1 exists to catch — nine of 29 EF
findings were misattributed, every correction coming from the oracle.

[DropInGapsEngineTests.cs:109-111](../Sources/Engine/OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests.cs#L109-L111)
holds three `[Ignore]`d cases:

```sql
SELECT * FROM A CROSS APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x
SELECT * FROM A OUTER APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x
SELECT * FROM (VALUES (1), (2)) AS V(Id)
```

Measured against the lexer: `TOP` and `APPLY` are **not tokens**, and `tableSource` has no derived
column list (`AS? alias` only, no `(Id)`). So making these three pass needs **four** features, not
one — and `TOP` is not in the phase's scope list at all, while `TOP` and `APPLY` are T-SQL, which
SQLite does not accept either.

**Before any of this is built, the oracle must settle it** (PR 0, §6): run the same three shapes on
SQLite and record the result. If SQLite rejects `TOP` and `APPLY` — as expected — then the tests as
written are asserting SQL Server syntax, and the finding needs restating before it is fixed. The
useful question is not "does the grammar parse `TOP`" but "does WitDatabase's own EF provider emit
these shapes".

### 3.2 …but the `APPLY` finding is probably real anyway, for a different reason

Measured: `WitQuerySqlGenerator` **inherits EF Core's `QuerySqlGenerator` and does not override
`VisitCrossApply`/`VisitOuterApply`** — it overrides only `VisitSqlBinary`, `VisitSqlUnary`,
`VisitOrdering`, `VisitCase` and `VisitCollate`. The base implementation emits the literal text
`CROSS APPLY`. Nothing in the provider searches for `APPLY` at all.

So whenever EF Core's query pipeline produces an apply node, WitDatabase emits SQL **its own parser
cannot read back**. That is self-inflicted and materially worse than a missing dialect feature.

**This is a prediction, not a finding.** It is settled by execution (rule 2): write the LINQ query
that forces an apply — a filtered/limited collection include, or a correlated `Take` — capture the
generated SQL, and feed it to `WitSql.Parse`. Two possible outcomes, and both are worth having:

- SQL contains `CROSS APPLY` and fails to parse → confirmed, and the fix has a choice: teach the
  grammar `APPLY` **and** lateral execution, or override the generator to emit a shape the engine
  already runs. The second is smaller and is what a provider without lateral support should do.
- The pipeline never produces an apply for this provider → the finding is restated, and three
  `[Ignore]`s come off as "not applicable" rather than being implemented.

### 3.3 Recommendation on items 6 and 7 — a decision for Dmitry

`CREATE FUNCTION` and `CREATE PROCEDURE` are documented in
[WitSQL.md](WitSQL.md) §22 and §23 with full syntax, and neither exists anywhere in the stack. The
verification tests
([DropInGapsEngineTests.cs:125-150](../Sources/Engine/OutWit.Database.Tests/AuditVerification/DropInGapsEngineTests.cs#L125-L150))
call `m_engine.Execute(...)`, so they demand **end-to-end execution**, not parsing.

Neither is grammar work, and neither is needed for drop-in parity — SQLite has neither, and EF Core
never emits either. Adding grammar alone would be actively worse than the status quo: a statement that
parses and then throws reads as a bug, whereas one that fails to parse reads as an unsupported
feature.

> **DECIDED 2026-07-28 (Dmitry): split out, and correct the doc now.** Phase 3 stays a grammar phase.
> `WitSQL.md` §22–23 get marked as not implemented in PR 8 so they stop generating findings, and UDFs
> and stored procedures are planned separately as the features they are. The two `[Ignore]`s are
> restated as "documented but unbuilt capability", not defects — the same shape as the JSON-columns
> item carried out of phase 2.

---

## 4. PR sequence

Each PR is a coherent piece, green CI before merge, per the standing workflow.

**PR 0 — oracle sweep. No production code.**
Run every phase-3 shape against EF Core's SQLite provider using the existing harness in
`Sources/Providers/OutWit.Database.EntityFramework.Specification.Tests/TestUtilities/Oracle/`, and
record what SQLite does with: `DEFAULT VALUES`, `0x1F`, `VALUES` as a table source, the derived column
list `AS V(Id)`, `TOP`, `CROSS APPLY`, `OUTER APPLY`. Plus the §3.2 execution check on WitDatabase's
own EF provider. **Output is a table of measured results that re-scopes PRs 4–7 before they are
written.** Rule 1 says this comes first, and phase 2 says it changes about one finding in three.

**PR 1 — the safety net, before any rule changes.**
- Ambiguity harness: whole corpus under `LL_EXACT_AMBIG_DETECTION`, zero reports required (§2.4).
- Round-trip corpus test: for every SQL string in the parser suite, `parse → serialize → re-parse` and
  compare ASTs. This is the instrument that proves "behaviour-preserving except `BETWEEN`".
- A recorded parse-throughput number, so §2.4's cost is attributable.
- Characterisation of current tree shapes for the shapes §2.5 will change.
Grammar untouched; CI green. If the ambiguity harness is already red on today's grammar, that is a
finding in its own right and gets reported before the restructure hides it.

**PR 2 — the restructure.** Three layers, the `expression : searchCondition` alias, the parenthesised
re-entry, the `LIKE` collapse, the `CASE` split, the visitor rewritten into three dispatchers.
Remove `[Ignore]` from `BetweenDoesNotSwallowTheFollowingConjunctTest` and
`NotBetweenDoesNotSwallowTheFollowingConjunctTest` — **in the same commit as the fix**, and only after
they have been run red on unfixed code.

Acceptance: all 12 `WitSqlEnginePrecedenceTests` green including the pins that must *not* change
(`GLOB`, `IN`, `AND`-tighter-than-`OR`, unary minus); parser at **723 passed / 10 skipped**; engine at
**1906 passed / 26 skipped** — the two `BETWEEN` tests live in `OutWit.Database.Tests`, so removing
their `[Ignore]` moves them from skipped to passed and the totals stay at 733 and 1932; round-trip
corpus green; zero ambiguity reports.

**PR 3 — the `BETWEEN` shapes nobody has executed.** From audit §4.2: `NOT BETWEEN … AND` with a
trailing `OR`, `BETWEEN` inside a `CASE`, `BETWEEN` with subquery bounds.

**PR 4 — `INSERT … DEFAULT VALUES`.** Grammar plus the executor path that builds a row from column
defaults. Closes `InsertDefaultValuesParsesTest`.

**PR 5 — hex literals.** Lexer rule plus visitor. **Note the behavioural change to pin**: `SELECT 0x1F`
currently parses as `0` aliased `x1F`
(`HexLiteralInASelectListCharacterisationTest` records exactly this). After the change it is the
integer 31. That is the intent, and it must be stated as a change rather than slipped in.

**PR 6 — `VALUES` table source**, scope set by PR 0, including the derived column list if it survives.

**PR 7 — `APPLY`**, scope set by PR 0 and §3.2. Per decision §6.2 this is a **generator fix, not a
grammar change**: override `VisitCrossApply`/`VisitOuterApply` to emit a shape the engine already
executes. The two ignored cases are restated to use `LIMIT` rather than `TOP` (§6.3).

**PR 8 — restate UDFs and procedures.** No implementation. `WitSQL.md` §22–23 marked as not
implemented, and `CreateFunctionIsSupportedTest`/`CreateProcedureIsSupportedTest` restated as
documented-but-unbuilt capability rather than defects (§3.3, §6.1).

**Checkpoint release: 3.0.0.** Major, not minor: `BETWEEN` changes answers previously returned —
queries that silently returned nothing will start returning rows. That is the loudest
consumer-visible change since 2.0.0, and it follows the 2.1.0 precedent of listing answer-changing
fixes as breaking.

---

## 5. The three standing rules, applied

1. **Oracle before fixing.** PR 0 exists for this and gates PRs 4–7. Concrete predictions on record,
   so they can be scored: SQLite accepts `DEFAULT VALUES` and `0x` hex; SQLite rejects `TOP`,
   `CROSS APPLY` and `OUTER APPLY`. If those hold, three ignored cases need restating, not implementing.
2. **Prove by execution.** No `[Ignore]` comes off without a recorded red run on unfixed code first.
   The round-trip and ambiguity harnesses in PR 1 exist so that "nothing else changed" is a measured
   claim rather than an assurance — this codebase has already changed nine behaviours without a test
   failing.
3. **Comments are claims.** Three in the blast radius, all to be re-verified rather than trusted: the
   `LIKE` split comment (§2.5), the `notExpr` placement comment at
   [WitSqlParser.g4:482-484](../Sources/Engine/OutWit.Database.Parser/Grammars/WitSqlParser.g4#L482-L484),
   and the `ParseIntegerLiteral` remark about promotion, which PR 5 touches.

---

## 6. Decisions taken, 2026-07-28

All three settled by Dmitry before implementation started.

1. **UDFs and stored procedures — split out of phase 3, and correct `WitSQL.md` now.** §3.3.
2. **`APPLY` — override the generator.** If PR 0 confirms the provider emits `CROSS`/`OUTER APPLY`
   that its own parser cannot read, the fix is to override `VisitCrossApply`/`VisitOuterApply` in
   `WitQuerySqlGenerator` to emit a shape the engine already executes, **not** to build lateral
   execution. A provider without lateral support should not generate lateral SQL. Grammar support for
   `APPLY` is then not required at all, and PR 7 shrinks accordingly.
3. **`TOP` — restate the tests to use `LIMIT`.** `TOP` is not added to the grammar. The two ignored
   cases were written in SQL Server syntax; restated against `LIMIT` they test the feature actually
   under examination. Scope is unchanged from the original phase-3 list.

## 7. PR 0 results — measured 2026-07-28

Harness:
[GrammarSyntaxOracle.cs](../Sources/Providers/OutWit.Database.EntityFramework.Specification.Tests/TestUtilities/Oracle/GrammarSyntaxOracle.cs),
`Category=Oracle`, 25 cases, green on net9.0 and net10.0. Characterisation only — it records, it does
not gate.

**All five predictions in §5 held.** SQLite accepts `DEFAULT VALUES` and `0x` hex; SQLite rejects
`TOP`, `CROSS APPLY` and `OUTER APPLY`.

### 7.1 The instrument was wrong first, and the controls caught it

The first sweep asked only "was the shape accepted", and reported **`SELECT 0x1F` as
"PARITY — both accept"**. That verdict is false. SQLite reads it as the integer **31**; WitDatabase
reads it as **`0` aliased `x1F`**. Both parse, and they return different answers.

The same blind spot hides the entire `BETWEEN` defect, which also parses on both engines. **An
acceptance oracle cannot see a wrong answer** — so a second theory was added that executes each
accepted shape against identical seeded data and compares the values.

That theory carries two `control` shapes with no known divergence, and on its first run **both went
red** — `SELECT COUNT(*)` returned `3` against `Integer:3`, because WitDatabase renders values as
`Type:value`. The harness was wrong, not the engine. Fixed, and both controls now agree; the controls
are why the three real divergences below can be trusted.

### 7.2 Acceptance

| Item | SQLite | WitDatabase | Verdict |
|---|---|---|---|
| `INSERT INTO G DEFAULT VALUES` | accepts | rejects | **real gap** |
| `SELECT * FROM T WHERE Flags & 0x0F = 1` | accepts | rejects | **real gap** |
| `SELECT * FROM (VALUES (1), (2))` | accepts | rejects | **real gap** |
| `SELECT * FROM (VALUES (1), (2)) AS V(Id)` | **rejects** | rejects | parity — inherited limit |
| `SELECT TOP 1 * FROM B` | **rejects** | rejects | parity — inherited limit |
| `CROSS APPLY` | **rejects** | rejects | parity — inherited limit |
| `OUTER APPLY` | **rejects** | rejects | parity — inherited limit |
| `CREATE FUNCTION` | **rejects** | rejects | parity — inherited limit |
| `CREATE PROCEDURE` | **rejects** | rejects | parity — inherited limit |

### 7.3 Agreement, for shapes both engines accept

| Item | SQLite | WitDatabase | Verdict |
|---|---|---|---|
| `Age BETWEEN 18 AND 65 AND Active = 1` | `1` | *(nothing)* | **DISAGREE** |
| `Age NOT BETWEEN 1 AND 20 AND Active = 0` | `3` | **`1,2,3`** | **DISAGREE** |
| `SELECT 0x1F` | `31` | `0` | **DISAGREE** |
| `Age NOT BETWEEN 18 AND 65 OR Active = 1` | `1,2` | `1,2` | agree |
| `CASE WHEN Age BETWEEN 1 AND 35 THEN …` | `1,1,0` | `1,1,0` | agree |
| `Age BETWEEN (SELECT MIN…) AND (SELECT MAX…)` | `1,2,3` | `1,2,3` | agree |
| control — `Age > 18 AND Active = 1` | `1` | `1` | agree |
| control — `COUNT(*)` | `3` | `3` | agree |

### 7.4 What this changes

**a. `NOT BETWEEN` returns *every* row — a defect nobody listed, and worse than the one on file.**
The recorded symptom is that `BETWEEN` returns nothing. Measured: `NOT BETWEEN 1 AND 20 AND Active = 0`
returns **all three rows** where SQLite returns one. Returning everything is far more dangerous than
returning nothing — this is the same shape as the `NOT LIKE` defect that "deleted every row in the
table". **A `DELETE … WHERE x NOT BETWEEN a AND b AND …` deletes rows it must not touch.** This goes
into PR 2's acceptance criteria as its own test.

**b. The defect is narrower and sharper than "the lower bound is interior".** It fires only when
`BETWEEN`'s upper bound is **followed by `AND`**. A trailing `OR`, a `CASE` terminated by `THEN`, and
parenthesised subquery bounds all parse correctly today, because each bounds the interior reference by
other means. The precise statement is: *`BETWEEN … AND …` followed by `AND` absorbs the following
conjunct.*

**c. PR 3 shrinks to regression pins.** [NEXT-SESSION-PLAN.md](NEXT-SESSION-PLAN.md) §A directs adding
the audit §4.2 shapes "that nobody has executed yet" — `NOT BETWEEN` with a trailing `OR`, `BETWEEN`
inside a `CASE`, `BETWEEN` with a subquery bound. **All three already agree with SQLite.** They are not
defects; they are shapes the restructure must not break. PR 3 becomes pins written *before* PR 2 lands,
which is more useful than the fixes it was scoped as.

**d. `APPLY`, `TOP` and the derived column list are not defects by the drop-in bar.** All three are
rejected by SQLite too. This confirms decisions §6.2 and §6.3 with measurement rather than
recollection: **PR 7 needs no grammar work at all** — it is a generator override. The `VALUES` finding
survives, but only in its bare form; `AS V(Id)` must come out of the restated test, because requiring it
would make WitDatabase stricter than nothing and looser than SQLite for no consumer's benefit.

**e. `CREATE FUNCTION`/`CREATE PROCEDURE` parity confirms §6.1** — neither is a drop-in requirement.

**f. The hex defect is understated on file too.** It is recorded as a parse failure. It is worse:
`SELECT 0x1F` **succeeds and returns the wrong number**. PR 5's test must assert the value `31`, not
merely that the statement parses.

### 7.5 Still to fill

- The parse-throughput baseline from PR 1.
- The §3.2 execution check: capture SQL from an EF query that forces an apply, and confirm whether
  `WitQuerySqlGenerator` emits `CROSS APPLY` its own parser cannot read. Acceptance parity above says
  the grammar need not learn `APPLY`; it does **not** settle whether the generator emits it.
