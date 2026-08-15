using System.Data;
using System.Reflection;
using OutWit.Database.Engine;

namespace OutWit.Database.AdoNet.Tests.Schema;

/// <summary>
/// Everything that answers "which version is this" answers the same thing, and it comes from the
/// assembly.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four places answered the literal <c>1.0.0</c> until 2026-08-15</b>, while the engine was on
/// 13.1.1: <c>SELECT VERSION()</c>, <c>ServerVersion</c>, and both version rows of
/// <c>GetSchema("DataSourceInformation")</c> - which is what tooling and ORMs read to decide what a
/// database can do. Four literals, four independent ways to go stale.
/// </para>
/// <para>
/// <b>The expectation is read from the assembly here, not written down.</b> A case carrying
/// <c>"13.1.1"</c> in its own text would be the same defect one layer out: it would pass today and
/// have to be edited at every release, which is exactly how the four literals survived thirteen
/// major versions.
/// </para>
/// </remarks>
[TestFixture]
public class VersionComesFromTheAssemblyTests
{
    #region Constants

    /// <summary>
    /// The engine assembly's own informational version, minus the commit sha the SDK appends. Read
    /// through a type of the ENGINE, because that is what a "server version" describes here.
    /// </summary>
    private static string EngineVersion =>
        typeof(WitSqlEngine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion
            .Split('+')[0];

    #endregion

    #region Tests

    [Test]
    public void ServerVersionIsTheEnginesVersionTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");

        Assert.Multiple(() =>
        {
            Assert.That(connection.ServerVersion, Is.EqualTo(EngineVersion));

            // CONTROL: if the assembly itself said 1.0.0, the comparison above would pass while the
            // defect was still there.
            Assert.That(EngineVersion, Is.Not.EqualTo("1.0.0"),
                "the engine assembly reports 1.0.0, so nothing here can tell the fix from the defect");
        });
    }

    [Test]
    public void TheSchemaRowsToolingReadsCarryTheSameVersionTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        var information = connection.GetSchema("DataSourceInformation");
        var row = information.Rows[0];

        var text = (string)row["DataSourceProductVersion"];
        var normalized = (string)row["DataSourceProductVersionNormalized"];

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(EngineVersion));
            Assert.That(text, Is.EqualTo(connection.ServerVersion),
                "the two surfaces must not be able to disagree");

            // The normalised form exists to be COMPARED as a string, so its shape is the point:
            // two digits of major, two of minor, four of build.
            Assert.That(normalized, Does.Match(@"^\d{2}\.\d{2}\.\d{4}$"));

            var parts = EngineVersion.Split('-', '+')[0].Split('.');

            Assert.That(normalized, Is.EqualTo(
                $"{int.Parse(parts[0]):00}.{int.Parse(parts[1]):00}.{int.Parse(parts[2]):0000}"),
                "and it is the same version, zero-padded - not a second answer");
        });
    }

    /// <summary>
    /// The engine's own answer, through SQL, is the same one. This is the surface a user reaches
    /// first, and it was the loudest of the four.
    /// </summary>
    [Test]
    public void SelectVersionAnswersTheSameTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT VERSION()";

        Assert.That(command.ExecuteScalar()?.ToString(), Is.EqualTo(EngineVersion));
    }

    /// <summary>
    /// CONTROL: the normalisation is a function of the version rather than of the current one, so it
    /// is measured on values that are not this build's.
    /// </summary>
    [Test]
    public void TheNormalisedFormIsPaddedAndDropsAPreReleaseSuffixTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WitDatabaseVersion.Normalized, Does.Match(@"^\d{2}\.\d{2}\.\d{4}$"));
            Assert.That(WitDatabaseVersion.Text, Is.Not.Empty);
            Assert.That(WitDatabaseVersion.Text, Does.Not.Contain("+"),
                "the commit sha is not part of an answer a person or a tool reads");
        });
    }

    #endregion
}
