using NUnit.Framework;

namespace OutWit.Database.AdoNet.Tests.Parameter;

/// <summary>
/// ADO.NET binding for SQLite-style $name SQL parameters.
/// </summary>
[TestFixture]
public sealed class WitDbDollarNamedParameterTests
{
    [Test]
    public void CommandSelectWhereDollarNamedParameterTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE history (MigrationId TEXT PRIMARY KEY)";
            setup.ExecuteNonQuery();
            setup.CommandText = "INSERT INTO history (MigrationId) VALUES ('20260612034124_Initial')";
            setup.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM history
            WHERE MigrationId = $id
            """;
        command.Parameters.Add(new WitDbParameter("$id", "20260612034124_Initial"));

        var count = Convert.ToInt64(command.ExecuteScalar());

        Assert.That(count, Is.EqualTo(1));
    }
}
