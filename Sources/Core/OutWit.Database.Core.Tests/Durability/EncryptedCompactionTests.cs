using System.Security.Cryptography;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.LSM;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Durability;

/// <summary>
/// Compaction of an encrypted LSM store.
/// </summary>
/// <remarks>
/// <c>Compactor</c> created its readers as <c>new SSTableReader(f)</c> and its output with
/// <c>encryptor: null</c> - it was never told about the store's encryptor at all. So compacting an
/// encrypted store either failed outright with <i>"SSTable is encrypted but no encryptor provided"</i>
/// or, had the reads somehow succeeded, would have rewritten every row **in clear text**.
///
/// It was latent because compaction rarely ran in the suites that use encryption. It surfaced when the
/// implicit per-statement transaction started committing on every autocommit write, which made
/// memtable flushes - and therefore compaction - happen far more often. Worth stating plainly: this
/// is a defect a behaviour change <i>revealed</i>, not one it caused.
/// </remarks>
[TestFixture]
[Category("Durability")]
public sealed class EncryptedCompactionTests
{
    #region Fields

    private const int ROWS = 40;

    private string m_directory = null!;
    private byte[] m_key = null!;
    private byte[] m_salt = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witdb-encrypted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);

        m_key = RandomNumberGenerator.GetBytes(32);
        m_salt = RandomNumberGenerator.GetBytes(16);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(m_directory, recursive: true); } catch { /* best effort */ }
    }

    #endregion

    #region Tests

    [Test]
    public void CompactingAnEncryptedStoreKeepsItsRowsTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        using (var store = new StoreLsm(m_directory, Options(encryptor)))
        {
            // Several flushes, so there is something to merge.
            for (int batch = 0; batch < 4; batch++)
            {
                for (int i = 0; i < ROWS / 4; i++)
                    store.Put(Key(batch * (ROWS / 4) + i), Value(batch * (ROWS / 4) + i));

                store.Checkpoint();
            }

            store.Compact();
            store.WaitForCompaction();
        }

        using var reopened = new StoreLsm(m_directory, Options(encryptor));

        var readable = Enumerable.Range(0, ROWS).Count(i => reopened.Get(Key(i)) != null);

        Assert.That(readable, Is.EqualTo(ROWS),
            "every row must survive compaction - the compactor was never given the store's encryptor, "
            + "so it could not read the tables it was merging");
    }

    /// <summary>
    /// And the merged output is still encrypted. A compactor that silently wrote clear text would
    /// pass the test above perfectly.
    /// </summary>
    [Test]
    public void CompactedOutputIsStillEncryptedTest()
    {
        using var provider = new EncryptorProviderAesGcm(m_key);
        using var encryptor = new EncryptorBlock(provider, m_salt);

        var marker = "unmistakable-plaintext-marker";

        using (var store = new StoreLsm(m_directory, Options(encryptor)))
        {
            for (int batch = 0; batch < 4; batch++)
            {
                for (int i = 0; i < ROWS / 4; i++)
                {
                    var index = batch * (ROWS / 4) + i;
                    store.Put(Key(index), System.Text.Encoding.UTF8.GetBytes($"{marker}-{index:D4}"));
                }

                store.Checkpoint();
            }

            store.Compact();
            store.WaitForCompaction();
        }

        var markerBytes = System.Text.Encoding.UTF8.GetBytes(marker);

        foreach (var table in Directory.GetFiles(m_directory, "sst_*.sst"))
        {
            var bytes = File.ReadAllBytes(table);

            Assert.That(Contains(bytes, markerBytes), Is.False,
                $"'{Path.GetFileName(table)}' holds the value in clear text - a compaction that "
                + "reads encrypted tables and writes an unencrypted one loses the confidentiality "
                + "the store was configured for, and every row-count assertion would still pass");
        }
    }

    #endregion

    #region Tools

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var found = true;

            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
                return true;
        }

        return false;
    }

    private static LsmOptions Options(EncryptorBlock encryptor) => new()
    {
        Encryptor = encryptor,
        BackgroundCompaction = false,
        MemTableSizeLimit = 64 * 1024
    };

    private static byte[] Key(int i) => System.Text.Encoding.UTF8.GetBytes($"k{i:D4}");

    private static byte[] Value(int i) => System.Text.Encoding.UTF8.GetBytes($"value-{i:D4}");

    #endregion
}
