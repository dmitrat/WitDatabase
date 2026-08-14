using System.Security.Cryptography;
using OutWit.Database.Core.Builder;

namespace OutWit.Database.AdoNet.Tests.Encryption;

/// <summary>
/// Phase 18 part 1, through the connection string: what a person meets, and what must keep working
/// after the format changes.
/// </summary>
/// <remarks>
/// <para>
/// <c>Fixtures/12.8.0-encrypted.witdb</c> and <c>Fixtures/12.8.0-encrypted-fast.witdb</c> were
/// written by the code at <c>main = 92b6056</c> - engine 12.8.0, the last version before the format
/// change - and are committed so that "old files keep opening" is answerable rather than asserted.
/// Both carry the same two tables, an index and 60 rows; the password is <c>phase18-fixture</c> and
/// there is nothing else in them.
/// </para>
/// <para>
/// They must not be regenerated. A fixture rewritten by the new code is a fixture that tests
/// nothing, so the generator lives outside the repository on purpose.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class EncryptionFormatCompatibilityTests
{
    #region Constants

    private const string FIXTURE_PASSWORD = "phase18-fixture";

    private const string DEFAULT_FIXTURE = "12.8.0-encrypted.witdb";

    private const string FAST_FIXTURE = "12.8.0-encrypted-fast.witdb";

    private const string LSM_FIXTURE = "12.8.0-encrypted-lsm";

    /// <summary>
    /// A secondary index does not live in the database file. It lives in a directory beside it,
    /// named after it - which is why a fixture is copied as a set and not as a file.
    /// </summary>
    private const string INDEX_SUFFIX = "_indexes";

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"EncryptionCompat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region A 12.8.0 database still opens

    /// <summary>
    /// The compatibility claim of the format change, as a test rather than as a comment: a database
    /// written before the change is opened by the code after it, and answers with its rows.
    /// </summary>
    /// <remarks>
    /// GREEN on 12.8.0, which is the point - it is a guard, not a finding. Its power is measured by
    /// <see cref="ControlATamperedFixtureDoesNotOpenTest"/>: without something that makes this shape
    /// fail, "the old file opened" is a sentence no run could contradict.
    /// </remarks>
    [Test]
    public void A1280EncryptedDatabaseStillOpensTest()
    {
        var path = CopyFixture(DEFAULT_FIXTURE);

        using var connection = new WitDbConnection($"Data Source={path};Password={FIXTURE_PASSWORD}");
        connection.Open();

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM Customers"), Is.EqualTo(20),
                "the 20 rows written by 12.8.0 must still be there");
            Assert.That(Scalar(connection, "SELECT Name FROM Customers WHERE Id = 7"), Is.EqualTo("Customer 7"),
                "and a row must still read back as itself, not merely be counted");
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM Orders WHERE CustomerId = 4"), Is.EqualTo(2),
                "and the secondary index the fixture carries must still answer");
        });
    }

    /// <summary>
    /// The same, for a file written with <c>Fast Encryption</c>. Its iteration count is not in the
    /// file, so it stays a property of the connection string for as long as the file exists - which
    /// is exactly why the format change cannot repair old files and why Studio's password change is
    /// the documented migration.
    /// </summary>
    [Test]
    public void A1280FastEncryptedDatabaseStillOpensWithTheFlagTest()
    {
        var path = CopyFixture(FAST_FIXTURE);

        using var connection = new WitDbConnection(
            $"Data Source={path};Password={FIXTURE_PASSWORD};FastEncryption=true");
        connection.Open();

        Assert.That(Scalar(connection, "SELECT Name FROM Customers WHERE Id = 7"), Is.EqualTo("Customer 7"),
            "an old fast-encrypted file must keep opening with the flag it was written with");
    }

    /// <summary>
    /// Control: this shape CAN fail. One byte of page 0 is flipped in a copy of the fixture, which is
    /// what a bad restore or a hostile edit looks like, and the open must be refused.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="A1280EncryptedDatabaseStillOpensTest"/> would pass for a build that
    /// had stopped authenticating anything at all.
    /// </remarks>
    [Test]
    public void ControlATamperedFixtureDoesNotOpenTest()
    {
        var path = CopyFixture(DEFAULT_FIXTURE, "tampered.witdb");

        var bytes = File.ReadAllBytes(path);
        bytes[64] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.That(() =>
        {
            using var connection = new WitDbConnection($"Data Source={path};Password={FIXTURE_PASSWORD}");
            connection.Open();

            return Scalar(connection, "SELECT COUNT(*) FROM Customers");
        }, Throws.InstanceOf<CryptographicException>().Or.InstanceOf<InvalidDataException>(),
            "a page whose ciphertext was edited must fail authentication rather than be read");
    }

    /// <summary>
    /// Control: the fixture is answering about the password and not merely about being a file. A
    /// build that ignored the password would pass every test above.
    /// </summary>
    [Test]
    public void ControlTheFixtureRefusesTheWrongPasswordTest()
    {
        var path = CopyFixture(DEFAULT_FIXTURE, "wrong-password.witdb");

        Assert.That(() =>
        {
            using var connection = new WitDbConnection($"Data Source={path};Password=not-the-password");
            connection.Open();

            return Scalar(connection, "SELECT COUNT(*) FROM Customers");
        }, Throws.Exception,
            "the wrong password must not open a 12.8.0 database");
    }

    /// <summary>
    /// The LSM store's half of the compatibility claim. Its directory was written by 12.8.0 and
    /// carries no crypto header, which is exactly how the new code recognises it.
    /// </summary>
    /// <remarks>
    /// <c>Fixtures/12.8.0-encrypted-lsm</c> was generated from a git worktree at the parent commit,
    /// so it is the old code's output rather than the new code's imitation of it.
    /// </remarks>
    [Test]
    public void A1280EncryptedLsmStoreStillOpensTest()
    {
        var directory = CopyLsmFixture();

        using var database = new WitDatabaseBuilder()
            .WithLsmTree(directory).WithEncryption(FIXTURE_PASSWORD).Build();

        Assert.That(System.Text.Encoding.UTF8.GetString(database.Get("key-007") ?? []),
            Is.EqualTo("value for key 7"),
            "an LSM store written before the crypto header must keep opening and answering");
    }

    /// <summary>
    /// Control: the same directory refuses the wrong password, so the test above is about the
    /// password and not about a build that decrypts nothing.
    /// </summary>
    [Test]
    public void ControlThe1280LsmFixtureRefusesTheWrongPasswordTest()
    {
        var directory = CopyLsmFixture("lsm-wrong-password");

        // What matters is that the data does not come out; whether the store throws or answers
        // nothing is a separate question and this case does not pretend to settle it.
        string? answered = null;

        try
        {
            using var database = new WitDatabaseBuilder()
                .WithLsmTree(directory).WithEncryption("not-the-password").Build();

            var value = database.Get("key-007");
            answered = value == null ? null : System.Text.Encoding.UTF8.GetString(value);
        }
        catch (Exception)
        {
            answered = null;
        }

        Assert.That(answered, Is.Not.EqualTo("value for key 7"),
            "the wrong password must not read a 12.8.0 LSM store");
    }

    #endregion

    #region E3 - the flag a person has to remember

    /// <summary>
    /// E3 as a user meets it. A database created with <c>FastEncryption=true</c> and opened without
    /// it fails with <c>Failed to decrypt page 0 - authentication failed</c>, which names neither the
    /// flag nor the iteration count; the password was right the whole time.
    /// </summary>
    /// <remarks>
    /// RED on 12.8.0. The count belongs in the file, so that a connection string carrying the correct
    /// password is enough. Old files are the exception, and
    /// <see cref="A1280FastEncryptedDatabaseStillOpensWithTheFlagTest"/> is where that is written
    /// down.
    /// </remarks>
    [Test]
    public void AFastEncryptionDatabaseOpensWithoutTheFlagTest()
    {
        var path = Path.Combine(m_directory, "fast.witdb");

        using (var created = new WitDbConnection($"Data Source={path};Password=fast-secret;FastEncryption=true"))
        {
            created.Open();

            using var command = created.CreateCommand();
            command.CommandText = "CREATE TABLE Notes (Id INT PRIMARY KEY, Body VARCHAR(64))";
            command.ExecuteNonQuery();

            command.CommandText = "INSERT INTO Notes VALUES (1, 'written with the flag')";
            command.ExecuteNonQuery();
        }

        Assert.That(() =>
        {
            using var reopened = new WitDbConnection($"Data Source={path};Password=fast-secret");
            reopened.Open();

            return Scalar(reopened, "SELECT Body FROM Notes WHERE Id = 1");
        }, Throws.Nothing,
            "the iteration count is a property of the file, so the right password must be enough to "
            + "open it");
    }

    /// <summary>
    /// Control: the file is sound and the password is right, so the test above is about the flag and
    /// not about a failed write.
    /// </summary>
    [Test]
    public void ControlAFastEncryptionDatabaseOpensWithTheFlagTest()
    {
        var path = Path.Combine(m_directory, "fast-control.witdb");

        using (var created = new WitDbConnection($"Data Source={path};Password=fast-secret;FastEncryption=true"))
        {
            created.Open();

            using var command = created.CreateCommand();
            command.CommandText = "CREATE TABLE Notes (Id INT PRIMARY KEY, Body VARCHAR(64))";
            command.ExecuteNonQuery();

            command.CommandText = "INSERT INTO Notes VALUES (1, 'written with the flag')";
            command.ExecuteNonQuery();
        }

        using var reopened = new WitDbConnection($"Data Source={path};Password=fast-secret;FastEncryption=true");
        reopened.Open();

        Assert.That(Scalar(reopened, "SELECT Body FROM Notes WHERE Id = 1"), Is.EqualTo("written with the flag"),
            "with the flag the same file must open and return what was written");
    }

    #endregion

    #region E1 and E2 through the connection string

    /// <summary>
    /// E1 and E2 where a person can see them: two databases made through the ADO.NET provider with
    /// one password must not share key material, and the file must not carry anything a guess at the
    /// password can be checked against.
    /// </summary>
    /// <remarks>
    /// RED on 12.8.0, twice over. Both committed fixtures begin <c>9638ABA4E53529EC</c> - the same
    /// eight bytes, because they share a password and the salt was that password's hash. Asserted on
    /// the whole file rather than on a header field, so that this rule survives any later change to
    /// where the salt is kept.
    /// </remarks>
    [Test]
    public void TwoDatabasesWithOnePasswordDoNotShareTheirBytesTest()
    {
        var first = Create("head-one.witdb", "one password for both");
        var second = Create("head-two.witdb", "one password for both");

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(second), Is.Not.EqualTo(File.ReadAllBytes(first)),
                "two databases made with one password, holding the same row, must not be the same "
                + "file");

            Assert.That(Head(first), Is.Not.EqualTo(DerivedSaltHead("one password for both")),
                "and neither may begin with a value the password alone computes, which is what made "
                + "a guess cost one SHA-256");
        });
    }

    /// <summary>
    /// Control: the comparison can see a difference between two files at all.
    /// </summary>
    [Test]
    public void ControlTwoDatabasesWithDifferentPasswordsDoNotShareTheirBytesTest()
    {
        var first = Create("differs-one.witdb", "one password");
        var second = Create("differs-two.witdb", "another password");

        Assert.That(File.ReadAllBytes(second), Is.Not.EqualTo(File.ReadAllBytes(first)),
            "the instrument must be able to see a difference between two files");
    }

    #endregion

    #region Tools

    private static string FixtureDirectory =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

    /// <summary>
    /// Copies a committed fixture into a working directory, WITH the sidecar directory its secondary
    /// index lives in.
    /// </summary>
    /// <remarks>
    /// The sidecar is not an implementation detail this helper is being polite about. Copying the
    /// <c>.witdb</c> alone and asking an indexed column for its rows answers <b>0</b> rather than
    /// failing - measured 2026-08-14, encrypted and plain alike, with the un-indexed arm of the same
    /// probe answering correctly. That is recorded as its own finding; here it only means a fixture
    /// copied by hand would have made this whole class report a format problem that is not one.
    /// </remarks>
    private string CopyFixture(string name, string? renameTo = null)
    {
        var source = Path.Combine(FixtureDirectory, name);

        Assert.That(File.Exists(source), Is.True,
            $"the committed fixture '{name}' must reach the test output directory, or every "
            + "compatibility claim in this fixture is about a file that is not there");

        var destination = Path.Combine(m_directory, renameTo ?? name);
        File.Copy(source, destination);

        var indexes = source + INDEX_SUFFIX;

        Assert.That(Directory.Exists(indexes), Is.True,
            $"the fixture's index sidecar '{Path.GetFileName(indexes)}' must be committed too");

        var destinationIndexes = destination + INDEX_SUFFIX;
        Directory.CreateDirectory(destinationIndexes);

        foreach (var file in Directory.GetFiles(indexes))
            File.Copy(file, Path.Combine(destinationIndexes, Path.GetFileName(file)));

        return destination;
    }

    private string CopyLsmFixture(string? renameTo = null)
    {
        var source = Path.Combine(FixtureDirectory, LSM_FIXTURE);

        Assert.That(Directory.Exists(source), Is.True,
            $"the committed LSM fixture '{LSM_FIXTURE}' must reach the test output directory");

        var destination = Path.Combine(m_directory, renameTo ?? LSM_FIXTURE);
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));

        return destination;
    }

    private string Create(string name, string password)
    {
        var path = Path.Combine(m_directory, name);

        using (var connection = new WitDbConnection($"Data Source={path};Password={password}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Notes (Id INT PRIMARY KEY, Body VARCHAR(64))";
            command.ExecuteNonQuery();

            command.CommandText = "INSERT INTO Notes VALUES (1, 'body')";
            command.ExecuteNonQuery();
        }

        return path;
    }

    private static byte[] Head(string path) => File.ReadAllBytes(path).AsSpan(0, 8).ToArray();

    /// <summary>
    /// What the first eight bytes of an encrypted database USED to be: the head of a salt computed
    /// from the password and nothing else.
    /// </summary>
    private static byte[] DerivedSaltHead(string password) =>
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(password + "_WitDB_Salt")).AsSpan(0, 8).ToArray();

    private static object? Scalar(WitDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    #endregion
}
