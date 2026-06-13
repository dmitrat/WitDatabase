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
    | LPAREN queryExpression RPAREN
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
    : SELECT (DISTINCT | ALL)? selectList
      fromClause?
      whereClause?
      groupByClause?
      havingClause?
      forClause?
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
    | LPAREN queryExpression RPAREN AS alias        # subqueryTableSource
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
 ;

// ============================================================================
// INSERT Statement
// ============================================================================

insertStatement
    : INSERT (OR (REPLACE | IGNORE))? INTO tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
      ( VALUES valuesList | selectStatement )
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

expression
    : literal                                       # literalExpr
    | columnRef                                     # columnRefExpr
    | functionCall                                  # functionCallExpr
    | parameter                                     # parameterExpr
    | LPAREN expression RPAREN                      # parenExpr
    | LPAREN queryExpression RPAREN                 # subqueryExpr
    | NOT? EXISTS LPAREN queryExpression RPAREN     # existsExpr
    | (PLUS | MINUS | NOT | TILDE) expression       # unaryExpr
    | expression (STAR | SLASH | PERCENT) expression    # mulDivExpr
    | expression (PLUS | MINUS) expression          # addSubExpr
    | expression (AMP | PIPE | RSHIFT | LSHIFT) expression # bitwiseExpr
    | expression (CONCAT) expression                # concatExpr
    | expression COLLATE collationName              # collateExpr
    | expression (LT | LE | GT | GE) expression     # compareExpr
    | expression (EQ | NE | NE2) expression         # equalityExpr
    | expression IS NOT? NULL                       # isNullExpr
    | expression NOT? BETWEEN expression AND expression # betweenExpr
    | expression NOT? IN LPAREN (expression (COMMA expression)* | queryExpression) RPAREN # inExpr
    | expression NOT? LIKE expression (ESCAPE expression)? # likeExpr
    | expression NOT? GLOB expression               # globExpr
    | expression comparisonOp (ANY | SOME | ALL) LPAREN queryExpression RPAREN # quantifiedExpr
    | expression AND expression                     # andExpr
    | expression OR expression                      # orExpr
    | CASE expression? (WHEN expression THEN expression)+ (ELSE expression)? END # caseExpr
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
    | FIRST | LAST | VALUE
    | PLAN | QUERY
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
