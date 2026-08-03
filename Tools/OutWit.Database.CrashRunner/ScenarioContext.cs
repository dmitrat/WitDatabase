using System.Text;

namespace OutWit.Database.CrashRunner;

/// <summary>
/// What a scenario is given, and the only way it is allowed to talk to the test that launched it.
/// </summary>
public sealed class ScenarioContext
{
    #region Constructors

    public ScenarioContext(string path, int rows, string table, string settings = "")
    {
        Path = path;
        Rows = rows;
        Table = table;
        Settings = settings;
    }

    #endregion

    #region Functions

    /// <summary>
    /// Announces that the database is open and the work is about to start.
    /// </summary>
    public void Ready() => WriteLine(CrashProtocol.READY);

    /// <summary>
    /// Announces that the work finished and the process is exiting cleanly.
    /// </summary>
    public void Done(params (string Key, object Value)[] facts) =>
        WriteLine(Format(CrashProtocol.DONE, facts));

    /// <summary>
    /// Announces that the scenario has reached the point it is meant to die at, and blocks until it
    /// is killed.
    /// </summary>
    /// <returns>
    /// The exit code to use if the wait expires - which only happens when the test failed to kill
    /// the process, and is a harness failure rather than a result.
    /// </returns>
    public int Park(params (string Key, object Value)[] facts)
    {
        WriteLine(Format(CrashProtocol.KILL_ME, facts));

        Thread.Sleep(CrashProtocol.ParkTimeout);

        Console.Error.WriteLine(
            $"{CrashProtocol.FAILED} parked scenario was never killed after {CrashProtocol.ParkTimeout}");

        return CrashProtocol.EXIT_NEVER_KILLED;
    }

    #endregion

    #region Tools

    private static string Format(string prefix, (string Key, object Value)[] facts)
    {
        if (facts.Length == 0)
            return prefix;

        var builder = new StringBuilder(prefix).Append(' ');

        for (int i = 0; i < facts.Length; i++)
        {
            if (i > 0)
                builder.Append(';');

            builder.Append(facts[i].Key).Append('=').Append(facts[i].Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes and flushes. Without the flush a killed process takes its buffered stdout with it, so
    /// the test would see neither READY nor KILLME and could not tell "died early" from "never
    /// started".
    /// </summary>
    private static void WriteLine(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }

    #endregion

    #region Properties

    /// <summary>Path of the database the scenario works on.</summary>
    public string Path { get; }

    /// <summary>How many rows the scenario writes.</summary>
    public int Rows { get; }

    /// <summary>Name of the table it writes them to.</summary>
    public string Table { get; }

    /// <summary>
    /// Connection-string settings appended after <c>Data Source=</c>, empty for the default
    /// configuration.
    /// </summary>
    /// <remarks>
    /// Durability had only ever been crashed in one configuration - the default - and durability is
    /// precisely the property a configuration changes: the transaction model decides what a commit
    /// writes, <c>Synchronous Commit</c> decides whether it waits, and the LSM store keeps its own
    /// write-ahead log. This is what lets a scenario be run under each of them.
    /// </remarks>
    public string Settings { get; }

    #endregion
}
