using OutWit.Database.Core.Builder;
using OutWit.Database.Core.Encryption;
using OutWit.Database.Core.Storage;

namespace OutWit.Database.Core.Tests.Builder
{
    /// <summary>
    /// Regression tests: a caller-supplied storage must not bypass encryption.
    /// </summary>
    /// <remarks>
    /// <c>BuildStorage</c> returned <c>Options.CustomStorage</c> as-is, skipping the encryptor
    /// entirely, while <c>BuildProviderMetadata</c> still set <c>ProviderFeatures.Encryption</c> from
    /// the requested options. The database reported itself as encrypted and wrote plaintext - and
    /// <c>WithIndexedDbStorage()</c>, the documented Blazor WASM path, goes through exactly this
    /// branch.
    /// </remarks>
    [TestFixture]
    public class CustomStorageEncryptionTests
    {
        #region Constants

        private const string CANARY = "PLAINTEXT-CANARY-9137";

        /// <summary>
        /// Encryption spends this many bytes of every physical page on the nonce and tag, so a
        /// custom storage has to be sized as (logical page size + overhead).
        /// </summary>
        private static int Overhead =>
            ((OutWit.Database.Core.Interfaces.ICryptoProvider)new EncryptorProviderAesGcm(new byte[32])).Overhead;

        private static int EncryptedPageSize => DatabaseConstants.DEFAULT_PAGE_SIZE + Overhead;

        #endregion

        #region Encryption Is Applied

        [Test]
        public void CustomStorageWithEncryptionDoesNotWritePlaintextTest()
        {
            var backing = new StorageMemory(initialPageCount: 0, pageSize: EncryptedPageSize);

            using var database = new WitDatabaseBuilder()
                .WithStorage(backing)
                .WithEncryption("correct-horse")
                .WithBTree()
                .Build();

            database.Put("k"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes(CANARY));
            database.Flush();

            // Read the backing pages while the database is still alive: it owns the storage and
            // disposes it.
            Assert.That(ContainsCanary(backing), Is.False,
                "A custom storage must go through the encryptor, not around it");
        }

        [Test]
        public void CustomStorageWithoutEncryptionIsUsedDirectlyTest()
        {
            // No encryption, so no per-page overhead to allow for: a plain page size is correct here.
            var backing = new StorageMemory(
                initialPageCount: 0, pageSize: DatabaseConstants.DEFAULT_PAGE_SIZE);

            using var database = new WitDatabaseBuilder()
                .WithStorage(backing)
                .WithBTree()
                .Build();

            database.Put("k"u8.ToArray(), "v"u8.ToArray());

            Assert.That(database.Get("k"u8.ToArray()), Is.EqualTo("v"u8.ToArray()));
        }

        [Test]
        public void CustomStorageWithEncryptionRoundTripsValuesTest()
        {
            var backing = new StorageMemory(initialPageCount: 0, pageSize: EncryptedPageSize);

            using var database = new WitDatabaseBuilder()
                .WithStorage(backing)
                .WithEncryption("correct-horse")
                .WithBTree()
                .Build();

            database.Put("k"u8.ToArray(), System.Text.Encoding.UTF8.GetBytes(CANARY));

            Assert.That(database.Get("k"u8.ToArray()), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(CANARY)));
        }

        #endregion

        #region Impossible Combinations Fail Loudly

        [Test]
        public void TooSmallACustomPageSizeIsRejectedTest()
        {
            // A legal page size on its own, but it leaves a non-power-of-two once the per-page
            // encryption overhead is taken out. Silently writing plaintext is the one thing that
            // must not happen, so this has to fail.
            var backing = new StorageMemory(initialPageCount: 0, pageSize: DatabaseConstants.DEFAULT_PAGE_SIZE);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new WitDatabaseBuilder()
                    .WithStorage(backing)
                    .WithEncryption("correct-horse")
                    .WithBTree()
                    .Build());

            Assert.That(exception!.Message, Does.Contain("page size"));
        }

        #endregion

        #region Helper Methods

        private static bool ContainsCanary(StorageMemory storage)
        {
            var buffer = new byte[storage.PageSize];
            var canary = System.Text.Encoding.UTF8.GetBytes(CANARY);

            for (long page = 0; page < storage.PageCount; page++)
            {
                storage.ReadPage(page, buffer);
                if (IndexOf(buffer, canary) >= 0)
                    return true;
            }

            return false;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i + needle.Length <= haystack.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return i;
            }

            return -1;
        }

        #endregion
    }
}
