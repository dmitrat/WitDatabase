using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Utils;

namespace OutWit.Database.Core.Tests.AuditVerification;

/// <summary>
/// Phase 18 part 1 - the four encryption-format findings, as rules rather than as measurements.
/// </summary>
/// <remarks>
/// <para>
/// Every test here is RED on 12.8.0 and says so in its own text. They were written before the format
/// change, so that the change is answerable to something: see <c>Docs/PHASE18-ENCRYPTION-PLAN.md</c>
/// and section D of <c>Docs/RELEASE-READINESS-2026-08-11.md</c>.
/// </para>
/// <para>
/// Each rule is paired with a control that must come out the OTHER way, because an assertion about
/// bytes on disk is easy to satisfy by accident - reading the wrong file, comparing a buffer with
/// itself, or measuring a derivation that no longer feeds the thing being measured.
/// </para>
/// <para>
/// <b>Both directions, measured 2026-08-14 rather than intended.</b> Red without the fix is the
/// easy half; the other half is that each rule CAN come out green, which no run of unfixed code
/// shows. So every rule has a control that is the same assertion over an arrangement where the
/// property already holds:
/// </para>
/// <list type="bullet">
/// <item><description>E1/E2 -
/// <see cref="ControlTwoDatabasesWithDifferentPasswordsHaveDifferentHeadsTest"/> is
/// "two heads differ" over two files whose heads do differ.</description></item>
/// <item><description>E3 - <see cref="ControlAFastEncryptionDatabaseOpensWithTheFlagTest"/> is
/// "this file opens and answers" over a configuration that can open it.</description></item>
/// <item><description>E4 - <see cref="ControlOneSessionDoesNotRepeatANonceForOnePageTest"/> is
/// "two nonces differ" over two writes that do get different nonces.</description></item>
/// </list>
/// <para>
/// 4 red and 6 green on 12.8.0. If a later run shows a rule red AND its paired control red, the
/// fixture broke rather than the subject.
/// </para>
/// </remarks>
[TestFixture]
[Category("AuditVerification")]
public class EncryptionFormatFindingsTests
{
    #region Constants

    private const string PASSWORD = "correct horse battery staple";

    private const string OTHER_PASSWORD = "a completely different password";

    #endregion

    #region Fields

    private string m_directory = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"EncryptionFormat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); }
        catch { /* a held handle is not a test result */ }
    }

    #endregion

    #region E1 - the salt must not be a function of the password

    /// <summary>
    /// E1. <c>DerivePasswordSalt(p) = SHA256(p + "_WitDB_Salt")[..16]</c>, so two databases created
    /// with one password have byte-identical salts and therefore byte-identical keys.
    /// </summary>
    /// <remarks>
    /// RED on 12.8.0: both heads read <c>00379B03582ABC05</c>. The salt is the file's, not the
    /// password's - it must be drawn at creation and stored.
    /// </remarks>
    [Test]
    public void TwoDatabasesWithOnePasswordHaveDifferentSaltsTest()
    {
        var first = CreateEncrypted("first.witdb", PASSWORD);
        var second = CreateEncrypted("second.witdb", PASSWORD);

        var headFirst = HeadOf(first);
        var headSecond = HeadOf(second);

        Assert.That(headSecond, Is.Not.EqualTo(headFirst),
            $"two databases created with one password must not begin with the same bytes; both read "
            + $"{Convert.ToHexString(headFirst)}, which is salt[0..8] and therefore the same key twice");
    }

    /// <summary>
    /// Control for the test above: the comparison can tell two files apart at all. Without it,
    /// a head-reading helper that returned a constant - or read the same file twice - would report
    /// the rule as satisfied the moment the rule became true, and also while it was false.
    /// </summary>
    [Test]
    public void ControlTwoDatabasesWithDifferentPasswordsHaveDifferentHeadsTest()
    {
        var first = CreateEncrypted("first.witdb", PASSWORD);
        var other = CreateEncrypted("other.witdb", OTHER_PASSWORD);

        Assert.That(HeadOf(other), Is.Not.EqualTo(HeadOf(first)),
            "the instrument must be able to see a difference between two files' first bytes");
    }

    /// <summary>
    /// Control: the helper reads the real head of the real file. An unencrypted database begins with
    /// the magic bytes, so if this fails, every byte comparison in this fixture is about something
    /// other than the beginning of a database.
    /// </summary>
    [Test]
    public void ControlTheHeadHelperReadsTheStartOfTheFileTest()
    {
        var plain = Path.Combine(m_directory, "plain.witdb");

        using (var database = new WitDatabaseBuilder().WithFilePath(plain).WithBTree().Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
            database.Flush();
        }

        var head = File.ReadAllBytes(plain).AsSpan(0, 16).ToArray();

        Assert.That(head, Is.EqualTo(DatabaseConstants.MAGIC_BYTES.ToArray()),
            "an unencrypted database begins with the magic bytes; if it does not, the helper is not "
            + "reading the head of a database file");
    }

    #endregion

    #region E2 - the file's first bytes must not be derivable from the password

    /// <summary>
    /// E2, and the reason part 1 comes before the write-path regression. The page nonce is
    /// <c>(salt[0..8] XOR pageNumber) || counter</c> written in the clear at the head of each page,
    /// and page 0's number is 0 - so the first eight bytes of the file ARE the salt.
    /// </summary>
    /// <remarks>
    /// That makes the file a password verifier costing one SHA-256. Measured 2026-08-14 on this
    /// machine, single-threaded: 2,000,007 candidates in 0.48 s against 5.6 hours through PBKDF2 at
    /// 100,000 - a factor of about 41,000 on one core, and the work factor buys none of it back.
    /// RED on 12.8.0.
    /// </remarks>
    [Test]
    public void TheFileHeadIsNotAFunctionOfThePasswordTest()
    {
        var path = CreateEncrypted("verifier.witdb", PASSWORD);

        var head = HeadOf(path);
        var derived = CryptoUtils.DerivePasswordSalt(PASSWORD).AsSpan(0, 8).ToArray();

        Assert.That(head, Is.Not.EqualTo(derived),
            "the head of an encrypted database must not be computable from the password alone; it is "
            + $"{Convert.ToHexString(head)}, which one SHA-256 of a candidate reproduces");
    }

    /// <summary>
    /// Control: the derivation this test compares against is still the one the builder uses. If
    /// <c>DerivePasswordSalt</c> were changed or bypassed without the format changing, the rule above
    /// would go green while the file still carried a verifier - a different one.
    /// </summary>
    [Test]
    public void ControlThePasswordDerivationIsStillDeterministicTest()
    {
        var once = CryptoUtils.DerivePasswordSalt(PASSWORD);
        var twice = CryptoUtils.DerivePasswordSalt(PASSWORD);

        Assert.Multiple(() =>
        {
            Assert.That(twice, Is.EqualTo(once), "the derivation under test must be deterministic");
            Assert.That(once, Has.Length.EqualTo(16), "and must still produce a 16-byte salt");
            Assert.That(CryptoUtils.DerivePasswordSalt(OTHER_PASSWORD), Is.Not.EqualTo(once),
                "and must still depend on the password, or the comparison above is vacuous");
        });
    }

    #endregion

    #region E3 - the iteration count must be a property of the file

    /// <summary>
    /// E3. <c>Fast Encryption</c> derives at 10,000 iterations instead of 100,000 and the count lives
    /// only in the caller's configuration, so a file written with it cannot be opened without it.
    /// </summary>
    /// <remarks>
    /// RED on 12.8.0 with <c>CryptographicException: Failed to decrypt page 0 - authentication
    /// failed</c>, which names neither the flag nor the count. The count belongs in the header.
    /// </remarks>
    [Test]
    public void AFastEncryptionDatabaseOpensWithoutTheFlagTest()
    {
        var path = Path.Combine(m_directory, "fast.witdb");

        using (var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree()
                   .WithEncryptionFast(PASSWORD).Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
            database.Flush();
        }

        Assert.That(() =>
        {
            using var reopened = new WitDatabaseBuilder().WithFilePath(path).WithBTree()
                .WithEncryption(PASSWORD).Build();

            return reopened.Get("k"u8.ToArray());
        }, Throws.Nothing,
            "the iteration count is a property of the file, so the password alone must be enough to "
            + "open it");
    }

    /// <summary>
    /// Control: the file itself is sound and the password is right. Without this the test above could
    /// be red because the write failed, and the fix would be looked for in the wrong place.
    /// </summary>
    [Test]
    public void ControlAFastEncryptionDatabaseOpensWithTheFlagTest()
    {
        var path = Path.Combine(m_directory, "fast-control.witdb");

        using (var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree()
                   .WithEncryptionFast(PASSWORD).Build())
        {
            database.Put("k"u8.ToArray(), "v"u8.ToArray());
            database.Flush();
        }

        using var reopened = new WitDatabaseBuilder().WithFilePath(path).WithBTree()
            .WithEncryptionFast(PASSWORD).Build();

        Assert.That(reopened.Get("k"u8.ToArray()), Is.EqualTo("v"u8.ToArray()),
            "with the flag the same file must open and return what was written");
    }

    #endregion

    #region E4 - a nonce must not be reused across sessions

    /// <summary>
    /// E4. <c>EncryptorPage</c>'s write counter is a field set to 0 in the constructor, and the
    /// encryptor is constructed when the database is OPENED. Two sessions therefore walk the same
    /// nonce sequence, and the salt half of the nonce is the same in both because the salt is derived
    /// from the password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RED on 12.8.0: both sessions write page 0 under nonce
    /// <c>00379B03582ABC0501000000</c>. This is the component the engine builds on every open, so the
    /// arrangement is not contrived - what a full session decides is only WHICH page lands on which
    /// counter value.
    /// </para>
    /// <para>
    /// AES-GCM under a repeated nonce leaks the XOR of the two plaintexts, which is why the failure
    /// message carries the second session's page recovered from the first with no key at all.
    /// </para>
    /// </remarks>
    [Test]
    public void TwoSessionsDoNotWriteOnePageUnderOneNonceTest()
    {
        var salt = CryptoUtils.DerivePasswordSalt(PASSWORD);
        var key = CryptoUtils.DeriveKey(PASSWORD, salt, CryptoUtils.WASM_PBKDF2_ITERATIONS);

        var pageOne = Plaintext("SESSION ONE wrote this page, and it says AAAAAAAAAAAAAAAAAAAAAAA");
        var pageTwo = Plaintext("SESSION TWO wrote this page, and it says BBBBBBBBBBBBBBBBBBBBBBB");

        var cipherOne = EncryptFirstPage(key, salt, pageOne);
        var cipherTwo = EncryptFirstPage(key, salt, pageTwo);

        var nonceOne = cipherOne.AsSpan(0, NONCE_SIZE).ToArray();
        var nonceTwo = cipherTwo.AsSpan(0, NONCE_SIZE).ToArray();

        Assert.That(nonceTwo, Is.Not.EqualTo(nonceOne),
            "two sessions must not encrypt one page under one nonce; both used "
            + $"{Convert.ToHexString(nonceOne)}, and session two's page falls out of session one's "
            + $"with no key: \"{System.Text.Encoding.ASCII.GetString(RecoverSecond(cipherOne, cipherTwo, pageOne))}\"");
    }

    /// <summary>
    /// Control: the counter does its job WITHIN a session, so the test above is about the boundary
    /// between sessions and not about the mechanism being absent. This one is green on 12.8.0 and
    /// must stay green.
    /// </summary>
    [Test]
    public void ControlOneSessionDoesNotRepeatANonceForOnePageTest()
    {
        var salt = CryptoUtils.DerivePasswordSalt(PASSWORD);
        var key = CryptoUtils.DeriveKey(PASSWORD, salt, CryptoUtils.WASM_PBKDF2_ITERATIONS);

        var first = new byte[PLAINTEXT_SIZE + OVERHEAD];
        var second = new byte[PLAINTEXT_SIZE + OVERHEAD];

        using (var session = new EncryptorPage(new EncryptorProviderAesGcm(key), salt))
        {
            session.Encrypt(Plaintext("first write"), 0, first);
            session.Encrypt(Plaintext("second write"), 0, second);
        }

        Assert.That(second.AsSpan(0, NONCE_SIZE).ToArray(), Is.Not.EqualTo(first.AsSpan(0, NONCE_SIZE).ToArray()),
            "within one session two writes of page 0 must get different nonces");
    }

    /// <summary>
    /// Control: the recovery this fixture uses to make the failure legible is real. If the XOR
    /// arithmetic were wrong, the message above would be reassuring nonsense.
    /// </summary>
    [Test]
    public void ControlTheXorRecoveryIsArithmeticallySoundTest()
    {
        var salt = CryptoUtils.DerivePasswordSalt(PASSWORD);
        var key = CryptoUtils.DeriveKey(PASSWORD, salt, CryptoUtils.WASM_PBKDF2_ITERATIONS);

        var pageOne = Plaintext("SESSION ONE wrote this page, and it says AAAAAAAAAAAAAAAAAAAAAAA");
        var pageTwo = Plaintext("SESSION TWO wrote this page, and it says BBBBBBBBBBBBBBBBBBBBBBB");

        var cipherOne = EncryptFirstPage(key, salt, pageOne);
        var cipherTwo = EncryptFirstPage(key, salt, pageTwo);

        // PINS A DEFECT, NOT CORRECT BEHAVIOUR. When the nonce stops repeating across sessions this
        // recovery stops working, and this test must be inverted to assert that it does not.
        Assert.That(RecoverSecond(cipherOne, cipherTwo, pageOne), Is.EqualTo(pageTwo),
            "on 12.8.0 the repeated nonce hands the second plaintext over; when that stops being true "
            + "this assertion is the one to invert");
    }

    #endregion

    #region Tools

    private const int NONCE_SIZE = 12;

    private const int TAG_SIZE = 16;

    private const int OVERHEAD = NONCE_SIZE + TAG_SIZE;

    private const int PLAINTEXT_SIZE = 64;

    private string CreateEncrypted(string name, string password)
    {
        var path = Path.Combine(m_directory, name);

        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree()
            .WithEncryption(password).Build();

        database.Put("k"u8.ToArray(), "v"u8.ToArray());
        database.Flush();

        return path;
    }

    private static byte[] HeadOf(string path) => File.ReadAllBytes(path).AsSpan(0, 8).ToArray();

    private static byte[] Plaintext(string text)
    {
        var buffer = new byte[PLAINTEXT_SIZE];
        System.Text.Encoding.ASCII.GetBytes(text).CopyTo(buffer, 0);
        return buffer;
    }

    private static byte[] EncryptFirstPage(byte[] key, byte[] salt, byte[] plaintext)
    {
        var ciphertext = new byte[PLAINTEXT_SIZE + OVERHEAD];

        // A fresh encryptor per call is exactly what opening the database twice produces.
        using var session = new EncryptorPage(new EncryptorProviderAesGcm(key), salt);
        session.Encrypt(plaintext, 0, ciphertext);

        return ciphertext;
    }

    /// <summary>
    /// Recovers the second plaintext from the first and the two ciphertexts, using no key. Only
    /// possible while the two share a nonce.
    /// </summary>
    private static byte[] RecoverSecond(byte[] cipherOne, byte[] cipherTwo, byte[] plaintextOne)
    {
        var recovered = new byte[PLAINTEXT_SIZE];

        for (var i = 0; i < PLAINTEXT_SIZE; i++)
            recovered[i] = (byte)(cipherOne[NONCE_SIZE + i] ^ cipherTwo[NONCE_SIZE + i] ^ plaintextOne[i]);

        return recovered;
    }

    #endregion
}
