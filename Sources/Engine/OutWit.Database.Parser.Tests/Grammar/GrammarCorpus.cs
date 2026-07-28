namespace OutWit.Database.Parser.Tests.Grammar;

/// <summary>
/// The SQL corpus the phase-3 grammar rework is measured against.
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 lifts the boolean layer out of the flat <c>expression</c> rule into
/// <c>searchCondition</c>/<c>predicate</c>/<c>valueExpression</c>, so that <c>BETWEEN</c>'s lower
/// bound stops being an interior recursive reference. **The restructure must be behaviour-preserving
/// everywhere except <c>BETWEEN</c>** - any other change in what parses is a regression.
/// </para>
/// <para>
/// This corpus is what makes that a measured claim rather than an assurance. Nine behaviours changed
/// during the 2026-07 audit without a single test failing, so "the tests pass" is not evidence here.
/// It is deliberately organised by <b>where an expression can appear</b> rather than by feature,
/// because the rework re-points every one of those positions, and a position nobody listed is exactly
/// how a regression gets through.
/// </para>
/// <para>
/// Used by three harnesses, each asking a different question of the same strings:
/// <see cref="GrammarAmbiguityTests"/> (does the grammar decide these deterministically),
/// <see cref="GrammarRoundTripTests"/> (does the serializer's output re-parse to the same AST), and
/// the parse-throughput baseline in <see cref="GrammarThroughputTests"/>.
/// </para>
/// </remarks>
internal static class GrammarCorpus
{
    /// <summary>
    /// Every position in the grammar that currently references the <c>expression</c> rule, and so
    /// changes meaning if the rework re-points it wrongly. There are 23 such sites in
    /// <c>WitSqlParser.g4</c>; each is represented here at least once, with a boolean payload
    /// wherever the position accepts one.
    /// </summary>
    public static readonly string[] ExpressionPositions =
    [
        // selectItem
        "SELECT Age + 1 FROM T",
        "SELECT Age > 18 AS IsAdult FROM T",
        "SELECT Age BETWEEN 1 AND 10 AS InRange FROM T",
        // join ON - a boolean position
        "SELECT * FROM A INNER JOIN B ON A.Id = B.AId",
        "SELECT * FROM A LEFT JOIN B ON A.Id = B.AId AND B.Name IS NOT NULL",
        "SELECT * FROM A LEFT JOIN B ON A.Id = B.AId AND B.Age BETWEEN 1 AND 10",
        // whereClause - a boolean position
        "SELECT * FROM T WHERE Age > 18",
        "SELECT * FROM T WHERE Age > 18 AND Name LIKE 'a%'",
        "SELECT * FROM T WHERE NOT Age > 18",
        "SELECT * FROM T WHERE (Age > 18 OR Age < 5) AND Name IS NOT NULL",
        // groupByClause
        "SELECT Name, COUNT(*) FROM T GROUP BY Name",
        "SELECT Name, COUNT(*) FROM T GROUP BY UPPER(Name)",
        // havingClause - a boolean position
        "SELECT Name FROM T GROUP BY Name HAVING COUNT(*) > 1",
        "SELECT Name FROM T GROUP BY Name HAVING COUNT(*) > 1 AND MIN(Age) < 30",
        // orderByItem
        "SELECT * FROM T ORDER BY Age DESC",
        "SELECT * FROM T ORDER BY Age + 1 ASC NULLS LAST",
        // limitClause, all three forms
        "SELECT * FROM T LIMIT 10",
        "SELECT * FROM T LIMIT 10 OFFSET 5",
        "SELECT * FROM T LIMIT 10, 5",
        "SELECT * FROM T OFFSET 5",
        // conflictAction WHERE - a boolean position
        "INSERT INTO T (Id, Name) VALUES (1, 'a') ON CONFLICT (Id) DO UPDATE SET Name = 'b' WHERE T.Age > 5",
        // valueRow
        "INSERT INTO T (Id, Name) VALUES (1, 'a'), (2, 'b')",
        "INSERT INTO T (Id, Age) VALUES (1, 2 + 3)",
        // setClause
        "UPDATE T SET Age = Age + 1 WHERE Id = 1",
        "UPDATE T SET Age = CASE WHEN Age < 0 THEN 0 ELSE Age END",
        // mergeStatement ON, and mergeClause AND - both boolean positions
        """
        MERGE INTO T USING S ON T.Id = S.Id
        WHEN MATCHED THEN UPDATE SET T.Name = S.Name
        """,
        """
        MERGE INTO T USING S ON T.Id = S.Id
        WHEN MATCHED AND S.Age > 18 THEN UPDATE SET T.Name = S.Name
        WHEN NOT MATCHED AND S.Age BETWEEN 1 AND 10 THEN INSERT (Id) VALUES (S.Id)
        """,
        // mergeSetClause and mergeInsertClause
        "MERGE INTO T USING S ON T.Id = S.Id WHEN NOT MATCHED THEN INSERT (Id, Name) VALUES (S.Id, S.Name)",
        // computedColumn - an expression inside DDL
        "CREATE TABLE C (Id INT, Total AS (Price * Qty) STORED)",
        "CREATE TABLE C (Id INT, Big AS (Price > 100) VIRTUAL)",
        // defaultConstraint, both the literal and the parenthesised-expression form
        "CREATE TABLE D (Id INT, N INT DEFAULT 0)",
        "CREATE TABLE D (Id INT, N INT DEFAULT (1 + 1))",
        // checkConstraint on a column - a boolean position
        "CREATE TABLE E (Id INT, Age INT CHECK (Age >= 0))",
        "CREATE TABLE E (Id INT, Age INT CHECK (Age >= 0 AND Age < 150))",
        "CREATE TABLE E (Id INT, Age INT CHECK (Age BETWEEN 0 AND 150))",
        // tableCheck - a boolean position
        "CREATE TABLE F (Id INT, Age INT, CONSTRAINT CK CHECK (Age >= 0 AND Id > 0))",
        "CREATE TABLE F (Id INT, Age INT, CHECK (Age BETWEEN 0 AND 150 AND Id > 0))",
        // alterColumnSetDefault
        "ALTER TABLE T ALTER COLUMN N SET DEFAULT 5",
        // indexExpressionElement
        "CREATE INDEX IX ON T ((Age + 1))",
        "CREATE INDEX IX ON T (LOWER(Name))",
        // partial index WHERE - a boolean position
        "CREATE UNIQUE INDEX IX ON T (Name) WHERE Age > 18",
        "CREATE UNIQUE INDEX IX ON T (Name) WHERE Age > 18 AND Name IS NOT NULL",
        "CREATE UNIQUE INDEX IX ON T (Name) WHERE Age BETWEEN 1 AND 10",
        // covering index
        "CREATE INDEX IX ON T (Name) INCLUDE (Age)",
        // trigger WHEN - a boolean position
        """
        CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW
        WHEN (Age > 18)
        BEGIN
            INSERT INTO Log (Id) VALUES (1);
        END
        """,
        """
        CREATE TRIGGER TR AFTER INSERT ON T FOR EACH ROW
        WHEN (Age > 18 AND Name IS NOT NULL)
        BEGIN
            INSERT INTO Log (Id) VALUES (1);
        END
        """,
    ];

    /// <summary>
    /// One case per labelled alternative of the <c>expression</c> rule, so the rework cannot silently
    /// drop one. Ordered as the alternatives appear in the grammar.
    /// </summary>
    public static readonly string[] ExpressionAlternatives =
    [
        // literalExpr, every literal form
        "SELECT 1",
        "SELECT 1.5",
        "SELECT 1.5e3",
        "SELECT 'text'",
        "SELECT X'DEADBEEF'",
        "SELECT TRUE",
        "SELECT FALSE",
        "SELECT NULL",
        "SELECT CURRENT_TIMESTAMP",
        "SELECT CURRENT_DATE",
        "SELECT CURRENT_TIME",
        // columnRefExpr
        "SELECT Name FROM T",
        "SELECT T.Name FROM T",
        "INSERT INTO T (Id) VALUES (1) ON CONFLICT (Id) DO UPDATE SET Name = EXCLUDED.Name",
        // functionCallExpr, including DISTINCT, STAR and OVER
        "SELECT UPPER(Name) FROM T",
        "SELECT COUNT(*) FROM T",
        "SELECT COUNT(DISTINCT Name) FROM T",
        "SELECT ROW_NUMBER() OVER (PARTITION BY Name ORDER BY Age) FROM T",
        "SELECT LAST_VALUE(Age) OVER (ORDER BY Id ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) FROM T",
        "SELECT SUM(Age) OVER (ORDER BY Id RANGE UNBOUNDED PRECEDING) FROM T",
        // parameterExpr, every parameter form
        "SELECT * FROM T WHERE Id = @p",
        "SELECT * FROM T WHERE Id = :p",
        "SELECT * FROM T WHERE Id = $p",
        "SELECT * FROM T WHERE Id = ?",
        "SELECT * FROM T WHERE Id = $1",
        // parenExpr
        "SELECT (1 + 2) * 3",
        // subqueryExpr
        "SELECT (SELECT MAX(Age) FROM T)",
        // existsExpr
        "SELECT * FROM T WHERE EXISTS (SELECT 1 FROM S WHERE S.Id = T.Id)",
        "SELECT * FROM T WHERE NOT EXISTS (SELECT 1 FROM S WHERE S.Id = T.Id)",
        // unaryExpr
        "SELECT -Age FROM T",
        "SELECT +Age FROM T",
        "SELECT ~Flags FROM T",
        // mulDivExpr, addSubExpr
        "SELECT Age * 2, Age / 2, Age % 2 FROM T",
        "SELECT Age + 1, Age - 1 FROM T",
        // bitwiseExpr
        "SELECT Flags & 1, Flags | 1, Flags << 1, Flags >> 1 FROM T",
        // concatExpr
        "SELECT Name || '!' FROM T",
        // collateExpr
        "SELECT * FROM T WHERE Name = 'a' COLLATE NOCASE",
        "SELECT * FROM T ORDER BY Name COLLATE BINARY",
        // compareExpr, equalityExpr
        "SELECT * FROM T WHERE Age < 1 AND Age <= 2 AND Age > 3 AND Age >= 4",
        "SELECT * FROM T WHERE Age = 1 AND Name <> 'a' AND Name != 'b'",
        // isNullExpr
        "SELECT * FROM T WHERE Name IS NULL",
        "SELECT * FROM T WHERE Name IS NOT NULL",
        // betweenExpr - the subject of the rework
        "SELECT * FROM T WHERE Age BETWEEN 1 AND 10",
        "SELECT * FROM T WHERE Age NOT BETWEEN 1 AND 10",
        // inExpr, both the list and the subquery form
        "SELECT * FROM T WHERE Id IN (1, 2, 3)",
        "SELECT * FROM T WHERE Id NOT IN (1, 2, 3)",
        "SELECT * FROM T WHERE Id IN (SELECT Id FROM S)",
        // likeExpr and likeEscapeExpr - the two-alternative split the rework collapses
        "SELECT * FROM T WHERE Name LIKE 'a%'",
        "SELECT * FROM T WHERE Name NOT LIKE 'a%'",
        "SELECT * FROM T WHERE Name LIKE 'a!%' ESCAPE '!'",
        "SELECT * FROM T WHERE Name NOT LIKE 'a!%' ESCAPE '!'",
        // globExpr
        "SELECT * FROM T WHERE Name GLOB 'a*'",
        "SELECT * FROM T WHERE Name NOT GLOB 'a*'",
        // quantifiedExpr
        "SELECT * FROM T WHERE Age > ANY (SELECT Age FROM S)",
        "SELECT * FROM T WHERE Age > SOME (SELECT Age FROM S)",
        "SELECT * FROM T WHERE Age >= ALL (SELECT Age FROM S)",
        // notExpr, andExpr, orExpr
        "SELECT * FROM T WHERE NOT Name IS NULL",
        "SELECT * FROM T WHERE Age > 1 AND Age < 10",
        "SELECT * FROM T WHERE Age > 1 OR Age < 10",
        // caseExpr, both the simple and the searched form
        "SELECT CASE WHEN Age > 18 THEN 'adult' ELSE 'minor' END FROM T",
        "SELECT CASE Age WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE 'many' END FROM T",
        "SELECT CASE WHEN Age > 18 THEN 'adult' END FROM T",
        // castExpr, convertExpr, iifExpr
        "SELECT CAST(Age AS VARCHAR(10)) FROM T",
        "SELECT CONVERT(VARCHAR(10), Age) FROM T",
        "SELECT IIF(Age > 18, 'adult', 'minor') FROM T",
    ];

    /// <summary>
    /// Precedence shapes whose <b>meaning</b> the rework must not disturb. Each is a case where a
    /// wrong binding is silent rather than a parse error, which is what makes them worth pinning:
    /// the <c>LIKE</c> and <c>NOT</c> entries are the defects fixed in 2.0.0, and the rework moves
    /// both into a different rule.
    /// </summary>
    public static readonly string[] PrecedenceShapes =
    [
        "SELECT * FROM T WHERE Name LIKE 'a%' AND Age > 18",
        "SELECT * FROM T WHERE Name LIKE 'a%' OR Age > 18",
        "SELECT * FROM T WHERE Name LIKE 'a!%' ESCAPE '!' AND Age > 18",
        "SELECT * FROM T WHERE Name NOT LIKE 'a%' AND Age > 18",
        "SELECT * FROM T WHERE Name GLOB 'a*' AND Age > 18",
        "SELECT * FROM T WHERE Id IN (1, 2) AND Age > 18",
        "SELECT * FROM T WHERE NOT Age > 18",
        "SELECT * FROM T WHERE NOT Age = 18",
        "SELECT * FROM T WHERE NOT Age > 18 AND Name IS NOT NULL",
        "SELECT * FROM T WHERE Age > 1 AND Age < 10 OR Name IS NULL",
        "SELECT * FROM T WHERE Age > -1",
        "SELECT * FROM T WHERE Age BETWEEN 1 AND 10 AND Flags = 1",
        "SELECT * FROM T WHERE Age NOT BETWEEN 1 AND 10 AND Flags = 1",
        "SELECT * FROM T WHERE Age NOT BETWEEN 1 AND 10 OR Flags = 1",
        "SELECT * FROM T WHERE Age BETWEEN 1 AND 10 AND Flags BETWEEN 1 AND 2",
        "DELETE FROM T WHERE Name NOT LIKE 'p' AND Id = 5",
        "DELETE FROM T WHERE Age NOT BETWEEN 1 AND 10 AND Id = 5",
        "UPDATE T SET Age = 0 WHERE Age BETWEEN 1 AND 10 AND Flags = 1",
    ];

    /// <summary>
    /// Statement shapes with no expression subtlety, present so the harnesses cover the grammar
    /// rather than only its expression corner. A rework that breaks <c>CREATE SEQUENCE</c> is still
    /// a broken rework.
    /// </summary>
    public static readonly string[] Statements =
    [
        "SELECT DISTINCT Name FROM T",
        "SELECT ALL Name FROM T",
        "SELECT * FROM T, S",
        "SELECT * FROM (SELECT Id FROM T) AS X",
        "SELECT * FROM A CROSS JOIN B",
        "SELECT * FROM A FULL OUTER JOIN B ON A.Id = B.AId",
        "SELECT * FROM T FOR UPDATE NOWAIT",
        "SELECT * FROM T FOR SHARE SKIP LOCKED",
        "SELECT Id FROM T UNION SELECT Id FROM S",
        "SELECT Id FROM T UNION ALL SELECT Id FROM S",
        "SELECT Id FROM T INTERSECT SELECT Id FROM S",
        "SELECT Id FROM T EXCEPT SELECT Id FROM S",
        "WITH C AS (SELECT Id FROM T) SELECT * FROM C",
        "WITH RECURSIVE C (Id) AS (SELECT Id FROM T) SELECT * FROM C",
        "INSERT INTO T (Id) SELECT Id FROM S",
        "INSERT OR REPLACE INTO T (Id) VALUES (1)",
        "INSERT OR IGNORE INTO T (Id) VALUES (1)",
        "INSERT INTO T (Id) VALUES (1) ON CONFLICT DO NOTHING",
        "INSERT INTO T (Id) VALUES (1) RETURNING Id",
        "UPDATE T SET Name = 'a' FROM S WHERE T.Id = S.Id",
        "DELETE FROM T USING S WHERE T.Id = S.Id",
        "DELETE FROM T AS X WHERE X.Id = 1 RETURNING Id",
        "CREATE TABLE IF NOT EXISTS T (Id BIGINT PRIMARY KEY AUTOINCREMENT)",
        "CREATE TABLE T (Id INT, Name VARCHAR(50) NOT NULL UNIQUE)",
        "CREATE TABLE T (Id INT, PId INT REFERENCES P (Id) ON DELETE CASCADE ON UPDATE SET NULL)",
        "CREATE TABLE T (Id INT, PId INT, CONSTRAINT FK FOREIGN KEY (PId) REFERENCES P (Id) ON DELETE RESTRICT)",
        "CREATE TABLE T (A INT, B INT, CONSTRAINT PK PRIMARY KEY (A, B))",
        "CREATE TABLE T (A INT, B INT, CONSTRAINT UQ UNIQUE (A, B))",
        "CREATE TABLE T (P DECIMAL(18, 2), S VARCHAR(MAX))",
        "DROP TABLE IF EXISTS T",
        "ALTER TABLE T ADD COLUMN N INT",
        "ALTER TABLE T DROP COLUMN N",
        "ALTER TABLE T DROP CONSTRAINT CK",
        "ALTER TABLE T RENAME TO U",
        "ALTER TABLE T RENAME COLUMN A TO B",
        "ALTER TABLE T ALTER COLUMN N TYPE BIGINT",
        "ALTER TABLE T ALTER COLUMN N SET NOT NULL",
        "ALTER TABLE T ALTER COLUMN N DROP NOT NULL",
        "ALTER TABLE T ALTER COLUMN N DROP DEFAULT",
        "CREATE VIEW V AS SELECT Id FROM T",
        "CREATE VIEW V (A) AS SELECT Id FROM T",
        "DROP VIEW IF EXISTS V",
        "CREATE TRIGGER TR BEFORE UPDATE OF Name ON T BEGIN SELECT 1; END",
        "CREATE TRIGGER TR INSTEAD OF DELETE ON T BEGIN SELECT 1; END",
        "DROP TRIGGER IF EXISTS TR",
        "CREATE SEQUENCE SQ START WITH 5",
        "ALTER SEQUENCE SQ RESTART WITH 1",
        "DROP SEQUENCE IF EXISTS SQ",
        "TRUNCATE TABLE T",
        "CREATE INDEX IF NOT EXISTS IX ON T (A ASC, B DESC)",
        "DROP INDEX IF EXISTS IX",
        "BEGIN TRANSACTION",
        "COMMIT",
        "ROLLBACK",
        "ROLLBACK TO SAVEPOINT SP",
        "SAVEPOINT SP",
        "RELEASE SAVEPOINT SP",
        "SET TRANSACTION ISOLATION LEVEL READ COMMITTED",
        "SET TRANSACTION ISOLATION LEVEL SERIALIZABLE",
        "SET TRANSACTION ISOLATION LEVEL SNAPSHOT",
        "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'no'",
        "EXPLAIN SELECT * FROM T",
        "EXPLAIN QUERY PLAN SELECT * FROM T",
        "SELECT * FROM T; SELECT * FROM S",
    ];

    /// <summary>Every corpus entry, which is what the harnesses actually iterate.</summary>
    public static IEnumerable<string> All =>
        ExpressionPositions
            .Concat(ExpressionAlternatives)
            .Concat(PrecedenceShapes)
            .Concat(Statements);
}
