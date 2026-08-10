using System.Data.Common;
using NUnit.Framework;

namespace OutWit.Database.AdoNet.Tests.AuditVerification;

/// <summary>
/// Verification of the ADO.NET-side <c>cross-cutting</c> findings of the 2026-07 audit.
/// </summary>
/// <remarks>See Docs/NEXT-SESSION-PLAN.md workstream B.</remarks>
[TestFixture]
[Category("AuditVerification")]
public class CrossCuttingAdoNetTests
{
    #region The engine never surfaces a DbException

    // FIXED 2026-07-31 (phase 6): every path out of the engine arrives as a WitDbException, which IS a
    // DbException. The suppression reason that used to live here was a FOSSIL - the [TestCase]s stopped
    // naming it when they were un-ignored, so the constant sat unreferenced while its text still read
    // "CONFIRMED 2026-07-27 ... neither derives from DbException". Found by the 2026-08-10 ledger census.

    [TestCase("SELECT * FROM NoSuchTable", TestName = "MissingTable")]
    [TestCase("THIS IS NOT SQL", TestName = "SyntaxError")]
    public void EngineErrorSurfacesAsADbExceptionTest(string sql)
    {
        // Finding: WitDbException.cs:119 - WitDbException derives from DbException and has a
        // FromException factory, but nothing calls it and WitDbCommand has no catch at all, so raw
        // engine exceptions escape. Every framework that handles database failures generically -
        // EF Core's execution strategies, Polly retry policies, ASP.NET diagnostics - keys off
        // DbException and will simply not see these.
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Assert.That(() => command.ExecuteNonQuery(), Throws.InstanceOf<DbException>(),
            "a provider must report database failures as DbException");
    }

    // FIXED 2026-07-31 (phase 6). Every path out of the engine now arrives as a WitDbException, which
    // IS a DbException; the provider's own guards for API misuse stay InvalidOperationException.
    [Test]
    public void ConstraintViolationSurfacesAsADbExceptionTest()
    {
        using var connection = OpenConnection();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE T (Id INT PRIMARY KEY)";
            create.ExecuteNonQuery();
        }
        using (var first = connection.CreateCommand())
        {
            first.CommandText = "INSERT INTO T (Id) VALUES (1)";
            first.ExecuteNonQuery();
        }

        using var duplicate = connection.CreateCommand();
        duplicate.CommandText = "INSERT INTO T (Id) VALUES (1)";

        Assert.That(() => duplicate.ExecuteNonQuery(), Throws.InstanceOf<DbException>(),
            "a primary-key violation is the canonical DbException case");
    }

    #endregion

    #region Helpers

    private static WitDbConnection OpenConnection()
    {
        var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    #endregion
}
