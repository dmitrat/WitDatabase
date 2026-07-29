namespace OutWit.Database.CrashRunner;

/// <summary>
/// The line protocol the runner speaks on stdout, and the exit codes it uses.
/// </summary>
/// <remarks>
/// It lives in the runner assembly and is referenced by the tests rather than duplicated there, so
/// the two halves of the harness cannot drift apart. A drifted protocol would not fail loudly - the
/// test would simply wait for a line that is never printed and time out, which reads like a defect
/// in the engine rather than in the instrument.
/// </remarks>
public static class CrashProtocol
{
    #region Lines

    /// <summary>
    /// Printed once the database is open and the scenario is about to do its work. A scenario that
    /// never prints it failed during startup, which is a harness problem and not a finding.
    /// </summary>
    public const string READY = "READY";

    /// <summary>
    /// Printed when a scenario has finished its work and is exiting cleanly. Followed by
    /// <c>key=value</c> pairs separated by <c>;</c>.
    /// </summary>
    public const string DONE = "DONE";

    /// <summary>
    /// Printed when a scenario has reached the point it is meant to be killed at, and has parked.
    /// Everything the scenario intended to write has been written by then; nothing further happens
    /// until the process is terminated. Followed by <c>key=value</c> pairs separated by <c>;</c>.
    /// </summary>
    public const string KILL_ME = "KILLME";

    /// <summary>
    /// Printed on stderr when the scenario itself threw.
    /// </summary>
    public const string FAILED = "FAILED";

    #endregion

    #region Exit codes

    /// <summary>The scenario ran to completion and shut down cleanly.</summary>
    public const int EXIT_OK = 0;

    /// <summary>The command line could not be understood.</summary>
    public const int EXIT_USAGE = 64;

    /// <summary>The scenario threw.</summary>
    public const int EXIT_SCENARIO_FAILED = 70;

    /// <summary>
    /// A parked scenario was never killed and gave up waiting. This is a harness failure and must
    /// never be read as a result about the database: it means the test did not kill the process it
    /// asked for.
    /// </summary>
    public const int EXIT_NEVER_KILLED = 99;

    #endregion

    #region Timeouts

    /// <summary>
    /// How long a parked scenario waits to be killed before giving up. Long enough that a loaded
    /// machine cannot trip it, short enough that a bug in the test does not leave processes behind.
    /// </summary>
    public static readonly TimeSpan ParkTimeout = TimeSpan.FromMinutes(2);

    #endregion
}
