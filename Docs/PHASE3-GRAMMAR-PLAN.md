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

> ## Correction, 2026-07-28 — what the oracle is for
>
> **Several conclusions in this document used "SQLite rejects it too, therefore it is not a defect".
> That reasoning is wrong, and it is corrected here rather than left in place.**
>
> WitDatabase is not aiming to be a SQLite clone. **The target is a drop-in replacement for the large
> engines — PostgreSQL and SQL Server** — so WitSQL may legitimately accept *more* than SQLite, and
> may be more convenient or more correct where the two differ.
>
> SQLite remains the right oracle, but for **attribution**, not as a ceiling. It answers "is this
> WitDatabase getting something wrong, or is it a limit every file-backed engine shares" — which is
> what corrected nine of 29 findings in phase 2. It does **not** answer "should WitSQL support this".
>
> Concretely, three conclusions below are restated:
>
> - **`CROSS`/`OUTER APPLY`** — not "correctly rejected because SQLite rejects it". PostgreSQL has it
>   as `LATERAL`, SQL Server as `APPLY`. It is an **unbuilt capability**: it needs lateral execution.
>   The generator refusing it (§13) is right *because we lack the capability*, and is a stopgap.
> - **`VALUES … AS V(Id)`** — a derived column list is **standard SQL**, supported by PostgreSQL and
>   SQL Server. SQLite's rejection is SQLite's limitation and no reason to inherit it.
> - **UDFs and stored procedures** — §3.3 argued they are not required because SQLite lacks them.
>   Both large engines have them. Splitting them out of phase 3 still stands, but only because they
>   are subsystems rather than grammar — not because they are unwanted.
>
> What does **not** change: `BETWEEN`, the ambiguity work, `DEFAULT VALUES`, and hex literals. Those
> were fixed because the behaviour was wrong on its own terms, and SQLite was used to pin the
> expected values — a legitimate use, since on those points the large engines agree with it.
>
> ### The acceptance criterion this comes from
>
> **Swapping WitDatabase in must be invisible to the rest of the application.** The use cases are
> tests, demo deployments, and deployments where no real server can be installed — and in all three
> the calling code is not supposed to notice which engine answers.
>
> SQLite is old; this is a new-generation engine, so its feature set is a **floor, never the goal**.
> Ideally WitSQL offers everything, or the bulk of everything, PostgreSQL and SQL Server offer.
>
> The operative test for "is this in scope" is therefore not *"does SQLite have it"* but:
> **would application code written against PostgreSQL or SQL Server notice its absence?** If yes, it
> is core scope. By that test the items this phase deferred are roadmap items, not curiosities:
> lateral joins, a `VALUES` table source with a derived column list, `TOP`, user-defined functions,
> and stored procedures.
>
> Being *more* capable or more correct than SQLite — or than SQL Server — is fine. Being **stricter
> than the engine being substituted for** is the thing that breaks the illusion.
>
> **A gap this leaves open:** every conformance instrument in this repository compares against SQLite.
> Nothing yet measures WitSQL against PostgreSQL or SQL Server, which is where the real bar now sits.
> Recorded in §14 as the natural successor to phase 3's oracle work.

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
>
> **Corrected 2026-07-28:** the argument above that "neither is needed for drop-in parity — SQLite has
> neither" is **wrong**, and the correction at the top of this document explains why. PostgreSQL and
> SQL Server both have functions and procedures, and they are exactly the engines WitDatabase means to
> substitute for. The decision to split them out is unchanged, but the reason is that they are
> **subsystems rather than grammar** — a procedural interpreter, a catalog, evaluator integration —
> not that they are unwanted.

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
   `WitQuerySqlGenerator` rather than to build lateral execution now. A provider without lateral
   support should not generate lateral SQL.

   > **Corrected 2026-07-28.** The intended override was "emit a shape the engine already executes".
   > There is no such shape: `APPLY` is a lateral join and no general rewrite preserves it, so the
   > override **refuses** instead (§13.2). And the follow-on claim that "grammar support for `APPLY`
   > is then not required at all" holds only for now — SQL Server spells this `APPLY`, PostgreSQL
   > spells it `LATERAL`, and both are engines WitDatabase means to replace. The refusal is a
   > stopgap until lateral execution exists.
3. **`TOP` — restate the tests to use `LIMIT`.** `TOP` is not added in phase 3.

   > **Corrected 2026-07-28.** The stated reason — "SQLite rejects `TOP` too" — is not a reason.
   > `TOP` is T-SQL's row limiter and a candidate for SQL Server source compatibility. It stays out
   > of phase 3 on **scope**, not because it is unwanted; `LIMIT` already covers the capability.

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

**d. `APPLY`, `TOP` and the derived column list are all rejected by SQLite too** — so none of them is
a case of WitDatabase getting something wrong that SQLite gets right, and PR 7 needs no grammar work
*in this phase*.

> **Corrected 2026-07-28.** This paragraph originally concluded they "are not defects by the drop-in
> bar". That inverts what the oracle is for. SQLite's rejection tells us these are not *regressions*;
> it says nothing about whether WitSQL should support them. PostgreSQL and SQL Server support all
> three, and those are the engines WitDatabase substitutes for — so all three remain unbuilt
> capability, tracked in `LargeEngineTableSourceParsesTest`.

**e. `CREATE FUNCTION`/`CREATE PROCEDURE` parity confirms §6.1** — neither is a drop-in requirement.

**f. The hex defect is understated on file too.** It is recorded as a parse failure. It is worse:
`SELECT 0x1F` **succeeds and returns the wrong number**. PR 5's test must assert the value `31`, not
merely that the statement parses.

### 7.5 Still to fill

- The §3.2 execution check: capture SQL from an EF query that forces an apply, and confirm whether
  `WitQuerySqlGenerator` emits `CROSS APPLY` its own parser cannot read. Acceptance parity above says
  the grammar need not learn `APPLY`; it does **not** settle whether the generator emits it.

---

## 8. PR 1 results — the safety net, measured 2026-07-28

Four fixtures under
[`Parser.Tests/Grammar/`](../Sources/Engine/OutWit.Database.Parser.Tests/Grammar/), built against a
**193-entry corpus** organised by *where an expression can appear* rather than by feature — because
the rework re-points every one of those positions, and a position nobody listed is how a regression
gets through. All 23 grammar sites and all 30 expression alternatives are represented.

The generated ANTLR classes are deliberately `internal` (`MakeInternal.ps1` keeps the parse tree out
of the public API), so the harness reaches them through `InternalsVisibleTo` rather than by adding
public diagnostic surface.

### 8.1 The grammar is already ambiguous — 7 of 193 entries

Measured before any rule changed, which was the point of running it first.

**Six of the seven are the same shape: `BETWEEN … AND …` followed by `AND`.** The reported conflict
is over the text beginning at `BETWEEN`'s `AND` — literally the question of which `AND` belongs to
the `BETWEEN`. This localises the phase-3 defect **structurally, without executing a query.**

It agrees exactly with §7.3, which found it by running queries: shapes where `BETWEEN` is followed by
`OR`, `THEN`, `AS`, or end-of-clause are neither ambiguous nor wrong. **Two independent instruments,
same sharper conclusion** — the defect is `BETWEEN` followed by `AND`.

The seventh is unrelated and benign: `NOT EXISTS (…)` is derivable both as `existsExpr`'s own `NOT`
and as `notExpr` applied to `EXISTS`. Both mean the same thing, so nothing is wrong today — but it is
a real ambiguity resolved silently by alternative order, and the rework should remove it rather than
inherit it.

`AmbiguousCorpusEntriesMatchTheRecordedBaselineTest` pins the exact set, so a **new** ambiguity fails
the build the moment the layers become mutually reachable. `CorpusIsFreeOfAmbiguityTest` states the
target and carries the marker until PR 2.

### 8.2 A defect the net found before the rework started

**69 of 193 entries do not round-trip**, from two pre-existing causes — and the second is a real
defect nobody had recorded:

**`WitSqlExpressionSerializer` replaces every subquery with the literal text `SELECT ...`.** Not an
abbreviation in a log message: the ellipsis is emitted into the SQL. It affects scalar subqueries,
`EXISTS`, `IN (SELECT …)` and quantified comparisons.

That would be harmless if the serializer were a debugging aid. It is not — the DDL path **persists
schema through it**: `CHECK` conditions and computed columns (`Ddl.Tables.cs`), a partial index's
`WHERE` (`Ddl.Indexes.cs`), a trigger's `WHEN` (`Ddl.Triggers.cs`), and the **entire body of a view**
(`Ddl.Views.cs`).

Confirmed by execution, and **worse than "the subquery is lost"**:

- `CREATE VIEW BigOrders AS SELECT Id FROM Orders WHERE Total > (SELECT 150)` **succeeds**. Every
  `SELECT` against the view then throws `WitSqlParsingException: mismatched input '.'`, because
  `QueryPlanner.CreateViewIterator` re-parses the stored body at query time. The view is accepted,
  written to the schema, and permanently unusable.
- `INFORMATION_SCHEMA.INDEXES.FILTER_CONDITION` for a partial index filtered by a subquery reads
  `(CustomerId IN (SELECT ...))`.

Recorded in
[SerializerSubqueryFindingsTests.cs](../Sources/Engine/OutWit.Database.Tests/AuditVerification/SerializerSubqueryFindingsTests.cs),
both markers proven red on unfixed code first. **It is not a phase-3 defect and is not fixed here** —
phase 3 is the grammar. It is on the ledger with a test waiting.

> One of the two tests first failed for the *wrong* reason — `KeyNotFoundException: Column 'IndexName'
> not found`, because the real column is `INDEX_NAME` and the view is `FILTER_CONDITION`. A red test
> is not automatically a proven defect; it was corrected until it failed on the claim it makes.

The first cause is already on record: the serializer covers DML only, and raises
`NotSupportedException` for every DDL statement and for `MERGE`.

`EveryRoundTripFailureHasAKnownCauseTest` **classifies** rather than counts, so a new DML round-trip
failure still fails the build even though 69 entries are already failing. A bare count would go green
again if the rework broke something while an unrelated fix repaired something else.

### 8.3 Parse-throughput baseline

**104.2 µs per parse** (193 entries × 20 iterations = 3,860 parses in 402 ms, after warm-up, this
machine). Logged, never asserted: this suite already carries 17 wall-clock assertions that measure
machine load rather than the engine, and the recorded verdict is that they should have been
diagnostics. This one is written as one.

### 8.4 Suite counts after PR 1

| Suite | Before | After |
|---|---|---|
| `Parser.Tests` | 723 passed / 10 skipped / 733 | **727 / 12 / 739** |
| `Tests` (`Category!=Performance`) | 1904 / 28 / 1932 | **1905 / 30 / 1935** |

Ledger: **74 → 78**. Four genuine new markers — two phase-3 target states that go green in PR 2, and
the two serializer findings.

> **The ledger command over-counts.** `grep -rho "\[Ignore" --include=*.cs Sources/ | wc -l` also
> matches prose mentions of the marker inside doc comments, and misses `Ignore = "…"` properties on
> individual `[TestCase]`s. A comment added in PR 0 inflated it by one before being reworded. The
> count is still the right ledger for comparing across phases — but only if nobody writes the literal
> marker text in prose, so don't.

---

## 9. PR 2 results — the restructure, landed 2026-07-28

The grammar is now three layers. **`BETWEEN` is fixed**, and the whole solution is green on both
frameworks with no change to the AST, the engine, or any consumer.

### 9.1 What the final rule shape is, and why

```antlr
expression      : searchCondition ;                       // entry point, unchanged for 23 call sites

searchCondition : predicate | NOT searchCondition
                | searchCondition AND searchCondition
                | searchCondition OR searchCondition ;    // order IS precedence, high to low

predicate       : predicate <op> valueExpression | … ;    // left-recursive on the LEFT operand only

valueExpression : … | LPAREN expression RPAREN ;          // re-entry, so serializer output re-parses
```

The load-bearing detail is in `predicate`: it recurses on its **left** operand and takes
`valueExpression` for **every other** operand. The left reference is in first position, so ANTLR
bounds it and comparisons still chain. The other operands are references to a *different rule*, and
ANTLR's precedence machinery does not apply across rules — so they cannot derive `AND` at all, since
`AND` lives two layers up. That is what removes the defect, structurally.

### 9.2 Two mistakes the net caught, both of which would have shipped

Neither was found by reading the grammar. Both were found by tests, within minutes, and both are the
reason PR 1 existed.

**1. The boolean operators were ordered backwards.** ANTLR binds an *earlier* alternative of a
left-recursive rule more tightly. I wrote `OR` first, which silently made `a AND b OR c` mean
`a AND (b OR c)`. `AndBindsTighterThanOrTest` and `NotBindsTighterThanAndTest` — the pins that exist
to state what must *not* change — went red immediately. **The two `BETWEEN` tests passed the whole
time**, so a green `BETWEEN` proved nothing on its own.

**2. The first `predicate` was not left-recursive at all**, with every operand a `valueExpression`.
It read more cleanly, it removed the `BETWEEN` defect, and **the entire solution stayed green** —
14 projects, both frameworks, zero failures. It had also silently stopped accepting `a = 1 = 1` and
`a < 5 < 3`, which **SQLite accepts**. Only the oracle caught it.

> This is the sharpest example so far of why the oracle exists. A full-suite pass across ~10,000
> tests was not evidence: no test in this repository chains a comparison. The bar is parity with the
> provider WitDatabase substitutes for, and a provider **stricter** than SQLite is not a drop-in one.
> Pinned now by `ComparisonsStillChainLeftAssociativelyTest` and by three oracle shapes.

### 9.3 Results

| Instrument | Before | After |
|---|---|---|
| Ambiguous corpus entries | **7 of 193** | **0** |
| Round-trip clean / broken | 124 / 69 | **124 / 69** — unchanged |
| Parse throughput | 104.2 µs | **~53 µs** (52.6 / 53.9 / 56.4 / 58.6 over four runs) |
| Oracle: `BETWEEN` shapes | 2 DISAGREE | **all AGREE** |
| `WitSqlEnginePrecedenceTests` | 12 passed, 2 skipped | **16 passed, 0 skipped** |

All seven ambiguities are gone, including the benign `NOT EXISTS` one — `existsExpr` lost its
optional `NOT`, and the visitor folds `NOT EXISTS` back into `Exists(IsNot: true)` so the emitted AST
is byte-identical. That mattered: `ExpressionEvaluator.Subquery` reads `exists.IsNot` directly.

**Round-trip is unchanged at 124/69**, which is the point — the same 69 failures, from the same two
pre-existing causes, and not one new one. That is the measurement behind "the restructure changed
nothing else".

**The new grammar parses about twice as fast.** Unexpected, and reported with the caveat it deserves:
one machine, four runs, a 193-entry corpus. Full-context attempts and context sensitivities were zero
both before and after, so the gain is a simpler ATN rather than avoided backtracking. It is not a
claim about engine throughput — phase 5 measures that.

### 9.4 A defect fixed that was never on the list

`NotBetweenInADeleteRemovesOnlyTheMatchingRowsTest` pins the half of this defect nobody recorded: the
negated form returned **every** row, so `DELETE … WHERE x NOT BETWEEN a AND b AND …` removed exactly
the rows the `WHERE` clause was written to protect. Found by the PR 0 oracle sweep, not by the audit.

### 9.5 Cleanups the restructure earned

- **The `LIKE` split is collapsed.** `ESCAPE` is an optional operand again; the positional workaround
  from `fde365d` is gone. Re-verified rather than assumed — all five `LIKE`/`GLOB` precedence tests
  pass across the change, per the standing rule that a comment about a past fix is a claim.
- **The `CASE` counting heuristic is deleted.** Simple and searched `CASE` are separate grammar
  alternatives now, so the visitor no longer infers which form it has from
  `whenCount * 2 + (hasElse ? 1 : 0)`.
- **Prefix `NOT` is correct by construction**, not by hand-ordering within a flat rule.

### 9.6 Suite counts after PR 2

| Suite | After PR 1 | After PR 2 |
|---|---|---|
| `Parser.Tests` | 727 / 11 skipped / 738 | **727 / 11 / 738** |
| `Tests` (`Category!=Performance`) | 1905 / 30 / 1935 | **1909 / 28 / 1937** |

Whole solution under the CI filter: **green, 14 projects, both frameworks, zero failures**, including
3,142 and 3,146 EF specification conformance tests.

Ledger: **78 → 75.** Three markers removed — the two `BETWEEN` tests, and the ambiguity target state.
The round-trip fixpoint marker stays, correctly: the serializer's subquery defect is real and is not
phase 3's to fix. The two serializer findings stay open with it.

---

## 10. PR 3 results — the `BETWEEN` shapes, 2026-07-28

Scoped by §7.4c as **pins rather than fixes**: all three audit §4.2 shapes already agreed with SQLite
before anything was built. `WitSqlEngineBetweenShapesTests` holds them, executed rather than parsed,
plus the more useful set — the same shapes combined with the trailing `AND` that *was* broken, in
each position the split re-pointed: `WHERE`, `HAVING`, join `ON`, partial-index filter, `CHECK`
constraint, `UPDATE`, and inside `NOT`.

**Every expected value was taken from the oracle, not reasoned out.** Five composite shapes were added
to `GrammarSyntaxOracle` first and run against SQLite; the engine assertions then use those answers.

### 10.1 The oracle found another pre-existing defect

`HAVING COUNT(*) BETWEEN 1 AND 5` raises
`InvalidOperationException: COUNT(*) should be handled by aggregation iterator`. SQLite returns both
groups.

Isolated by narrowing:

| Shape | SQLite | WitDatabase |
|---|---|---|
| `HAVING COUNT(*) > 1` | `1` | `1` — agree |
| `HAVING COUNT(*) > 1 AND Active = 1` | `1` | `1` — agree |
| `HAVING COUNT(*) BETWEEN 1 AND 5` | `0,1` | **throws** |
| `HAVING COUNT(*) IN (1, 2)` | `0,1` | **throws** |

**`IN` failing is the giveaway** — `IN` was never part of the `BETWEEN` precedence problem, so this is
not a grammar defect. The aggregation iterator collects aggregates from comparison operands only, so
an aggregate inside `BETWEEN` or `IN` reaches the row-level evaluator, which refuses it.

**Confirmed pre-existing by execution**, not by argument: a worktree at parent commit `39d22e4` — the
commit before the grammar changed — fails both shapes identically. Recorded in
`HavingAggregateFindingsTests` with a passing control (`COUNT(*) > 1`) so the two findings cannot
quietly start describing something else. Not fixed here; phase 3 is the grammar.

`BetweenFollowedByAndInAHavingClauseTest` is kept and marked rather than deleted, so the `HAVING`
position stays represented and turns green when the aggregation defect is fixed.

> Worth noting how it was found: only because the oracle compares **answers**. Both shapes parse
> perfectly, so every acceptance-level check — including the whole 10,000-test suite — is blind to it.

### 10.2 Counts after PR 3

| Suite | After PR 2 | After PR 3 |
|---|---|---|
| `Tests` (`Category!=Performance`) | 1909 / 28 skipped / 1937 | **1921 / 31 / 1952** |

Twelve new passing pins, three new markers. Whole solution green under the CI filter, 14 projects,
both frameworks. Ledger **75 → 78** — the increase is honest: three defects that were already there,
now on the books with tests waiting.

---

## 11. PR 4 results — `INSERT … DEFAULT VALUES`, 2026-07-28

One of the three shapes the oracle showed SQLite accepting and WitDatabase rejecting. EF Core emits
it for an entity whose columns are all store-generated.

### 11.1 The executor needed no change at all

`DEFAULT VALUES` became an alternative of `insertStatement`, and the visitor turns it into a **single
empty value row**. That is the whole implementation, because
`BuildInsertRowWithAutoGenInfo` already:

1. seeds every column with its default, auto-increment value or `ROWVERSION`, then
2. applies the supplied values — in a loop bounded by the value count, so an empty row applies
   nothing, then
3. computes `STORED` computed columns and validates `NOT NULL`.

Representing the feature as data the existing path already handles, rather than as a new flag with a
new branch, means there is no second code path to keep correct. Grammar and visitor only.

### 11.2 One test passed before the fix, for the wrong reason

Six tests, run against unfixed `main` in a worktree first. **Five failed and one passed** —
`NotNullColumnWithoutADefaultStillRefusesTest`, asserting `Throws.Exception`. It passed because the
statement failed to **parse**, which is not the claim it makes.

Tightened to `Throws.Exception.With.Message.Contains("NOT NULL")`, after which all six fail on
unfixed code and all six pass on fixed code. This is the same trap recorded twice before in this
project — asserting that *something* went wrong rather than *what* — and a bare `Throws.Exception` on
a feature that does not parse yet will always find one.

### 11.3 Coverage

Six engine tests: the all-generated EF Core case including counter advance, declared string and
integer defaults, a parenthesised expression default, `NOT NULL` still refused, `RETURNING`, and a
row count. The parser-level marker in `ParserFindingsTests` is removed.

### 11.4 Counts after PR 4

| Suite | After PR 3 | After PR 4 |
|---|---|---|
| `Parser.Tests` | 727 / 11 skipped / 738 | **728 / 10 / 738** |
| `Tests` (`Category!=Performance`) | 1921 / 31 / 1952 | **1927 / 31 / 1958** |

Whole solution green under the CI filter. Ledger **78 → 77**.

Oracle divergences remaining: `hexLiteral` (PR 5), `valuesTableSource` (PR 6), and the three
`HAVING`-aggregate shapes recorded in §10.1 as pre-existing and out of phase-3 scope.

---

## 12. PR 5 results — hexadecimal literals, 2026-07-28

### 12.1 The finding understated the defect, again

Recorded as a **parse failure**. Only half true, and the wrong half. `SELECT 0x1F` did **not** fail:
the lexer split it into the integer `0` and the identifier `x1F`, so the statement **succeeded and
returned 0** under the alias `x1F`. Only `Flags & 0x0F` failed outright.

A silently wrong number is the dangerous half. This is the fourth finding in phase 3 whose recorded
symptom was milder than the measured behaviour — after `NOT BETWEEN` returning every row, the
serializer's view corruption, and `EnsureDeleted` in phase 2.

### 12.2 The semantics were measured, and the obvious choice was wrong

Nine hex shapes were run against SQLite **before** anything was implemented:

| Shape | SQLite |
|---|---|
| `0x0`, `0x1F`, `0xff`, `0XFF` | `0`, `31`, `255`, `255` — prefix and digits both case-insensitive |
| `0x0000000000000010` | `16` — leading zeros do not count against the width limit |
| `0x10 + 1`, `-0x10` | `17`, `-16` |
| `0x7FFFFFFFFFFFFFFF` | `9223372036854775807` |
| **`0xFFFFFFFFFFFFFFFF`** | **`-1`** |
| `0x1FFFFFFFFFFFFFFFF` | error: `hex literal too big` |

**The overflow row is the one that mattered.** This codebase widens an oversized *decimal* literal to
`DECIMAL` to preserve its value — `ParseIntegerLiteral` does exactly that, deliberately, and says so.
Following that precedent for hex would have produced `18446744073709551615` and disagreed with
SQLite. Hex reinterprets its 64 bits as signed, which is the point of writing a bit pattern in hex.
`ParseHexLiteral` is a separate method for that reason, and its remarks record why it must not mirror
its neighbour.

Past 64 bits SQLite raises rather than truncating, so this does too — truncating silently would be
the same class of defect the whole fix is about.

### 12.3 The instrument had a false-positive verdict, and it is fixed

The agreement theory reported `0x1FFFFFFFFFFFFFFFF` as DISAGREE once both engines rejected it,
because it compared error *strings* and the messages differ. Both refusing is **parity**. The verdict
logic now distinguishes "both reject" from "one failed and the other did not", so the oracle stops
manufacturing a finding out of matching behaviour.

### 12.4 Guards against the lexer change reaching too far

Adding `HEX_LITERAL` before `INTEGER_LITERAL` changes tokenisation, so three tests pin what must
*not* move: `SELECT 0 x1F` with a space is still an integer aliased `x1F`; a column genuinely named
`x1F` still resolves; and `X'DEADBEEF'` blob literals are untouched (a different rule — `X` followed
by a quote).

### 12.5 Counts after PR 5

| Suite | After PR 4 | After PR 5 |
|---|---|---|
| `Parser.Tests` | 728 / 10 skipped / 738 | **729 / 9 / 738** |
| `Tests` (`Category!=Performance`) | 1927 / 31 / 1958 | **1941 / 31 / 1972** |

Fourteen new engine tests, every expected value taken from the oracle. Whole solution green under the
CI filter. Ledger unchanged at **77** — the hex marker was a `TestCase`-level `Ignore =` property,
which the ledger command does not count either way (§8.4).

Oracle divergences remaining: **`valuesTableSource` only** (PR 6), plus the three `HAVING`-aggregate
shapes recorded as pre-existing and out of scope.

---

## 13. PR 6 results — `APPLY` refused, `VALUES` deferred, 2026-07-28

This PR was scoped as "`VALUES` table source". **The measurement inverted it**: `APPLY` turned out to
be the real defect and `VALUES` turned out not to be one.

### 13.1 The check §7.5 left open, finally run

`GeneratedSqlIsParseableTests` asks what the acceptance oracle cannot: **does WitDatabase's own EF
provider generate SQL its own parser can read?** Three LINQ shapes, SQL captured with
`ToQueryString()`, fed straight to `WitSql.Parse`.

| LINQ shape | Generated SQL | Parses? |
|---|---|---|
| `EF.Constant(ids).Contains(r.Id)` | `WHERE "r"."Id" IN (1, 2, 3)` | yes |
| `ids.Contains(r.Id)` | `WHERE "r"."Id" IN (@ids1, @ids2, @ids3)` | yes |
| correlated `Take(1)` | **`OUTER APPLY ( … ) AS "r1"`** | **no** |

**Both halves of §3.2's prediction are now settled by execution.**

- **`APPLY` is emitted, and it is self-inflicted.** `WitQuerySqlGenerator` inherits
  `VisitCrossApply`/`VisitOuterApply` from EF Core and never overrode them, so the provider produced
  SQL its own engine rejects. The model builds cleanly and the query dies at execution with a syntax
  error naming a construct the caller never wrote.
- **`VALUES` is *not* emitted.** Collections translate to `IN (…)`, inlined or parameterised. The
  audit's claim that EF Core emits `VALUES` for inlined lists **does not hold for this provider**.

### 13.2 The oracle supplied the fix, not just the diagnosis

Before choosing a replacement, EF Core's SQLite provider was asked the identical query. It raises:

> `InvalidOperationException: Translating this query requires the SQL APPLY operation, which is not
> supported on SQLite.`

So it **refuses at translation time** rather than substituting another shape — and that corrects
decision §6.2, which had offered "emit a shape the engine already runs". There is no general rewrite:
`APPLY` is a lateral join, its right side re-evaluated per left row, and no join this engine has
preserves that. Refusing is the correct implementation, not a concession.

`VisitCrossApply` and `VisitOuterApply` now throw a message that names the provider, the LINQ shape
that caused it, and a way out. Two tests: one that the query is refused, one that the refusal is
*actionable* — a loud but useless error would pass the first alone.

This is the third time in the audit's history that the answer was "refuse loudly": the same shape as
`AddPrimaryKey`/`DropPrimaryKey`/`RenameIndex`/`AlterColumn` in phase 2, where emitting a comment let
a migration be recorded as applied.

### 13.3 The original finding restated — its *justification* was wrong, not its subject

`EfShapedTableSourceParsesTest` demanded the grammar learn `CROSS APPLY`, `OUTER APPLY` and
`VALUES … AS V(Id)` **because EF Core emits them**. Measured, that is largely false for this provider:
collections translate to `IN (…)`, and the one shape the generator really did emit — `OUTER APPLY` —
is now refused at translation time.

> **Corrected 2026-07-28.** A first version of this section turned the three shapes into *parity
> pins* asserting they must stay rejected, on the grounds that SQLite rejects them. That is the wrong
> test to write. PostgreSQL and SQL Server accept all three, and they are the target — so these are
> **unbuilt capability**, not correct behaviour. They are now ignored specifications in
> `LargeEngineTableSourceParsesTest`, each turning green the day it is built.

### 13.4 `VALUES` deferred — on cost, not on desirability

A `VALUES` table source is supported by PostgreSQL and SQL Server, so WitSQL should have it. It is
deferred because it is **executor work, not grammar**: the engine has to materialise a row set and
name its columns. Nothing on the current drop-in path forces the timing — the EF provider emits
`IN (…)`, measured — so it can wait for a phase that budgets engine work.

The derived column list `AS V(Id)` belongs with it, and is the better design of the two: naming the
columns explicitly beats inheriting SQLite's positional `column1..columnN`.

### 13.5 Counts after PR 6

| Suite | After PR 5 | After PR 6 |
|---|---|---|
| `Tests` (`Category!=Performance`) | 1941 / 31 skipped / 1972 | **1941 / 32 / 1973** |
| `EntityFramework.Tests` | 547 / 1 / 548 | **552 / 1 / 553** |

Whole solution green under the CI filter, 14 projects, both frameworks. Ledger **77**, unchanged: the
four table-source shapes stay marked as unbuilt capability (`TestCase`-level `Ignore =`, which the
ledger command does not count — §8.4), and the five new EF tests are all passing.

---

## 14. What the sharpened criterion leaves on the table

Written 2026-07-29, after the scope correction at the top of this document. Phase 3 measured a great
deal about which shapes WitSQL accepts; **re-read against "would PostgreSQL/SQL Server code notice",
several results change from "settled" to "backlog".** Collected here so the next session inherits the
list rather than re-deriving it.

### 14.1 Capability gaps, all measured during phase 3

| Gap | Who has it | What it costs | Where it is tracked |
|---|---|---|---|
| **Lateral joins** — `LATERAL` (PostgreSQL), `CROSS`/`OUTER APPLY` (SQL Server) | both | engine: re-evaluate the right side per left row. Currently **refused** by the EF generator as a stopgap | `LargeEngineTableSourceParsesTest`, `GeneratedSqlIsParseableTests` |
| **`VALUES` table source** | both | engine: materialise a row set | `LargeEngineTableSourceParsesTest` |
| **Derived column list** `AS V(Id)` | both | grammar, small — and a better design than SQLite's positional `column1..columnN` | same |
| **`TOP n`** | SQL Server | grammar only; `LIMIT` already covers the capability | same |
| **User-defined functions** | both | subsystem: catalog, evaluator integration, persistence | `CreateFunctionIsSupportedTest` |
| **Stored procedures** | both | subsystem: procedural interpreter, variables, `CALL` | `CreateProcedureIsSupportedTest` |
| **JSON columns** | both | convention + query/update generator support — carried out of phase 2 | audit state |

None of these is a defect in the sense the audit used the word. All of them are places where
application code written against a large engine would notice the substitution — which is the bar.

### 14.2 The instrument gap, and the natural successor to phase 3

**Every conformance instrument in this repository compares against SQLite.** That was right for
attribution and it earned its keep — nine of 29 EF findings in phase 2, and four more restatements in
phase 3. But it cannot answer the question that now defines scope, because SQLite lacks most of the
list above.

The successor to `GrammarSyntaxOracle` is the same idea aimed one level up: **run the same SQL against
PostgreSQL and SQL Server** — Testcontainers or a developer-supplied connection string, excluded from
CI exactly as the SQLite oracle is — and produce a **dialect coverage report** rather than a
pass/fail gate. Two things would fall out of it immediately:

- a measured list of what those engines accept and WitSQL does not, replacing the hand-assembled table
  in §14.1;
- the same **agreement** check phase 3 added, which is what caught `0x1F` returning `0` and
  `NOT BETWEEN` returning every row. Acceptance parity is not behavioural parity, and that lesson
  transfers directly.

That is a proposal, not a decision. It is the cheapest way to stop the roadmap being assembled from
recollection — which is exactly the failure mode this project has already paid for twice.

---

## 15. PR 7 results — WitSQL.md marked honestly, 2026-07-29

The last piece of phase 3, and the one the scope correction changed most.

The original plan called this "correct `WitSQL.md` so it stops generating findings", on the reasoning
that UDFs and procedures are not needed because SQLite lacks them. **That reasoning was wrong.** Both
large engines have them, application code written against those engines uses them, and the whole
point is that such code should not notice the substitution.

So the sections are not withdrawn — they are **marked as not implemented and explicitly still
planned**, each with the reason it sits outside the grammar phase and a pointer to its executable
specification:

| Section | Status | Why it is not phase-3 work |
|---|---|---|
| §22 User-Defined Functions | not implemented | needs a function catalog, evaluator integration, persistence |
| §23 Stored Procedures | not implemented | needs a procedural interpreter — variables, control flow, `CALL` |
| §2.8 CREATE TRIGGER | **partly** implemented | reading `OLD`/`NEW` and `SIGNAL` work; **assigning** to `NEW` does not parse, and the executor would have to let a BEFORE trigger mutate the pending row |

The trigger entry is new. It was on the ledger as a parser finding, and re-reading it against the
sharpened criterion makes it a documentation problem too: §2.8 states "in BEFORE triggers, modifying
`NEW.column_name` changes the value" and shows `SET NEW.UpdatedAt = NOW()`, which does not parse.
**Partly implemented is the honest label** — the surrounding claims about `OLD`/`NEW` and `SIGNAL` are
true, so marking the whole section unimplemented would be its own inaccuracy.

The three markers are restated in the same terms: **unbuilt capability, wanted, out of scope on
cost** — not "confirmed defect" and not "not required".

Whole solution green under the CI filter, 14 projects, both frameworks. Ledger **77**.

---

## 16. Phase 3, closed

| PR | Subject | Outcome |
|---|---|---|
| #28 | Plan, and the SQLite oracle | All five predictions held; three results re-scoped the phase |
| #29 | The regression net | Found the grammar already ambiguous, and a serializer defect that corrupts views |
| #30 | The boolean-layer split | `BETWEEN` fixed; 7 ambiguities → 0; parse ~2× faster |
| #31 | `BETWEEN` shapes pinned | Found the `HAVING`-aggregate defect |
| #32 | `INSERT … DEFAULT VALUES` | Executor needed no change |
| #33 | Hex literals | `SELECT 0x1F` stopped returning `0` |
| #34 | `APPLY` refused, `VALUES` deferred | Provider stopped emitting SQL its own parser rejects |
| #35 | `WitSQL.md` marked honestly | Three sections labelled, three markers restated |

**What the phase actually delivered**, beyond the scope list: four defects nobody had recorded — the
`NOT BETWEEN`-deletes-everything half, the serializer's subquery placeholder corrupting views and
partial indexes, the `HAVING`-aggregate refusal, and hex literals returning a silently wrong number
rather than failing.

**The pattern worth carrying forward.** Every one of those four was found by an instrument that
compares *answers*, not by reading code and not by the existing 10,000 tests. Twice the instrument
itself was wrong first — the acceptance-only oracle reporting false parity on `0x1F`, and the
agreement theory's controls going red on a formatting difference — and both times the controls caught
it. **Build the control into the instrument, or the instrument will lie quietly.**

Next: phases 4 (durability) and 5 (performance) as planned, plus the capability backlog in §14 and the
PostgreSQL/SQL Server dialect oracle it argues for.
