using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Exceptions;

namespace OutWit.Database.AdoNet.Tests.Encryption;

/// <summary>
/// A database written under the encryption scheme that preceded the crypto preamble is REFUSED,
/// and the refusal names the two ways forward.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is wrong with such a file cannot be repaired by opening it.</b> Its salt is
/// <c>SHA256(password + "_WitDB_Salt")</c> - a pure function of the password, so one password means
/// one key across every database ever created with it, and the salt is written in the clear as the
/// file's first eight bytes, which makes the head of the file a password verifier costing one
/// SHA-256. Its nonce counter is a field set to zero in a constructor that runs on OPEN, so two
/// sessions walk the same sequence and AES-GCM under a repeated nonce hands the plaintext to anyone
/// holding both ciphertexts. All three are properties of bytes already on disk (13.0.0's changelog,
/// E1, E2 and E4).
/// </para>
/// <para>
/// <b>13.0.0 chose to keep opening such files and 14.0.0 chooses not to.</b> The data is not taken
/// away: <c>WithLegacyEncryption()</c>, or <c>Legacy Encryption=true</c> in a connection string,
/// opens exactly as before - it exists so that a converter can read the source, and Studio's
/// password change uses it. What changes is that the old regime is now something a caller asks for
/// rather than something they get without being told.
/// </para>
/// <para>
/// The fixtures are the ones 13.0.0 committed: written by the code at <c>main = 92b6056</c>, engine
/// 12.8.0, the last version before the format change, with the LSM directory generated from a
/// worktree at the parent commit. <b>They must not be regenerated</b> - a fixture rewritten by the
/// new code is a fixture that tests nothing.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class LegacyEncryptionIsRefusedTests
{
    #region Constants

    private const string FIXTURE_PASSWORD = "phase18-fixture";

    private const string DEFAULT_FIXTURE = "12.8.0-encrypted.witdb";

    private const string LSM_FIXTURE = "12.8.0-encrypted-lsm";

    private const string INDEX_SUFFIX = "_indexes";

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"LegacyRefused_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region Refused

    [Test]
    public void APagedDatabaseFromBeforeThePreambleIsRefusedTest()
    {
        var path = CopyFixture(DEFAULT_FIXTURE);

        var error = Assert.Throws<LegacyEncryptionException>(() =>
        {
            using var database = new WitDatabaseBuilder()
                .WithFilePath(path).WithBTree().WithEncryption(FIXTURE_PASSWORD).Build();
        });

        Assert.Multiple(() =>
        {
            // The message has a job: somebody whose application stopped working on upgrade reads it
            // and has to learn what happened and what to do. Both halves are asserted.
            Assert.That(error!.Message, Does.Contain("before"),
                "the message has to say the file predates the current scheme");
            Assert.That(error.Message, Does.Contain("WithLegacyEncryption"),
                "and name the way to read it anyway");
            Assert.That(error.Message, Does.Contain("password"),
                "and point at the conversion, which is a password change");

            // A TYPE rather than a sentence to match: Studio branches on this to offer the
            // conversion, and matching the message text would break the moment a word changed.
            Assert.That(error.IsDirectory, Is.False, "this one is a paged database");
        });
    }

    [Test]
    public void AnLsmDirectoryFromBeforeTheHeaderIsRefusedTest()
    {
        var directory = CopyLsmFixture();

        var error = Assert.Throws<LegacyEncryptionException>(() =>
        {
            using var database = new WitDatabaseBuilder()
                .WithLsmTree(directory).WithEncryption(FIXTURE_PASSWORD).Build();
        });

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("WithLegacyEncryption"),
                "a directory of SSTables with no crypto header is the same situation and gets the "
                + "same answer");
            Assert.That(error.IsDirectory, Is.True, "and the exception says which kind it was");
        });
    }

    #endregion

    #region The opt-in, which is what makes the refusal fair

    /// <summary>
    /// With the opt-in the old file opens and answers with its rows - unchanged from 13.1.1. This is
    /// the case that makes "or convert it" possible: a converter has to be able to read the source.
    /// </summary>
    [Test]
    public void TheOptInOpensThePagedFixtureAndReadsItsRowsTest()
    {
        var path = CopyFixture(DEFAULT_FIXTURE);

        using var connection = new WitDbConnection(
            $"Data Source={path};Password={FIXTURE_PASSWORD};Legacy Encryption=true");
        connection.Open();

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM Customers"), Is.EqualTo(20),
                "the 20 rows written by 12.8.0 are still there");
            Assert.That(Scalar(connection, "SELECT Name FROM Customers WHERE Id = 7"),
                Is.EqualTo("Customer 7"),
                "and a row reads back as itself rather than merely being counted");
        });
    }

    [Test]
    public void TheOptInOpensTheLsmFixtureTest()
    {
        var directory = CopyLsmFixture();

        using var database = new WitDatabaseBuilder()
            .WithLsmTree(directory).WithEncryption(FIXTURE_PASSWORD).WithLegacyEncryption().Build();

        Assert.That(System.Text.Encoding.UTF8.GetString(database.Get("key-007") ?? []),
            Is.EqualTo("value for key 7"));
    }

    #endregion

    #region The refusal must be narrow, and these decide it

    /// <summary>
    /// CONTROL: a database written by THIS version opens with no opt-in. The refusal is about the
    /// old scheme, not about encryption.
    /// </summary>
    [Test]
    public void ControlADatabaseWrittenByThisVersionOpensTest()
    {
        var path = Path.Combine(m_directory, "current.witdb");

        using (var database = new WitDatabaseBuilder()
                   .WithFilePath(path).WithBTree().WithEncryption("current-password").Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
        }

        using var reopened = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithEncryption("current-password").Build();

        Assert.That(System.Text.Encoding.UTF8.GetString(reopened.Get("k"u8.ToArray()) ?? []),
            Is.EqualTo("v"));
    }

    /// <summary>
    /// CONTROL, and it is the one that could have gone wrong: an UNENCRYPTED database looks exactly
    /// like a legacy one to <c>CryptoPreamble.Inspect</c> - both are "page 0 is neither a preamble
    /// nor zeros". The branch is only reached when encryption was asked for, and this says so.
    /// </summary>
    [Test]
    public void ControlAnUnencryptedDatabaseIsUntouchedTest()
    {
        var path = Path.Combine(m_directory, "plain.witdb");

        using (var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
        }

        using var reopened = new WitDatabaseBuilder().WithFilePath(path).WithBTree().Build();

        Assert.That(System.Text.Encoding.UTF8.GetString(reopened.Get("k"u8.ToArray()) ?? []),
            Is.EqualTo("v"),
            "an unencrypted database is not a legacy encrypted one and must open as it always did");
    }

    /// <summary>
    /// CONTROL: the opt-in does not become a way past a wrong password. It selects the old scheme
    /// and nothing else.
    /// </summary>
    [Test]
    public void ControlTheOptInStillRefusesTheWrongPasswordTest()
    {
        var path = CopyFixture(DEFAULT_FIXTURE, "wrong-password.witdb");

        Assert.That(() =>
        {
            using var connection = new WitDbConnection(
                $"Data Source={path};Password=not-the-password;Legacy Encryption=true");
            connection.Open();

            return Scalar(connection, "SELECT COUNT(*) FROM Customers");
        }, Throws.Exception);
    }

    #endregion

    #region Tools

    private string CopyFixture(string fixture, string? name = null)
    {
        var source = Path.Combine(FixturesFolder(), fixture);
        var target = Path.Combine(m_directory, name ?? fixture);

        File.Copy(source, target);

        // A secondary index lives beside the file, not in it - a fixture is copied as a set.
        var indexes = source + INDEX_SUFFIX;

        if (Directory.Exists(indexes))
            CopyDirectory(indexes, target + INDEX_SUFFIX);

        return target;
    }

    private string CopyLsmFixture(string? name = null)
    {
        var target = Path.Combine(m_directory, name ?? LSM_FIXTURE);

        CopyDirectory(Path.Combine(FixturesFolder(), LSM_FIXTURE), target);

        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static string FixturesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Fixtures");

            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, DEFAULT_FIXTURE)))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("the 12.8.0 fixtures were not found from " + AppContext.BaseDirectory);
    }

    private static object? Scalar(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    #endregion
}
