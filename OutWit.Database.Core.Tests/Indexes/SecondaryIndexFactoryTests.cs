using NUnit.Framework;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Indexes
{
    [TestFixture]
    public class SecondaryIndexFactoryTests
    {
        #region Fields

        private string m_testDir = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_testDir = Path.Combine(Path.GetTempPath(), "WitDB_FactoryTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_testDir))
            {
                try { Directory.Delete(m_testDir, true); } catch { }
            }
        }

        #endregion

        #region SecondaryIndexFactoryKeyValueStore Tests

        [Test]
        public void KeyValueStoreFactoryCreatesUniqueIndexTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Act
            using var index = factory.CreateIndex("unique_idx", isUnique: true);

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index.Name, Is.EqualTo("unique_idx"));
            Assert.That(index.IsUnique, Is.True);
            Assert.That(index, Is.InstanceOf<SecondaryIndexKeyValueStore>());
        }

        [Test]
        public void KeyValueStoreFactoryCreatesNonUniqueIndexTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Act
            using var index = factory.CreateIndex("non_unique_idx", isUnique: false);

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index.Name, Is.EqualTo("non_unique_idx"));
            Assert.That(index.IsUnique, Is.False);
        }

        [Test]
        public void KeyValueStoreFactoryProviderKeyReturnsDefaultTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Assert
            Assert.That(factory.ProviderKey, Is.EqualTo(SecondaryIndexFactoryKeyValueStore.PROVIDER_KEY));
        }

        [Test]
        public void KeyValueStoreFactoryProviderKeyCanBeCustomizedTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory(), "custom");

            // Assert
            Assert.That(factory.ProviderKey, Is.EqualTo("custom"));
        }

        [Test]
        public void KeyValueStoreFactoryCreatesMultipleIndependentIndexesTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Act
            using var index1 = factory.CreateIndex("idx1", isUnique: true);
            using var index2 = factory.CreateIndex("idx2", isUnique: true);

            index1.Add(GetBytes("key1"), GetBytes("pk1"));
            index2.Add(GetBytes("key2"), GetBytes("pk2"));

            // Assert
            Assert.That(index1.Find(GetBytes("key1")).Any(), Is.True);
            Assert.That(index1.Find(GetBytes("key2")).Any(), Is.False);
            Assert.That(index2.Find(GetBytes("key2")).Any(), Is.True);
            Assert.That(index2.Find(GetBytes("key1")).Any(), Is.False);
        }

        [Test]
        public void KeyValueStoreFactoryThrowsOnNullNameTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => factory.CreateIndex(null!, isUnique: true));
        }

        [Test]
        public void KeyValueStoreFactoryThrowsOnEmptyNameTest()
        {
            // Arrange
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => factory.CreateIndex("", isUnique: true));
        }

        [Test]
        public void KeyValueStoreFactoryWithLsmCreatesWorkingIndexTest()
        {
            // Arrange
            int indexCounter = 0;
            var factory = new SecondaryIndexFactoryKeyValueStore(
                name => new StoreLsm(Path.Combine(m_testDir, $"idx_{indexCounter++}")), 
                "lsm");

            // Act
            using var index = factory.CreateIndex("lsm_idx", isUnique: false);
            var indexKey = GetBytes("category");
            
            for (int i = 0; i < 5; i++)
            {
                index.Add(indexKey, GetBytes($"pk-{i}"));
            }

            var results = index.Find(indexKey).ToList();

            // Assert
            Assert.That(factory.ProviderKey, Is.EqualTo("lsm"));
            Assert.That(results, Has.Count.EqualTo(5));
        }

        #endregion

        #region ISecondaryIndexFactory Interface Tests

        [Test]
        public void FactoryImplementsInterfaceTest()
        {
            // Arrange & Act
            var factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());

            // Assert
            Assert.That(factory, Is.InstanceOf<ISecondaryIndexFactory>());
        }

        #endregion

        #region Helpers

        private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #endregion
    }
}
