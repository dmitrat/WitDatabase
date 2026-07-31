using System.Data;
using NUnit.Framework;

namespace OutWit.Database.AdoNet.Tests.AuditVerification;

/// <summary>
/// Verification of the <c>adonet</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>
/// The connection-pool entry of this dimension is settled under <c>core-concurrency</c>: the permit
/// leak is confirmed, but the pool is unreachable from the provider. Only the open-reader claim is
/// new here.
///
/// See Docs/NEXT-SESSION-PLAN.md workstream B.
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class AdoNetFindingsTests
{
    #region Nothing tracks an open reader

    [Test]
    // FIXED 2026-07-31 (phase 6). The connection now remembers the reader it handed out and closes
    // it before the engine goes.
    public void ClosingTheConnectionClosesItsOpenReaderTest()
    {
        // Finding: WitDbCommand.cs:131 - the reader is handed the connection but the connection is
        // never told about the reader, so Close() disposes the engine underneath it. Every ADO.NET
        // provider closes its readers when the connection closes; leaving one live and pointing at
        // disposed storage is the shape that produces torn reads rather than a clean error.
        var connection = OpenSeededConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM T ORDER BY Id";
        var reader = command.ExecuteReader();

        Assert.That(reader.Read(), Is.True, "the reader should yield the first row");

        connection.Close();

        Assert.That(reader.IsClosed, Is.True,
            "closing the connection must close the readers that depend on it");
    }

    [Test]
    // FIXED 2026-07-31 (phase 6). See ClosingTheConnectionClosesItsOpenReaderTest.
    public void ReadingAfterTheConnectionClosesFailsCleanlyTest()
    {
        // Whatever the provider decides about tracking, the one outcome that must not happen is
        // reading through a disposed store. A clean exception is acceptable; silent data is not.
        var connection = OpenSeededConnection();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM T ORDER BY Id";
        var reader = command.ExecuteReader();
        reader.Read();

        connection.Close();

        var outcome = Probe(reader);
        TestContext.Out.WriteLine($"after Close(), further reads: {outcome}");

        Assert.That(outcome, Does.Not.StartWith("kept streaming"),
            "a reader must not keep returning rows out of storage the connection has disposed");
    }

    [Test]
    // FIXED 2026-07-31 (phase 6). The decisive shape - a real FileStream underneath - and the reader
    // is closed before it is disposed.
    public void ReadingAfterTheConnectionClosesFailsCleanlyOnAFileDatabaseTest()
    {
        // The decisive shape. WitSqlResult wraps an IEnumerable<WitSqlRow> with a cursor-style
        // Read(), so the reader is genuinely pulling from the engine's iterator rather than from a
        // materialised list - which means Close() really does dispose the store underneath it. On
        // `:memory:` that survives; on a file database Dispose closes an actual FileStream, which is
        // where a torn read or a raw I/O exception would surface.
        var directory = Path.Combine(Path.GetTempPath(), "witdb-adonet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reader.witdb");

        try
        {
            var connection = new WitDbConnection($"Data Source={path}");
            connection.Open();
            Seed(connection);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id FROM T ORDER BY Id";
            var reader = command.ExecuteReader();
            reader.Read();

            connection.Close();

            var outcome = Probe(reader);
            TestContext.Out.WriteLine($"file database, after Close(): {outcome}");

            Assert.That(outcome, Does.Not.StartWith("kept streaming"),
                "a reader must not keep pulling rows out of a store the connection has disposed");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }

    #endregion

    #region Helpers

    private static string Probe(IDataReader reader)
    {
        try
        {
            var rows = 0;
            while (reader.Read())
                rows++;

            return rows > 0
                ? $"kept streaming {rows} more row(s)"
                : "returned no further rows";
        }
        catch (Exception e)
        {
            return $"threw {e.GetType().Name}";
        }
    }

    private static WitDbConnection OpenSeededConnection()
    {
        var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();
        Seed(connection);
        return connection;
    }

    private static void Seed(WitDbConnection connection)
    {
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY)";
        create.ExecuteNonQuery();

        for (int i = 1; i <= 5; i++)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = $"INSERT INTO T (Id) VALUES ({i})";
            insert.ExecuteNonQuery();
        }
    }

    #endregion
}
