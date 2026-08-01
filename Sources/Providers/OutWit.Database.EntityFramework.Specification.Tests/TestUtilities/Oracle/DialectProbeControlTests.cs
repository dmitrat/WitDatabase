namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// Proves the dialect probe works, using the one engine that is always available.
/// </summary>
/// <remarks>
/// <para>
/// The oracle's whole point is to answer "does the drop-in target support this", and it answers it
/// by talking to PostgreSQL and SQL Server - which need Docker or a connection string, and are
/// therefore not always reachable. An instrument that can only be exercised where it cannot be
/// checked is the shape this project has been burned by repeatedly: a harness that is stable, green
/// and powerless.
/// </para>
/// <para>
/// So the mechanism is proved here against SQLite, in memory, with no setup - the corpus runs, the
/// controls fire, and the outcomes are the ones SQLite is known to give. What remains unexercised
/// until a server is reachable is only the <b>connection</b> code, which is the smallest part.
/// </para>
/// <para>
/// This fixture is <b>not</b> tagged Oracle: it needs nothing external and should run in CI with
/// everything else, because it is what stops the oracle rotting between the sessions that can run it.
/// </para>
/// </remarks>
public class DialectProbeControlTests
{
    [Fact]
    public void ProbeReportsWhatSqliteActuallyDoesTest()
    {
        var results = DialectProbe.Sqlite().Run(DialectCorpus.Dialect.Sqlite);

        Outcome(results, "values-as-table-source").Should(DialectProbe.Outcome.Accepted,
            "SQLite has had a VALUES table source since 3.8");

        Outcome(results, "row-limit").Should(DialectProbe.Outcome.Accepted,
            "LIMIT is SQLite's spelling and it is in the corpus for SQLite");

        Outcome(results, "lateral-join").Should(DialectProbe.Outcome.Absent,
            "SQLite has no LATERAL, and the corpus records that as absent rather than rejected - the "
            + "distinction matters, because 'nobody spells it' is a different fact from 'refused'");

        Outcome(results, "stored-procedure").Should(DialectProbe.Outcome.Absent,
            "SQLite has no stored procedures at all");
    }

    /// <summary>
    /// The negative control, inverted: if the probe could not tell a refusal from an acceptance it
    /// would have to say so rather than report.
    /// </summary>
    [Fact]
    public void ProbeRefusesToReportWhenItCannotTellAcceptanceFromRejectionTest()
    {
        // A connection that answers every command successfully, which is what a swallowed-error
        // provider looks like from here.
        var probe = new DialectProbe(() => new AlwaysSucceedsConnection());

        var thrown = Assert.Throws<InvalidOperationException>(
            () => probe.Run(DialectCorpus.Dialect.Sqlite));

        Assert.Contains("negative control", thrown.Message);
    }

    #region Helpers

    private static DialectProbe.Outcome Outcome(IReadOnlyList<DialectProbe.Result> results, string capability) =>
        results.Single(r => r.Capability == capability).Outcome;

    #endregion

    #region A connection that never fails

    private sealed class AlwaysSucceedsConnection : System.Data.Common.DbConnection
    {
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => string.Empty;
        public override string DataSource => string.Empty;
        public override string ServerVersion => string.Empty;
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }

        protected override System.Data.Common.DbTransaction BeginDbTransaction(
            System.Data.IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override System.Data.Common.DbCommand CreateDbCommand() => new AlwaysSucceedsCommand();
    }

    private sealed class AlwaysSucceedsCommand : System.Data.Common.DbCommand
    {
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override System.Data.CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override System.Data.UpdateRowSource UpdatedRowSource { get; set; }
        protected override System.Data.Common.DbConnection? DbConnection { get; set; }
        protected override System.Data.Common.DbParameterCollection DbParameterCollection { get; } = null!;
        protected override System.Data.Common.DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }

        protected override System.Data.Common.DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override System.Data.Common.DbDataReader ExecuteDbDataReader(
            System.Data.CommandBehavior behavior) => throw new NotSupportedException();
    }

    #endregion
}

internal static class OutcomeAssertions
{
    public static void Should(this DialectProbe.Outcome actual, DialectProbe.Outcome expected, string because) =>
        Assert.True(actual == expected, $"expected {expected} but got {actual}: {because}");
}
