using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OutWit.Database.EntityFramework.Extensions;
using OutWit.Database.EntityFramework.Infrastructure;

namespace OutWit.Database.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Regression tests for the credential redaction in <see cref="WitDbContextOptionsExtension"/>.
/// </summary>
/// <remarks>
/// EF Core writes <c>LogFragment</c> at Information level the first time a context is used, and
/// surfaces <c>PopulateDebugInfo</c> in diagnostics. Before this was fixed, both carried the
/// connection string verbatim, so an encryption password reached ordinary application logs.
///
/// These assert through the public surface rather than the redaction helper: what matters is what
/// EF Core is handed, not how it is produced. The audit's own reproduction lives in
/// AuditVerification/CrossCuttingEfTests.
/// </remarks>
[TestFixture]
public class ConnectionStringRedactionTests
{
    private const string Password = "hunter2";

    #region The password never reaches either surface

    [Test]
    public void LogFragmentRedactsThePasswordTest()
    {
        var fragment = Info($"Data Source=app.witdb;Password={Password}").LogFragment;

        Assert.Multiple(() =>
        {
            Assert.That(fragment, Does.Not.Contain(Password));
            Assert.That(fragment, Does.Contain("*****"),
                $"the password must be replaced, not dropped silently. Was: {fragment}");
        });
    }

    [Test]
    public void DebugInfoRedactsThePasswordTest()
    {
        var debugInfo = new Dictionary<string, string>();
        Info($"Data Source=app.witdb;Password={Password}").PopulateDebugInfo(debugInfo);

        Assert.That(string.Join(";", debugInfo.Values), Does.Not.Contain(Password));
    }

    #endregion

    #region Redaction must not cost the diagnostics their value

    [Test]
    public void LogFragmentKeepsTheDataSourceTest()
    {
        // A log line that says nothing is its own kind of failure: the whole point of LogFragment is
        // telling an operator which database the context opened.
        var fragment = Info($"Data Source=app.witdb;Password={Password}").LogFragment;

        Assert.That(fragment, Does.Contain("app.witdb"),
            $"redaction must remove the secret and keep the diagnostics. Was: {fragment}");
    }

    [Test]
    public void ConnectionStringWithoutAPasswordIsLoggedUnchangedTest()
    {
        var fragment = Info("Data Source=app.witdb;Store=btree").LogFragment;

        Assert.Multiple(() =>
        {
            Assert.That(fragment, Does.Contain("app.witdb"));
            Assert.That(fragment, Does.Contain("btree"));
            Assert.That(fragment, Does.Not.Contain("*****"),
                "there is no secret here, so nothing should be masked");
        });
    }

    [Test]
    public void InMemoryConnectionIsUnaffectedTest()
    {
        var options = new DbContextOptionsBuilder<RedactionContext>()
            .UseWitDbInMemory()
            .Options;

        var fragment = options.FindExtension<WitDbContextOptionsExtension>()!.Info.LogFragment;

        Assert.That(fragment, Does.Contain("in-memory"));
    }

    #endregion

    #region Helpers

    private static DbContextOptionsExtensionInfo Info(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RedactionContext>()
            .UseWitDb(connectionString)
            .Options;

        var extension = options.FindExtension<WitDbContextOptionsExtension>();
        Assert.That(extension, Is.Not.Null);
        return extension!.Info;
    }

    #endregion

    private sealed class RedactionContext : DbContext
    {
        public RedactionContext(DbContextOptions<RedactionContext> options) : base(options) { }
    }
}
