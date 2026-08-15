using System.Security.Cryptography;
using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Encryption;

namespace OutWit.Database.Core.Tests.Encryption;

/// <summary>
/// Changing a password must be a rewrap of the wrapped key, not a rewrite of the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this fixture exists.</b> Since the format change the data key is drawn at random and the
/// password only WRAPS it - <c>CryptoHeader.CreateWrapping</c> - so a password change is 60 bytes,
/// and <c>CryptoHeader.Rewrap</c> says exactly that in its own comment. Measured 2026-08-15, none of
/// it was reachable: <c>CryptoPreamble.Rewrap</c> had <b>zero</b> callers, <c>Rewrap</c> appeared in
/// <b>zero</b> tests, and nothing above <c>OutWit.Database.Core</c> offered a password change at
/// all. Studio still migrates the whole database, and could not do otherwise.
/// </para>
/// <para>
/// <b>The red this fixture was written against</b> is not a wrong answer - it is a missing one.
/// Written before <c>WitDatabase.ChangePassword</c> existed, every case here failed to COMPILE, and
/// the compiler named the defect: the capability is in the engine and there is no way to call it.
/// </para>
/// <para>
/// <b>Every rule is paired with a control that comes out the other way</b>, because each of these
/// assertions is easy to satisfy by accident - a database that opens under any password would pass
/// R1, and a page region that never changes would pass R3 whatever the rewrap did.
/// </para>
/// <list type="bullet">
/// <item><description>R2 (the old password stops working) is paired with
/// <see cref="ControlAWrongPasswordIsRefusedBeforeAnyRewrapTest"/> - without it, "refused" says
/// nothing about the rewrap.</description></item>
/// <item><description>R3 (the pages are untouched) is paired with
/// <see cref="ControlThePageRegionMovesWhenARowIsWrittenTest"/> - without it, "identical" is a
/// statement about a number that cannot change.</description></item>
/// <item><description>R1 asserts the ROWS and not merely that the file opens, because an empty
/// database opens perfectly well.</description></item>
/// </list>
/// <para>
/// <b>The route matters and is asserted.</b> The rewrap goes through the LIVE preamble, because the
/// preamble holds the header in memory and rewrites it whenever it reserves another block of nonce
/// numbers. Rewrapping the file from outside while it is open therefore survives only until the
/// session exhausts its block - <c>RESERVE</c> is 65,536 - and is then silently undone. That is why
/// the operation is a method on the open database and not a static over a path.
/// </para>
/// </remarks>
[TestFixture]
public class PasswordRewrapTests
{
    #region Constants

    private const string PASSWORD = "correct horse battery staple";

    private const string NEW_PASSWORD = "a completely different password";

    private const int ITERATIONS = 60_000;

    private const int PAGE = 4096;

    private const int ROWS = 200;

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"PasswordRewrap_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region R1 - the new password opens the database and every row is there

    [Test]
    public void AfterAChangeTheNewPasswordAnswersEveryRowTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        ChangePassword(path, PASSWORD, NEW_PASSWORD);

        // The ROWS, not "it opened": an empty database opens under any password it was given.
        Assert.That(ReadRows(path, NEW_PASSWORD), Is.EqualTo(ROWS),
            "the new password must open the database and find every row that was in it");
    }

    #endregion

    #region R2 - the old password stops working

    [Test]
    public void AfterAChangeTheOldPasswordIsRefusedTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        ChangePassword(path, PASSWORD, NEW_PASSWORD);

        Assert.That(() => ReadRows(path, PASSWORD), Throws.InstanceOf<CryptographicException>(),
            "the old password must no longer unwrap the key");
    }

    /// <summary>
    /// Control for R2: a wrong password is refused BEFORE any rewrap happens. Without it, R2's
    /// refusal could be reporting that this database refuses everything.
    /// </summary>
    [Test]
    public void ControlAWrongPasswordIsRefusedBeforeAnyRewrapTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        Assert.Multiple(() =>
        {
            Assert.That(() => ReadRows(path, NEW_PASSWORD), Throws.InstanceOf<CryptographicException>(),
                "CONTROL: a password that was never set must be refused");

            Assert.That(ReadRows(path, PASSWORD), Is.EqualTo(ROWS),
                "CONTROL: and the password that WAS set must work, or this database refuses everything");
        });
    }

    #endregion

    #region R3 - the pages are not touched, which is the whole point

    [Test]
    public void AChangeDoesNotTouchASinglePageTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        var pagesBefore = HashPages(path);
        var preambleBefore = HashPreamble(path);

        ChangePassword(path, PASSWORD, NEW_PASSWORD);

        Assert.Multiple(() =>
        {
            Assert.That(HashPages(path), Is.EqualTo(pagesBefore),
                "a password change must rewrap the key and leave every encrypted page alone - that "
                + "is the whole difference from the migration it replaces");

            Assert.That(HashPreamble(path), Is.Not.EqualTo(preambleBefore),
                "and the preamble must have changed, or nothing was written at all");
        });
    }

    /// <summary>
    /// Control for R3: the page region MOVES when a row is written. Without it, "the pages are
    /// identical" is an assertion about a hash that no operation could ever change - a fixture whose
    /// database is small enough to sit entirely in the preamble would pass R3 with any implementation.
    /// </summary>
    [Test]
    public void ControlThePageRegionMovesWhenARowIsWrittenTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        var before = HashPages(path);

        AppendRow(path, PASSWORD);

        Assert.That(HashPages(path), Is.Not.EqualTo(before),
            "CONTROL: writing a row must change the encrypted page region, or R3 is comparing a "
            + "constant with itself");
    }

    #endregion

    #region R4 - the change survives the session that made it

    [Test]
    public void AChangeSurvivesTheSessionThatMadeItTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        ChangePassword(path, PASSWORD, NEW_PASSWORD);

        // Twice, because the first reopen could be answering from something still in memory.
        Assert.Multiple(() =>
        {
            Assert.That(ReadRows(path, NEW_PASSWORD), Is.EqualTo(ROWS), "the first reopen");
            Assert.That(ReadRows(path, NEW_PASSWORD), Is.EqualTo(ROWS), "and the second");
        });
    }

    /// <summary>
    /// The change is made on an OPEN database and the session keeps working afterwards. This is the
    /// case that separates the safe route from the unsafe one: the preamble holds the header in
    /// memory and rewrites it on its next reservation, so a rewrap that did not go through the live
    /// preamble would be undone by the very next block of nonce numbers.
    /// </summary>
    [Test]
    public void TheSessionKeepsWorkingAfterItChangesItsOwnPasswordTest()
    {
        var path = Path.Combine(m_directory, "live.witdb");

        var database = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithEncryption(PASSWORD, ITERATIONS).Build();

        try
        {
            for (var i = 0; i < ROWS; i++)
                database.Put(Key(i), Value(i));

            Assert.That(database.CanChangePassword, Is.True,
                "an encrypted paged database must offer the change");

            database.ChangePassword(PASSWORD, NEW_PASSWORD);

            // Keep writing THROUGH the same session, so the preamble reserves and persists again.
            for (var i = ROWS; i < ROWS * 2; i++)
                database.Put(Key(i), Value(i));
        }
        finally
        {
            database.Dispose();
        }

        Assert.That(ReadRows(path, NEW_PASSWORD), Is.EqualTo(ROWS * 2),
            "everything written before and after the change must be readable under the new password");
    }

    #endregion

    #region R5 - a wrong current password changes nothing

    [Test]
    public void AWrongCurrentPasswordIsRefusedAndChangesNothingTest()
    {
        var path = CreateEncrypted("secret.witdb", PASSWORD, ROWS);

        var before = HashPreamble(path);

        Assert.That(() => ChangePassword(path, "not the password", NEW_PASSWORD),
            Throws.InstanceOf<CryptographicException>(),
            "a change that cannot unwrap the key must be refused");

        Assert.Multiple(() =>
        {
            Assert.That(HashPreamble(path), Is.EqualTo(before),
                "and it must leave the preamble exactly as it was - a half-written header is a "
                + "database nobody can open");

            Assert.That(ReadRows(path, PASSWORD), Is.EqualTo(ROWS),
                "and the original password must still work");
        });
    }

    #endregion

    #region An unencrypted database has nothing to rewrap, and says so

    [Test]
    public void AnUnencryptedDatabaseDoesNotOfferTheChangeTest()
    {
        var path = Path.Combine(m_directory, "plain.witdb");

        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree().Build();

        Assert.Multiple(() =>
        {
            Assert.That(database.CanChangePassword, Is.False,
                "there is no wrapped key in an unencrypted database, so there is nothing to rewrap; "
                + "going from none to a password is a migration and stays one");

            Assert.That(() => database.ChangePassword(PASSWORD, NEW_PASSWORD),
                Throws.InstanceOf<NotSupportedException>(),
                "and asking for it must say so rather than appear to work");
        });
    }

    /// <summary>
    /// A database whose caller brought its own key has a preamble but NO wrapped key, so there is
    /// nothing a password could rewrap either.
    /// </summary>
    /// <remarks>
    /// <b>This case exists because sabotage found a part that did nothing.</b> Removing the
    /// "no wrapped key" guard inside <c>StorageEncrypted.RewrapPassword</c> turned NOTHING red: an
    /// unencrypted database never reaches that class at all - it has no encrypted storage wrapper -
    /// so <c>StoreBTree</c> refuses first and the guard below it was unreachable from every case
    /// there was. A caller-owned key is the arrangement that does reach it.
    /// </remarks>
    [Test]
    public void ADatabaseWhoseCallerOwnsTheKeyDoesNotOfferTheChangeTest()
    {
        var path = Path.Combine(m_directory, "raw-key.witdb");
        var key = RandomNumberGenerator.GetBytes(32);

        using var database = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithAesEncryption(key).Build();

        database.Put(Key(0), Value(0));

        Assert.Multiple(() =>
        {
            Assert.That(database.CanChangePassword, Is.False,
                "there is no wrapped key here - the caller holds the key material, and no password "
                + "unwraps anything");

            Assert.That(() => database.ChangePassword(PASSWORD, NEW_PASSWORD),
                Throws.InstanceOf<NotSupportedException>(),
                "and asking must say so rather than appear to work");
        });
    }

    #endregion

    #region The LSM store, whose preamble is a file beside the SSTables

    /// <summary>
    /// An LSM database has no page 0 to put a preamble in, so its header is a small file beside the
    /// SSTables. The mechanism is the same and the route is a different one, so it is asserted
    /// separately - a capability that works for one store and silently answers false for the other
    /// is how this whole finding started.
    /// </summary>
    [Test]
    public void AnLsmDatabaseChangesItsPasswordTheSameWayTest()
    {
        var directory = Path.Combine(m_directory, "lsm");

        using (var database = new WitDatabaseBuilder()
                   .WithLsmTree(directory).WithEncryption(PASSWORD, ITERATIONS).Build())
        {
            for (var i = 0; i < ROWS; i++)
                database.Put(Key(i), Value(i));

            Assert.That(database.CanChangePassword, Is.True,
                "an encrypted LSM database must offer the change too");

            database.ChangePassword(PASSWORD, NEW_PASSWORD);
        }

        Assert.Multiple(() =>
        {
            Assert.That(ReadLsmRows(directory, NEW_PASSWORD), Is.EqualTo(ROWS),
                "the new password must open the directory and find every row");

            Assert.That(() => ReadLsmRows(directory, PASSWORD),
                Throws.InstanceOf<CryptographicException>(),
                "and the old one must be refused");
        });
    }

    /// <summary>
    /// Control: without the change, both passwords keep their meaning on an LSM directory. The wrong
    /// one is tried FIRST, because a refused open used to leave the file held.
    /// </summary>
    [Test]
    public void ControlAnLsmDatabaseKeepsItsPasswordsMeaningTest()
    {
        var directory = Path.Combine(m_directory, "lsm-control");

        using (var database = new WitDatabaseBuilder()
                   .WithLsmTree(directory).WithEncryption(PASSWORD, ITERATIONS).Build())
        {
            for (var i = 0; i < ROWS; i++)
                database.Put(Key(i), Value(i));
        }

        Assert.Multiple(() =>
        {
            Assert.That(() => ReadLsmRows(directory, NEW_PASSWORD),
                Throws.InstanceOf<CryptographicException>(),
                "CONTROL: a password that was never set must be refused");

            Assert.That(ReadLsmRows(directory, PASSWORD), Is.EqualTo(ROWS),
                "CONTROL: and the one that was set must work afterwards");
        });
    }

    #endregion

    #region Tools

    private static int ReadLsmRows(string directory, string password)
    {
        using var database = new WitDatabaseBuilder()
            .WithLsmTree(directory).WithEncryption(password, ITERATIONS).Build();

        var found = 0;
        for (var i = 0; i < ROWS * 2; i++)
        {
            if (database.Get(Key(i)) != null)
                found++;
        }

        return found;
    }

    private string CreateEncrypted(string name, string password, int rows)
    {
        var path = Path.Combine(m_directory, name);

        using var database = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithEncryption(password, ITERATIONS).Build();

        for (var i = 0; i < rows; i++)
            database.Put(Key(i), Value(i));

        return path;
    }

    private static void ChangePassword(string path, string current, string next)
    {
        using var database = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithEncryption(current, ITERATIONS).Build();

        database.ChangePassword(current, next);
    }

    private static int ReadRows(string path, string password)
    {
        using var database = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithEncryption(password, ITERATIONS).Build();

        var found = 0;
        for (var i = 0; i < ROWS * 2; i++)
        {
            if (database.Get(Key(i)) != null)
                found++;
        }

        return found;
    }

    private static void AppendRow(string path, string password)
    {
        using var database = new WitDatabaseBuilder()
            .WithFilePath(path).WithBTree().WithEncryption(password, ITERATIONS).Build();

        database.Put(Key(ROWS + 1), Value(ROWS + 1));
    }

    private static byte[] Key(int index) => System.Text.Encoding.UTF8.GetBytes($"key-{index:D6}");

    private static byte[] Value(int index) => System.Text.Encoding.UTF8.GetBytes($"value-{index:D6}");

    /// <summary>Everything past the preamble page - the ciphertext, which a rewrap must not touch.</summary>
    private static string HashPages(string path)
    {
        using var stream = File.OpenRead(path);

        if (stream.Length <= PAGE)
            Assert.Fail("the fixture wrote nothing past the preamble, so R3 would be vacuous");

        stream.Position = PAGE;

        using var sha = SHA256.Create();

        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string HashPreamble(string path)
    {
        var page = new byte[PAGE];

        using var stream = File.OpenRead(path);
        stream.ReadExactly(page, 0, PAGE);

        return Convert.ToHexString(SHA256.HashData(page));
    }

    #endregion
}
