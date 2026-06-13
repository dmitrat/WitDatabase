lexer grammar WitSqlLexer;

// ============================================================================
// Keywords - SQL Statements
// ============================================================================

ADD: A D D;
ANALYZE: A N A L Y Z E;
ANY: A N Y;
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
COLUMN: C O L U M N;
CONSTRAINT: C O N S T R A I N T;
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
EXPLAIN: E X P L A I N;
FALSE: F A L S E;
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
IGNORE: I G N O R E;
INCLUDE: I N C L U D E;
IS: I S;
JOIN: J O I N;
KEY: K E Y;
LEFT: L E F T;
LIKE: L I K E;
LIMIT: L I M I T;
NOT: N O T;
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
SOME: S O M E;
SET: S E T;
STORED: S T O R E D;
TABLE: T A B L E;
THEN: T H E N;
TO: T O;
TRUE: T R U E;
UNIQUE: U N I Q U E;
UPDATE: U P D A T E;
VALUES: V A L U E S;
VIRTUAL: V I R T U A L;
WHEN: W H E N;
WHERE: W H E R E;
MAX: M A X;
ACTION: A C T I O N;
NO: N O;
OF: O F;

// DDL Objects
VIEW: V I E W;
TRIGGER: T R I G G E R;
SEQUENCE: S E Q U E N C E;

// Transactions
BEGIN: B E G I N;
COMMIT: C O M M I T;
ROLLBACK: R O L L B A C K;
TRANSACTION: T R A N S A C T I O N;
SAVEPOINT: S A V E P O I N T;
RELEASE: R E L E A S E;

// Set Operations
UNION: U N I O N;
INTERSECT: I N T E R S E C T;
EXCEPT: E X C E P T;

// CTE
WITH: W I T H;
RECURSIVE: R E C U R S I V E;

// Expressions
GLOB: G L O B;
IIF: I I F;

// Triggers
INSTEAD: I N S T E A D;
AFTER: A F T E R;
BEFORE: B E F O R E;
FOR: F O R;
EACH: E A C H;

// SIGNAL SQLSTATE
SIGNAL: S I G N A L;
SQLSTATE: S Q L S T A T E;
MESSAGE_TEXT: M E S S A G E '_' T E X T;

// Sequence
START: S T A R T;
RESTART: R E S T A R T;

// Misc
TYPE: T Y P E;
RETURNING: R E T U R N I N G;
TRUNCATE: T R U N C A T E;
QUERY: Q U E R Y;
PLAN: P L A N;

// ============================================================================
// NULL-related (order matters: longer first)
// ============================================================================

NULLIF: N U L L I F;
NULLS: N U L L S;
NULL: N U L L;

// ============================================================================
// Built-in Literals (CURRENT_* before CURRENT)
// ============================================================================

CURRENT_TIMESTAMP: C U R R E N T '_' T I M E S T A M P;
CURRENT_DATE: C U R R E N T '_' D A T E;
CURRENT_TIME: C U R R E N T '_' T I M E;

// ============================================================================
// Window Functions (longer tokens first)
// ============================================================================

FIRST_VALUE: F I R S T '_' V A L U E;
LAST_VALUE: L A S T '_' V A L U E;
NTH_VALUE: N T H '_' V A L U E;
ROW_NUMBER: R O W '_' N U M B E R;
DENSE_RANK: D E N S E '_' R A N K;
PERCENT_RANK: P E R C E N T '_' R A N K;
CUME_DIST: C U M E '_' D I S T;
RANK: R A N K;
NTILE: N T I L E;
LAG: L A G;
LEAD: L E A D;

// ORDER BY keywords (after FIRST_VALUE, LAST_VALUE)
FIRST: F I R S T;
LAST: L A S T;

// ============================================================================
// Frame Clause (ROWVERSION before ROW/ROWS)
// ============================================================================

ROWVERSION: R O W V E R S I O N;
ROWS: R O W S;
ROW: R O W;
RANGE: R A N G E;
UNBOUNDED: U N B O U N D E D;
PRECEDING: P R E C E D I N G;
FOLLOWING: F O L L O W I N G;
CURRENT: C U R R E N T;

// ============================================================================
// Data Types - Integer (longer first: INT64 before INT)
// ============================================================================

TINYINT: T I N Y I N T;
UTINYINT: U T I N Y I N T;
SMALLINT: S M A L L I N T;
USMALLINT: U S M A L L I N T;
BIGINT: B I G I N T;
UBIGINT: U B I G I N T;
INTEGER: I N T E G E R;
INT64: I N T '6' '4';
INT32: I N T '3' '2';
INT16: I N T '1' '6';
INT8: I N T '8';
UINT64: U I N T '6' '4';
UINT32: U I N T '3' '2';
UINT16: U I N T '1' '6';
UINT8: U I N T '8';
ULONG: U L O N G;
LONG: L O N G;
UINT: U I N T;
INT: I N T;

// ============================================================================
// Data Types - Floating Point (longer first)
// ============================================================================

FLOAT64: F L O A T '6' '4';
FLOAT32: F L O A T '3' '2';
FLOAT16: F L O A T '1' '6';
FLOAT: F L O A T;
HALF: H A L F;
REAL: R E A L;
DOUBLE: D O U B L E;
DECIMAL: D E C I M A L;
MONEY: M O N E Y;
NUMERIC: N U M E R I C;

// ============================================================================
// Data Types - Boolean
// ============================================================================

BOOLEAN: B O O L E A N;
BOOL: B O O L;

// ============================================================================
// Data Types - Date/Time (longer first)
// ============================================================================

DATETIMEOFFSET: D A T E T I M E O F F S E T;
DATETIME: D A T E T I M E;
DATEONLY: D A T E O N L Y;
TIMESTAMP: T I M E S T A M P;
TIMEONLY: T I M E O N L Y;
TIMESPAN: T I M E S P A N;
INTERVAL: I N T E R V A L;
DATE: D A T E;
TIME: T I M E;

// ============================================================================
// Data Types - GUID
// ============================================================================

UNIQUEIDENTIFIER: U N I Q U E I D E N T I F I E R;
GUID: G U I D;
UUID: U U I D;

// ============================================================================
// Data Types - String (longer first)
// ============================================================================

NVARCHAR: N V A R C H A R;
VARCHAR: V A R C H A R;
NCHAR: N C H A R;
NTEXT: N T E X T;
CHAR: C H A R;
TEXT: T E X T;

// ============================================================================
// Data Types - Binary (longer first)
// ============================================================================

VARBINARY: V A R B I N A R Y;
BINARY: B I N A R Y;
BLOB: B L O B;

// ============================================================================
// Data Types - JSON
// ============================================================================

JSONB: J S O N B;
JSON: J S O N;

// ============================================================================
// JSON Functions (longer first)
// ============================================================================

JSON_EXTRACT: J S O N '_' E X T R A C T;
JSON_INSERT: J S O N '_' I N S E R T;
JSON_OBJECT: J S O N '_' O B J E C T;
JSON_REMOVE: J S O N '_' R E M O V E;
JSON_REPLACE: J S O N '_' R E P L A C E;
JSON_ARRAY: J S O N '_' A R R A Y;
JSON_QUERY: J S O N '_' Q U E R Y;
JSON_TYPE: J S O N '_' T Y P E;
JSON_VALID: J S O N '_' V A L I D;
JSON_VALUE: J S O N '_' V A L U E;
JSON_SET: J S O N '_' S E T;

// ============================================================================
// Functions - Generator
// ============================================================================

NOW: N O W;
NEWGUID: N E W G U I D;
NEWUUID: N E W U U I D;
LASTINCREMENT: L A S T I N C R E M E N T;
INCREMENT: I N C R E M E N T;

// ============================================================================
// Functions - Aggregate
// ============================================================================

GROUP_CONCAT: G R O U P '_' C O N C A T;
COALESCE: C O A L E S C E;
COUNT: C O U N T;
SUM: S U M;
AVG: A V G;
MIN: M I N;

// ============================================================================
// Functions - String
// ============================================================================

OCTET_LENGTH: O C T E T '_' L E N G T H;
CHAR_LENGTH: C H A R '_' L E N G T H;
CONCAT_WS: C O N C A T '_' W S;
CONCAT_FUNC: C O N C A T;
SUBSTRING: S U B S T R I N G;
SPACE_FUNC: S P A C E;
POSITION: P O S I T I O N;
REPLACE: R E P L A C E;
REVERSE: R E V E R S E;
LENGTH: L E N G T H;
SUBSTR: S U B S T R;
FORMAT: F O R M A T;
UPPER: U P P E R;
LOWER: L O W E R;
LTRIM: L T R I M;
RTRIM: R T R I M;
INSTR: I N S T R;
TRIM: T R I M;
LPAD: L P A D;
RPAD: R P A D;
REPEAT: R E P E A T;

// ============================================================================
// UPSERT Keywords
// ============================================================================

CONFLICT: C O N F L I C T;
DO: D O;
NOTHING: N O T H I N G;
EXCLUDED: E X C L U D E D;
USING: U S I N G;
MERGE: M E R G E;
MATCHED: M A T C H E D;

// ============================================================================
// Transaction Isolation Level
// ============================================================================

ISOLATION: I S O L A T I O N;
LEVEL: L E V E L;
READ: R E A D;
UNCOMMITTED: U N C O M M I T T E D;
COMMITTED: C O M M I T T E D;
REPEATABLE: R E P E A T A B L E;
SERIALIZABLE: S E R I A L I Z A B L E;
SNAPSHOT: S N A P S H O T;

// ============================================================================
// Locking Hints
// ============================================================================

SHARE: S H A R E;
NOWAIT: N O W A I T;
SKIP_: S K I P;
LOCKED: L O C K E D;
WRITE: W R I T E;

// ============================================================================
// Functions - Math (longer first)
// ============================================================================

CEILING: C E I L I N G;
DEGREES: D E G R E E S;
RADIANS: R A D I A N S;
RANDOM: R A N D O M;
POWER: P O W E R;
ROUND: R O U N D;
FLOOR: F L O O R;
ATAN2: A T A N '2';
LOG10: L O G '1' '0';
LOG2: L O G '2';
TRUNC: T R U N C;
SQRT: S Q R T;
CEIL: C E I L;
SIGN: S I G N;
ATAN: A T A N;
ACOS: A C O S;
ASIN: A S I N;
ABS: A B S;
MOD: M O D;
EXP: E X P;
LOG: L O G;
SIN: S I N;
COS: C O S;
TAN: T A N;
PI: P I;

// ============================================================================
// Functions - Date/Time (longer first)
// ============================================================================

WEEKOFYEAR: W E E K O F Y E A R;
DAYOFWEEK: D A Y O F W E E K;
DAYOFYEAR: D A Y O F Y E A R;
DATEDIFF: D A T E D I F F;
MAKETIME: M A K E T I M E;
MAKEDATE: M A K E D A T E;
STRFTIME: S T R F T I M E;
DATEADD: D A T E A D D;
QUARTER: Q U A R T E R;
MINUTE: M I N U T E;
SECOND: S E C O N D;
MONTH: M O N T H;
YEAR: Y E A R;
HOUR: H O U R;
DAY: D A Y;

// ============================================================================
// Functions - Null Handling
// ============================================================================

IFNULL: I F N U L L;
NVL: N V L;

// ============================================================================
// Functions - Conversion (longer first)
// ============================================================================

TODATETIME: T O D A T E T I M E;
TODECIMAL: T O D E C I M A L;
TOBOOLEAN: T O B O O L E A N;
TODOUBLE: T O D O U B L E;
TOSTRING: T O S T R I N G;
UNBASE64: U N B A S E '6' '4';
CONVERT: C O N V E R T;
TODATE: T O D A T E;
TOGUID: T O G U I D;
BASE64: B A S E '6' '4';
TYPEOF: T Y P E O F;
UNHEX: U N H E X;
TOINT: T O I N T;
HEX: H E X;

// ============================================================================
// Functions - System (longer first)
// ============================================================================

LAST_INSERT_ROWID: L A S T '_' I N S E R T '_' R O W I D;
VERSION_FUNC: V E R S I O N;
DATABASE_FUNC: D A T A B A S E;
CHANGES: C H A N G E S;
ROWID: R O W I D;

// ============================================================================
// Collation Keywords
// ============================================================================

COLLATE: C O L L A T E;
UNICODE_CI: U N I C O D E '_' C I;
NOCASE: N O C A S E;
UNICODE_COLLATE: U N I C O D E;

// ============================================================================
// Operators
// ============================================================================

// Comparison (multi-char first)
LE: '<=';
GE: '>=';
NE: '<>';
NE2: '!=';
LSHIFT: '<<';
RSHIFT: '>>';
CONCAT: '||';

// Single char
LT: '<';
GT: '>';
EQ: '=';
STAR: '*';
SLASH: '/';
PLUS: '+';
MINUS: '-';
PERCENT: '%';
TILDE: '~';
AMP: '&';
PIPE: '|';

// ============================================================================
// Punctuation
// ============================================================================

LPAREN: '(';
RPAREN: ')';
COMMA: ',';
SEMI: ';';
DOT: '.';

// ============================================================================
// Literals
// ============================================================================

INTEGER_LITERAL: DIGIT+;
REAL_LITERAL: DIGIT+ DOT DIGIT* | DOT DIGIT+ | DIGIT+ DOT? DIGIT* [Ee] [+-]? DIGIT+;
STRING_LITERAL: '\'' ( ~'\'' | '\'\'' )* '\'';
BLOB_LITERAL: [Xx] '\'' [0-9A-Fa-f]* '\'';

// ============================================================================
// Identifiers
// ============================================================================

IDENTIFIER
    : [a-zA-Z_] [a-zA-Z0-9_]*
    | '"' (~'"' | '""')* '"'
    | '[' ~']'* ']'
    | '`' ~'`'* '`'
    ;

PARAM_NAMED: '@' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_COLON: ':' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_POSITIONAL: '?';
PARAM_DOLLAR_NAMED: '$' [a-zA-Z_] [a-zA-Z0-9_]*;
PARAM_NUMBERED: '$' DIGIT+;

// ============================================================================
// Whitespace and Comments
// ============================================================================

WS: [ \t\r\n]+ -> skip;
LINE_COMMENT: '--' ~[\r\n]* -> skip;
BLOCK_COMMENT: '/*' .*? '*/' -> skip;

// ============================================================================
// Fragments
// ============================================================================

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
