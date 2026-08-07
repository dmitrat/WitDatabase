using NUnit.Framework;
using OutWit.Database.Studio.Tests.Helpers;

namespace OutWit.Database.Studio.Tests.Services;

/// <summary>
/// What the engine will actually do when an imported row collides with one that is already there
/// (6.4). The design's step 3 offers three answers - skip the row, update it, or stop - and says the
/// update is done with <c>MERGE</c>. That is a claim about this engine, so it is executed before
/// anything is built on it.
/// </summary>
[TestFixture]
public class ImportConflictProbeTests
{
    #region Fields

    private StudioFixture m_studio = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetUp()
    {
        m_studio = await StudioFixture.CreateAsync();

        await m_studio.Database.ExecuteNonQueryAsync(
            "CREATE TABLE Target (Id INTEGER PRIMARY KEY, Name VARCHAR(50))");
        await m_studio.Database.ExecuteNonQueryAsync("INSERT INTO Target (Id, Name) VALUES (1, 'one')");
    }

    [TearDown]
    public async Task TearDown()
    {
        await m_studio.DisposeAsync();
    }

    #endregion

    #region The three answers

    /// <summary>
    /// The refusal a plain INSERT gives, which is what "stop the import" and "skip the row" are both
    /// built on. Naming the message matters: it is what tells the two apart from a connection failure.
    /// </summary>
    [Test]
    public async Task AnInsertOnAnExistingKeyIsRefusedAsync()
    {
        var error = await TryAsync("INSERT INTO Target (Id, Name) VALUES (1, 'again')");

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null, "the engine refuses it rather than overwriting");
            Assert.That(error, Does.Contain("UNIQUE"));
        });
    }

    /// <summary>
    /// MEASURED, and it is what the design's third option depends on. If this is red the option must
    /// not be offered - an interface that offers an update the engine cannot do is worse than one that
    /// offers only skip and stop.
    /// </summary>
    [Test]
    public async Task MergeUpdatesTheRowThatIsAlreadyThereAsync()
    {
        var error = await TryAsync(
            "MERGE INTO Target AS t USING (SELECT 1 AS Id, 'updated' AS Name) AS s ON t.Id = s.Id "
            + "WHEN MATCHED THEN UPDATE SET Name = s.Name "
            + "WHEN NOT MATCHED THEN INSERT (Id, Name) VALUES (s.Id, s.Name)");

        Assert.That(error, Is.Null, "MERGE is what the design's 'update' option is built on");

        Assert.That(await ReadAsync("SELECT Name FROM Target WHERE Id = 1"), Is.EqualTo(new[] { "updated" }),
            "and it really updated rather than reporting success and doing nothing");
    }

    /// <summary>
    /// The other half of the same statement: a row that does NOT collide is inserted. An update path
    /// that only updates would silently drop every new row in the file.
    /// </summary>
    [Test]
    public async Task MergeInsertsTheRowThatIsNotThereAsync()
    {
        await TryAsync(
            "MERGE INTO Target AS t USING (SELECT 2 AS Id, 'two' AS Name) AS s ON t.Id = s.Id "
            + "WHEN MATCHED THEN UPDATE SET Name = s.Name "
            + "WHEN NOT MATCHED THEN INSERT (Id, Name) VALUES (s.Id, s.Name)");

        Assert.That(await ReadAsync("SELECT Name FROM Target ORDER BY Id"),
            Is.EqualTo(new[] { "one", "two" }));
    }

    #endregion

    #region Tools

    private async Task<string?> TryAsync(string sql)
    {
        try
        {
            await m_studio.Database.ExecuteNonQueryAsync(sql);

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task<string[]> ReadAsync(string sql)
    {
        var result = await m_studio.Database.ExecuteQueryAsync(sql);

        return result.Data == null
            ? []
            : result.Data.Rows.Cast<System.Data.DataRow>()
                .Select(row => string.Join("|", row.ItemArray.Select(value => value?.ToString())))
                .ToArray();
    }

    #endregion
}
