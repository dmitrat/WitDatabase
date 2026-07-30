using System.Data.Common;
using OutWit.Database.AdoNet;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;

namespace OutWit.Database.CrashRunner;

/// <summary>
/// The scenarios this runner can play. Each one creates a database, does something specific to it,
/// and then either exits cleanly or parks and waits to be killed.
/// </summary>
/// <remarks>
/// Three of the four are <b>controls</b>, and they are the reason the fourth can be believed:
/// <list type="bullet">
/// <item><c>control-clean</c> - nothing is killed, so everything must survive. If it does not, the
/// harness is broken and no other result from this runner means anything.</item>
/// <item><c>control-durable-kill</c> - the durable path is taken in full before the kill, so
/// everything must still survive. If it does not, the kill itself is destroying data and every
/// measurement taken after a kill is worthless.</item>
/// <item><c>control-autocommit-kill</c> - nothing is flushed before the kill. What survives here is
/// <b>recorded, not asserted</b>: it is the baseline cost of a process kill on this platform, and a
/// real defect has to lose more than this, or lose it differently.</item>
/// </list>
/// </remarks>
public static class Scenarios
{
    #region Constants

    /// <summary>Nothing is killed - the shape every other scenario is compared against.</summary>
    public const string CONTROL_CLEAN = "control-clean";

    /// <summary>Commit, flush, then die. Everything must survive.</summary>
    public const string CONTROL_DURABLE_KILL = "control-durable-kill";

    /// <summary>Autocommit writes with no flush at all, then die. Calibration, not an assertion.</summary>
    public const string CONTROL_AUTOCOMMIT_KILL = "control-autocommit-kill";

    /// <summary>
    /// Commit a transaction and die immediately afterwards, with no flush. The subject: inside a
    /// transaction the row-id counter is kept in memory only and is written to the store after the
    /// commit returns, so this is the window in which the rows can outlive the counter that names
    /// them.
    /// </summary>
    public const string ROWID_COMMIT_KILL = "rowid-commit-kill";

    /// <summary>
    /// The ADO.NET provider with a bare <c>Data Source=</c> connection string, closed cleanly. The
    /// control for the scenario below: it establishes that this path can store anything at all.
    /// </summary>
    public const string ADONET_CONTROL_CLEAN = "adonet-control-clean";

    /// <summary>
    /// The ADO.NET provider with a bare <c>Data Source=</c> connection string - which defaults to
    /// <c>MVCC=true</c> and <c>SynchronousCommit=true</c>, documented as "a commit is flushed to
    /// storage before it returns". Commit, then die. This is the configuration the README calls
    /// "durable commit - what an ADO.NET or EF Core consumer actually gets".
    /// </summary>
    public const string ADONET_COMMIT_KILL = "adonet-commit-kill";

    /// <summary>
    /// The same commit, at the engine level, over a database built with MVCC and synchronous commit
    /// explicitly - the configuration the provider is supposed to be producing. It exists to bisect:
    /// if this survives a kill and <see cref="ADONET_COMMIT_KILL"/> does not, the break is in the
    /// provider's wiring; if neither survives, the break is in the commit itself.
    /// </summary>
    public const string MVCC_ENGINE_COMMIT_KILL = "mvcc-engine-commit-kill";

    /// <summary>
    /// Open the database, hold the exclusive lock, and park - without writing anything.
    /// </summary>
    /// <remarks>
    /// The subject is the lock rather than the data. 5.0.0 enforces one engine per database with a
    /// <c>.lock</c> sidecar, and both <c>WitSQL.md</c> and
    /// <c>DatabaseAlreadyOpenException</c>'s own message promise that the operating system releases the
    /// handle when the owning process exits - so a process that dies without running <c>Dispose</c>
    /// does not leave the database permanently unopenable. That promise was written in prose and never
    /// executed, and it is exactly the kind of claim phase 4 learned not to trust: a crash runs no
    /// cleanup, so nothing in this process's own code can be what releases the lock.
    ///
    /// This scenario makes the claim testable from both sides. While it is parked the lock is held by a
    /// <b>different process</b>, so the test can check that a second opener is really refused - which no
    /// in-process test can establish, because the guard would then be arguing with itself. After the
    /// kill, the test checks that the database opens again.
    /// </remarks>
    public const string LOCK_HELD_KILL = "lock-held-kill";

    #endregion

    #region Functions

    /// <summary>
    /// Runs a scenario by name.
    /// </summary>
    /// <returns>The process exit code, or null if the name is not known.</returns>
    public static int? Run(string name, ScenarioContext context) => name switch
    {
        CONTROL_CLEAN => ControlClean(context),
        CONTROL_DURABLE_KILL => ControlDurableKill(context),
        CONTROL_AUTOCOMMIT_KILL => ControlAutocommitKill(context),
        ROWID_COMMIT_KILL => RowIdCommitKill(context),
        ADONET_CONTROL_CLEAN => AdoNetControlClean(context),
        ADONET_COMMIT_KILL => AdoNetCommitKill(context),
        MVCC_ENGINE_COMMIT_KILL => MvccEngineCommitKill(context),
        LOCK_HELD_KILL => LockHeldKill(context),
        _ => null
    };

    /// <summary>Every scenario name, for the usage message and for the test that pins the list.</summary>
    public static IReadOnlyList<string> Names { get; } = new[]
    {
        CONTROL_CLEAN,
        CONTROL_DURABLE_KILL,
        CONTROL_AUTOCOMMIT_KILL,
        ROWID_COMMIT_KILL,
        ADONET_CONTROL_CLEAN,
        ADONET_COMMIT_KILL,
        MVCC_ENGINE_COMMIT_KILL,
        LOCK_HELD_KILL
    };

    #endregion

    #region Scenarios

    /// <summary>
    /// Opens the database through the ADO.NET provider - the surface a consumer holds - and parks with
    /// the exclusive lock held. Writes one row first, so the test can also tell that the database is
    /// usable afterwards rather than merely openable.
    /// </summary>
    private static int LockHeldKill(ScenarioContext context)
    {
        var connection = new WitDbConnection(ConnectionString(context));
        connection.Open();

        context.Ready();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"CREATE TABLE {context.Table} (Id BIGINT PRIMARY KEY, V INT)";
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"INSERT INTO {context.Table} (Id, V) VALUES (1, 42)";
            command.ExecuteNonQuery();
        }

        // No Dispose, no Close: the lock must still be held when the kill arrives, because a lock this
        // process released itself would prove nothing about what happens when a process dies.
        return context.Park(("lockPath", context.Path + ".lock"));
    }

    private static int ControlClean(ScenarioContext context)
    {
        using var engine = Open(context);
        context.Ready();

        CreateTable(engine, context);
        InsertRows(engine, context);

        var lastRowId = engine.LastInsertRowId;

        // Dispose through the using below flushes; do it before reporting so that a failure in
        // shutdown is reported as a failure rather than as a clean run.
        engine.Flush();

        context.Done(("rows", context.Rows), ("lastRowId", lastRowId));
        return CrashProtocol.EXIT_OK;
    }

    private static int ControlDurableKill(ScenarioContext context)
    {
        var engine = Open(context);
        context.Ready();

        CreateTable(engine, context);

        using (engine.BeginTransaction())
        {
            InsertRows(engine, context);
            engine.Commit();
        }

        engine.Flush();

        // No dispose: the process is about to be killed, which is the point.
        return context.Park(("rows", context.Rows), ("lastRowId", engine.LastInsertRowId));
    }

    private static int ControlAutocommitKill(ScenarioContext context)
    {
        var engine = Open(context);
        context.Ready();

        CreateTable(engine, context);
        InsertRows(engine, context);

        return context.Park(("rows", context.Rows), ("lastRowId", engine.LastInsertRowId));
    }

    private static int RowIdCommitKill(ScenarioContext context)
    {
        var engine = Open(context);
        context.Ready();

        CreateTable(engine, context);

        // The table is created and flushed first, so that what the crash is being asked about is the
        // counter and the rows - not whether the schema itself survived. Without this the answer
        // would be dominated by the create, and a lost table looks the same as a lost counter.
        engine.Flush();

        using (engine.BeginTransaction())
        {
            InsertRows(engine, context);
            engine.Commit();
        }

        // Killed here: the commit has returned, and nothing since has been flushed.
        return context.Park(("rows", context.Rows), ("lastRowId", engine.LastInsertRowId));
    }

    private static int AdoNetControlClean(ScenarioContext context)
    {
        using var connection = new WitDbConnection(ConnectionString(context));
        connection.Open();
        context.Ready();

        Command(connection, $"CREATE TABLE {context.Table} (Id BIGINT PRIMARY KEY AUTOINCREMENT, V INT)");

        using (var transaction = connection.BeginTransaction())
        {
            InsertRows(connection, context, transaction);
            transaction.Commit();
        }

        connection.Close();

        context.Done(("rows", context.Rows));
        return CrashProtocol.EXIT_OK;
    }

    private static int AdoNetCommitKill(ScenarioContext context)
    {
        var connection = new WitDbConnection(ConnectionString(context));
        connection.Open();
        context.Ready();

        Command(connection, $"CREATE TABLE {context.Table} (Id BIGINT PRIMARY KEY AUTOINCREMENT, V INT)");

        using (var transaction = connection.BeginTransaction())
        {
            InsertRows(connection, context, transaction);
            transaction.Commit();
        }

        // Killed here. Nothing is closed, nothing is disposed - the commit has returned, and the
        // connection string says nothing beyond Data Source, so this is the configuration the
        // documentation calls durable.
        return context.Park(("rows", context.Rows));
    }

    private static int MvccEngineCommitKill(ScenarioContext context)
    {
        var database = new WitDatabaseBuilder()
            .WithFilePath(context.Path)
            .WithMvcc()
            .Build();

        var engine = new WitSqlEngine(database, ownsStore: true);
        context.Ready();

        CreateTable(engine, context);

        using (engine.BeginTransaction())
        {
            InsertRows(engine, context);
            engine.Commit();
        }

        return context.Park(("rows", context.Rows));
    }

    #endregion

    #region Tools

    private static string ConnectionString(ScenarioContext context) => $"Data Source={context.Path}";

    // Deliberately through the ADO.NET base types rather than the Wit ones: a drop-in provider is
    // used through DbConnection and DbTransaction, and this suite has already been bitten once by a
    // member that behaved differently when reached through the base type.
    private static void Command(DbConnection connection, string sql, DbTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (transaction != null)
            command.Transaction = transaction;

        command.ExecuteNonQuery();
    }

    private static void InsertRows(
        DbConnection connection,
        ScenarioContext context,
        DbTransaction? transaction)
    {
        for (int i = 0; i < context.Rows; i++)
            Command(connection, $"INSERT INTO {context.Table} (V) VALUES ({i})", transaction);
    }

    private static WitSqlEngine Open(ScenarioContext context) =>
        new(WitDatabase.Create(context.Path), ownsStore: true);

    private static void CreateTable(WitSqlEngine engine, ScenarioContext context) =>
        engine.Execute($"CREATE TABLE {context.Table} (Id BIGINT PRIMARY KEY AUTOINCREMENT, V INT)");

    private static void InsertRows(WitSqlEngine engine, ScenarioContext context)
    {
        for (int i = 0; i < context.Rows; i++)
            engine.Execute($"INSERT INTO {context.Table} (V) VALUES ({i})");
    }

    #endregion
}
