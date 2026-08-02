using BenchmarkDotNet.Running;

namespace OutWit.Database.Benchmarks;

public class Program
{
    public static int Main(string[] args)
    {
        // "verify" runs the equivalence check rather than a timing sweep: every benchmark body
        // once, comparing what WitDatabase, SQLite and LiteDB actually return. A timing comparison
        // between engines that do not compute the same thing is not a measurement, so this is meant
        // to be green before any sweep is believed. Exits non-zero on a disagreement.
        if (args.Length > 0 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            var mode = args.Length > 1
                ? Enum.Parse<WitDbEngineMode>(args[1], ignoreCase: true)
                : WitDbEngineMode.Default;

            return EquivalenceCheck.Run(mode);
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
