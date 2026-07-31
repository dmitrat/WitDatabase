using NUnit.Framework;
using System.Data;

namespace OutWit.Database.AdoNet.Tests.Connection;

/// <summary>
/// Tests for WitDbConnection properties.
/// </summary>
[TestFixture]
public class WitDbConnectionPropertiesTests
{
    #region DataSource Property

    [Test]
    public void DataSourceReturnsValueFromConnectionStringTest()
    {
        using var connection = new WitDbConnection("Data Source=test.db");

        Assert.That(connection.DataSource, Is.EqualTo("test.db"));
    }

    [Test]
    public void DataSourceWithMemoryReturnsMemoryValueTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");

        Assert.That(connection.DataSource, Is.EqualTo(":memory:"));
    }

    [Test]
    public void DataSourceWithPathReturnsFullPathTest()
    {
        using var connection = new WitDbConnection(@"Data Source=C:\Data\mydb.witdb");

        Assert.That(connection.DataSource, Is.EqualTo(@"C:\Data\mydb.witdb"));
    }

    [Test]
    public void DataSourceWhenNotSetReturnsNullOrEmptyTest()
    {
        using var connection = new WitDbConnection();

        Assert.That(connection.DataSource, Is.Null.Or.Empty);
    }

    #endregion

    #region Database Property

    [Test]
    public void DatabaseReturnsFileNameWithoutExtensionTest()
    {
        using var connection = new WitDbConnection("Data Source=mydata.witdb");

        Assert.That(connection.Database, Is.EqualTo("mydata"));
    }

    [Test]
    public void DatabaseWithPathReturnsFileNameOnlyTest()
    {
        using var connection = new WitDbConnection(@"Data Source=C:\Data\mydb.witdb");

        Assert.That(connection.Database, Is.EqualTo("mydb"));
    }

    [Test]
    public void DatabaseWithMemoryReturnsMainTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");

        Assert.That(connection.Database, Is.EqualTo("main").Or.EqualTo(":memory:"));
    }

    #endregion

    #region ServerVersion Property

    [Test]
    public void ServerVersionReturnsExpectedValueTest()
    {
        using var connection = new WitDbConnection();

        Assert.That(connection.ServerVersion, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ServerVersionMatchesExpectedFormatTest()
    {
        using var connection = new WitDbConnection();

        Assert.That(connection.ServerVersion, Does.Match(@"^\d+\.\d+\.\d+"));
    }

    #endregion

    #region State Property

    [Test]
    public void StateInitiallyIsClosedTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
    }

    [Test]
    public void StateAfterOpenIsOpenTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Open));
    }

    [Test]
    public void StateAfterCloseIsClosedTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();
        connection.Close();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
    }

    #endregion

    #region ConnectionTimeout Property

    [Test]
    public void ConnectionTimeoutReturnsDefaultValueTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");

        Assert.That(connection.ConnectionTimeout, Is.GreaterThanOrEqualTo(0));
    }

    /// <summary>
    /// INVERTED 2026-07-31 (phase 6). This used to assert 15 - the base class's default - and its own
    /// comment said why: the property was not overridden, so it reported a number this provider had
    /// never heard of. It was a test pinning a gap as though it were behaviour, and it carried no
    /// marker.
    /// </summary>
    [Test]
    public void ConnectionTimeoutReportsTheOpenWaitTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:;Connection Timeout=42");

        Assert.That(connection.ConnectionTimeout, Is.EqualTo(42),
            "ConnectionTimeout must report the wait this provider actually performs at Open");
    }

    /// <summary>
    /// And the sibling keyword, which means the command timeout in ADO.NET and was read by nothing.
    /// </summary>
    [Test]
    public void DefaultTimeoutBecomesTheCommandTimeoutTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:;Default Timeout=60");
        connection.Open();

        using var command = connection.CreateCommand();

        Assert.Multiple(() =>
        {
            Assert.That(command.CommandTimeout, Is.EqualTo(60),
                "Default Timeout is what a new command starts with");
            Assert.That(connection.ConnectionTimeout, Is.EqualTo(5),
                "and it must not be confused with the wait at Open, which has its own keyword");
        });
    }

    #endregion
}
