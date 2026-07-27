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

    private const string DbExceptionIgnore =
        "CONFIRMED 2026-07-27: a missing table raises InvalidOperationException(\"Table 'NoSuchTable' " +
        "not found\") and bad SQL raises WitSqlParsingException - neither derives from DbException. " +
        "WitDbException does derive from it and has a FromException factory, but nothing calls it and " +
        "WitDbCommand contains no catch at all. Every framework that handles database failures " +
        "generically - EF Core execution strategies, Polly, ASP.NET diagnostics - keys off " +
        "DbException and will not see these. cross-cutting, AdoNet/WitDbException.cs:119";

    [TestCase("SELECT * FROM NoSuchTable", TestName = "MissingTable", Ignore = DbExceptionIgnore)]
    [TestCase("THIS IS NOT SQL", TestName = "SyntaxError", Ignore = DbExceptionIgnore)]
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

    [Test]
    [Ignore("CONFIRMED 2026-07-27: raises InvalidOperationException(\"UNIQUE constraint failed: T.Id\"), "
            + "not a DbException.")]
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
