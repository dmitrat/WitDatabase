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

        // An empty selection would silently measure nothing at all, which is the failure mode this
        // whole exercise exists to avoid. Refuse instead.
        if (selected.Length == 0)
            throw new InvalidOperationException(
                $"WITDB_BENCH_MODES='{requested}' selected none of: {string.Join(", ", all)}");

        return selected;
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
        if (selected.Length == 0)
            throw new InvalidOperationException(
                $"WITDB_BENCH_SIZES='{requested}' selected none of: {string.Join(", ", all)}");

        return selected;
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

            WitDbEngineMode.BTree =>
                $"Data Source={path};Store=btree;Transactions=true;MVCC=false",
            
            WitDbEngineMode.Lsm => 
                $"Data Source={path};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false",
            
            WitDbEngineMode.BTreeParallelAuto => 
                $"Data Source={path};Store=btree;Transactions=true;MVCC=false;Parallel Mode=Auto",
            
            WitDbEngineMode.LsmParallelAuto => 
                $"Data Source={path};Store=lsm;Transactions=true;MVCC=false;SyncWrites=false;Parallel Mode=Auto",
            
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
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
