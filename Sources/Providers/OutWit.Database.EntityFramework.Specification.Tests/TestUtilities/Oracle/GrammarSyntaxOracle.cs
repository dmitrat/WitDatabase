using System.Text;
using Microsoft.Data.Sqlite;
using OutWit.Database.Parser;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// The differential oracle for phase 3, at the level of <b>syntax</b> rather than of conformance
/// suites.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SqliteTestStore"/> answers "does this EF Core suite pass on SQLite". This answers the
/// question phase 3 actually asks: <b>does SQLite accept this SQL shape at all.</b> Both are needed,
/// because the phase-3 backlog is a list of grammar shapes, not a list of suites.
/// </para>
/// <para>
/// Why it exists: during phase 2, nine of the 29 EF findings were misattributed, and every correction
/// came from running the same thing on SQLite instead of reading code. Three of the phase-3
/// ignored cases are written in SQL Server syntax (<c>TOP</c>, <c>CROSS APPLY</c>), so the
/// same check has to happen before anything is built rather than after.
///
/// NB: the phrase "ignored cases" above deliberately avoids the literal marker text. The project's
/// ledger command counts markers with a plain grep, so a prose mention of one inflates the backlog.
/// </para>
/// <para>
/// This is a <b>characterisation</b> fixture: it records what each engine does and asserts nothing
/// about which is right. The bar is parity with the provider WitDatabase substitutes for, so a shape
/// that SQLite also rejects is not a WitDatabase defect.
/// </para>
/// </remarks>
[Trait("Category", "Oracle")]
public class GrammarSyntaxOracle
{
    /// <summary>
    /// The schema every shape is checked against, so that a failure is a syntax failure rather than
    /// an unresolved name.
    /// </summary>
    private const string Schema = """
        CREATE TABLE A (Id INTEGER PRIMARY KEY, Name TEXT);
        CREATE TABLE B (Id INTEGER PRIMARY KEY, AId INTEGER, Name TEXT);
        CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT, Age INTEGER, Flags INTEGER, Active INTEGER);
        CREATE TABLE G (Id INTEGER PRIMARY KEY AUTOINCREMENT);
        """;

    public static TheoryData<string, string> Shapes()
    {
        var data = new TheoryData<string, string>();

        // The item each shape belongs to, then the shape itself.
        void Add(string item, string sql) => data.Add(item, sql);

        // 1 - BETWEEN precedence. Both engines must agree on what this MEANS, which syntax alone
        //     cannot show; recorded here so the shape is on file, settled by execution in PR 2/3.
        Add("betweenPrecedence", "SELECT * FROM T WHERE Age BETWEEN 18 AND 65 AND Active = 1");
        Add("betweenPrecedence", "SELECT * FROM T WHERE Age NOT BETWEEN 18 AND 65 OR Active = 1");
        Add("betweenInCase", "SELECT CASE WHEN Age BETWEEN 1 AND 10 THEN 'a' ELSE 'b' END FROM T");
        Add("betweenSubqueryBound", "SELECT * FROM T WHERE Age BETWEEN (SELECT MIN(Age) FROM T) AND (SELECT MAX(Age) FROM T)");

        // 2 - INSERT ... DEFAULT VALUES.
        Add("insertDefaultValues", "INSERT INTO G DEFAULT VALUES");

        // 3 - Hexadecimal literals.
        Add("hexLiteral", "SELECT 0x1F");
        Add("hexLiteral", "SELECT * FROM T WHERE Flags & 0x0F = 1");

        // 4 - VALUES as a table source, and the derived column list the ignored test also needs.
        Add("valuesTableSource", "SELECT * FROM (VALUES (1), (2))");
        Add("valuesDerivedColumns", "SELECT * FROM (VALUES (1), (2)) AS V(Id)");

        // 5 - APPLY, and TOP, which two of the ignored cases use. Both are T-SQL.
        Add("crossApply", "SELECT * FROM A CROSS APPLY (SELECT * FROM B WHERE B.AId = A.Id LIMIT 1) x");
        Add("outerApply", "SELECT * FROM A OUTER APPLY (SELECT * FROM B WHERE B.AId = A.Id LIMIT 1) x");
        Add("top", "SELECT TOP 1 * FROM B");
        // The ignored cases verbatim, TOP and all - the shape actually on file today.
        Add("crossApplyAsWritten", "SELECT * FROM A CROSS APPLY (SELECT TOP 1 * FROM B WHERE B.AId = A.Id) x");
        Add("valuesAsWritten", "SELECT * FROM (VALUES (1), (2)) AS V(Id)");
        // What the engine already supports, as the control for decision 6.2/6.3.
        Add("lateralAlternative", "SELECT * FROM A LEFT JOIN B ON B.AId = A.Id");

        // Chained comparisons. Added when the boolean layer was split: `predicate` is deliberately
        // not left-recursive, so `a = b = c` can no longer parse as `(a = b) = c`. Whether that is a
        // regression depends on what SQLite does, not on what reads well.
        Add("chainedComparison", "SELECT * FROM T WHERE Age = 1 = 1");
        Add("chainedComparison", "SELECT * FROM T WHERE Age < 5 < 3");
        Add("parenthesisedComparison", "SELECT * FROM T WHERE (Age = 1) = 1");

        // 6/7 - UDFs and stored procedures. Split out of phase 3; recorded so the decision has a
        //       measurement behind it rather than an assumption.
        Add("createFunction", "CREATE FUNCTION Doubled(x INT) RETURNS INT BEGIN RETURN x * 2; END");
        Add("createProcedure", "CREATE PROCEDURE AddOne(IN x INT) BEGIN SELECT x + 1; END");

        return data;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ShapeIsAcceptedOrRejectedByBothEngines(string item, string sql)
    {
        var sqlite = CheckSqlite(sql);
        var witDb = CheckWitDatabase(sql);

        var verdict = (sqlite.Accepted, witDb.Accepted) switch
        {
            (true, true) => "PARITY - both accept",
            (false, false) => "PARITY - both reject (inherited limit, NOT a WitDatabase defect)",
            (true, false) => "DIVERGENCE - SQLite accepts, WitDatabase rejects (a real gap)",
            (false, true) => "DIVERGENCE - WitDatabase accepts, SQLite rejects (a superset)"
        };

        var report = new StringBuilder()
            .AppendLine($"item     : {item}")
            .AppendLine($"sql      : {sql}")
            .AppendLine($"sqlite   : {Describe(sqlite)}")
            .AppendLine($"witdb    : {Describe(witDb)}")
            .AppendLine($"verdict  : {verdict}")
            .ToString();

        // Characterisation only. The oracle records; it does not decide.
        Assert.True(true, report);
        Console.WriteLine(report);
    }

    /// <summary>
    /// The shapes both engines <b>accept</b>, so the question becomes whether they <b>agree</b>.
    /// </summary>
    /// <remarks>
    /// Added after the acceptance sweep above reported "PARITY - both accept" for <c>SELECT 0x1F</c>.
    /// That verdict is false: SQLite reads it as the integer 31, WitDatabase reads it as <c>0</c>
    /// aliased <c>x1F</c>. Both parse; they return different answers. An oracle that only asks
    /// "was it accepted" reports parity on a silently wrong result - and it would miss the whole of
    /// the BETWEEN defect too, since that shape also parses on both.
    /// </remarks>
    public static TheoryData<string, string> AgreementShapes()
    {
        var data = new TheoryData<string, string>();

        void Add(string item, string sql) => data.Add(item, sql);

        // The BETWEEN defect itself: accepted by both, answered differently by both.
        Add("betweenPrecedence", "SELECT Id FROM T WHERE Age BETWEEN 18 AND 65 AND Active = 1 ORDER BY Id");
        Add("notBetweenPrecedence", "SELECT Id FROM T WHERE Age NOT BETWEEN 1 AND 20 AND Active = 0 ORDER BY Id");
        Add("betweenTrailingOr", "SELECT Id FROM T WHERE Age NOT BETWEEN 18 AND 65 OR Active = 1 ORDER BY Id");
        Add("betweenInCase", "SELECT CASE WHEN Age BETWEEN 1 AND 35 THEN 1 ELSE 0 END FROM T ORDER BY Id");
        Add("betweenSubqueryBound", "SELECT Id FROM T WHERE Age BETWEEN (SELECT MIN(Age) FROM T) AND (SELECT MAX(Age) FROM T) ORDER BY Id");

        // The shapes that combine a BETWEEN with the trailing AND that used to break it, in each of
        // the positions the boolean-layer split re-pointed. Expected values for the engine tests are
        // taken from here rather than reasoned out.
        Add("betweenSubqueryBoundThenAnd", "SELECT Id FROM T WHERE Age BETWEEN (SELECT MIN(Age) FROM T) AND 35 AND Active = 1 ORDER BY Id");
        Add("betweenInCaseThenAnd", "SELECT CASE WHEN Age BETWEEN 1 AND 35 AND Active = 1 THEN 1 ELSE 0 END FROM T ORDER BY Id");
        Add("twoBetweensConjoined", "SELECT Id FROM T WHERE Age BETWEEN 1 AND 35 AND Active BETWEEN 1 AND 2 ORDER BY Id");
        Add("betweenThenAndInsideNot", "SELECT Id FROM T WHERE NOT (Age BETWEEN 1 AND 35 AND Active = 1) ORDER BY Id");
        Add("betweenThenAndInHaving", "SELECT Active FROM T GROUP BY Active HAVING COUNT(*) BETWEEN 1 AND 5 AND Active = 1");
        // Isolating the HAVING divergence: is it BETWEEN, or is it any aggregate outside a plain
        // comparison? Narrowest forms first.
        Add("havingCountCompare", "SELECT Active FROM T GROUP BY Active HAVING COUNT(*) > 1");
        Add("havingCountBetween", "SELECT Active FROM T GROUP BY Active HAVING COUNT(*) BETWEEN 1 AND 5");
        Add("havingCountIn", "SELECT Active FROM T GROUP BY Active HAVING COUNT(*) IN (1, 2)");
        Add("havingCountCompareAnd", "SELECT Active FROM T GROUP BY Active HAVING COUNT(*) > 1 AND Active = 1");

        // The false parity that prompted this whole theory.
        Add("hexLiteral", "SELECT 0x1F");

        // Controls: shapes with no known divergence, so a red here means the harness is wrong.
        Add("control", "SELECT Id FROM T WHERE Age > 18 AND Active = 1 ORDER BY Id");
        Add("control", "SELECT COUNT(*) FROM T");

        return data;
    }

    [Theory]
    [MemberData(nameof(AgreementShapes))]
    public void AcceptedShapeReturnsTheSameAnswerOnBothEngines(string item, string sql)
    {
        var sqlite = RunSqlite(sql);
        var witDb = RunWitDatabase(sql);

        var agree = sqlite.Error is null && witDb.Error is null &&
                    string.Equals(sqlite.Rows, witDb.Rows, StringComparison.Ordinal);

        var report = new StringBuilder()
            .AppendLine($"item     : {item}")
            .AppendLine($"sql      : {sql}")
            .AppendLine($"sqlite   : {sqlite.Error ?? sqlite.Rows}")
            .AppendLine($"witdb    : {witDb.Error ?? witDb.Rows}")
            .AppendLine($"verdict  : {(agree ? "AGREE" : "DISAGREE - the answers differ")}")
            .ToString();

        Assert.True(true, report);
        Console.WriteLine(report);
    }

    private static Answer RunSqlite(string sql)
    {
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            using (var setup = connection.CreateCommand())
            {
                setup.CommandText = Schema + Seed;
                setup.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            using var reader = command.ExecuteReader();

            var rows = new List<string>();
            while (reader.Read())
            {
                rows.Add(reader.IsDBNull(0) ? "NULL" : reader.GetValue(0).ToString() ?? "NULL");
            }

            return new Answer(string.Join(",", rows), null);
        }
        catch (Exception exception)
        {
            return new Answer(string.Empty, $"{exception.GetType().Name}: {Flatten(exception.Message)}");
        }
    }

    private static Answer RunWitDatabase(string sql)
    {
        try
        {
            using var database = Core.Builder.WitDatabase.CreateInMemory();
            using var engine = new Database.Engine.WitSqlEngine(database, ownsStore: true);

            foreach (var statement in (Schema + Seed).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    engine.Execute(statement.Trim());
                }
            }

            var rows = engine.Query(sql)
                .Select(row => Normalise(row[0].ToString()))
                .ToList();

            return new Answer(string.Join(",", rows), null);
        }
        catch (Exception exception)
        {
            return new Answer(string.Empty, $"{exception.GetType().Name}: {Flatten(exception.Message)}");
        }
    }

    /// <summary>Rows shared by both engines, so a difference in answer is a difference in semantics.</summary>
    private const string Seed = """
        INSERT INTO T (Id, Name, Age, Flags, Active) VALUES (1, 'alice', 30, 15, 1);
        INSERT INTO T (Id, Name, Age, Flags, Active) VALUES (2, 'bob', 10, 15, 1);
        INSERT INTO T (Id, Name, Age, Flags, Active) VALUES (3, 'anna', 40, 15, 0);
        """;

    /// <summary>
    /// Strips WitDatabase's <c>Type:value</c> rendering down to the value, so the comparison is about
    /// the answer rather than about how each engine formats it.
    /// </summary>
    /// <remarks>
    /// The two <c>control</c> shapes exist to police exactly this: they have no known divergence, so
    /// if the harness ever reports them as DISAGREE the harness is wrong, not the engine. They caught
    /// this on the first run - <c>SELECT COUNT(*)</c> came back as <c>3</c> against <c>Integer:3</c>.
    /// </remarks>
    private static string Normalise(string? rendered)
    {
        if (string.IsNullOrEmpty(rendered))
        {
            return "NULL";
        }

        var separator = rendered.IndexOf(':');

        return separator >= 0 ? rendered[(separator + 1)..] : rendered;
    }

    private readonly record struct Answer(string Rows, string? Error);

    private static Result CheckSqlite(string sql)
    {
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();

            using (var schema = connection.CreateCommand())
            {
                schema.CommandText = Schema;
                schema.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;

            // Prepare compiles the statement without running it, so a shape that would change data
            // is still checked, and an unresolved name is reported as loudly as a syntax error.
            command.Prepare();

            return new Result(true, null);
        }
        catch (Exception exception)
        {
            return new Result(false, $"{exception.GetType().Name}: {Flatten(exception.Message)}");
        }
    }

    private static Result CheckWitDatabase(string sql)
    {
        try
        {
            WitSql.Parse(sql);

            return new Result(true, null);
        }
        catch (Exception exception)
        {
            return new Result(false, $"{exception.GetType().Name}: {Flatten(exception.Message)}");
        }
    }

    private static string Describe(Result result) =>
        result.Accepted ? "ACCEPTED" : $"REJECTED - {result.Error}";

    private static string Flatten(string message) =>
        message.ReplaceLineEndings(" ").Trim();

    private readonly record struct Result(bool Accepted, string? Error);
}
