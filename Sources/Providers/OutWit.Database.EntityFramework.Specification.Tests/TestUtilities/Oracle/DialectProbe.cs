using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;

namespace OutWit.Database.EntityFramework.Specification.Tests.TestUtilities.Oracle;

/// <summary>
/// Runs <see cref="DialectCorpus"/> against a live server and records what it accepted.
/// </summary>
/// <remarks>
/// <para>
/// A <b>characterisation</b> harness: it asserts nothing about which engine is right. The question
/// phase 9 asks is what the drop-in target supports, and the answer is whatever those servers do.
/// </para>
/// <para>
/// The one thing it does assert is about <i>itself</i>. A probe that reports "accepted" for
/// everything - because it swallowed the error, or never actually sent the SQL - would make every
/// capability look universally supported and the whole decision pass worthless. So the run carries
/// two controls, and they are checked before any result is reported.
/// </para>
/// </remarks>
public sealed class DialectProbe
{
    #region Types

    public enum Outcome
    {
        /// <summary>The server ran it.</summary>
        Accepted,

        /// <summary>The server refused it.</summary>
        Rejected,

        /// <summary>The dialect has no spelling for this capability at all.</summary>
        Absent
    }

    public sealed record Result(string Capability, Outcome Outcome, string? Detail);

    #endregion

    #region Fields

    private readonly Func<DbConnection> m_connect;

    #endregion

    public DialectProbe(Func<DbConnection> connect) => m_connect = connect;

    #region Functions

    /// <summary>
    /// Runs every corpus entry for <paramref name="dialect"/> and returns what happened.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If either control fails - see the remarks on the class. Throwing rather than returning is
    /// deliberate: a probe that cannot tell acceptance from rejection must not produce a report at
    /// all, because a wrong report here becomes a roadmap.
    /// </exception>
    public IReadOnlyList<Result> Run(DialectCorpus.Dialect dialect)
    {
        using var connection = m_connect();
        connection.Open();

        foreach (var statement in DialectCorpus.Schema)
            Execute(connection, statement);

        VerifyControls(connection, dialect);

        var results = new List<Result>();

        foreach (var entry in DialectCorpus.All)
        {
            var sql = entry.For(dialect);

            if (sql is null)
            {
                results.Add(new Result(entry.Capability, Outcome.Absent,
                    "this dialect has no spelling for it"));
                continue;
            }

            var error = Execute(connection, sql);

            results.Add(error is null
                ? new Result(entry.Capability, Outcome.Accepted, null)
                : new Result(entry.Capability, Outcome.Rejected, Short(error)));
        }

        return results;
    }

    /// <summary>
    /// Proves the probe can tell the two answers apart on this very connection.
    /// </summary>
    /// <remarks>
    /// Without this, a connection that silently swallowed every error would report the entire corpus
    /// as supported by every engine - which is exactly the shape of report that would be believed and
    /// acted on. The positive control also matters: a probe that reported everything as rejected
    /// would look conservative and be equally useless.
    /// </remarks>
    private void VerifyControls(DbConnection connection, DialectCorpus.Dialect dialect)
    {
        var accepted = Execute(connection, "SELECT Id FROM T");

        if (accepted is not null)
            throw new InvalidOperationException(
                $"{dialect}: the probe's positive control failed - plain 'SELECT Id FROM T' was " +
                $"refused, so the schema is not there and every result would be a false rejection. " +
                $"({Short(accepted)})");

        var rejected = Execute(connection, "SELECT THIS IS NOT SQL FROM");

        if (rejected is null)
            throw new InvalidOperationException(
                $"{dialect}: the probe's negative control failed - deliberate nonsense was accepted, " +
                $"so errors are being swallowed and every capability would read as supported.");
    }

    private static string? Execute(DbConnection connection, string sql)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static string Short(string message)
    {
        var flat = message.ReplaceLineEndings(" ").Trim();
        return flat.Length > 120 ? flat[..120] + "…" : flat;
    }

    #endregion

    #region Report

    /// <summary>
    /// The coverage report, which is the phase's actual deliverable - not a pass/fail gate.
    /// </summary>
    public static string Report(IReadOnlyDictionary<DialectCorpus.Dialect, IReadOnlyList<Result>> byDialect)
    {
        var builder = new StringBuilder();
        var dialects = byDialect.Keys.OrderBy(d => d.ToString()).ToArray();

        builder.Append($"{"capability",-38}");
        foreach (var dialect in dialects)
            builder.Append($"{dialect,-14}");
        builder.AppendLine();

        foreach (var entry in DialectCorpus.All)
        {
            builder.Append($"{entry.Capability,-38}");

            foreach (var dialect in dialects)
            {
                var result = byDialect[dialect].FirstOrDefault(r => r.Capability == entry.Capability);
                builder.Append($"{Word(result?.Outcome),-14}");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Word(Outcome? outcome) => outcome switch
    {
        Outcome.Accepted => "yes",
        Outcome.Rejected => "REJECTED",
        Outcome.Absent => "-",
        _ => "?"
    };

    #endregion

    #region Connections

    /// <summary>An in-memory SQLite, always available - which is what makes the harness testable.</summary>
    public static DialectProbe Sqlite() =>
        new(() => new SqliteConnection("Data Source=:memory:"));

    #endregion
}
