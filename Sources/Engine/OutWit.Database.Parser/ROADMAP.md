# OutWit.Database.Parser - Version 2.0 Roadmap

**Last Updated:** 2026-01-20

This document outlines planned features for version 2.0 of OutWit.Database.Parser.

---

## Version 2.0 - Planned Features

### Priority 2: SQL Enhancements

**User-Defined Functions**

| Feature | Syntax |
|---------|--------|
| CREATE FUNCTION | `CREATE FUNCTION name (params) RETURNS type AS BEGIN ... END` |
| Table-valued functions | `RETURNS TABLE (col1 type, col2 type, ...)` |
| DETERMINISTIC | `CREATE FUNCTION name (...) DETERMINISTIC RETURNS ...` |
| DROP FUNCTION | `DROP FUNCTION [IF EXISTS] name` |

**Stored Procedures**

| Feature | Syntax |
|---------|--------|
| CREATE PROCEDURE | `CREATE PROCEDURE name (params) AS BEGIN ... END` |
| DROP PROCEDURE | `DROP PROCEDURE [IF EXISTS] name` |
| CALL | `CALL procedure_name(args)` |
| EXECUTE | `EXECUTE procedure_name(args)` |
| Parameters | `IN param_name type`, `OUT param_name type`, `INOUT param_name type` |

**Extended EXPLAIN**

| Feature | Syntax |
|---------|--------|
| EXPLAIN ANALYZE | `EXPLAIN ANALYZE SELECT ...` (actual execution stats) |
| Format options | `EXPLAIN (FORMAT JSON) SELECT ...` |
| Format options | `EXPLAIN (FORMAT TEXT) SELECT ...` |

**Database Administration**

| Feature | Syntax |
|---------|--------|
| CREATE DATABASE | `CREATE DATABASE name` |
| DROP DATABASE | `DROP DATABASE [IF EXISTS] name` |
| ATTACH DATABASE | `ATTACH DATABASE 'path' AS alias` |
| DETACH DATABASE | `DETACH DATABASE alias` |
| VACUUM | `VACUUM [table_name]` |
| ANALYZE | `ANALYZE [table_name]` |
| PRAGMA | `PRAGMA name [= value]` |

---

## Implementation Details

### User-Defined Functions

Grammar additions to `WitSqlParser.g4`:

```antlr
createFunctionStatement
    : CREATE FUNCTION functionName
      LPAREN parameterList? RPAREN
      (DETERMINISTIC)?
      RETURNS (dataType | tableType)
      AS BEGIN statementList END
    ;

tableType
    : TABLE LPAREN columnDefinition (COMMA columnDefinition)* RPAREN
    ;

dropFunctionStatement
    : DROP FUNCTION (IF EXISTS)? functionName
    ;
```

### Stored Procedures

Grammar additions:

```antlr
createProcedureStatement
    : CREATE PROCEDURE procedureName
      LPAREN procedureParameterList? RPAREN
      AS BEGIN statementList END
    ;

procedureParameter
    : (IN | OUT | INOUT)? parameterName dataType
    ;

callStatement
    : (CALL | EXECUTE) procedureName LPAREN expressionList? RPAREN
    ;
```

### EXPLAIN ANALYZE

```antlr
explainStatement
    : EXPLAIN (ANALYZE)? (LPAREN explainOption (COMMA explainOption)* RPAREN)?
      selectStatement
    ;

explainOption
    : FORMAT (JSON | TEXT)
    | COSTS (TRUE | FALSE)
    | BUFFERS (TRUE | FALSE)
    ;
```

---

## AST Classes to Add

| Class | Purpose |
|-------|---------|
| `WitSqlStatementCreateFunction` | CREATE FUNCTION AST |
| `WitSqlStatementDropFunction` | DROP FUNCTION AST |
| `WitSqlStatementCreateProcedure` | CREATE PROCEDURE AST |
| `WitSqlStatementDropProcedure` | DROP PROCEDURE AST |
| `WitSqlStatementCall` | CALL/EXECUTE AST |
| `WitSqlStatementVacuum` | VACUUM AST |
| `WitSqlStatementAnalyze` | ANALYZE AST |
| `WitSqlStatementPragma` | PRAGMA AST |

---

## See Also

- [README.md](README.md) - Project documentation
- [ROADMAP.md](../../../ROADMAP.md) - Main project roadmap
