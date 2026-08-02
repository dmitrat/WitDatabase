parser grammar WitSqlParser;

options {
    tokenVocab = WitSqlLexer;
}

// ============================================================================
// Entry Point
// ============================================================================

script
    : statement (SEMI statement)* SEMI? EOF
 ;

// ============================================================================
// Statements
// ============================================================================

statement
    : dmlStatement
    | ddlStatement
    | transactionStatement
    | signalStatement
    | explainStatement
    | callStatement
    ;

dmlStatement
    : queryExpression
    | insertStatement
    | updateStatement
    | deleteStatement
    | mergeStatement
    ;

ddlStatement
    : createTableStatement
    | dropTableStatement
    | alterTableStatement
    | createIndexStatement
    | dropIndexStatement
    | createViewStatement
    | dropViewStatement
    | createTriggerStatement
    | dropTriggerStatement
    | createSequenceStatement
    | dropSequenceStatement
    | alterSequenceStatement
    | truncateTableStatement
    | createFunctionStatement
    | dropFunctionStatement
    | createProcedureStatement
    | dropProcedureStatement
    ;

transactionStatement
    : beginTransaction
    | commitStatement
    | rollbackStatement
    | savepointStatement
    | releaseStatement
    | setTransactionStatement
    ;

beginTransaction
    : BEGIN TRANSACTION?
    ;

commitStatement
    : COMMIT
    ;

rollbackStatement
    : ROLLBACK (TO SAVEPOINT? IDENTIFIER)?
    ;

savepointStatement
    : SAVEPOINT IDENTIFIER
    ;

releaseStatement
    : RELEASE SAVEPOINT? IDENTIFIER
    ;

setTransactionStatement
    : SET TRANSACTION ISOLATION LEVEL isolationLevel
    ;

isolationLevel
    : READ UNCOMMITTED
    | READ COMMITTED
    | REPEATABLE READ
    | SERIALIZABLE
    | SNAPSHOT
    ;

queryExpression
    : withClause? queryTerm (setOperation queryTerm)* orderByClause? limitClause?
    ;

withClause
    : WITH RECURSIVE? cteDefinition (COMMA cteDefinition)*
    ;

cteDefinition
    : IDENTIFIER (LPAREN columnName (COMMA columnName)* RPAREN)? AS LPAREN queryExpression RPAREN
    ;

queryTerm
    : selectStatement
    | valuesQuery
    | LPAREN queryExpression RPAREN
    ;

valuesQuery
    : VALUES valuesList
    ;

setOperation
    : UNION ALL?
    | INTERSECT
    | EXCEPT
 ;

// ============================================================================
// SELECT Statement
// ============================================================================

selectStatement
    : SELECT (DISTINCT | ALL)? topClause? selectList
      fromClause?
      whereClause?
      groupByClause?
      havingClause?
      forClause?
    ;

topClause
    : TOP expression
    ;

forClause
    : FOR (UPDATE | SHARE) forClauseOption*
    ;

forClauseOption
    : NOWAIT
    | SKIP_ LOCKED
    ;

selectList
    : selectItem (COMMA selectItem)*
    ;

selectItem
    : STAR                                          # selectAll
    | tableName DOT STAR                            # selectTableAll
    | expression (AS? alias)?                       # selectExpression
    ;

fromClause
    : FROM tableSource (COMMA tableSource)*
    ;

tableSource
    : tableName (AS? alias)?                        # simpleTableSource
    | tableSource joinType tableSource (ON expression)?   # joinTableSource
    | LPAREN queryExpression RPAREN AS alias derivedColumnList?   # subqueryTableSource
    | LATERAL LPAREN queryExpression RPAREN AS? alias derivedColumnList?   # lateralTableSource
    | tableSource applyKind LPAREN queryExpression RPAREN AS? alias derivedColumnList?   # applyTableSource
    ;

derivedColumnList
    : LPAREN columnName (COMMA columnName)* RPAREN
    ;

applyKind
    : CROSS APPLY
    | OUTER APPLY
    ;

joinType
    : INNER? JOIN
    | LEFT OUTER? JOIN
    | RIGHT OUTER? JOIN
    | FULL OUTER? JOIN
    | CROSS JOIN
    ;

whereClause
    : WHERE expression
    ;

groupByClause
    : GROUP BY expression (COMMA expression)*
    ;

havingClause
    : HAVING expression
    ;

orderByClause
    : ORDER BY orderByItem (COMMA orderByItem)*
    ;

orderByItem
    : expression (ASC | DESC)? (NULLS (FIRST | LAST))?
    ;

limitClause
    : LIMIT expression (OFFSET expression)?
    | LIMIT expression COMMA expression
    // OFFSET with no LIMIT. Standard SQL and PostgreSql allow it, and without it a provider has to
    // emit SQLite's `LIMIT -1 OFFSET n` for EF Core's Skip(n) without Take(n).
    | OFFSET expression
 ;

// ============================================================================
// INSERT Statement
// ============================================================================

insertStatement
    : INSERT (OR (REPLACE | IGNORE))? INTO tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
      // DEFAULT VALUES inserts one row using every column's default. SQLite accepts it, and EF Core
      // emits it for an entity whose columns are all store-generated. The visitor turns it into a
      // single EMPTY value row, which the executor already handles: it seeds every column with its
      // default, auto-increment or ROWVERSION first, then applies the supplied values - of which
      // there are none. NOT NULL is still validated, so a table with a non-nullable, defaultless
      // column still refuses the insert, as it should.
      ( VALUES valuesList | DEFAULT VALUES | selectStatement )
      onConflictClause?
      returningClause?
    ;

onConflictClause
    : ON CONFLICT (LPAREN columnName (COMMA columnName)* RPAREN)?
      DO (conflictAction)
    ;

conflictAction
    : NOTHING
    | UPDATE SET setClause (COMMA setClause)* (WHERE expression)?
    ;

valuesList
    : valueRow (COMMA valueRow)*
    ;

valueRow
    : LPAREN expression (COMMA expression)* RPAREN
    ;

updateStatement
    : UPDATE tableName (AS? alias)?
      SET setClause (COMMA setClause)*
      (FROM tableSource (COMMA tableSource)*)?
      whereClause?
      returningClause?
    ;

setClause
    : columnName EQ expression
    ;

deleteStatement
    : DELETE FROM tableName (AS? alias)?
      (USING tableSource (COMMA tableSource)*)?
      whereClause? 
      returningClause?
    ;

// ============================================================================
// MERGE Statement
// ============================================================================

mergeStatement
    : MERGE INTO tableName (AS? alias)?
      USING mergeSource (AS? alias)? ON expression
      mergeClause+
    ;

mergeSource
    : tableName
    | LPAREN selectStatement RPAREN
    ;

mergeClause
    : WHEN MATCHED (AND expression)? THEN mergeUpdateClause    # mergeMatchedClause
    | WHEN NOT MATCHED (AND expression)? THEN mergeInsertClause # mergeNotMatchedClause
    ;

mergeUpdateClause
    : UPDATE SET mergeSetClause (COMMA mergeSetClause)*
    | DELETE
    ;

mergeSetClause
    : columnRef EQ expression
    ;

mergeInsertClause
    : INSERT (LPAREN columnName (COMMA columnName)* RPAREN)?
      VALUES LPAREN expression (COMMA expression)* RPAREN
    ;

createTableStatement
    : CREATE TABLE (IF NOT EXISTS)? tableName
      LPAREN tableElement (COMMA tableElement)* RPAREN
    ;

tableElement
    : columnDefinition
    | tableConstraint
    ;

columnDefinition
    : columnName dataType columnConstraint*                           # regularColumn
    | columnName AS LPAREN expression RPAREN (STORED | VIRTUAL)?      # computedColumn
    ;

dataType
    : typeName (LPAREN typeParam (COMMA typeParam)* RPAREN)?
    ;

typeName
    : TINYINT | INT8 | UTINYINT | UINT8
    | SMALLINT | INT16 | USMALLINT | UINT16
    | INT | INT32 | INTEGER | UINT | UINT32
    | BIGINT | INT64 | LONG | UBIGINT | UINT64 | ULONG
    | FLOAT16 | HALF
    | FLOAT | FLOAT32 | REAL
    | DOUBLE | FLOAT64
    | DECIMAL | MONEY | NUMERIC
    | BOOLEAN | BOOL
    | DATE | DATEONLY
    | TIME | TIMEONLY
    | DATETIME | TIMESTAMP
    | DATETIMEOFFSET
    | INTERVAL | TIMESPAN
    | GUID | UUID | UNIQUEIDENTIFIER
    | CHAR | NCHAR
    | VARCHAR | NVARCHAR
    | TEXT | NTEXT
    | BINARY
    | VARBINARY
    | BLOB
    | ROWVERSION
    | JSON | JSONB
    ;

typeParam
    : INTEGER_LITERAL
    | MAX
   ;

columnConstraint
    : NOT? NULL                                     # nullConstraint
    | PRIMARY KEY AUTOINCREMENT?                    # primaryKeyConstraint
    | UNIQUE                                        # uniqueConstraint
    | DEFAULT (literal | LPAREN expression RPAREN)  # defaultConstraint
    | CHECK LPAREN expression RPAREN                # checkConstraint
    | REFERENCES tableName (LPAREN columnName RPAREN)?
        referenceOption*                            # referencesConstraint
    ;

referenceOption
    : ON DELETE referenceAction
    | ON UPDATE referenceAction
    ;

referenceAction
    : NO ACTION
    | RESTRICT
    | CASCADE
    | SET NULL
    | SET DEFAULT
   ;

tableConstraint
    : (CONSTRAINT constraintName)? PRIMARY KEY LPAREN columnName (COMMA columnName)* RPAREN   # tablePrimaryKey
    | (CONSTRAINT constraintName)? UNIQUE LPAREN columnName (COMMA columnName)* RPAREN        # tableUnique
    | (CONSTRAINT constraintName)? FOREIGN KEY LPAREN columnName (COMMA columnName)* RPAREN
        REFERENCES tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
        referenceOption*                                          # tableForeignKey
    | (CONSTRAINT constraintName)? CHECK LPAREN expression RPAREN  # tableCheck
    ;

constraintName
    : IDENTIFIER
    ;

dropTableStatement
    : DROP TABLE (IF EXISTS)? tableName
    ;

alterTableStatement
    : ALTER TABLE tableName alterAction
    ;

alterAction
    : ADD COLUMN? columnDefinition                          # alterAddColumn
    | ADD (CONSTRAINT constraintName)? tableConstraint      # alterAddConstraint
    | DROP COLUMN? columnName                               # alterDropColumn
    | DROP CONSTRAINT constraintName                        # alterDropConstraint
    | RENAME TO tableName                                   # alterRenameTable
    | RENAME COLUMN? columnName TO columnName               # alterRenameColumn
    | ALTER COLUMN? columnName alterColumnAction            # alterAlterColumn
    ;

alterColumnAction
    : TYPE dataType                                         # alterColumnType
    | SET DEFAULT expression                                # alterColumnSetDefault
    | DROP DEFAULT                                          # alterColumnDropDefault
    | SET NOT NULL                                          # alterColumnSetNotNull
    | DROP NOT NULL                                         # alterColumnDropNotNull
    ;

createIndexStatement
    : CREATE UNIQUE? INDEX (IF NOT EXISTS)? indexName
      ON tableName LPAREN indexElement (COMMA indexElement)* RPAREN
      includeClause?
      whereClause?
    ;

indexElement
    : columnName (ASC | DESC)?                      # indexColumnElement
    | LPAREN expression RPAREN (ASC | DESC)?        # indexExpressionElement
    | functionCall (ASC | DESC)?                    # indexFunctionElement
    ;

includeClause
    : INCLUDE LPAREN columnName (COMMA columnName)* RPAREN
    ;

indexName
    : IDENTIFIER
    ;

dropIndexStatement
    : DROP INDEX (IF EXISTS)? indexName
    ;

createViewStatement
    : CREATE VIEW (IF NOT EXISTS)? viewName (LPAREN columnName (COMMA columnName)* RPAREN)?
      AS queryExpression
    ;

dropViewStatement
    : DROP VIEW (IF EXISTS)? viewName
    ;

viewName
    : IDENTIFIER
    ;

createTriggerStatement
    : CREATE TRIGGER (IF NOT EXISTS)? triggerName
      triggerTime triggerEvent ON tableName
      (FOR EACH ROW)?
      (WHEN LPAREN expression RPAREN)?
      BEGIN statement (SEMI statement)* SEMI? END
    ;

triggerTime
    : BEFORE
    | AFTER
    | INSTEAD OF
    ;

triggerEvent
    : INSERT
    | UPDATE (OF columnName (COMMA columnName)*)?
    | DELETE
    ;

dropTriggerStatement
    : DROP TRIGGER (IF EXISTS)? triggerName
    ;

triggerName
    : IDENTIFIER
    ;

// ============================================================================
// Routines - functions and procedures
// ============================================================================
//
// The spellings are fixed by the dialect oracle's corpus rather than chosen here, which is what
// makes them a measurement instead of a preference:
//
//     CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END
//     CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T; END
//
// A function's body is ONE expression, which is the load-bearing decision of phase 9d: invoking a
// function becomes substitution inside the expression evaluator rather than re-entry into the
// statement executor, so it consumes no execution nesting and cannot open a transaction. RETURN is
// therefore part of this rule and is NOT a statement of its own - it has exactly one legal position,
// and a statement type for it would be a permanent union tag bought for nothing.
//
// A procedure's body is a statement list, exactly as a trigger's is.
//
// LANGUAGE is admitted as an identifier rather than pinned to SQL here, so that a caller writing
// LANGUAGE plpgsql is told what is wrong by the executor instead of getting a parse error pointing
// at a token. Anything but SQL is refused there.

createFunctionStatement
    : CREATE FUNCTION (IF NOT EXISTS)? routineName
      LPAREN routineParameters? RPAREN
      RETURNS dataType
      (LANGUAGE routineLanguage)?
      AS BEGIN RETURN expression SEMI? END
    ;

dropFunctionStatement
    : DROP FUNCTION (IF EXISTS)? routineName
    ;

createProcedureStatement
    : CREATE PROCEDURE (IF NOT EXISTS)? routineName
      (LPAREN routineParameters? RPAREN)?
      (LANGUAGE routineLanguage)?
      AS BEGIN statement (SEMI statement)* SEMI? END
    ;

dropProcedureStatement
    : DROP PROCEDURE (IF EXISTS)? routineName
    ;

callStatement
    : CALL routineName LPAREN (expression (COMMA expression)*)? RPAREN
    ;

routineParameters
    : routineParameter (COMMA routineParameter)*
    ;

routineParameter
    : routineParameterName dataType
    ;

// A parameter name is an ordinary identifier and admits the non-reserved keywords, like a column
// name does. Measured while building the catalog: a parameter called Text could not be referred to
// from a body, because the lexer takes TEXT as a type keyword.
routineParameterName
    : IDENTIFIER
    | nonReservedKeyword
    ;

routineName
    : IDENTIFIER
    | nonReservedKeyword
    ;

routineLanguage
    : IDENTIFIER
    | nonReservedKeyword
    ;

createSequenceStatement
    : CREATE SEQUENCE (IF NOT EXISTS)? sequenceName
      (START WITH INTEGER_LITERAL)?
    ;

dropSequenceStatement
    : DROP SEQUENCE (IF EXISTS)? sequenceName
    ;

alterSequenceStatement
    : ALTER SEQUENCE sequenceName RESTART (WITH INTEGER_LITERAL)?
    ;

truncateTableStatement
    : TRUNCATE TABLE tableName
    ;

sequenceName
    : IDENTIFIER
    ;

// ============================================================================
// Expressions - three layers
// ============================================================================
//
// The boolean operators live in `searchCondition`, the comparison and pattern predicates in
// `predicate`, and everything that produces a value in `valueExpression`.
//
// WHY, and it is not style. ANTLR eliminates left recursion by compiling each alternative's
// recursive references with a precedence argument. A reference that is FIRST or LAST in its
// alternative is bound to the rule's own precedence and stops where it should. A reference that is
// INTERIOR - neither first nor last - is compiled as expression(0), full precedence, and consumes
// everything after it.
//
// BETWEEN's lower bound is interior, and the token after it is AND, which used to be an operator of
// this same rule. So `Age BETWEEN 1 AND 10 AND Flag = 1` parsed as
// Between(Age, lower = (1 AND 10), upper = (Flag = 1)), and returned nothing. Worse, the negated
// form returned EVERY row: `Age NOT BETWEEN 1 AND 20 AND Active = 0` matched all of them, which in a
// DELETE removes rows the WHERE clause was written to protect.
//
// LIKE had the same shape and was fixed positionally, by splitting its optional ESCAPE block into a
// separate alternative so the pattern moved to the trailing position. BETWEEN cannot be fixed that
// way: its AND keyword sits structurally in the MIDDLE of the alternative, so no reordering can move
// the lower bound out of the interior position.
//
// With the layers split, BETWEEN's operands are `valueExpression`, which cannot derive AND at all -
// AND lives one layer up. The bug is removed structurally rather than worked around, and the LIKE
// split is no longer needed and has been collapsed back into one alternative.
//
// `expression` is kept as the entry point, so that all 23 clause references (WHERE, HAVING, ON,
// CHECK, computed columns, trigger WHEN, partial indexes, DEFAULT, MERGE ...) and the visitor call
// sites are unchanged, and every one of them gets the full boolean layer for free.

expression
    : searchCondition
    ;

// ORDER IS PRECEDENCE, and it runs high-to-low: ANTLR binds an earlier alternative of a
// left-recursive rule more tightly than a later one. So NOT before AND before OR, which is the same
// relative order the flat rule used. Writing OR first instead is not a style choice - it silently
// makes `a AND b OR c` mean `a AND (b OR c)`, which is what AndBindsTighterThanOrTest and
// NotBindsTighterThanAndTest caught the first time this rule was written.
searchCondition
    : predicate                                     # predicateExpr
    // NOT binds looser than every comparison and predicate. It used to sit in the same flat rule as
    // the comparisons, which made `NOT Age > 18` mean `(NOT Age) > 18` until it was reordered by
    // hand; now the layering guarantees it instead of the ordering doing so.
    | NOT searchCondition                           # notExpr
    | searchCondition AND searchCondition           # andExpr
    | searchCondition OR searchCondition            # orExpr
    ;

// Left-recursive on its LEFT operand only. That combination is the whole trick:
//
//   - the left operand is a recursive reference in FIRST position, which ANTLR bounds to this rule's
//     precedence, so `a = 1 = 1` and `a < 5 < 3` still chain the way they always did. SQLite accepts
//     both, and a provider stricter than the one it substitutes for is not a drop-in one - this was
//     measured, after a first version of this rule took the recursion out entirely and silently
//     stopped accepting them;
//   - every OTHER operand is a `valueExpression`, a reference to a different rule. ANTLR's precedence
//     machinery does not apply across rules, so those operands simply cannot derive AND - AND lives
//     two layers up in searchCondition. That is what stops BETWEEN's lower bound from swallowing the
//     following conjunct, and it is why the LIKE workaround is no longer needed.
predicate
    : predicate (LT | LE | GT | GE) valueExpression         # compareExpr
    | predicate (EQ | NE | NE2) valueExpression             # equalityExpr
    | predicate IS NOT? NULL                                # isNullExpr
    | predicate NOT? BETWEEN valueExpression AND valueExpression # betweenExpr
    | predicate NOT? IN LPAREN (expression (COMMA expression)* | queryExpression) RPAREN # inExpr
    | predicate NOT? LIKE valueExpression (ESCAPE valueExpression)? # likeExpr
    | predicate NOT? GLOB valueExpression                   # globExpr
    | predicate comparisonOp (ANY | SOME | ALL) LPAREN queryExpression RPAREN # quantifiedExpr
    // NOT is deliberately absent here: `NOT EXISTS (...)` is negated by searchCondition's NOT, and
    // the visitor folds that back into Exists(IsNot = true) so the AST is unchanged. Carrying a
    // NOT? here as well made the input derivable two ways, which ANTLR resolved silently by
    // alternative order.
    | EXISTS LPAREN queryExpression RPAREN                  # existsExpr
    // A bare value used as a condition - `WHERE Flag`, `WHERE 1`. Last, so it is only tried once no
    // predicate matches.
    | valueExpression                                       # valuePredicate
    ;

valueExpression
    : literal                                       # literalExpr
    | columnRef                                     # columnRefExpr
    | functionCall                                  # functionCallExpr
    | parameter                                     # parameterExpr
    // The re-entry that makes the two layers mutually reachable. It is required, not convenience:
    // WitSqlExpressionSerializer parenthesises every binary node unconditionally, so `a AND b`
    // round-trips as `(a AND b)` and must keep parsing in a value position.
    | LPAREN expression RPAREN                      # parenExpr
    | LPAREN queryExpression RPAREN                 # subqueryExpr
    | (PLUS | MINUS | TILDE) valueExpression        # unaryExpr
    | valueExpression (STAR | SLASH | PERCENT) valueExpression # mulDivExpr
    | valueExpression (PLUS | MINUS) valueExpression # addSubExpr
    | valueExpression (AMP | PIPE | RSHIFT | LSHIFT) valueExpression # bitwiseExpr
    | valueExpression (CONCAT) valueExpression      # concatExpr
    | valueExpression COLLATE collationName         # collateExpr
    // Two alternatives rather than one rule with an optional operand. The visitor used to tell the
    // simple form from the searched one by COUNTING how many expressions the context held; the
    // layers make them structurally distinct, so the counting heuristic is gone.
    | CASE valueExpression (WHEN valueExpression THEN expression)+ (ELSE expression)? END # simpleCaseExpr
    | CASE (WHEN searchCondition THEN expression)+ (ELSE expression)? END # searchedCaseExpr
    | CAST LPAREN expression AS dataType RPAREN     # castExpr
    | CONVERT LPAREN dataType COMMA expression RPAREN # convertExpr
    | IIF LPAREN expression COMMA expression COMMA expression RPAREN # iifExpr
   ;

collationName
    : BINARY
    | NOCASE
    | UNICODE_CI
    | UNICODE_COLLATE
    | IDENTIFIER
    ;

parameter
    : PARAM_NAMED                                   # namedParameter
    | PARAM_COLON                                   # colonParameter
    | PARAM_DOLLAR_NAMED                            # dollarNamedParameter
    | PARAM_POSITIONAL                              # positionalParameter
    | PARAM_NUMBERED                                # numberedParameter
    ;

comparisonOp
    : EQ
    | NE
    | NE2
    | LT
    | LE
    | GT
    | GE
    ;

literal
    : INTEGER_LITERAL                               # intLiteral
    | HEX_LITERAL                                   # hexLiteral
    | REAL_LITERAL                                  # realLiteral
    | STRING_LITERAL                                # stringLiteral
    | BLOB_LITERAL                                  # blobLiteral
    | TRUE                                          # trueLiteral
    | FALSE                                         # falseLiteral
    | NULL                                          # nullLiteral
    | CURRENT_TIMESTAMP                             # currentTimestampLiteral
    | CURRENT_DATE                                  # currentDateLiteral
    | CURRENT_TIME                                  # currentTimeLiteral
    ;

columnRef
    : (tableName DOT)? columnName                   # simpleColumnRef
    | EXCLUDED DOT columnName                       # excludedColumnRef
   ;

functionCall
    : functionName LPAREN (DISTINCT? expression (COMMA expression)* | STAR)? RPAREN
      (OVER windowSpec)?
    ;

functionName
    : IDENTIFIER
    | COUNT | SUM | AVG | MIN | MAX | GROUP_CONCAT
    | UPPER | LOWER | LENGTH | SUBSTR | SUBSTRING | TRIM | REPLACE
    | LTRIM | RTRIM | INSTR | REVERSE | CONCAT_FUNC | CONCAT_WS
    | CHAR_LENGTH | OCTET_LENGTH | LPAD | RPAD | REPEAT | SPACE_FUNC
    | POSITION | FORMAT | LEFT | RIGHT
    | ABS | ROUND | FLOOR | CEIL | CEILING | SIGN | TRUNC | MOD
    | POWER | SQRT | EXP | LOG | LOG10 | LOG2 | PI | RANDOM
    | SIN | COS | TAN | ASIN | ACOS | ATAN | ATAN2
    | DEGREES | RADIANS
    | DATE | TIME | DATETIME | NOW
    | YEAR | MONTH | DAY | HOUR | MINUTE | SECOND
    | DAYOFWEEK | DAYOFYEAR | WEEKOFYEAR | QUARTER
    | DATEADD | DATEDIFF | STRFTIME | MAKEDATE | MAKETIME
    | COALESCE | NULLIF | CAST | IFNULL | NVL
    | CONVERT | HEX | UNHEX | TYPEOF
    | TOSTRING | TOINT | TODOUBLE | TODECIMAL | TOBOOLEAN
    | TODATE | TODATETIME | TOGUID
    | BASE64 | UNBASE64
    | NEWGUID | NEWUUID | INCREMENT | LASTINCREMENT
    | LAST_INSERT_ROWID | DATABASE_FUNC | VERSION_FUNC | CHANGES
    | ROW_NUMBER | RANK | DENSE_RANK | NTILE
    | LAG | LEAD | FIRST_VALUE | LAST_VALUE | NTH_VALUE
    | PERCENT_RANK | CUME_DIST
    | JSON_VALUE | JSON_QUERY | JSON_EXTRACT
    | JSON_SET | JSON_INSERT | JSON_REPLACE | JSON_REMOVE
    | JSON_TYPE | JSON_VALID | JSON_ARRAY | JSON_OBJECT
   ;

windowSpec
    : LPAREN
        (PARTITION BY expression (COMMA expression)*)?
        orderByClause?
        frameClause?
      RPAREN
    ;

frameClause
    : (ROWS | RANGE) frameBound
    | (ROWS | RANGE) BETWEEN frameBound AND frameBound
    ;

frameBound
    : UNBOUNDED PRECEDING
    | INTEGER_LITERAL PRECEDING
    | CURRENT ROW
    | INTEGER_LITERAL FOLLOWING
    | UNBOUNDED FOLLOWING
    ;

tableName
    : (schemaName DOT)? simpleTableName
    ;

simpleTableName
    : IDENTIFIER
    | nonReservedKeyword
    ;

schemaName
    : IDENTIFIER
    | nonReservedKeyword
    ;

columnName
    : IDENTIFIER
    | ROWID
    | LEVEL
    | nonReservedKeyword
    ;

alias
    : IDENTIFIER
    | LEVEL
    | nonReservedKeyword
    ;

nonReservedKeyword
    // Math functions
    : ABS | ROUND | FLOOR | CEIL | CEILING | SIGN | TRUNC | MOD
    | POWER | SQRT | EXP | LOG | LOG10 | LOG2 | PI | RANDOM
    | SIN | COS | TAN | ASIN | ACOS | ATAN | ATAN2
    | DEGREES | RADIANS
    // String functions
    | UPPER | LOWER | LENGTH | SUBSTR | SUBSTRING | TRIM | REPLACE
    | LTRIM | RTRIM | INSTR | REVERSE | CONCAT_FUNC | CONCAT_WS
    | CHAR_LENGTH | OCTET_LENGTH | LPAD | RPAD | REPEAT | SPACE_FUNC
    | POSITION | FORMAT
    // Date/time functions
    | DATE | TIME | DATETIME | NOW
    | YEAR | MONTH | DAY | HOUR | MINUTE | SECOND
    | DAYOFWEEK | DAYOFYEAR | WEEKOFYEAR | QUARTER
    | DATEADD | DATEDIFF | STRFTIME | MAKEDATE | MAKETIME
    // Null handling functions
    | COALESCE | NULLIF | IFNULL | NVL
    // Conversion functions
    | CONVERT | HEX | UNHEX | TYPEOF
    | TOSTRING | TOINT | TODOUBLE | TODECIMAL | TOBOOLEAN
    | TODATE | TODATETIME | TOGUID
    | BASE64 | UNBASE64
    // ID generation functions
    | NEWGUID | NEWUUID | INCREMENT | LASTINCREMENT
    // System functions
    | LAST_INSERT_ROWID | DATABASE_FUNC | VERSION_FUNC | CHANGES
    // Window functions
    | ROW_NUMBER | RANK | DENSE_RANK | NTILE
    | LAG | LEAD | FIRST_VALUE | LAST_VALUE | NTH_VALUE
    | PERCENT_RANK | CUME_DIST
    // JSON functions
    | JSON_VALUE | JSON_QUERY | JSON_EXTRACT
    | JSON_SET | JSON_INSERT | JSON_REPLACE | JSON_REMOVE
    | JSON_TYPE | JSON_VALID | JSON_ARRAY | JSON_OBJECT
    // Aggregate functions
    | COUNT | SUM | AVG | MIN | MAX | GROUP_CONCAT
    // Other common identifiers
    | ACTION | TYPE | ISOLATION | LEVEL | SNAPSHOT
    | CONFLICT | DO | NOTHING | WRITE | SHARE
    | FIRST | LAST
    | PLAN | QUERY
    // TOP is SQL Server's row limit, added 2026-08-01. Kept usable as an identifier: it was one
    // before, the keyword corpus caught it being taken away, and PostgreSQL does not reserve it
    // either. SELECT TOP 1 Id and a column named Top both parse.
    | TOP | APPLY | LATERAL
    // The routine keywords, added 2026-08-01. Every one of them was usable as a column name before
    // it was a token - measured, all thirteen candidates - and taking one away is exactly what
    // adding TOP did in phase 9b. RETURNS and RETURN are not in the list: they appear only inside
    // CREATE FUNCTION, where an identifier cannot stand, so admitting them as identifiers elsewhere
    // costs nothing, and the keyword corpus is what will say if that reasoning is wrong.
    | FUNCTION | PROCEDURE | CALL | LANGUAGE | RETURNS | RETURN
    // KEY is an ordinary column name in PostgreSQL, SQL Server and SQLite, and it only ever appears
    // after PRIMARY or FOREIGN in this grammar, so it is unambiguous here. Until 2026-07-30
    // `CREATE TABLE T (Key TEXT)` did not parse, and the failure had been recorded against
    // Parallel Mode=Buffered instead. See Docs/PHASE5-CONCURRENCY-PLAN.md section 5.
    | KEY
    // VALUE was listed here but is not a lexer token at all, so ANTLR defined it implicitly and the
    // alternative could never match - it emitted `warning(125): implicit definition of token VALUE`
    // on every build. `Value` as a column name works, and always did, by matching IDENTIFIER.
    ;

signalStatement
    : SIGNAL SQLSTATE STRING_LITERAL (SET MESSAGE_TEXT EQ expression)?
    ;

explainStatement
    : EXPLAIN (QUERY PLAN)? queryExpression
    ;

returningClause
    : RETURNING selectList
    ;
