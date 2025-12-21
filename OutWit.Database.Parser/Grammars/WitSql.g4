/*
 * WitSQL Grammar for ANTLR4
 * Based on SQLite syntax with .NET type extensions
 */

grammar WitSql;

// ============================================================================
// Parser Rules
// ============================================================================

// Entry point - can be multiple statements
script
    : statement (SEMI statement)* SEMI? EOF
    ;

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
    ;

transactionStatement
    : beginTransaction
    | commitStatement
    | rollbackStatement
    | savepointStatement
    | releaseStatement
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


// ----------------------------------------------------------------------------
// Query Expressions (with set operations and CTE)
// ----------------------------------------------------------------------------

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

// ----------------------------------------------------------------------------
// SELECT Statement
// ----------------------------------------------------------------------------

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
    | LIMIT expression COMMA expression  // MySQL style: LIMIT offset, count
    ;

// ----------------------------------------------------------------------------
// INSERT Statement
// ----------------------------------------------------------------------------

insertStatement
    : INSERT INTO tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
      ( VALUES valuesList
      | selectStatement
      )
    ;

valuesList
    : valueRow (COMMA valueRow)*
    ;

valueRow
    : LPAREN expression (COMMA expression)* RPAREN
    ;

// ----------------------------------------------------------------------------
// UPDATE Statement
// ----------------------------------------------------------------------------

updateStatement
    : UPDATE tableName
      SET setClause (COMMA setClause)*
      whereClause?
    ;

setClause
    : columnName EQ expression
    ;

// ----------------------------------------------------------------------------
// DELETE Statement
// ----------------------------------------------------------------------------

deleteStatement
    : DELETE FROM tableName whereClause?
   ;

// ----------------------------------------------------------------------------
// CREATE TABLE Statement
// ----------------------------------------------------------------------------

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

dataType
    : typeName (LPAREN typeParam (COMMA typeParam)* RPAREN)?
    ;

typeName
    // Integer types
    : TINYINT | INT8 | UTINYINT | UINT8
    | SMALLINT | INT16 | USMALLINT | UINT16
    | INT | INT32 | INTEGER | UINT | UINT32
    | BIGINT | INT64 | LONG | UBIGINT | UINT64 | ULONG
    // Float types
    | FLOAT16 | HALF
    | FLOAT | FLOAT32 | REAL
    | DOUBLE | FLOAT64
    | DECIMAL | MONEY | NUMERIC
    // Boolean
    | BOOLEAN | BOOL
    // Date/Time
    | DATE | DATEONLY
    | TIME | TIMEONLY
    | DATETIME | TIMESTAMP
    | DATETIMEOFFSET
    | INTERVAL | TIMESPAN
    // GUID
    | GUID | UUID | UNIQUEIDENTIFIER
    // String
    | CHAR | NCHAR
    | VARCHAR | NVARCHAR
    | TEXT | NTEXT
    // Binary
    | BINARY
    | VARBINARY
    | BLOB
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
    : PRIMARY KEY LPAREN columnName (COMMA columnName)* RPAREN
        # tablePrimaryKey
    | UNIQUE LPAREN columnName (COMMA columnName)* RPAREN
        # tableUnique
    | FOREIGN KEY LPAREN columnName (COMMA columnName)* RPAREN
        REFERENCES tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
        referenceOption*
        # tableForeignKey
    | CHECK LPAREN expression RPAREN
        # tableCheck
    ;

    // ----------------------------------------------------------------------------
// DROP TABLE Statement
// ----------------------------------------------------------------------------

dropTableStatement
    : DROP TABLE (IF EXISTS)? tableName
    ;

    // ----------------------------------------------------------------------------
// ALTER TABLE Statement
// ----------------------------------------------------------------------------

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

    // ----------------------------------------------------------------------------
// CREATE INDEX Statement
// ----------------------------------------------------------------------------

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

    // ----------------------------------------------------------------------------
// DROP INDEX Statement
// ----------------------------------------------------------------------------

dropIndexStatement
    : DROP INDEX (IF EXISTS)? indexName
    ;

    // ----------------------------------------------------------------------------
// CREATE/DROP VIEW Statements
// ----------------------------------------------------------------------------

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

    // ----------------------------------------------------------------------------
// CREATE/DROP TRIGGER Statements
// ----------------------------------------------------------------------------

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

    // ----------------------------------------------------------------------------
// SEQUENCE Statements
// ----------------------------------------------------------------------------

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

sequenceName
    : IDENTIFIER
    ;

    // ----------------------------------------------------------------------------
// Expressions
// ----------------------------------------------------------------------------

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
    | expression (AMP | PIPE | LSHIFT | RSHIFT) expression # bitwiseExpr
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
    | IIF LPAREN expression COMMA expression COMMA expression RPAREN # iifExpr
    ;

parameter
    : PARAM_NAMED                                   # namedParameter
    | PARAM_COLON                                   # colonParameter
    | PARAM_POSITIONAL                              # positionalParameter
    | PARAM_NUMBERED                                # numberedParameter
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
    : (tableName DOT)? columnName
    ;

functionCall
    : functionName LPAREN (DISTINCT? expression (COMMA expression)* | STAR)? RPAREN
      (OVER windowSpec)?
    ;

functionName
    : IDENTIFIER
    // Built-in functions that might conflict with keywords
    | COUNT | SUM | AVG | MIN | MAX
    | UPPER | LOWER | LENGTH | SUBSTR | TRIM | REPLACE
    | ABS | ROUND | FLOOR | CEIL
    | DATE | TIME | DATETIME
    | COALESCE | NULLIF | CAST
    | NOW | NEWGUID | INCREMENT
    | ROW_NUMBER | RANK | DENSE_RANK | LAG | LEAD
    ;

windowSpec
    : LPAREN
        (PARTITION BY expression (COMMA expression)*)?
        orderByClause?
      RPAREN
    ;

// Identifiers and names
tableName
    : IDENTIFIER
    ;

columnName
    : IDENTIFIER
    ;

alias
    : IDENTIFIER
    ;

// ============================================================================
// Lexer Rules
// ============================================================================

// Keywords (alphabetical order)
ADD: A D D;
ALL: A L L;
ALTER: A L T E R;
AND: A N D;
AS: A S;
ASC: A S C;
AUTOINCREMENT: A U T O I N C R E M E N T;
BETWEEN: B E T W E E N;
BY: B Y;
CASCADE: C A S C A D E;
CASE: C A S E;
CAST: C A S T;
CHECK: C H E C K;
COALESCE: C O A L E S C E;
COLUMN: C O L U M N;
CREATE: C R E A T E;
CROSS: C R O S S;
DEFAULT: D E F A U L T;
DELETE: D E L E T E;
DESC: D E S C;
DISTINCT: D I S T I N C T;
DROP: D R O P;
ELSE: E L S E;
END: E N D;
ESCAPE: E S C A P E;
EXISTS: E X I S T S;
FALSE: F A L S E;
FIRST: F I R S T;
FOREIGN: F O R E I G N;
FROM: F R O M;
FULL: F U L L;
GROUP: G R O U P;
HAVING: H A V I N G;
IF: I F;
IN: I N;
INDEX: I N D E X;
INNER: I N N E R;
INSERT: I N S E R T;
INTO: I N T O;
IS: I S;
JOIN: J O I N;
KEY: K E Y;
LAST: L A S T;
LEFT: L E F T;
LIKE: L I K E;
LIMIT: L I M I T;
NOT: N O T;
NULL: N U L L;
NULLIF: N U L L I F;
NULLS: N U L L S;
OFFSET: O F F S E T;
ON: O N;
OR: O R;
ORDER: O R D E R;
OUTER: O U T E R;
OVER: O V E R;
PARTITION: P A R T I T I O N;
PRIMARY: P R I M A R Y;
REFERENCES: R E F E R E N C E S;
RENAME: R E N A M E;
RESTRICT: R E S T R I C T;
RIGHT: R I G H T;
SELECT: S E L E C T;
SET: S E T;
TABLE: T A B L E;
THEN: T H E N;
TO: T O;
TRUE: T R U E;
UNIQUE: U N I Q U E;
UPDATE: U P D A T E;
VALUES: V A L U E S;
WHEN: W H E N;
WHERE: W H E R E;
MAX: M A X;
ACTION: A C T I O N;
NO: N O;
OF: O F;

// Additional keywords for full WitSQL coverage
VIEW: V I E W;
TRIGGER: T R I G G E R;
BEGIN: B E G I N;
COMMIT: C O M M I T;
ROLLBACK: R O L L B A C K;
TRANSACTION: T R A N S A C T I O N;
SAVEPOINT: S A V E P O I N T;
RELEASE: R E L E A S E;
UNION: U N I O N;
INTERSECT: I N T E R S E C T;
EXCEPT: E X C E P T;
WITH: W I T H;
RECURSIVE: R E C U R S I V E;
GLOB: G L O B;
IIF: I I F;
INSTEAD: I N S T E A D;
AFTER: A F T E R;
BEFORE: B E F O R E;
FOR: F O R;
EACH: E A C H;
ROW: R O W;
SEQUENCE: S E Q U E N C E;
START: S T A R T;
RESTART: R E S T A R T;
TYPE: T Y P E;

// Data type keywords
TINYINT: T I N Y I N T;
INT8: I N T '8';
UTINYINT: U T I N Y I N T;
UINT8: U I N T '8';
SMALLINT: S M A L L I N T;
INT16: I N T '1' '6';
USMALLINT: U S M A L L I N T;
UINT16: U I N T '1' '6';
INT: I N T;
INT32: I N T '3' '2';
INTEGER: I N T E G E R;
UINT: U I N T;
UINT32: U I N T '3' '2';
BIGINT: B I G I N T;
INT64: I N T '6' '4';
LONG: L O N G;
UBIGINT: U B I G I N T;
UINT64: U I N T '6' '4';
ULONG: U L O N G;
FLOAT16: F L O A T '1' '6';
HALF: H A L F;
FLOAT: F L O A T;
FLOAT32: F L O A T '3' '2';
REAL: R E A L;
DOUBLE: D O U B L E;
FLOAT64: F L O A T '6' '4';
DECIMAL: D E C I M A L;
MONEY: M O N E Y;
NUMERIC: N U M E R I C;
BOOLEAN: B O O L E A N;
BOOL: B O O L;
DATE: D A T E;
DATEONLY: D A T E O N L Y;
TIME: T I M E;
TIMEONLY: T I M E O N L Y;
DATETIME: D A T E T I M E;
TIMESTAMP: T I M E S T A M P;
DATETIMEOFFSET: D A T E T I M E O F F S E T;
INTERVAL: I N T E R V A L;
TIMESPAN: T I M E S P A N;
GUID: G U I D;
UUID: U U I D;
UNIQUEIDENTIFIER: U N I Q U E I D E N T I F I E R;
CHAR: C H A R;
NCHAR: N C H A R;
VARCHAR: V A R C H A R;
NVARCHAR: N V A R C H A R;
TEXT: T E X T;
NTEXT: N T E X T;
BINARY: B I N A R Y;
VARBINARY: V A R B I N A R Y;
BLOB: B L O B;

// Time functions
CURRENT_TIMESTAMP: C U R R E N T '_' T I M E S T A M P;
CURRENT_DATE: C U R R E N T '_' D A T E;
CURRENT_TIME: C U R R E N T '_' T I M E;
NOW: N O W;
NEWGUID: N E W G U I D;
INCREMENT: I N C R E M E N T;

// Aggregate functions
COUNT: C O U N T;
SUM: S U M;
AVG: A V G;
MIN: M I N;

// String functions
UPPER: U P P E R;
LOWER: L O W E R;
LENGTH: L E N G T H;
SUBSTR: S U B S T R;
TRIM: T R I M;
REPLACE: R E P L A C E;

// Math functions
ABS: A B S;
ROUND: R O U N D;
FLOOR: F L O O R;
CEIL: C E I L;

// Window functions
ROW_NUMBER: R O W '_' N U M B E R;
RANK: R A N K;
DENSE_RANK: D E N S E '_' R A N K;
LAG: L A G;
LEAD: L E A D;

// Operators
STAR: '*';
SLASH: '/';
PLUS: '+';
MINUS: '-';
PERCENT: '%';
EQ: '=';
NE: '<>';
NE2: '!=';
LT: '<';
LE: '<=';
GT: '>';
GE: '>=';
CONCAT: '||';
TILDE: '~';
AMP: '&';
PIPE: '|';
LSHIFT: '<<';
RSHIFT: '>>';

// Punctuation
LPAREN: '(';
RPAREN: ')';
COMMA: ',';
SEMI: ';';
DOT: '.';

// Literals
INTEGER_LITERAL
    : DIGIT+
    ;

REAL_LITERAL
    : DIGIT+ DOT DIGIT*
    | DOT DIGIT+
    | DIGIT+ DOT? DIGIT* [Ee] [+-]? DIGIT+
    ;

STRING_LITERAL
    : '\'' ( ~'\'' | '\'\'' )* '\''
    ;

BLOB_LITERAL
    : [Xx] '\'' [0-9A-Fa-f]* '\''
   ;

// Identifier
IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    | '"' (~'"' | '""')* '"'
    | '[' ~']'* ']'
    | '`' ~'`'* '`'
    ;

    // Parameter placeholders
PARAM_NAMED: '@' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_COLON: ':' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_POSITIONAL: '?';
PARAM_NUMBERED: '$' DIGIT+;

// Whitespace and comments
WS: [ \t\r\n]+ -> skip;
LINE_COMMENT: '--' ~[\r\n]* -> skip;
BLOCK_COMMENT: '/*' .*? '*/' -> skip;

// Fragment rules for case-insensitive keywords
fragment A: [Aa];
fragment B: [Bb];
fragment C: [Cc];
fragment D: [Dd];
fragment E: [Ee];
fragment F: [Ff];
fragment G: [Gg];
fragment H: [Hh];
fragment I: [Ii];
fragment J: [Jj];
fragment K: [Kk];
fragment L: [Ll];
fragment M: [Mm];
fragment N: [Nn];
fragment O: [Oo];
fragment P: [Pp];
fragment Q: [Qq];
fragment R: [Rr];
fragment S: [Ss];
fragment T: [Tt];
fragment U: [Uu];
fragment V: [Vv];
fragment W: [Ww];
fragment X: [Xx];
fragment Y: [Yy];
fragment Z: [Zz];
fragment DIGIT: [0-9];
