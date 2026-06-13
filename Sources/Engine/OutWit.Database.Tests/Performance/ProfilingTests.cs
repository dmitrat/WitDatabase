using OutWit.Database.Sql;
using OutWit.Database.Utils;
using OutWit.Database.Values;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Detailed profiling tests to identify specific bottlenecks.
/// </summary>
[TestFixture]
[Category("Performance")]
public class ProfilingTests : PerformanceTestsBase
{
    #region Constants

    private const int ROW_COUNT = 500;

    #endregion

    #region SQL Parsing Tests

    [Test]
    public void SqlParsingOverheadTest()
    {
        // Measure just SQL parsing without execution
        var sql = "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)";
        
        // Warm up
        for (int i = 0; i < 10; i++)
        {
            Parser.WitSql.Parse(sql);
        }

        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                Parser.WitSql.Parse(sql);
            }
        });

        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                Parser.WitSql.Parse(sql);
            }
        });

        TestContext.Out.WriteLine($"SQL Parsing {ROW_COUNT}x:");
        TestContext.Out.WriteLine($"  Time: {elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Memory: {allocatedBytes / 1024.0:F2} KB");
        TestContext.Out.WriteLine($"  Per parse: {elapsed.TotalMilliseconds / ROW_COUNT:F3} ms, {allocatedBytes / ROW_COUNT:F0} bytes");
    }

    [Test]
    public void ExpressionParsingOverheadTest()
    {
        // Measure expression parsing (for defaults, checks, etc.)
        var expressions = new[] { "1", "@id", "@name", "NOW()", "Id + 1" };
        
        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                foreach (var expr in expressions)
                {
                    Parser.WitSql.ParseExpression(expr);
                }
            }
        });

        TestContext.Out.WriteLine($"Expression Parsing {ROW_COUNT * expressions.Length}x:");
        TestContext.Out.WriteLine($"  Time: {elapsed.TotalMilliseconds:F2} ms");
    }

    #endregion

    #region Insert Breakdown Tests

    [Test]
    public void InsertWithoutUniqueCheckTest()
    {
        // Table without PRIMARY KEY - should skip UNIQUE validation
        m_engine.Execute(@"
            CREATE TABLE NoKey (
                Id INT,
                Name VARCHAR(100),
                Value DOUBLE
            )");

        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                m_engine.Execute(
                    "INSERT INTO NoKey (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        });

        TestContext.Out.WriteLine($"INSERT without PK (no UNIQUE check) {ROW_COUNT}x:");
        TestContext.Out.WriteLine($"  Time: {elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Per insert: {elapsed.TotalMilliseconds / ROW_COUNT:F3} ms");
    }

    [Test]
    public void InsertWithAutoIncrementPkTest()
    {
        // Table with AUTOINCREMENT PK - value is generated
        m_engine.Execute(@"
            CREATE TABLE AutoKey (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100),
                Value DOUBLE
            )");

        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                m_engine.Execute(
                    "INSERT INTO AutoKey (Name, Value) VALUES (@name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        });

        TestContext.Out.WriteLine($"INSERT with AUTOINCREMENT PK {ROW_COUNT}x:");
        TestContext.Out.WriteLine($"  Time: {elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Per insert: {elapsed.TotalMilliseconds / ROW_COUNT:F3} ms");
    }

    [Test]
    public void InsertWithExplicitPkTest()
    {
        // Table with explicit INT PK - UNIQUE check required
        CreateTestTable();

        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                m_engine.Execute(
                    "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        });

        TestContext.Out.WriteLine($"INSERT with explicit PK (UNIQUE check) {ROW_COUNT}x:");
        TestContext.Out.WriteLine($"  Time: {elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Per insert: {elapsed.TotalMilliseconds / ROW_COUNT:F3} ms");
    }

    #endregion

    #region Serialization Tests

    [Test]
    public void RowSerializationOverheadTest()
    {
        CreateTestTable();
        var table = m_engine.GetTable("T")!;
        
        var row = new WitSqlRow(
            [WitSqlValue.FromInt(1), WitSqlValue.FromText("Test"), WitSqlValue.FromDecimal(1.5m)],
            ["Id", "Name", "Value"]);

        // Warm up
        for (int i = 0; i < 10; i++)
        {
            table.SerializeRow(row);
        }

        var elapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                table.SerializeRow(row);
            }
        });

        var allocatedBytes = MeasureAllocatedBytes(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                table.SerializeRow(row);
            }
        });

        TestContext.Out.WriteLine($"Row Serialization {ROW_COUNT}x:");
        TestContext.Out.WriteLine($"  Time: {elapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"  Memory: {allocatedBytes / 1024.0:F2} KB");
        TestContext.Out.WriteLine($"  Per serialize: {elapsed.TotalMilliseconds / ROW_COUNT:F3} ms");
    }

    #endregion

    #region Prepared Statement Tests

    [Test]
    public void PreparedStatementVsDirectExecuteTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Prepared (
                Id INT,
                Name VARCHAR(100),
                Value DOUBLE
            )");

        // Direct execute (parses SQL each time)
        var directElapsed = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                m_engine.Execute(
                    "INSERT INTO Prepared (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        });

        m_engine.Execute("DELETE FROM Prepared");

        // Using Prepare (parses SQL once)
        var preparedElapsed = MeasureTime(() =>
        {
            using var stmt = m_engine.Prepare("INSERT INTO Prepared (Id, Name, Value) VALUES (@id, @name, @value)");
            for (int i = 0; i < ROW_COUNT; i++)
            {
                stmt.SetParameter("@id", i)
                    .SetParameter("@name", $"Name{i}")
                    .SetParameter("@value", i * 1.5)
                    .Execute();
            }
        });

        TestContext.Out.WriteLine($"Direct Execute {ROW_COUNT}x: {directElapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"Prepared Statement {ROW_COUNT}x: {preparedElapsed.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"Speedup: {directElapsed.TotalMilliseconds / preparedElapsed.TotalMilliseconds:F2}x");
    }

    #endregion

    #region Full Pipeline Breakdown

    [Test]
    public void FullInsertPipelineBreakdownTest()
    {
        CreateTestTable();
        
        // 1. Just SQL parsing
        var parseTime = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                Parser.WitSql.Parse("INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)");
            }
        });

        // 2. Full insert with all overhead
        m_engine.Execute("DROP TABLE T");
        CreateTestTable();
        
        var fullTime = MeasureTime(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                m_engine.Execute(
                    "INSERT INTO T (Id, Name, Value) VALUES (@id, @name, @value)",
                    new Dictionary<string, object?>
                    {
                        { "@id", i },
                        { "@name", $"Name{i}" },
                        { "@value", i * 1.5 }
                    });
            }
        });

        var executionTime = fullTime - parseTime;
        
        TestContext.Out.WriteLine($"=== INSERT Pipeline Breakdown ({ROW_COUNT} rows) ===");
        TestContext.Out.WriteLine($"SQL Parsing:  {parseTime.TotalMilliseconds:F2} ms ({parseTime.TotalMilliseconds / fullTime.TotalMilliseconds * 100:F1}%)");
        TestContext.Out.WriteLine($"Execution:    {executionTime.TotalMilliseconds:F2} ms ({executionTime.TotalMilliseconds / fullTime.TotalMilliseconds * 100:F1}%)");
        TestContext.Out.WriteLine($"Total:        {fullTime.TotalMilliseconds:F2} ms");
        TestContext.Out.WriteLine($"Per insert:   {fullTime.TotalMilliseconds / ROW_COUNT:F3} ms");
    }

    #endregion
}
