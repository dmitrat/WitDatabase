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
    ;

dmlStatement
    : queryExpression
    | insertStatement
    | updateStatement
    | deleteStatement
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
    ;

// ============================================================================
// Transaction Statements
// ============================================================================

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

// ============================================================================
// Query Expression (SELECT with CTE and Set Operations)
// ============================================================================

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
    ;

selectList
    : selectItem (COMMA selectItem)*
    ;

selectItem
    : STAR                                          # selectAll
    | tableName DOT STAR                            # selectTableAll
    | expression (AS? alias)?                       # selectExpression
    ;

// ============================================================================
// FROM Clause
// ============================================================================

fromClause
    : FROM tableSource (COMMA tableSource)*
    ;

tableSource
    : tableName (AS? alias)?                        # simpleTableSource
    | tableSource joinType tableSource (ON expression)?   # joinTableSource
    | LPAREN selectStatement RPAREN AS alias        # subqueryTableSource
    ;

joinType
    : INNER? JOIN
    | LEFT OUTER? JOIN
    | RIGHT OUTER? JOIN
    | FULL OUTER? JOIN
    | CROSS JOIN
    ;

// ============================================================================
// WHERE, GROUP BY, HAVING Clauses
// ============================================================================

whereClause
    : WHERE expression
    ;

groupByClause
    : GROUP BY expression (COMMA expression)*
    ;

havingClause
    : HAVING expression
    ;

// ============================================================================
// ORDER BY, LIMIT Clauses
// ============================================================================

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
    : INSERT INTO tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
      ( VALUES valuesList | selectStatement )
      returningClause?
    ;

valuesList
    : valueRow (COMMA valueRow)*
    ;

valueRow
    : LPAREN expression (COMMA expression)* RPAREN
    ;

// ============================================================================
// UPDATE Statement
// ============================================================================

updateStatement
    : UPDATE tableName
      SET setClause (COMMA setClause)*
      whereClause?
      returningClause?
    ;

setClause
    : columnName EQ expression
    ;

// ============================================================================
// DELETE Statement
// ============================================================================

deleteStatement
    : DELETE FROM tableName whereClause? returningClause?
    ;

// ============================================================================
// RETURNING Clause
// ============================================================================

returningClause
    : RETURNING selectList
    ;

// ============================================================================
// CREATE TABLE Statement
// ============================================================================

createTableStatement
    : CREATE TABLE (IF NOT EXISTS)? tableName
      LPAREN tableElement (COMMA tableElement)* RPAREN
    ;

tableElement
    : columnDefinition
    | tableConstraint
    ;

columnDefinition
    : columnName dataType columnConstraint*
    ;

// ============================================================================
// Data Types
// ============================================================================

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

// ============================================================================
// Column Constraints
// ============================================================================

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

// ============================================================================
// Table Constraints
// ============================================================================

tableConstraint
    : PRIMARY KEY LPAREN columnName (COMMA columnName)* RPAREN   # tablePrimaryKey
    | UNIQUE LPAREN columnName (COMMA columnName)* RPAREN        # tableUnique
    | FOREIGN KEY LPAREN columnName (COMMA columnName)* RPAREN
        REFERENCES tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
        referenceOption*                                          # tableForeignKey
    | CHECK LPAREN expression RPAREN                              # tableCheck
    ;

// ============================================================================
// DROP TABLE Statement
// ============================================================================

dropTableStatement
    : DROP TABLE (IF EXISTS)? tableName
    ;

// ============================================================================
// ALTER TABLE Statement
// ============================================================================

alterTableStatement
    : ALTER TABLE tableName alterAction
    ;

alterAction
    : ADD COLUMN? columnDefinition                          # alterAddColumn
    | DROP COLUMN? columnName                               # alterDropColumn
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

// ============================================================================
// CREATE INDEX Statement
// ============================================================================

createIndexStatement
    : CREATE UNIQUE? INDEX (IF NOT EXISTS)? indexName
      ON tableName LPAREN indexColumn (COMMA indexColumn)* RPAREN
    ;

indexColumn
    : columnName (ASC | DESC)?
    ;

indexName
    : IDENTIFIER
    ;

// ============================================================================
// DROP INDEX Statement
// ============================================================================

dropIndexStatement
    : DROP INDEX (IF EXISTS)? indexName
    ;

// ============================================================================
// CREATE VIEW Statement
// ============================================================================

createViewStatement
    : CREATE VIEW (IF NOT EXISTS)? viewName (LPAREN columnName (COMMA columnName)* RPAREN)?
      AS queryExpression
    ;

// ============================================================================
// DROP VIEW Statement
// ============================================================================

dropViewStatement
    : DROP VIEW (IF EXISTS)? viewName
    ;

viewName
    : IDENTIFIER
    ;

// ============================================================================
// CREATE TRIGGER Statement
// ============================================================================

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

// ============================================================================
// DROP TRIGGER Statement
// ============================================================================

dropTriggerStatement
    : DROP TRIGGER (IF EXISTS)? triggerName
    ;

triggerName
    : IDENTIFIER
    ;

// ============================================================================
// CREATE SEQUENCE Statement
// ============================================================================

createSequenceStatement
    : CREATE SEQUENCE (IF NOT EXISTS)? sequenceName
      (START WITH INTEGER_LITERAL)?
    ;

// ============================================================================
// DROP SEQUENCE Statement
// ============================================================================

dropSequenceStatement
    : DROP SEQUENCE (IF EXISTS)? sequenceName
    ;

// ============================================================================
// ALTER SEQUENCE Statement
// ============================================================================

alterSequenceStatement
    : ALTER SEQUENCE sequenceName RESTART (WITH INTEGER_LITERAL)?
    ;

// ============================================================================
// TRUNCATE TABLE Statement
// ============================================================================

truncateTableStatement
    : TRUNCATE TABLE tableName
    ;

sequenceName
    : IDENTIFIER
    ;

// ============================================================================
// Expressions
// ============================================================================

expression
    : literal                                       # literalExpr
    | columnRef                                     # columnRefExpr
    | functionCall                                  # functionCallExpr
    | parameter                                     # parameterExpr
    | LPAREN expression RPAREN                      # parenExpr
    | LPAREN selectStatement RPAREN                 # subqueryExpr
    | NOT? EXISTS LPAREN selectStatement RPAREN     # existsExpr
    | (PLUS | MINUS | NOT | TILDE) expression       # unaryExpr
    | expression (STAR | SLASH | PERCENT) expression    # mulDivExpr
    | expression (PLUS | MINUS) expression          # addSubExpr
    | expression (AMP | PIPE | RSHIFT | LSHIFT) expression # bitwiseExpr
    | expression (CONCAT) expression                # concatExpr
    | expression (LT | LE | GT | GE) expression     # compareExpr
    | expression (EQ | NE | NE2) expression         # equalityExpr
    | expression IS NOT? NULL                       # isNullExpr
    | expression NOT? BETWEEN expression AND expression # betweenExpr
    | expression NOT? IN LPAREN (expression (COMMA expression)* | selectStatement) RPAREN # inExpr
    | expression NOT? LIKE expression (ESCAPE expression)? # likeExpr
    | expression NOT? GLOB expression               # globExpr
    | expression AND expression                     # andExpr
    | expression OR expression                      # orExpr
    | CASE expression? (WHEN expression THEN expression)+ (ELSE expression)? END # caseExpr
    | CAST LPAREN expression AS dataType RPAREN     # castExpr
    | CONVERT LPAREN dataType COMMA expression RPAREN # convertExpr
    | IIF LPAREN expression COMMA expression COMMA expression RPAREN # iifExpr
    ;

// ============================================================================
// Parameters
// ============================================================================

parameter
    : PARAM_NAMED                                   # namedParameter
    | PARAM_COLON                                   # colonParameter
    | PARAM_POSITIONAL                              # positionalParameter
    | PARAM_NUMBERED                                # numberedParameter
    ;

// ============================================================================
// Literals
// ============================================================================

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

// ============================================================================
// Column Reference
// ============================================================================

columnRef
    : (tableName DOT)? columnName
    ;

// ============================================================================
// Function Call
// ============================================================================

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
    ;

// ============================================================================
// Window Specification
// ============================================================================

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

// ============================================================================
// Identifiers
// ============================================================================

tableName
    : IDENTIFIER
    ;

columnName
    : IDENTIFIER
    | ROWID
    ;

alias
    : IDENTIFIER
    ;
