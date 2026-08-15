using System.Security.Cryptography;

namespace OutWit.Database.AdoNet.Tests.Encryption;

/// <summary>
/// Changing a password through a connection - the layer a consumer actually holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this fixture exists and not only the Core one.</b> The rewrap was in
/// <c>OutWit.Database.Core</c> for a whole release and callable by nobody: <c>CryptoPreamble.Rewrap</c>
/// had zero callers and nothing above Core offered a password change, so Studio kept migrating the
/// whole database. A capability that stops one layer short of its consumer is the defect, so the
/// route is asserted from where the consumer stands rather than from where the code lives.
/// </para>
/// <para>
/// The Core fixture <c>PasswordRewrapTests</c> owns the mechanism - pages untouched, the old
/// password refused, a wrong current password writing nothing. This one owns the ROUTE and the two
/// things only a connection can show: that the connection keeps working afterwards, and that its own
/// connection string is now stale.
/// </para>
/// </remarks>
[TestFixture]
public class PasswordChangeThroughTheProviderTests
{
    #region Constants

    private const string PASSWORD = "correct horse battery staple";

    private const string NEW_PASSWORD = "a completely different password";

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"ProviderPasswordChange_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region The route reaches the provider

    [Test]
    public void AConnectionCanChangeItsOwnPasswordAndKeepWorkingTest()
    {
        var path = Create("secret.witdb");

        using (var connection = Open(path, PASSWORD))
        {
            Assert.That(connection.CanChangePassword, Is.True,
                "an open connection to an encrypted database must offer the change - if this is "
                + "false the capability has stopped below the provider again");

            connection.ChangePassword(PASSWORD, NEW_PASSWORD);

            // The connection is still the same connection, on the same live preamble.
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Items (Note) VALUES ('after the change')";
            command.ExecuteNonQuery();
        }

        Assert.Multiple(() =>
        {
            Assert.That(CountRows(path, NEW_PASSWORD), Is.EqualTo(4),
                "the new password must open it and find the row written after the change");

            Assert.That(() => CountRows(path, PASSWORD), Throws.InstanceOf<CryptographicException>(),
                "and the old password must be refused");
        });
    }

    /// <summary>
    /// Control: without the change, the original password still opens the database and the new one
    /// does not. Without this, the assertions above could be describing a database that refuses
    /// everything, or one that accepts anything.
    /// </summary>
    /// <remarks>
    /// <b>The order is deliberate and it is the second half of this case.</b> The wrong password is
    /// tried FIRST, because a refused open used to leave the file held for the life of the process -
    /// so the attempt that follows a refusal met "the process cannot access the file" instead of
    /// working. Written the other way round this control passed with that defect in place: the leak
    /// happened after its last look at the file. Measured 2026-08-15 by disabling the fix, which
    /// reddens this case and nothing else at this layer.
    /// </remarks>
    [Test]
    public void ControlWithoutAChangeThePasswordsKeepTheirMeaningTest()
    {
        var path = Create("secret.witdb");

        Assert.Multiple(() =>
        {
            Assert.That(() => CountRows(path, NEW_PASSWORD), Throws.InstanceOf<CryptographicException>(),
                "CONTROL: the password that was never set must be refused");

            Assert.That(CountRows(path, PASSWORD), Is.EqualTo(3),
                "CONTROL: and the one that WAS set must still work afterwards - a refused open must "
                + "not leave the file held");
        });
    }

    /// <summary>
    /// An encrypted database keeps its secondary indexes readable after a password change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the case that decides whether the whole feature is safe.</b> A secondary index does
    /// not live in the database file - it lives in a directory beside it, encrypted too. If an index
    /// sidecar carried its OWN password-wrapped key, rewrapping the main file would leave every index
    /// unopenable under the new password: a database that opens, answers unindexed queries, and fails
    /// the indexed ones. That is a wrong answer with no error, which is the worst shape there is.
    /// </para>
    /// <para>
    /// It does not, and the reason is in <c>CreateBTreeIndexFactory</c>: the sidecar is wrapped with
    /// the DATABASE's data key passed as <c>sharedDataKey</c>, so its header is an unwrapped one and
    /// no password unlocks it. The data key never changes during a rewrap, so the indexes never
    /// notice. Asserted here rather than left to that argument.
    /// </para>
    /// </remarks>
    [Test]
    public void SecondaryIndexesKeepWorkingAfterAPasswordChangeTest()
    {
        var path = Create("indexed.witdb");

        using (var connection = Open(path, PASSWORD))
        {
            using var command = connection.CreateCommand();

            // Above MIN_ROWS_FOR_INDEX, or the planner answers by scanning and this case would pass
            // against an index nobody could open.
            for (var i = 4; i <= 40; i++)
            {
                command.CommandText = $"INSERT INTO Items (Note) VALUES ('bulk {i}')";
                command.ExecuteNonQuery();
            }

            command.CommandText = "CREATE INDEX IX_Items_Note ON Items (Note)";
            command.ExecuteNonQuery();
        }

        Assert.That(Directory.Exists(path + "_indexes"), Is.True,
            "CONTROL: the index must be on disk in its own directory, or this case is about nothing");

        using (var connection = Open(path, PASSWORD))
        {
            connection.ChangePassword(PASSWORD, NEW_PASSWORD);
        }

        using var reopened = Open(path, NEW_PASSWORD);
        using var query = reopened.CreateCommand();

        query.CommandText = "SELECT Id FROM Items WHERE Note = 'bulk 17'";

        using var reader = query.ExecuteReader();

        var found = 0;
        while (reader.Read())
            found++;

        Assert.That(found, Is.EqualTo(1),
            "the indexed column must still answer under the new password - an index sidecar rides on "
            + "the database's data key, and a rewrap does not change that key");
    }

    #endregion

    #region What the provider alone can show

    /// <summary>
    /// The connection string is not rewritten, and a caller that reconnects with the stored one is
    /// refused. Asserted rather than left to be discovered: this is the sharp edge of doing the
    /// change in place, and a consumer has to update whatever it saved.
    /// </summary>
    [Test]
    public void TheConnectionStringIsStaleAfterAChangeTest()
    {
        var path = Create("secret.witdb");
        var original = ConnectionString(path, PASSWORD);

        using (var connection = new WitDbConnection(original))
        {
            connection.Open();
            connection.ChangePassword(PASSWORD, NEW_PASSWORD);
        }

        Assert.That(() =>
        {
            using var reconnected = new WitDbConnection(original);
            reconnected.Open();
        }, Throws.InstanceOf<CryptographicException>(),
            "reconnecting with the saved string must fail - it still carries the old password, and "
            + "nothing in the provider rewrites a string it does not own");
    }

    [Test]
    public void AClosedConnectionRefusesTheChangeTest()
    {
        var path = Create("secret.witdb");

        using var connection = new WitDbConnection(ConnectionString(path, PASSWORD));

        Assert.Multiple(() =>
        {
            Assert.That(connection.CanChangePassword, Is.False,
                "there is no preamble to rewrap until the database is open");

            Assert.That(() => connection.ChangePassword(PASSWORD, NEW_PASSWORD),
                Throws.InstanceOf<InvalidOperationException>(),
                "and it must say so rather than throw something about a null");
        });
    }

    [Test]
    public void AnUnencryptedDatabaseDoesNotOfferTheChangeTest()
    {
        var path = Path.Combine(m_directory, "plain.witdb");

        using var connection = new WitDbConnection($"Data Source={path}");
        connection.Open();

        Assert.Multiple(() =>
        {
            Assert.That(connection.CanChangePassword, Is.False,
                "there is no wrapped key in an unencrypted database");

            Assert.That(() => connection.ChangePassword(PASSWORD, NEW_PASSWORD),
                Throws.InstanceOf<NotSupportedException>(),
                "and encrypting one is a migration, not a rewrap");
        });
    }

    #endregion

    #region Tools

    private string Create(string name)
    {
        var path = Path.Combine(m_directory, name);

        using var connection = Open(path, PASSWORD);
        using var command = connection.CreateCommand();

        command.CommandText = "CREATE TABLE Items (Id INTEGER PRIMARY KEY AUTOINCREMENT, Note VARCHAR(64))";
        command.ExecuteNonQuery();

        for (var i = 1; i <= 3; i++)
        {
            command.CommandText = $"INSERT INTO Items (Note) VALUES ('row {i}')";
            command.ExecuteNonQuery();
        }

        return path;
    }

    private static string ConnectionString(string path, string password)
        => $"Data Source={path};Password={password}";

    private static WitDbConnection Open(string path, string password)
    {
        var connection = new WitDbConnection(ConnectionString(path, password));
        connection.Open();

        return connection;
    }

    private static int CountRows(string path, string password)
    {
        using var connection = Open(path, password);
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT Id FROM Items";

        using var reader = command.ExecuteReader();

        var count = 0;
        while (reader.Read())
            count++;

        return count;
    }

    #endregion
}
