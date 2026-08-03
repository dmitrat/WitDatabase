namespace OutWit.Database.CrashRunner;

/// <summary>
/// Runs one durability scenario in a process of its own, so that "the process died exactly here" can
/// be arranged rather than approximated.
/// </summary>
/// <remarks>
/// Why a second process is unavoidable: a file-backed engine opens its storage with
/// <c>FileShare.None</c>, so a second engine cannot be opened over a live database - and disposing
/// the first engine is precisely the operation that flushes the metadata a crash test is trying to
/// catch unflushed. Every in-process trick used elsewhere in the suite (an in-memory inner store
/// outliving the wrapper that never got to flush) works at the store layer and cannot reach the
/// engine's schema.
///
/// Usage:
///   OutWit.Database.CrashRunner --scenario &lt;name&gt; --path &lt;database&gt; [--rows N] [--table T]
///     [--settings "MVCC=false;Journal=wal"]
/// </remarks>
public static class Program
{
    #region Constants

    private const int DEFAULT_ROWS = 20;
    private const string DEFAULT_TABLE = "T";

    #endregion

    #region Main

    public static int Main(string[] args)
    {
        string? scenario = null;
        string? path = null;
        var rows = DEFAULT_ROWS;
        var table = DEFAULT_TABLE;
        var settings = "";

        for (int i = 0; i < args.Length - 1; i += 2)
        {
            switch (args[i])
            {
                case "--scenario": scenario = args[i + 1]; break;
                case "--path": path = args[i + 1]; break;
                case "--table": table = args[i + 1]; break;
                case "--settings": settings = args[i + 1]; break;
                case "--rows":
                    if (!int.TryParse(args[i + 1], out rows) || rows <= 0)
                        return Usage($"--rows must be a positive integer, got '{args[i + 1]}'");
                    break;
                default:
                    return Usage($"unknown argument '{args[i]}'");
            }
        }

        if (string.IsNullOrWhiteSpace(scenario))
            return Usage("--scenario is required");

        if (string.IsNullOrWhiteSpace(path))
            return Usage("--path is required");

        try
        {
            var exitCode = Scenarios.Run(scenario, new ScenarioContext(path, rows, table, settings));

            if (exitCode == null)
                return Usage($"unknown scenario '{scenario}'");

            return exitCode.Value;
        }
        catch (Exception e)
        {
            // Reported on stderr and with a distinct exit code, so that a scenario that broke is
            // never mistaken for a scenario whose subject misbehaved.
            Console.Error.WriteLine($"{CrashProtocol.FAILED} {e.GetType().Name}: {e.Message}");
            Console.Error.WriteLine(e.StackTrace);
            Console.Error.Flush();

            return CrashProtocol.EXIT_SCENARIO_FAILED;
        }
    }

    #endregion

    #region Tools

    private static int Usage(string problem)
    {
        Console.Error.WriteLine($"{CrashProtocol.FAILED} {problem}");
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            "usage: OutWit.Database.CrashRunner --scenario <name> --path <database> [--rows N] "
            + "[--table T] [--settings \"MVCC=false;Journal=wal\"]");
        Console.Error.WriteLine($"scenarios: {string.Join(", ", Scenarios.Names)}");
        Console.Error.Flush();

        return CrashProtocol.EXIT_USAGE;
    }

    #endregion
}
