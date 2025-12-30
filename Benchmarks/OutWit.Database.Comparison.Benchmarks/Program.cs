using BenchmarkDotNet.Running;

namespace OutWit.Database.Comparison.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        // Run all benchmarks
        var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        
        // Or run specific benchmarks:
        // BenchmarkRunner.Run<InsertBenchmarks>();
        // BenchmarkRunner.Run<SelectBenchmarks>();
        // BenchmarkRunner.Run<TransactionBenchmarks>();
        // BenchmarkRunner.Run<MixedWorkloadBenchmarks>();
        // BenchmarkRunner.Run<ConcurrencyBenchmarks>();
    }
}
