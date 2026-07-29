using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using OutWit.Database.CrashRunner;

namespace OutWit.Database.Tests.Durability;

/// <summary>
/// Launches <see cref="OutWit.Database.CrashRunner"/> as a separate process and drives it.
/// </summary>
/// <remarks>
/// Every failure of the harness itself throws <see cref="CrashHarnessException"/> rather than
/// producing a wrong answer. That distinction is the whole point: a test that cannot start the
/// runner must not look like a database that lost data.
/// </remarks>
public static class CrashRunnerHarness
{
    #region Constants

    /// <summary>
    /// How long to wait for a protocol line. Generous, because a cold CI runner pays JIT and
    /// first-run assembly loading on top of the work itself - but bounded, because a scenario that
    /// never speaks must fail rather than hang the suite.
    /// </summary>
    internal static readonly TimeSpan LineTimeout = TimeSpan.FromSeconds(90);

    private const string RUNNER_DIRECTORY_KEY = "CrashRunnerDirectory";
    private const string RUNNER_ASSEMBLY = "OutWit.Database.CrashRunner.dll";

    #endregion

    #region Functions

    /// <summary>
    /// Starts a scenario and returns once it has announced <see cref="CrashProtocol.READY"/>.
    /// </summary>
    public static CrashRun Start(string scenario, string databasePath, int rows = 20, string table = "T")
    {
        var runner = ResolveRunnerAssembly();

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(runner);
        startInfo.ArgumentList.Add("--scenario");
        startInfo.ArgumentList.Add(scenario);
        startInfo.ArgumentList.Add("--path");
        startInfo.ArgumentList.Add(databasePath);
        startInfo.ArgumentList.Add("--rows");
        startInfo.ArgumentList.Add(rows.ToString());
        startInfo.ArgumentList.Add("--table");
        startInfo.ArgumentList.Add(table);

        var process = Process.Start(startInfo)
            ?? throw new CrashHarnessException($"could not start 'dotnet exec {runner}'");

        var run = new CrashRun(process, scenario);
        run.WaitFor(CrashProtocol.READY);

        return run;
    }

    /// <summary>
    /// Runs a scenario that is expected to finish on its own, and returns what it reported.
    /// </summary>
    public static CrashResult RunToCompletion(
        string scenario,
        string databasePath,
        int rows = 20,
        string table = "T")
    {
        using var run = Start(scenario, databasePath, rows, table);

        var facts = run.WaitFor(CrashProtocol.DONE);
        var exitCode = run.WaitForExit();

        if (exitCode != CrashProtocol.EXIT_OK)
        {
            throw new CrashHarnessException(
                $"scenario '{scenario}' reported DONE and then exited with {exitCode}{run.Diagnostics()}");
        }

        return new CrashResult(facts, exitCode);
    }

    #endregion

    #region Tools

    /// <summary>
    /// Finds the runner's built assembly. The directory is baked in at build time by the test
    /// project, so it always points at the matching configuration and target framework.
    /// </summary>
    private static string ResolveRunnerAssembly()
    {
        var directory = typeof(CrashRunnerHarness).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == RUNNER_DIRECTORY_KEY)?.Value;

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new CrashHarnessException(
                $"the test assembly carries no '{RUNNER_DIRECTORY_KEY}' metadata - the "
                + "AssemblyMetadata item in OutWit.Database.Tests.csproj is missing or was renamed");
        }

        var assembly = Path.Combine(directory, RUNNER_ASSEMBLY);

        if (!File.Exists(assembly))
        {
            throw new CrashHarnessException(
                $"the crash runner is not built: expected '{assembly}'. The test project has a "
                + "ProjectReference to it, so a build of this project should have produced it");
        }

        return assembly;
    }

    #endregion
}

/// <summary>
/// A running scenario.
/// </summary>
public sealed class CrashRun : IDisposable
{
    #region Fields

    private readonly Process m_process;
    private readonly string m_scenario;
    private readonly BlockingCollection<string> m_output = new();
    private readonly List<string> m_seen = new();
    private readonly List<string> m_errors = new();

    #endregion

    #region Constructors

    internal CrashRun(Process process, string scenario)
    {
        m_process = process;
        m_scenario = scenario;

        m_process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
                m_output.CompleteAdding();
            else
                m_output.Add(e.Data);
        };

        m_process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                lock (m_errors) m_errors.Add(e.Data);
        };

        m_process.BeginOutputReadLine();
        m_process.BeginErrorReadLine();
    }

    #endregion

    #region Functions

    /// <summary>
    /// Waits for a protocol line and returns the <c>key=value</c> facts it carried.
    /// </summary>
    public IReadOnlyDictionary<string, string> WaitFor(string prefix)
    {
        var deadline = DateTime.UtcNow + CrashRunnerHarness.LineTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (!m_output.TryTake(out var line, TimeSpan.FromMilliseconds(250)))
            {
                if (m_output.IsAddingCompleted && m_output.Count == 0)
                {
                    throw new CrashHarnessException(
                        $"scenario '{m_scenario}' ended without printing '{prefix}'{Diagnostics()}");
                }

                continue;
            }

            m_seen.Add(line);

            if (line == prefix || line.StartsWith(prefix + " ", StringComparison.Ordinal))
                return ParseFacts(line, prefix);
        }

        throw new CrashHarnessException(
            $"scenario '{m_scenario}' did not print '{prefix}' within "
            + $"{CrashRunnerHarness.LineTimeout.TotalSeconds:F0} s{Diagnostics()}");
    }

    /// <summary>
    /// Kills the process the way a power failure would: no shutdown, no flush, no dispose.
    /// </summary>
    public void Kill()
    {
        if (m_process.HasExited)
        {
            throw new CrashHarnessException(
                $"scenario '{m_scenario}' had already exited with {m_process.ExitCode} when the test "
                + $"went to kill it - so nothing was actually crashed{Diagnostics()}");
        }

        m_process.Kill(entireProcessTree: true);
        m_process.WaitForExit();
    }

    /// <summary>Waits for the process to exit on its own and returns its exit code.</summary>
    public int WaitForExit()
    {
        if (!m_process.WaitForExit(milliseconds: 90_000))
        {
            m_process.Kill(entireProcessTree: true);
            throw new CrashHarnessException(
                $"scenario '{m_scenario}' did not exit within 90 s{Diagnostics()}");
        }

        return m_process.ExitCode;
    }

    /// <summary>Everything the process said, for a failure message.</summary>
    public string Diagnostics()
    {
        lock (m_errors)
        {
            var stdout = m_seen.Count == 0 ? "(nothing)" : string.Join(" | ", m_seen);
            var stderr = m_errors.Count == 0 ? "(nothing)" : string.Join(" | ", m_errors);

            return $"\n  stdout: {stdout}\n  stderr: {stderr}";
        }
    }

    #endregion

    #region Tools

    private static IReadOnlyDictionary<string, string> ParseFacts(string line, string prefix)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);

        if (line.Length <= prefix.Length)
            return facts;

        foreach (var pair in line[(prefix.Length + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0)
                facts[pair[..separator]] = pair[(separator + 1)..];
        }

        return facts;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        try
        {
            if (!m_process.HasExited)
                m_process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cleanup only.
        }

        m_process.Dispose();
        m_output.Dispose();
    }

    #endregion
}

/// <summary>What a completed scenario reported.</summary>
public sealed record CrashResult(IReadOnlyDictionary<string, string> Facts, int ExitCode);

/// <summary>
/// The harness could not do its job. Never a statement about the database.
/// </summary>
public sealed class CrashHarnessException : Exception
{
    public CrashHarnessException(string message) : base(message)
    {
    }
}
