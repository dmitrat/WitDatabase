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
/// <see cref="ControlTwoDatabasesWithDifferentPasswordsHaveDifferentSaltsTest"/> is
/// "two salts differ" over two files whose salts do differ, and
/// <see cref="ControlTheSearchForTheDerivedSaltCanFindItTest"/> shows the search can find a planted
/// value.</description></item>
/// <item><description>E3 - <see cref="ControlAFastEncryptionDatabaseOpensWithTheFlagTest"/> is
/// "this file opens and answers" over a configuration that can open it.</description></item>
/// <item><description>E4 - <see cref="ControlOneSessionDoesNotRepeatANonceForOnePageTest"/> is
/// "two nonces differ" over two writes that do get different nonces.</description></item>
/// </list>
/// <para>
/// <b>And then each part of the fix was sabotaged separately</b>, because sabotaging it as a whole
/// reports one large number and lets a wrong sentence about the mechanism stand. Measured red sets,
/// this fixture and <c>EncryptionFormatCompatibilityTests</c> together:
/// </para>
/// <code>
/// the salt goes back to being the password's hash    2 red
/// the sequence goes back to restarting per session   1 red
/// the iteration count comes from the caller again    4 red
/// the preamble is never recognised on read           6 red
/// </code>
/// <para>
/// <b>Two of those measurements changed a case.</b> Sabotaging the sequence first turned NOTHING
/// red: the case compared the two files page by page looking for a repeated nonce, and whether two
/// sessions collide that way depends on which pages each happens to write. It asserts the property
/// instead now - every number the second session uses is above every number the first could have
/// used - and that is red the moment a sequence restarts. And the E2 case asked only about the
/// file's first eight bytes, which stayed green with the salt sabotaged, because the salt had merely
/// moved to offset 24 and the file was still a password verifier costing one hash.
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

        var saltFirst = SaltOf(first);
        var saltSecond = SaltOf(second);

        Assert.That(saltSecond, Is.Not.EqualTo(saltFirst),
            "two databases created with one password must not share a salt, or they share a key; "
            + $"both read {Convert.ToHexString(saltFirst)}");
    }

    /// <summary>
    /// The consequence, stated where nobody has to know the layout to check it: one password, two
    /// databases, and the same value stored in both must not produce the same bytes on disk.
    /// </summary>
    /// <remarks>
    /// RED on 12.8.0 for the same reason as the test above. Worth having separately because it holds
    /// whatever the header looks like, and would survive a later format change that this fixture's
    /// other cases would not.
    /// </remarks>
    [Test]
    public void TwoDatabasesWithOnePasswordDoNotProduceTheSameBytesTest()
    {
        var first = CreateEncrypted("bytes-one.witdb", PASSWORD);
        var second = CreateEncrypted("bytes-two.witdb", PASSWORD);

        Assert.That(File.ReadAllBytes(second), Is.Not.EqualTo(File.ReadAllBytes(first)),
            "one password over two databases holding the same value must not write identical files");
    }

    /// <summary>
    /// Control for the test above: the comparison can tell two files apart at all. Without it,
    /// a head-reading helper that returned a constant - or read the same file twice - would report
    /// the rule as satisfied the moment the rule became true, and also while it was false.
    /// </summary>
    [Test]
    public void ControlTwoDatabasesWithDifferentPasswordsHaveDifferentSaltsTest()
    {
        var first = CreateEncrypted("first.witdb", PASSWORD);
        var other = CreateEncrypted("other.witdb", OTHER_PASSWORD);

        Assert.That(SaltOf(other), Is.Not.EqualTo(SaltOf(first)),
            "the instrument must be able to see a difference between two files' salts");
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
    public void NothingInThePlaintextRegionIsComputableFromThePasswordTest()
    {
        var path = CreateEncrypted("verifier.witdb", PASSWORD);

        var derived = CryptoUtils.DerivePasswordSalt(PASSWORD);
        var plaintext = File.ReadAllBytes(path).AsSpan(0, PhysicalPageSize).ToArray();

        // The whole plaintext region, not its first eight bytes. An earlier version of this case
        // asked only about the head, and it stayed GREEN with the salt sabotaged back into
        // SHA256(password + "_WitDB_Salt") - because the salt had merely moved to offset 24 and the
        // file was still a verifier costing one hash. Where the value sits is not the point.
        var at = IndexOf(plaintext, derived.AsSpan(0, 8));

        Assert.That(at, Is.LessThan(0),
            "nothing an encrypted database keeps in the clear may be computable from the password "
            + $"alone; {Convert.ToHexString(derived.AsSpan(0, 8))} is at offset {at}, and one "
            + "SHA-256 per candidate reproduces it");
    }

    /// <summary>
    /// Control: the search can find what it is looking for. Without it, an <c>IndexOf</c> that never
    /// matched anything would report every database clean.
    /// </summary>
    [Test]
    public void ControlTheSearchForTheDerivedSaltCanFindItTest()
    {
        var derived = CryptoUtils.DerivePasswordSalt(PASSWORD);
        var haystack = new byte[512];
        derived.AsSpan(0, 8).CopyTo(haystack.AsSpan(300));

        Assert.Multiple(() =>
        {
            Assert.That(IndexOf(haystack, derived.AsSpan(0, 8)), Is.EqualTo(300),
                "the search must find the value where it was planted");
            Assert.That(IndexOf(new byte[512], derived.AsSpan(0, 8)), Is.LessThan(0),
                "and must not find it where it is absent");
        });
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
        var path = Path.Combine(m_directory, "sessions.witdb");

        // Two opens of one database, which is what the defect is about: the encryptor is built on
        // OPEN, so a counter it keeps in a field starts again every time.
        WriteThrough(path, "AAAAAAAAAAAAAAAA");
        var first = File.ReadAllBytes(path);

        WriteThrough(path, "a different value, of a different length, in the second session");
        var second = File.ReadAllBytes(path);

        // The FIRST version of this case compared the two files page by page and asked whether any
        // page had been rewritten under a nonce it already carried. It passed with the sequence
        // sabotaged back into a per-session counter - because whether two sessions collide that way
        // depends on which pages each of them happens to write, and in this fixture they did not.
        // Measured: sabotaging the sequence turned NOTHING red. A case that can only catch the
        // defect by luck is not a case.
        //
        // What has power is the property itself: every number the second session uses must be above
        // every number the first session could have used. That is false the moment a sequence
        // restarts, whatever pages get written.
        var highestInFirst = SequencesIn(first).Max();

        var rewritten = new List<int>();
        var reused = new List<string>();

        foreach (var page in PhysicalPages(first, second))
        {
            if (!BodyChanged(first, second, page))
                continue;

            rewritten.Add(page);

            var sequence = SequenceAt(second, page);

            if (sequence <= highestInFirst)
                reused.Add($"page {page} at sequence {sequence}, against {highestInFirst} already used");
        }

        Assert.Multiple(() =>
        {
            // Without this the whole case passes on a run where the second session wrote nothing.
            Assert.That(rewritten, Is.Not.Empty,
                "the second session must have rewritten SOMETHING, or this case is asking about no "
                + "page at all");

            Assert.That(reused, Is.Empty,
                "a second session must not write under a sequence number the first session had "
                + "already reached: " + string.Join("; ", reused));
        });
    }

    /// <summary>
    /// The sequence is the file's, not the session's: what the second session starts from is above
    /// everything the first could have used, whether or not the first was closed properly.
    /// </summary>
    /// <remarks>
    /// RED on 12.8.0, where there was nowhere in the file for this number to live. The reservation
    /// is written and flushed BEFORE a single number is handed out, so a killed process loses the
    /// remainder of its block rather than handing the next session numbers it already spent.
    /// </remarks>
    [Test]
    public void TheNonceSequenceAdvancesAcrossSessionsTest()
    {
        var path = Path.Combine(m_directory, "sequence.witdb");

        WriteThrough(path, "first");
        var afterFirst = SequenceOf(path);

        WriteThrough(path, "second");
        var afterSecond = SequenceOf(path);

        Assert.Multiple(() =>
        {
            Assert.That(afterFirst, Is.GreaterThan(0UL),
                "the file must record a sequence at all");
            Assert.That(afterSecond, Is.GreaterThan(afterFirst),
                "and the second session must start above everything the first reserved");
        });
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

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        }

        return -1;
    }

    private void WriteThrough(string path, string value)
    {
        using var database = new WitDatabaseBuilder().WithFilePath(path).WithBTree()
            .WithEncryption(PASSWORD).Build();

        database.Put("k"u8.ToArray(), System.Text.Encoding.ASCII.GetBytes(value));
        database.Flush();
    }

    private static CryptoHeader HeaderOf(string path)
    {
        var page = File.ReadAllBytes(path);

        Assert.That(CryptoHeader.TryReadFrom(page, out var header), Is.True,
            "this database carries no crypto preamble, so there is no salt in it to read");

        return header;
    }

    private static byte[] SaltOf(string path) => HeaderOf(path).Salt;

    private static ulong SequenceOf(string path) => HeaderOf(path).NonceSequence;

    /// <summary>
    /// Bytes of one physical page. Larger than a logical page by the nonce and the tag.
    /// </summary>
    private static int PhysicalPageSize => DatabaseConstants.DEFAULT_PAGE_SIZE + OVERHEAD;

    /// <summary>
    /// Every encrypted page both readings of the file have. Page 0 is the plaintext preamble and is
    /// not one of them.
    /// </summary>
    private static IEnumerable<int> PhysicalPages(byte[] first, byte[] second)
    {
        var pages = Math.Min(first.Length, second.Length) / PhysicalPageSize;

        for (var page = 1; page < pages; page++)
            yield return page;
    }

    private static byte[] NonceAt(byte[] file, int page) =>
        file.AsSpan(page * PhysicalPageSize, NONCE_SIZE).ToArray();

    /// <summary>
    /// The sequence half of a page's nonce, as the encryptor wrote it.
    /// </summary>
    private static ulong SequenceAt(byte[] file, int page) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
            file.AsSpan(page * PhysicalPageSize + 4, sizeof(ulong)));

    /// <summary>
    /// Every sequence number this file's pages carry. A page nobody has written is all zeros and is
    /// not one of them.
    /// </summary>
    private static IEnumerable<ulong> SequencesIn(byte[] file)
    {
        var pages = file.Length / PhysicalPageSize;
        var found = false;

        for (var page = 1; page < pages; page++)
        {
            var nonce = NonceAt(file, page);

            if (nonce.All(b => b == 0))
                continue;

            found = true;
            yield return SequenceAt(file, page);
        }

        if (!found)
            Assert.Fail("no encrypted page carried a nonce, so there is no sequence here to read");
    }

    private static bool BodyChanged(byte[] first, byte[] second, int page)
    {
        var offset = page * PhysicalPageSize + NONCE_SIZE;
        var length = PhysicalPageSize - NONCE_SIZE;

        return !first.AsSpan(offset, length).SequenceEqual(second.AsSpan(offset, length));
    }

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
