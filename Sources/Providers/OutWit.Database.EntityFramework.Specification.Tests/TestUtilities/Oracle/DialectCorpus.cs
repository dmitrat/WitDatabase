namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// Which dialect spells a capability how, so "does the drop-in target have this" can be measured
/// rather than recalled.
/// </summary>
/// <remarks>
/// <para>
/// Phase 9 decides, per capability, whether WitDatabase should build it. The rule agreed for that is
/// <b>value for the drop-in goal × how often real code uses it</b>, and the first half of that cannot
/// be answered by the instruments this repository already has: every one of them compares against
/// SQLite, and SQLite lacks most of the list too. A shape SQLite also rejects tells us nothing about
/// whether PostgreSQL or SQL Server users expect it.
/// </para>
/// <para>
/// So each capability carries <b>one entry per dialect</b>, because the same capability is often
/// spelled differently: <c>TOP n</c> is SQL Server's and <c>LIMIT</c> is everyone else's;
/// <c>LATERAL</c> is PostgreSQL's and <c>CROSS APPLY</c> is SQL Server's. A capability that only one
/// dialect has, spelled a way only that dialect uses, is a much weaker case for building than one
/// both have.
/// </para>
/// <para>
/// <c>null</c> means <b>the dialect has no spelling for this</b> - which is itself a finding, and the
/// reason the report distinguishes it from "rejected".
/// </para>
/// </remarks>
public static class DialectCorpus
{
    #region Types

    public enum Dialect
    {
        Sqlite,
        PostgreSql,
        SqlServer
    }

    /// <param name="Capability">The phase-9 item this shape belongs to.</param>
    /// <param name="Sqlite">How SQLite spells it, or null if it has no spelling.</param>
    /// <param name="PostgreSql">How PostgreSQL spells it, or null.</param>
    /// <param name="SqlServer">How SQL Server spells it, or null.</param>
    /// <param name="WitDatabase">How WitDatabase would spell it if it had it - the shape phase 9 measured as absent.</param>
    public sealed record Entry(
        string Capability,
        string? Sqlite,
        string? PostgreSql,
        string? SqlServer,
        string WitDatabase)
    {
        public string? For(Dialect dialect) => dialect switch
        {
            Dialect.Sqlite => Sqlite,
            Dialect.PostgreSql => PostgreSql,
            Dialect.SqlServer => SqlServer,
            _ => null
        };
    }

    #endregion

    #region Schema

    /// <summary>
    /// Created on each server before the shapes run, so a rejection is about the shape rather than
    /// an unresolved name. Deliberately the plainest SQL that all three accept unchanged.
    /// </summary>
    public static readonly string[] Schema =
    [
        "CREATE TABLE T (Id INT PRIMARY KEY, Name VARCHAR(50), Age INT)",
        "CREATE TABLE S (Id INT PRIMARY KEY, TId INT, Score INT)",
        "INSERT INTO T (Id, Name, Age) VALUES (1, 'a', 10)",
        "INSERT INTO T (Id, Name, Age) VALUES (2, 'b', 20)",
        "INSERT INTO S (Id, TId, Score) VALUES (1, 1, 100)",
        "INSERT INTO S (Id, TId, Score) VALUES (2, 1, 200)"
    ];

    #endregion

    #region The list

    /// <summary>
    /// The seven capabilities phase 9 measured as genuinely absent from WitDatabase, plus the two the
    /// plan recorded as absent and which turned out to be present - kept so the report shows the
    /// whole list rather than only its open half.
    /// </summary>
    public static readonly Entry[] All =
    [
        new("lateral-join",
            Sqlite: null,
            PostgreSql: "SELECT T.Id, X.Score FROM T, LATERAL (SELECT Score FROM S WHERE S.TId = T.Id) AS X",
            SqlServer: null,
            WitDatabase: "SELECT T.Id, X.Score FROM T, LATERAL (SELECT Score FROM S WHERE S.TId = T.Id) AS X"),

        new("cross-apply",
            Sqlite: null,
            PostgreSql: null,
            SqlServer: "SELECT T.Id, X.Score FROM T CROSS APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X",
            WitDatabase: "SELECT T.Id, X.Score FROM T CROSS APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X"),

        new("outer-apply",
            Sqlite: null,
            PostgreSql: "SELECT T.Id, X.Score FROM T LEFT JOIN LATERAL (SELECT Score FROM S WHERE S.TId = T.Id) AS X ON TRUE",
            SqlServer: "SELECT T.Id, X.Score FROM T OUTER APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X",
            WitDatabase: "SELECT T.Id, X.Score FROM T OUTER APPLY (SELECT Score FROM S WHERE S.TId = T.Id) AS X"),

        // Deliberately WITHOUT a column alias list. The first version of this entry wrote
        // "(VALUES (1), (2)) AS V (N)", which entangles two capabilities: SQLite has the VALUES
        // source and lacks the derived column list, so its rejection was being attributed to the
        // wrong item. One capability per shape, or the report misdirects the decision it exists for.
        new("values-as-table-source",
            Sqlite: "SELECT * FROM (VALUES (1), (2))",
            PostgreSql: "SELECT * FROM (VALUES (1), (2)) AS V",
            SqlServer: "SELECT * FROM (VALUES (1), (2)) AS V (N)",
            WitDatabase: "SELECT * FROM (VALUES (1), (2)) AS V"),

        new("derived-column-list",
            Sqlite: "SELECT * FROM (SELECT Id FROM T) AS V (Alias)",
            PostgreSql: "SELECT * FROM (SELECT Id FROM T) AS V (Alias)",
            SqlServer: "SELECT * FROM (SELECT Id FROM T) AS V (Alias)",
            WitDatabase: "SELECT * FROM (SELECT Id FROM T) AS V (Alias)"),

        new("row-limit",
            Sqlite: "SELECT Id FROM T LIMIT 1",
            PostgreSql: "SELECT Id FROM T LIMIT 1",
            SqlServer: "SELECT TOP 1 Id FROM T",
            WitDatabase: "SELECT TOP 1 Id FROM T"),

        new("user-defined-function",
            Sqlite: null,
            PostgreSql: "CREATE FUNCTION Doubled(N INT) RETURNS INT AS $$ SELECT N * 2 $$ LANGUAGE SQL",
            SqlServer: "CREATE FUNCTION Doubled(@N INT) RETURNS INT AS BEGIN RETURN @N * 2 END",
            WitDatabase: "CREATE FUNCTION Doubled(N INT) RETURNS INT AS BEGIN RETURN N * 2; END"),

        new("stored-procedure",
            Sqlite: null,
            PostgreSql: "CREATE PROCEDURE GetAll() LANGUAGE SQL AS $$ SELECT 1 $$",
            SqlServer: "CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T END",
            WitDatabase: "CREATE PROCEDURE GetAll AS BEGIN SELECT * FROM T; END"),

        // Present in WitDatabase - carried so the report covers the whole phase-9 list.
        new("json-extract",
            Sqlite: "SELECT json_extract('{\"a\":1}', '$.a')",
            PostgreSql: "SELECT ('{\"a\":1}'::json) -> 'a'",
            SqlServer: "SELECT JSON_VALUE('{\"a\":1}', '$.a')",
            WitDatabase: "SELECT JSON_EXTRACT('{\"a\":1}', '$.a')"),

        new("aggregate-inside-between-in-having",
            Sqlite: "SELECT TId FROM S GROUP BY TId HAVING COUNT(*) BETWEEN 1 AND 5",
            PostgreSql: "SELECT TId FROM S GROUP BY TId HAVING COUNT(*) BETWEEN 1 AND 5",
            SqlServer: "SELECT TId FROM S GROUP BY TId HAVING COUNT(*) BETWEEN 1 AND 5",
            WitDatabase: "SELECT TId FROM S GROUP BY TId HAVING COUNT(*) BETWEEN 1 AND 5")
    ];

    #endregion
}
