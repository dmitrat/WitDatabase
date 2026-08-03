using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;

namespace OutWit.Database.Benchmarks;

/// <summary>
/// Base configuration for all SQL engine benchmarks.
/// </summary>
public class SqlEngineBenchmarkConfig : ManualConfig
{
    public SqlEngineBenchmarkConfig()
    {
        SummaryStyle = SummaryStyle.Default
            .WithRatioStyle(RatioStyle.Trend)
            .WithTimeUnit(Perfolizer.Horology.TimeUnit.Millisecond);
        HideColumns(Column.Error, Column.StdDev, Column.RatioSD);
    }
}

/// <summary>
/// WitDb engine mode for benchmarking different storage and parallelism configurations.
/// </summary>
public enum WitDbEngineMode
{
    /// <summary>
    /// Exactly what a consumer gets from <c>Data Source=…</c> and nothing else: MVCC on, durable
    /// commit, B+Tree.
    /// </summary>
    /// <remarks>
    /// The modes below all pass <c>MVCC=false</c>, which is not the provider default - MVCC defaults
    /// to true in <c>WitDbConnectionStringBuilder</c>, so this is the configuration behind every
    /// ADO.NET and EF Core consumer. Measuring only the tuned modes is how a published figure ends up
    /// describing a configuration nobody runs.
    /// </remarks>
    Default,

    /// <summary>
    /// Everything in memory: <c>Mode=Memory</c>, no file behind it.
    /// </summary>
    /// <remarks>
    /// Not a configuration anyone deploys - it exists to split a measurement. When a cost is fixed
    /// per operation and independent of table size, running the same shape with no file underneath
    /// says whether the cost lives in the storage layer or above it.
    /// </remarks>
    Memory,

    /// <summary>
    /// BTree storage engine without parallel writes.
    /// Best for read-heavy workloads.
    /// </summary>
    BTree,

    /// <summary>
    /// LSM-Tree storage engine without parallel writes.
    /// Best for write-heavy workloads.
    /// </summary>
    Lsm,

    /// <summary>
    /// BTree storage engine with Auto parallel write mode.
    /// Automatically selects optimal parallelism strategy.
    /// </summary>
    BTreeParallelAuto,

    /// <summary>
    /// LSM-Tree storage engine with Auto parallel write mode.
    /// Automatically selects optimal parallelism strategy.
    /// </summary>
    LsmParallelAuto
}

/// <summary>
/// Lets a sweep be narrowed without editing the benchmark classes.
/// </summary>
/// <remarks>
/// The full matrix is five engine modes times two or three table sizes across seven classes -
/// roughly 1,175 benchmark cases, which is hours per pass. A baseline that is only ever taken once
/// is not a baseline: a single run of anything in this repository has twice reported the opposite
/// of what repeated runs reported. Narrowing the matrix from the environment is what makes running
/// the same sweep twice and comparing the spread affordable.
///
/// <c>WITDB_BENCH_MODES=Default,BTree</c> and <c>WITDB_BENCH_SIZES=min</c> (or an explicit list such
/// as <c>1000,5000</c>). Unset means the whole matrix, so a plain run is unchanged.
/// </remarks>
public static class BenchmarkSweep
{
    public static IEnumerable<WitDbEngineMode> Modes(params WitDbEngineMode[] all)
    {
        var requested = Environment.GetEnvironmentVariable("WITDB_BENCH_MODES");
        if (string.IsNullOrWhiteSpace(requested))
            return all;

        var names = requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var selected = all.Where(m => names.Contains(m.ToString(), StringComparer.OrdinalIgnoreCase)).ToArray();

        return selected.Length > 0
            ? selected
            : NoIntersection("WITDB_BENCH_MODES", requested, all);
    }

    public static IEnumerable<int> Sizes(params int[] all)
    {
        var requested = Environment.GetEnvironmentVariable("WITDB_BENCH_SIZES");
        if (string.IsNullOrWhiteSpace(requested))
            return all;

        if (requested.Equals("min", StringComparison.OrdinalIgnoreCase))
            return new[] { all.Min() };

        if (requested.Equals("max", StringComparison.OrdinalIgnoreCase))
            return new[] { all.Max() };

        var wanted = requested
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();

        var selected = all.Where(wanted.Contains).ToArray();

        return selected.Length > 0
            ? selected
            : NoIntersection("WITDB_BENCH_SIZES", requested, all);
    }

    /// <summary>
    /// What to do when the requested narrowing does not apply to this class at all.
    /// </summary>
    /// <remarks>
    /// This threw at first, on the principle that an empty selection silently measures nothing.
    /// The principle is right and the implementation was wrong: BenchmarkDotNet evaluates every
    /// class's <c>[ParamsSource]</c> before it applies <c>--filter</c>, so asking for 100,000 rows
    /// while filtering to one class killed the whole run on a class that was never going to be
    /// measured. Found by using it, which is the only way this kind of thing is found.
    ///
    /// So: keep the class's own values, and say so loudly. Nothing is measured silently, and a
    /// narrowing aimed at one class no longer breaks the others.
    /// </remarks>
    private static T[] NoIntersection<T>(string variable, string requested, T[] all)
    {
        Console.Error.WriteLine(
            $"[sweep] {variable}='{requested}' matches none of [{string.Join(", ", all)}] " +
            "- this class keeps its own values. Filter it out if you did not mean to run it.");

        return all;
    }
}

/// <summary>
/// Helper class for creating WitDb connections with different configurations.
/// </summary>
public static class WitDbConnectionHelper
{
    public static string BuildConnectionString(string path, WitDbEngineMode mode)
    {
        return mode switch
        {
            // Deliberately nothing but the data source: whatever the provider defaults to is what
            // this measures.
            WitDbEngineMode.Default =>
                $"Data Source={path}",

            // No Data Source at all: the builder requires one unless the mode is Memory.
            WitDbEngineMode.Memory =>
                "Mode=Memory",

            WitDbEngineMode.BTree =>
                $"Data Source={path};Store=btree;Transactions=true;MVCC=false",
            
            WitDbEngineMode.Lsm => 
                $"Data Source={path};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false",
            
            WitDbEngineMode.BTreeParallelAuto => 
                $"Data Source={path};Store=btree;Transactions=true;MVCC=false",
            
            WitDbEngineMode.LsmParallelAuto => 
                $"Data Source={path};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false",
            
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}

/// <summary>
/// Checks that a write benchmark actually wrote, outside the timed region.
/// </summary>
/// <remarks>
/// The write benchmarks returned <c>void</c>, so the equivalence check could say nothing about them:
/// an engine that silently wrote nothing benchmarked as fast. The obvious repair - return the
/// affected-row count - is not enough on its own, and this project knows exactly why. The worst
/// defect ever found here was <c>Store=lsm</c> with a parallel mode **losing acknowledged writes**:
/// ten <c>INSERT</c>s all reported success and 0-1 rows were present afterwards. Affected rows is
/// the acknowledgement that lied.
///
/// So the claim and the data are checked separately. Each write benchmark returns what the engine
/// claimed, and <see cref="Verify"/> counts what a scan can actually see. It never asks
/// <c>COUNT(*)</c>: on this engine that is answered from a cached per-table counter, which is
/// separate state and has disagreed with the rows after a crash.
/// </remarks>
public static class WriteVerification
{
    /// <summary>
    /// Counts the rows a scan actually yields. Deliberately not <c>COUNT(*)</c>.
    /// </summary>
    public static int CountRowsByScan(System.Data.Common.DbConnection connection, string table)
    {
        using var c = connection.CreateCommand();
        c.CommandText = $"SELECT Id FROM {table}";

        using var r = c.ExecuteReader();

        var count = 0;
        while (r.Read())
            count++;

        return count;
    }

    /// <summary>
    /// Throws if the rows a scan can see do not match what the engine said it wrote.
    /// </summary>
    public static void Verify(string engine, int claimed, int scanned)
    {
        if (claimed != scanned)
            throw new InvalidOperationException(
                $"{engine} claimed {claimed} row(s) written but a scan sees {scanned}. " +
                "The benchmark timed a write that did not happen as reported.");
    }
}

/// <summary>
/// Helper class for generating unique benchmark paths.
/// </summary>
public static class BenchmarkPathHelper
{
    /// <summary>
    /// Generates a unique path for benchmark database files.
    /// Uses a new GUID each time to guarantee uniqueness.
    /// </summary>
    public static string GenerateUniquePath(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        return path;
    }

    /// <summary>
    /// Safely cleans up a path (file or directory) with retries.
    /// </summary>
    public static void SafeCleanup(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
            
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else if (File.Exists(path))
                    File.Delete(path);
                return; // Success
            }
            catch
            {
                if (attempt < 4)
                    Thread.Sleep(50 * (attempt + 1)); // Increasing delay
            }
        }
    }
}

#region LiteDB Document Classes for Benchmarks

/// <summary>
/// Generic document for INSERT benchmarks.
/// </summary>
public class BenchmarkDoc
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// User document for query benchmarks.
/// </summary>
public class BenchmarkUser
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
    public string City { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Order document for join benchmarks.
/// </summary>
public class BenchmarkOrder
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Order item document for join benchmarks.
/// </summary>
public class BenchmarkOrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// Product document for join benchmarks.
/// </summary>
public class BenchmarkProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
}

#endregion
