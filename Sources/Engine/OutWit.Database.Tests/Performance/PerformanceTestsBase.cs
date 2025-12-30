using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.Tests.Performance;

/// <summary>
/// Base class for performance tests providing common test infrastructure.
/// </summary>
public abstract class PerformanceTestsBase
{
    #region Fields

    protected WitSqlEngine m_engine = null!;

    #endregion

    #region Setup

    [SetUp]
    public virtual void Setup()
    {
        var database = WitDatabase.CreateInMemory();
        m_engine = new WitSqlEngine(database, ownsStore: true);
    }

    [TearDown]
    public virtual void TearDown()
    {
        m_engine?.Dispose();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a test table with Id, Name, Value columns.
    /// </summary>
    protected void CreateTestTable()
    {
        m_engine.Execute(@"
            CREATE TABLE T (
                Id INT PRIMARY KEY,
                Name VARCHAR(100),
                Value DOUBLE
            )");
    }

    /// <summary>
    /// Inserts specified number of rows.
    /// </summary>
    protected void InsertRows(int count)
    {
        for (int i = 0; i < count; i++)
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
    }

    /// <summary>
    /// Measures allocated memory during an action.
    /// </summary>
    protected static long MeasureAllocatedBytes(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var before = GC.GetTotalAllocatedBytes(precise: true);
        action();
        var after = GC.GetTotalAllocatedBytes(precise: true);
        
        return after - before;
    }

    /// <summary>
    /// Measures execution time of an action.
    /// </summary>
    protected static TimeSpan MeasureTime(Action action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        action();
        sw.Stop();
        return sw.Elapsed;
    }

    #endregion
}
