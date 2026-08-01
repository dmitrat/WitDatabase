using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// The phase-9 dialect coverage report: what PostgreSQL and SQL Server actually support, measured.
/// </summary>
/// <remarks>
/// <para>
/// Every other conformance instrument in this repository compares against SQLite, and SQLite lacks
/// most of the phase-9 list itself - so it cannot answer the question the phase asks, which is
/// whether the <b>drop-in target</b> has a capability. This one asks the target.
/// </para>
/// <para>
/// It produces a <b>report, not a verdict</b>. Nothing here asserts that WitDatabase should have a
/// capability; that is the decision, and it needs the measurement first.
/// </para>
/// <para>
/// <b>How it connects.</b> A connection string in <c>WITDB_ORACLE_POSTGRES</c> or
/// <c>WITDB_ORACLE_SQLSERVER</c> wins; otherwise Testcontainers starts a server if Docker is
/// running. If neither is available the test <b>skips</b>, loudly, naming what it wanted - a machine
/// without Docker must run the rest of the suite unaffected, and a silent skip would let the oracle
/// rot unnoticed between the sessions that can run it.
/// </para>
/// <para>
/// The harness itself is proved without any of that, against in-memory SQLite, in
/// <see cref="DialectProbeControlTests"/> - so what is unexercised here is only the connection code.
/// </para>
/// </remarks>
[Trait("Category", "Oracle")]
public class DialectCoverageOracle
{
    #region Tests

    [Fact]
    public async Task ReportCoverageAcrossDialectsTest()
    {
        var byDialect = new Dictionary<DialectCorpus.Dialect, IReadOnlyList<DialectProbe.Result>>();
        var skipped = new List<string>();

        await using var postgres = await StartPostgresAsync(skipped);
        await using var sqlServer = await StartSqlServerAsync(skipped);

        // SQLite always runs: it costs nothing and it is the column that shows the report is live.
        byDialect[DialectCorpus.Dialect.Sqlite] =
            DialectProbe.Sqlite().Run(DialectCorpus.Dialect.Sqlite);

        if (postgres.ConnectionString is { } postgresConnection)
        {
            byDialect[DialectCorpus.Dialect.PostgreSql] =
                new DialectProbe(() => new NpgsqlConnection(postgresConnection))
                    .Run(DialectCorpus.Dialect.PostgreSql);
        }

        if (sqlServer.ConnectionString is { } sqlServerConnection)
        {
            byDialect[DialectCorpus.Dialect.SqlServer] =
                new DialectProbe(() => new SqlConnection(sqlServerConnection))
                    .Run(DialectCorpus.Dialect.SqlServer);
        }

        Console.WriteLine(DialectProbe.Report(byDialect));

        foreach (var note in skipped)
            Console.WriteLine($"skipped: {note}");

        // Named, not silent. A reader of the report must be able to see which columns are missing and
        // why, or an absent column reads as an absent capability.
        Assert.True(byDialect.Count > 1 || skipped.Count > 0,
            "neither a server nor a skip reason - the oracle produced a report of one column and said " +
            "nothing about why");
    }

    #endregion

    #region Servers

    private sealed record Server(string? ConnectionString, IAsyncDisposable? Container) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (Container is not null)
                await Container.DisposeAsync();
        }
    }

    private static async Task<Server> StartPostgresAsync(List<string> skipped)
    {
        var supplied = Environment.GetEnvironmentVariable("WITDB_ORACLE_POSTGRES");

        if (!string.IsNullOrWhiteSpace(supplied))
            return new Server(supplied, null);

        try
        {
            var container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
            await container.StartAsync();
            return new Server(container.GetConnectionString(), container);
        }
        catch (Exception exception)
        {
            skipped.Add($"PostgreSQL - no WITDB_ORACLE_POSTGRES and Docker did not start one " +
                        $"({exception.GetType().Name})");
            return new Server(null, null);
        }
    }

    private static async Task<Server> StartSqlServerAsync(List<string> skipped)
    {
        var supplied = Environment.GetEnvironmentVariable("WITDB_ORACLE_SQLSERVER");

        if (!string.IsNullOrWhiteSpace(supplied))
            return new Server(supplied, null);

        try
        {
            var container = new MsSqlBuilder().Build();
            await container.StartAsync();
            return new Server(container.GetConnectionString(), container);
        }
        catch (Exception exception)
        {
            skipped.Add($"SQL Server - no WITDB_ORACLE_SQLSERVER and Docker did not start one " +
                        $"({exception.GetType().Name})");
            return new Server(null, null);
        }
    }

    #endregion
}
