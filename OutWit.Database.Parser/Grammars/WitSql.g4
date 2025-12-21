grammar WitSql;

// ============================================================================
// Parser Rules
// ============================================================================

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
    | truncateTableStatement
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
    | LIMIT expression COMMA expression
    ;

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

updateStatement
    : UPDATE tableName
      SET setClause (COMMA setClause)*
      whereClause?
      returningClause?
    ;

setClause
    : columnName EQ expression
    ;

deleteStatement
    : DELETE FROM tableName whereClause? returningClause?
    ;

returningClause
    : RETURNING selectList
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
    : columnName dataType columnConstraint*
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
    : PRIMARY KEY LPAREN columnName (COMMA columnName)* RPAREN   # tablePrimaryKey
    | UNIQUE LPAREN columnName (COMMA columnName)* RPAREN        # tableUnique
    | FOREIGN KEY LPAREN columnName (COMMA columnName)* RPAREN
        REFERENCES tableName (LPAREN columnName (COMMA columnName)* RPAREN)?
        referenceOption*                                          # tableForeignKey
    | CHECK LPAREN expression RPAREN                              # tableCheck
    ;

dropTableStatement
    : DROP TABLE (IF EXISTS)? tableName
    ;

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
    | COUNT | SUM | AVG | MIN | MAX | GROUP_CONCAT
    | UPPER | LOWER | LENGTH | SUBSTR | SUBSTRING | TRIM | REPLACE
    | LTRIM | RTRIM | INSTR | REVERSE | CONCAT_FUNC | CONCAT_WS
    | CHAR_LENGTH | OCTET_LENGTH | LPAD | RPAD | REPEAT | SPACE_FUNC
    | POSITION | FORMAT
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
    : IDENTIFIER
    ;

columnName
    : IDENTIFIER
    | ROWID
    ;

alias
    : IDENTIFIER
    ;

    // ============================================================================
    // Lexer Rules
    // ============================================================================

// Window function tokens (must be before FIRST/LAST keywords)
FIRST_VALUE: F I R S T '_' V A L U E;
LAST_VALUE: L A S T '_' V A L U E;
NTH_VALUE: N T H '_' V A L U E;

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
// ROWVERSION must be before ROW, ROWS, ROWID (longer match wins in ANTLR4)
ROWVERSION: R O W V E R S I O N;
ROW: R O W;
ROWS: R O W S;
RANGE: R A N G E;
UNBOUNDED: U N B O U N D E D;
PRECEDING: P R E C E D I N G;
FOLLOWING: F O L L O W I N G;
CURRENT: C U R R E N T;
SEQUENCE: S E Q U E N C E;
START: S T A R T;
RESTART: R E S T A R T;
TYPE: T Y P E;
RETURNING: R E T U R N I N G;
TRUNCATE: T R U N C A T E;

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
JSON: J S O N;
JSONB: J S O N B;

CURRENT_TIMESTAMP: C U R R E N T '_' T I M E S T A M P;
CURRENT_DATE: C U R R E N T '_' D A T E;
CURRENT_TIME: C U R R E N T '_' T I M E;
NOW: N O W;
NEWGUID: N E W G U I D;
NEWUUID: N E W U U I D;
INCREMENT: I N C R E M E N T;
LASTINCREMENT: L A S T I N C R E M E N T;

COUNT: C O U N T;
SUM: S U M;
AVG: A V G;
MIN: M I N;
GROUP_CONCAT: G R O U P '_' C O N C A T;

UPPER: U P P E R;
LOWER: L O W E R;
LENGTH: L E N G T H;
SUBSTR: S U B S T R;
SUBSTRING: S U B S T R I N G;
TRIM: T R I M;
REPLACE: R E P L A C E;
LTRIM: L T R I M;
RTRIM: R T R I M;
INSTR: I N S T R;
REVERSE: R E V E R S E;
CONCAT_FUNC: C O N C A T;
CONCAT_WS: C O N C A T '_' W S;
CHAR_LENGTH: C H A R '_' L E N G T H;
OCTET_LENGTH: O C T E T '_' L E N G T H;
LPAD: L P A D;
RPAD: R P A D;
REPEAT: R E P E A T;
SPACE_FUNC: S P A C E;
POSITION: P O S I T I O N;
FORMAT: F O R M A T;

ABS: A B S;
ROUND: R O U N D;
FLOOR: F L O O R;
CEIL: C E I L;
CEILING: C E I L I N G;
SIGN: S I G N;
TRUNC: T R U N C;
MOD: M O D;
POWER: P O W E R;
SQRT: S Q R T;
EXP: E X P;
LOG: L O G;
LOG10: L O G '1' '0';
LOG2: L O G '2';
PI: P I;
RANDOM: R A N D O M;
SIN: S I N;
COS: C O S;
TAN: T A N;
ASIN: A S I N;
ACOS: A C O S;
ATAN: A T A N;
ATAN2: A T A N '2';
DEGREES: D E G R E E S;
RADIANS: R A D I A N S;

YEAR: Y E A R;
MONTH: M O N T H;
DAY: D A Y;
HOUR: H O U R;
MINUTE: M I N U T E;
SECOND: S E C O N D;
DAYOFWEEK: D A Y O F W E E K;
DAYOFYEAR: D A Y O F Y E A R;
WEEKOFYEAR: W E E K O F Y E A R;
QUARTER: Q U A R T E R;
DATEADD: D A T E A D D;
DATEDIFF: D A T E D I F F;
STRFTIME: S T R F T I M E;
MAKEDATE: M A K E D A T E;
MAKETIME: M A K E T I M E;

IFNULL: I F N U L L;
NVL: N V L;
TYPEOF: T Y P E O F;
CONVERT: C O N V E R T;
HEX: H E X;
UNHEX: U N H E X;

// Conversion functions
TOSTRING: T O S T R I N G;
TOINT: T O I N T;
TODOUBLE: T O D O U B L E;
TODECIMAL: T O D E C I M A L;
TOBOOLEAN: T O B O O L E A N;
TODATE: T O D A T E;
TODATETIME: T O D A T E T I M E;
TOGUID: T O G U I D;
BASE64: B A S E '6' '4';
UNBASE64: U N B A S E '6' '4';

LAST_INSERT_ROWID: L A S T '_' I N S E R T '_' R O W I D;
ROWID: R O W I D;
DATABASE_FUNC: D A T A B A S E;
VERSION_FUNC: V E R S I O N;
CHANGES: C H A N G E S;

ROW_NUMBER: R O W '_' N U M B E R;
RANK: R A N K;
DENSE_RANK: D E N S E '_' R A N K;
NTILE: N T I L E;
LAG: L A G;
LEAD: L E A D;
PERCENT_RANK: P E R C E N T '_' R A N K;
CUME_DIST: C U M E '_' D I S T;

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

LPAREN: '(';
RPAREN: ')';
COMMA: ',';
SEMI: ';';
DOT: '.';

INTEGER_LITERAL: DIGIT+;
REAL_LITERAL: DIGIT+ DOT DIGIT* | DOT DIGIT+ | DIGIT+ DOT? DIGIT* [Ee] [+-]? DIGIT+;
STRING_LITERAL: '\'' ( ~'\'' | '\'\'' )* '\'';
BLOB_LITERAL: [Xx] '\'' [0-9A-Fa-f]* '\'';

IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    | '"' (~'"' | '""')* '"'
    | '[' ~']'* ']'
    | '`' ~'`'* '`'
    ;

PARAM_NAMED: '@' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_COLON: ':' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_POSITIONAL: '?';
PARAM_NUMBERED: '$' DIGIT+;

WS: [ \t\r\n]+ -> skip;
LINE_COMMENT: '--' ~[\r\n]* -> skip;
BLOCK_COMMENT: '/*' .*? '*/' -> skip;

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
