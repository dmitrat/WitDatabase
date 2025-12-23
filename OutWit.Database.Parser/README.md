# OutWit.Database.Parser

**WitSQL Parser** - SQL parser for WitDatabase embedded database engine.

This library provides a complete SQL parser that converts WitSQL text into an Abstract Syntax Tree (AST) for execution by the database engine.

---

## Overview

OutWit.Database.Parser is a high-performance SQL parser built on [ANTLR4](https://www.antlr.org/). It supports the full WitSQL dialect, which is designed to be compatible with SQLite while leveraging .NET's rich type system.

### Key Features

- **Complete SQL Support** - DDL, DML, transactions, window functions, CTEs
- **ANTLR4-based** - Industry-standard parser generator
- **Rich AST** - Fully typed Abstract Syntax Tree
- **Visitor Pattern** - Easy AST traversal and transformation
- **Expression Serializer** - Convert AST back to SQL text
- **Detailed Error Reporting** - Line/column positions for syntax errors
- **.NET 9/10** - Targets latest .NET versions

---

## Installation

```xml
<PackageReference Include="OutWit.Database.Parser" Version="1.0.0" />
```

---

## Quick Start

### Parse SQL Statements

```csharp
using OutWit.Database.Parser;

// Parse multiple statements
var statements = WitSql.Parse(@"
    CREATE TABLE Users (
        Id GUID PRIMARY KEY,
        Name VARCHAR(100) NOT NULL
    );
    
    INSERT INTO Users (Id, Name) VALUES (NEWGUID(), 'John');
");

foreach (var stmt in statements)
{
    Console.WriteLine(stmt.GetType().Name);
}
// Output:
// WitSqlStatementCreateTable
// WitSqlStatementInsert
```

### Parse Single Statement

```csharp
var stmt = WitSql.ParseStatement("SELECT * FROM Users WHERE Age > 18");

if (stmt is WitSqlStatementSelect select)
{
    Console.WriteLine($"From: {select.From[0].TableName}");
    // Output: From: Users
}
```

### Parse Expression

```csharp
var expr = WitSql.ParseExpression("Price * Quantity * (1 - Discount)");

if (expr is WitSqlExpressionBinary binary)
{
    Console.WriteLine($"Operator: {binary.Operator}");
}
```

### Handle Parsing Errors

```csharp
var result = WitSql.TryParse("SELECT * FORM Users"); // typo: FORM instead of FROM

if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Line {error.Line}, Column {error.Column}: {error.Message}");
    }
}
```

---

## Architecture

### Project Structure

```
OutWit.Database.Parser/
??? Grammars/                    # ANTLR4 grammar files
?   ??? WitSqlLexer.g4          # Lexer rules (tokens)
?   ??? WitSqlParser.g4         # Parser rules (grammar)
??? Expressions/                 # Expression AST nodes
?   ??? WitSqlExpression.cs     # Base class
?   ??? WitSqlExpressionBinary.cs
?   ??? WitSqlExpressionUnary.cs
?   ??? WitSqlExpressionLiteral.cs
?   ??? WitSqlExpressionColumnRef.cs
?   ??? WitSqlExpressionFunctionCall.cs
?   ??? WitSqlExpressionCase.cs
?   ??? WitSqlExpressionCast.cs
?   ??? WitSqlExpressionBetween.cs
?   ??? WitSqlExpressionIn.cs
?   ??? WitSqlExpressionLike.cs
?   ??? WitSqlExpressionGlob.cs
?   ??? WitSqlExpressionIsNull.cs
?   ??? WitSqlExpressionExists.cs
?   ??? WitSqlExpressionSubquery.cs
?   ??? WitSqlExpressionQuantified.cs
?   ??? WitSqlExpressionParameter.cs
?   ??? WitSqlExpressionIif.cs
?   ??? WitSqlExpressionCollate.cs
??? Statements/                  # Statement AST nodes
?   ??? WitSqlStatement.cs      # Base class
?   ??? WitSqlStatementSelect.cs
?   ??? WitSqlStatementInsert.cs
?   ??? WitSqlStatementUpdate.cs
?   ??? WitSqlStatementDelete.cs
?   ??? WitSqlStatementMerge.cs
?   ??? WitSqlStatementCreateTable.cs
?   ??? WitSqlStatementDropTable.cs
?   ??? WitSqlStatementAlterTable.cs
?   ??? WitSqlStatementCreateIndex.cs
?   ??? WitSqlStatementDropIndex.cs
?   ??? WitSqlStatementCreateView.cs
?   ??? WitSqlStatementDropView.cs
?   ??? WitSqlStatementCreateTrigger.cs
?   ??? WitSqlStatementDropTrigger.cs
?   ??? WitSqlStatementCreateSequence.cs
?   ??? WitSqlStatementDropSequence.cs
?   ??? WitSqlStatementAlterSequence.cs
?   ??? WitSqlStatementTruncate.cs
?   ??? WitSqlStatementBeginTransaction.cs
?   ??? WitSqlStatementCommit.cs
?   ??? WitSqlStatementRollback.cs
?   ??? WitSqlStatementSavepoint.cs
?   ??? WitSqlStatementReleaseSavepoint.cs
?   ??? WitSqlStatementSetTransaction.cs
?   ??? WitSqlStatementSignal.cs
??? Schema/                      # Supporting schema types
?   ??? WitSqlColumn.cs
?   ??? WitSqlDataType.cs
?   ??? Types/                   # Enums and type definitions
?   ?   ??? BinaryOperator.cs
?   ?   ??? UnaryOperatorType.cs
?   ?   ??? LiteralType.cs
?   ?   ??? JoinType.cs
?   ?   ??? SetOperationType.cs
?   ?   ??? IsolationLevelType.cs
?   ?   ??? LockingType.cs
?   ?   ??? ParameterType.cs
?   ?   ??? QuantifierType.cs
?   ?   ??? ...
?   ??? Clauses/                 # Clause definitions
?   ?   ??? ClauseSelectItem.cs
?   ?   ??? ClauseOrderByItem.cs
?   ?   ??? ClauseCteDefinition.cs
?   ?   ??? ClauseOnConflict.cs
?   ?   ??? ...
?   ??? TableSources/            # FROM clause sources
?   ?   ??? TableSource.cs
?   ?   ??? TableSourceSimple.cs
?   ?   ??? TableSourceJoin.cs
?   ?   ??? TableSourceSubquery.cs
?   ??? ColumnConstraints/       # Column-level constraints
?   ?   ??? ColumnConstraint.cs
?   ?   ??? ColumnConstraintPrimaryKey.cs
?   ?   ??? ColumnConstraintNotNull.cs
?   ?   ??? ...
?   ??? TableConstraints/        # Table-level constraints
?   ?   ??? TableConstraint.cs
?   ?   ??? TableConstraintPrimaryKey.cs
?   ?   ??? ...
?   ??? AlterActions/            # ALTER TABLE actions
?   ?   ??? AlterAction.cs
?   ?   ??? AlterActionAddColumn.cs
?   ?   ??? ...
?   ??? MergeClauses/            # MERGE statement clauses
?       ??? ClauseMergeWhen.cs
??? Visitor/                     # ANTLR visitor implementation
?   ??? WitSqlVisitor.cs         # Main visitor (partial)
?   ??? WitSqlVisitor.DDL.cs     # DDL statement handling
?   ??? WitSqlVisitor.DML.cs     # DML statement handling
?   ??? WitSqlVisitor.Expressions.cs
?   ??? WitSqlVisitor.Helpers.cs
??? Serializers/                 # AST to SQL text
?   ??? WitSqlExpressionSerializer.cs
??? Nodes/                       # Base AST node
?   ??? WitSqlNode.cs
??? Interfaces/                  # Visitor interface
?   ??? IWitSqlVisitor.cs
??? Exceptions/                  # Exception types
?   ??? WitSqlParsingException.cs
??? WitSql.cs                    # Main entry point
??? WitSqlParsingResult.cs       # Parsing result
??? WitSqlParsingError.cs        # Error details
??? WitSqlParsingErrorListener.cs
```

### AST Class Hierarchy

```
WitSqlNode (abstract)
??? WitSqlStatement (abstract)
?   ??? WitSqlStatementSelect
?   ??? WitSqlStatementInsert
?   ??? WitSqlStatementUpdate
?   ??? WitSqlStatementDelete
?   ??? WitSqlStatementMerge
?   ??? WitSqlStatementCreateTable
?   ??? WitSqlStatementDropTable
?   ??? WitSqlStatementAlterTable
?   ??? WitSqlStatementCreateIndex
?   ??? WitSqlStatementDropIndex
?   ??? WitSqlStatementCreateView
?   ??? WitSqlStatementDropView
?   ??? WitSqlStatementCreateTrigger
?   ??? WitSqlStatementDropTrigger
?   ??? WitSqlStatementCreateSequence
?   ??? WitSqlStatementDropSequence
?   ??? WitSqlStatementAlterSequence
?   ??? WitSqlStatementTruncate
?   ??? WitSqlStatementBeginTransaction
?   ??? WitSqlStatementCommit
?   ??? WitSqlStatementRollback
?   ??? WitSqlStatementSavepoint
?   ??? WitSqlStatementReleaseSavepoint
?   ??? WitSqlStatementSetTransaction
?   ??? WitSqlStatementSignal
??? WitSqlExpression (abstract)
    ??? WitSqlExpressionLiteral
    ??? WitSqlExpressionColumnRef
    ??? WitSqlExpressionBinary
    ??? WitSqlExpressionUnary
    ??? WitSqlExpressionFunctionCall
    ??? WitSqlExpressionCase
    ??? WitSqlExpressionCast
    ??? WitSqlExpressionBetween
    ??? WitSqlExpressionIn
    ??? WitSqlExpressionLike
    ??? WitSqlExpressionGlob
    ??? WitSqlExpressionIsNull
    ??? WitSqlExpressionExists
    ??? WitSqlExpressionSubquery
    ??? WitSqlExpressionQuantified
    ??? WitSqlExpressionParameter
    ??? WitSqlExpressionIif
    ??? WitSqlExpressionCollate
```

---

## Supported SQL Features

### Data Types

| Category | Types |
|----------|-------|
| Integer | `TINYINT`, `SMALLINT`, `INT`, `BIGINT` (signed/unsigned) |
| Floating | `FLOAT16`, `FLOAT`, `DOUBLE`, `DECIMAL` |
| Boolean | `BOOLEAN`, `BOOL` |
| Date/Time | `DATE`, `TIME`, `DATETIME`, `DATETIMEOFFSET`, `INTERVAL` |
| String | `CHAR(n)`, `VARCHAR(n)`, `TEXT` |
| Binary | `BINARY(n)`, `VARBINARY(n)`, `BLOB` |
| Special | `GUID`, `ROWVERSION`, `JSON`, `JSONB` |

### DDL Statements

- `CREATE TABLE` with all constraint types
- `DROP TABLE`
- `ALTER TABLE` (ADD/DROP COLUMN, RENAME, etc.)
- `CREATE INDEX` (unique, partial, expression, covering)
- `DROP INDEX`
- `CREATE VIEW` / `DROP VIEW`
- `CREATE TRIGGER` / `DROP TRIGGER`
- `CREATE SEQUENCE` / `ALTER SEQUENCE` / `DROP SEQUENCE`
- `TRUNCATE TABLE`

### DML Statements

- `SELECT` with all clauses (WHERE, GROUP BY, HAVING, ORDER BY, LIMIT)
- `INSERT` with VALUES, SELECT, RETURNING
- `UPDATE` with FROM clause, RETURNING
- `DELETE` with USING clause, RETURNING
- `MERGE` (UPSERT)
- `INSERT ... ON CONFLICT` (UPSERT)

### Joins and Set Operations

- `INNER JOIN`, `LEFT JOIN`, `RIGHT JOIN`, `FULL JOIN`, `CROSS JOIN`
- `UNION`, `UNION ALL`, `INTERSECT`, `EXCEPT`

### Common Table Expressions (CTE)

- `WITH ... AS`
- `WITH RECURSIVE ... AS`
- Multiple CTEs

### Subqueries

- Scalar subqueries
- Table subqueries (FROM clause)
- `IN (subquery)`
- `EXISTS (subquery)`
- `ANY`, `SOME`, `ALL` operators

### Transactions

- `BEGIN TRANSACTION`
- `COMMIT`
- `ROLLBACK`
- `SAVEPOINT` / `RELEASE SAVEPOINT`
- `SET TRANSACTION ISOLATION LEVEL`
- Locking hints: `FOR UPDATE`, `FOR SHARE`, `NOWAIT`, `SKIP LOCKED`

### Window Functions

- `ROW_NUMBER()`, `RANK()`, `DENSE_RANK()`, `NTILE()`
- `LAG()`, `LEAD()`, `FIRST_VALUE()`, `LAST_VALUE()`, `NTH_VALUE()`
- `PERCENT_RANK()`, `CUME_DIST()`
- `OVER (PARTITION BY ... ORDER BY ... ROWS/RANGE ...)`

### Built-in Functions

| Category | Functions |
|----------|-----------|
| Aggregate | `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`, `GROUP_CONCAT` |
| String | `LENGTH`, `UPPER`, `LOWER`, `SUBSTR`, `TRIM`, `REPLACE`, `CONCAT`, etc. |
| Numeric | `ABS`, `ROUND`, `FLOOR`, `CEIL`, `POWER`, `SQRT`, `MOD`, etc. |
| Date/Time | `NOW`, `YEAR`, `MONTH`, `DAY`, `DATEADD`, `DATEDIFF`, etc. |
| Conversion | `CAST`, `CONVERT`, `TOSTRING`, `TOINT`, `HEX`, `BASE64`, etc. |
| Null | `COALESCE`, `NULLIF`, `IFNULL`, `NVL` |
| JSON | `JSON_VALUE`, `JSON_QUERY`, `JSON_EXTRACT`, `JSON_SET`, etc. |
| System | `NEWGUID`, `LAST_INSERT_ROWID`, `CHANGES`, `VERSION`, etc. |

### Parameters

```sql
-- Named parameters
SELECT * FROM Users WHERE Id = @UserId;
SELECT * FROM Users WHERE Name = :name;

-- Positional parameters
SELECT * FROM Users WHERE Id = ?;
SELECT * FROM Users WHERE Name = $1 AND Age = $2;
```

### Collation

```sql
SELECT * FROM Users ORDER BY Name COLLATE NOCASE;
```

---

## Visitor Pattern

The parser implements the visitor pattern for easy AST traversal:

```csharp
public class MyVisitor : IWitSqlVisitor<string>
{
    public string VisitStatementSelect(WitSqlStatementSelect node)
    {
        return $"SELECT from {node.From.Count} tables";
    }
    
    public string VisitExpressionBinary(WitSqlExpressionBinary node)
    {
        var left = node.Left.Accept(this);
        var right = node.Right.Accept(this);
        return $"({left} {node.Operator} {right})";
    }
    
    // ... implement other Visit methods
}
```

---

## Expression Serialization

Convert AST back to SQL text:

```csharp
using OutWit.Database.Parser.Serializers;

var expr = WitSql.ParseExpression("a + b * c");
var sql = WitSqlExpressionSerializer.Serialize(expr);
// Output: "(a + (b * c))"
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Antlr4.Runtime.Standard | 4.13.1 | ANTLR4 runtime |
| Antlr4BuildTasks | 12.11.0 | Build-time grammar compilation |
| OutWit.Common | 1.3.1 | Common utilities |

---

## Related Projects

| Project | Description |
|---------|-------------|
| OutWit.Database.Core | Storage engine (B+Tree, LSM, MVCC) |
| OutWit.Database | SQL execution engine |
| OutWit.Database.Core.BouncyCastle | BouncyCastle encryption provider |

---

## License

MIT License - see LICENSE file for details.

---

## See Also

- [WitSql.md](../WitSql.md) - Full WitSQL language specification
- [Roadmap.Parser.md](../Roadmap.Parser.md) - Parser roadmap
- [Status.md](Status.md) - Implementation status
