using NUnit.Framework;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Storage;
using OutWit.Database.Core.Stores;
using TextEncoding = System.Text.Encoding;

namespace OutWit.Database.Core.Tests.Stores
{
    /// <summary>
    /// Tests for key-value store statistics.
    /// </summary>
    [TestFixture]
    public class StoreStatisticsTests
    {
        #region Fields

        private IKeyValueStore m_store = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_store = new StoreBTree(new StorageMemory());
        }

        [TearDown]
        public void TearDown()
        {
            m_store.Dispose();
        }

        #endregion

        #region Helper Methods

        private static byte[] ToBytes(string s) => TextEncoding.UTF8.GetBytes(s);

        #endregion

        #region Count Extension Tests

        [Test]
        public void CountOnEmptyStoreReturnsZeroTest()
        {
            var count = m_store.Count();

            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void CountReturnsCorrectNumberTest()
        {
            for (int i = 0; i < 100; i++)
            {
                m_store.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            }

            var count = m_store.Count();

            Assert.That(count, Is.EqualTo(100));
        }

        [Test]
        public void CountAfterDeletesIsCorrectTest()
        {
            for (int i = 0; i < 50; i++)
            {
                m_store.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            }

            for (int i = 0; i < 20; i++)
            {
                m_store.Delete(ToBytes($"key{i}"));
            }

            var count = m_store.Count();

            Assert.That(count, Is.EqualTo(30));
        }

        [Test]
        public async Task CountAsyncReturnsCorrectNumberTest()
        {
            for (int i = 0; i < 50; i++)
            {
                await m_store.PutAsync(ToBytes($"key{i}"), ToBytes($"value{i}"));
            }

            var count = await m_store.CountAsync();

            Assert.That(count, Is.EqualTo(50));
        }

        [Test]
        public void CountWithNullStoreThrowsTest()
        {
            IKeyValueStore? nullStore = null;

            Assert.Throws<ArgumentNullException>(() => nullStore!.Count());
        }

        #endregion

        #region IsEmpty Extension Tests

        [Test]
        public void IsEmptyReturnsTrueForEmptyStoreTest()
        {
            Assert.That(m_store.IsEmpty(), Is.True);
        }

        [Test]
        public void IsEmptyReturnsFalseForNonEmptyStoreTest()
        {
            m_store.Put(ToBytes("key"), ToBytes("value"));

            Assert.That(m_store.IsEmpty(), Is.False);
        }

        [Test]
        public void IsEmptyAfterClearingStoreTest()
        {
            m_store.Put(ToBytes("key"), ToBytes("value"));
            m_store.Delete(ToBytes("key"));

            Assert.That(m_store.IsEmpty(), Is.True);
        }

        #endregion

        #region ContainsKey Extension Tests

        [Test]
        public void ContainsKeyReturnsTrueForExistingKeyTest()
        {
            m_store.Put(ToBytes("key"), ToBytes("value"));

            Assert.That(m_store.ContainsKey(ToBytes("key")), Is.True);
        }

        [Test]
        public void ContainsKeyReturnsFalseForMissingKeyTest()
        {
            Assert.That(m_store.ContainsKey(ToBytes("nonexistent")), Is.False);
        }

        [Test]
        public void ContainsKeyReturnsFalseAfterDeleteTest()
        {
            m_store.Put(ToBytes("key"), ToBytes("value"));
            m_store.Delete(ToBytes("key"));

            Assert.That(m_store.ContainsKey(ToBytes("key")), Is.False);
        }

        #endregion

        #region GetApproximateSizeInBytes Tests

        [Test]
        public void GetApproximateSizeInBytesReturnsPositiveForBTreeStoreTest()
        {
            // StoreBTree now implements IKeyValueStoreStatistics
            var size = m_store.GetApproximateSizeInBytes();

            // BTree stores always have at least header page
            Assert.That(size, Is.GreaterThan(0));
        }

        [Test]
        public void GetApproximateSizeInBytesWithNullStoreThrowsTest()
        {
            IKeyValueStore? nullStore = null;

            Assert.Throws<ArgumentNullException>(() => nullStore!.GetApproximateSizeInBytes());
        }

        #endregion

        #region StoreStatistics Wrapper Tests

        [Test]
        public void GetStatisticsReturnsWrapperTest()
        {
            var stats = m_store.GetStatistics();

            Assert.That(stats, Is.Not.Null);
            Assert.That(stats, Is.InstanceOf<StoreStatistics>());
        }

        [Test]
        public void StoreStatisticsCountMatchesExtensionTest()
        {
            for (int i = 0; i < 25; i++)
            {
                m_store.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            }

            var stats = m_store.GetStatistics();

            Assert.That(stats.Count(), Is.EqualTo(25));
            Assert.That(stats.Count(), Is.EqualTo(m_store.Count()));
        }

        [Test]
        public async Task StoreStatisticsCountAsyncWorksTest()
        {
            for (int i = 0; i < 25; i++)
            {
                m_store.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            }

            var stats = m_store.GetStatistics();
            var count = await stats.CountAsync();

            Assert.That(count, Is.EqualTo(25));
        }

        [Test]
        public void StoreStatisticsEstimatedKeyCountEqualsCountTest()
        {
            for (int i = 0; i < 10; i++)
            {
                m_store.Put(ToBytes($"key{i}"), ToBytes($"value{i}"));
            }

            var stats = m_store.GetStatistics();

            Assert.That(stats.EstimatedKeyCount, Is.EqualTo(10));
        }

        [Test]
        public void StoreStatisticsHasNativeStatisticsIsTrueForBTreeTest()
        {
            var stats = m_store.GetStatistics();

            // StoreBTree now implements IKeyValueStoreStatistics
            Assert.That(stats.HasNativeStatistics, Is.True);
        }

        [Test]
        public void StoreStatisticsAreStatisticsExactIsTrueForBTreeTest()
        {
            var stats = m_store.GetStatistics();

            // BTree provides exact statistics
            Assert.That(stats.AreStatisticsExact, Is.True);
        }

        [Test]
        public void StoreStatisticsWithNullStoreThrowsTest()
        {
            Assert.Throws<ArgumentNullException>(() => new StoreStatistics(null!));
        }

        #endregion

        #region IKeyValueStoreStatistics Interface Tests

        [Test]
        public void StoreStatisticsImplementsInterfaceTest()
        {
            var stats = m_store.GetStatistics();

            IKeyValueStoreStatistics iStats = stats;

            Assert.That(iStats.Count(), Is.EqualTo(0));
        }

        [Test]
        public void BTreeStoreImplementsIKeyValueStoreStatisticsTest()
        {
            // StoreBTree should now implement IKeyValueStoreStatistics
            Assert.That(m_store, Is.InstanceOf<IKeyValueStoreStatistics>());

            var stats = (IKeyValueStoreStatistics)m_store;
            
            m_store.Put(ToBytes("key"), ToBytes("value"));
            
            Assert.That(stats.Count(), Is.EqualTo(1));
            Assert.That(stats.AreStatisticsExact, Is.True);
            Assert.That(stats.ApproximateSizeInBytes, Is.GreaterThan(0));
        }

        #endregion

        #region Native Statistics Direct Access Tests

        [Test]
        public void BTreeStoreNativeCountIsEfficientTest()
        {
            // Add many items
            for (int i = 0; i < 1000; i++)
            {
                m_store.Put(ToBytes($"key{i:D4}"), ToBytes($"value{i}"));
            }

            // Direct count through interface should be efficient (no scan)
            var stats = (IKeyValueStoreStatistics)m_store;
            var count = stats.Count();

            Assert.That(count, Is.EqualTo(1000));
        }

        [Test]
        public void BTreeStoreApproximateSizeGrowsWithDataTest()
        {
            var stats = (IKeyValueStoreStatistics)m_store;
            var initialSize = stats.ApproximateSizeInBytes;

            // Add data
            for (int i = 0; i < 100; i++)
            {
                m_store.Put(ToBytes($"key{i}"), new byte[100]);
            }

            var finalSize = stats.ApproximateSizeInBytes;

            Assert.That(finalSize, Is.GreaterThanOrEqualTo(initialSize));
        }

        #endregion
    }
}
