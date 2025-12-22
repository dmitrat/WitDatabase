using NUnit.Framework;
using OutWit.Database.Core.Indexes;
using OutWit.Database.Core.Interfaces;
using OutWit.Database.Core.Stores;

namespace OutWit.Database.Core.Tests.Indexes
{
    [TestFixture]
    public class IndexManagerTests
    {
        #region Fields

        private ISecondaryIndexFactory m_factory = null!;

        #endregion

        #region Setup

        [SetUp]
        public void Setup()
        {
            m_factory = new SecondaryIndexFactoryKeyValueStore(_ => new StoreInMemory());
        }

        [TearDown]
        public void TearDown()
        {
        }

        #endregion

        #region CreateIndex Tests

        [Test]
        public void CreateIndexSucceedsTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act
            var index = manager.CreateIndex("idx_email", isUnique: true);

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index.Name, Is.EqualTo("idx_email"));
            Assert.That(index.IsUnique, Is.True);
        }

        [Test]
        public void CreateNonUniqueIndexSucceedsTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act
            var index = manager.CreateIndex("idx_category", isUnique: false);

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index.IsUnique, Is.False);
        }

        [Test]
        public void CreateDuplicateIndexThrowsTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_email", isUnique: true);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => manager.CreateIndex("idx_email", isUnique: false));
        }

        [Test]
        public void CreateIndexWithNullNameThrowsTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => manager.CreateIndex(null!, isUnique: true));
        }

        #endregion

        #region GetIndex Tests

        [Test]
        public void GetIndexReturnsExistingIndexTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_email", isUnique: true);

            // Act
            var index = manager.GetIndex("idx_email");

            // Assert
            Assert.That(index, Is.Not.Null);
            Assert.That(index!.Name, Is.EqualTo("idx_email"));
        }

        [Test]
        public void GetIndexReturnsNullForNonExistentTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act
            var index = manager.GetIndex("nonexistent");

            // Assert
            Assert.That(index, Is.Null);
        }

        [Test]
        public void GetIndexIsCaseInsensitiveTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_Email", isUnique: true);

            // Act
            var index = manager.GetIndex("IDX_EMAIL");

            // Assert
            Assert.That(index, Is.Not.Null);
        }

        #endregion

        #region DropIndex Tests

        [Test]
        public void DropIndexSucceedsTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_email", isUnique: true);

            // Act
            bool dropped = manager.DropIndex("idx_email");

            // Assert
            Assert.That(dropped, Is.True);
            Assert.That(manager.HasIndex("idx_email"), Is.False);
            Assert.That(manager.IndexCount, Is.EqualTo(0));
        }

        [Test]
        public void DropNonExistentIndexReturnsFalseTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act
            bool dropped = manager.DropIndex("nonexistent");

            // Assert
            Assert.That(dropped, Is.False);
        }

        #endregion

        #region HasIndex Tests

        [Test]
        public void HasIndexReturnsTrueForExistingIndexTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_email", isUnique: true);

            // Act & Assert
            Assert.That(manager.HasIndex("idx_email"), Is.True);
        }

        [Test]
        public void HasIndexReturnsFalseForNonExistentIndexTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act & Assert
            Assert.That(manager.HasIndex("nonexistent"), Is.False);
        }

        #endregion

        #region OnRowInserted Tests

        [Test]
        public void OnRowInsertedUpdatesIndexTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            var index = manager.CreateIndex("idx_email", isUnique: true);

            var primaryKey = GetBytes("user-123");
            var indexKeys = new Dictionary<string, byte[]>
            {
                ["idx_email"] = GetBytes("test@example.com")
            };

            // Act
            manager.OnRowInserted(primaryKey, indexKeys);

            // Assert
            var results = index.Find(GetBytes("test@example.com")).ToList();
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(primaryKey));
        }

        [Test]
        public void OnRowInsertedUpdatesMultipleIndexesTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            var emailIndex = manager.CreateIndex("idx_email", isUnique: true);
            var categoryIndex = manager.CreateIndex("idx_category", isUnique: false);

            var primaryKey = GetBytes("user-123");
            var indexKeys = new Dictionary<string, byte[]>
            {
                ["idx_email"] = GetBytes("test@example.com"),
                ["idx_category"] = GetBytes("premium")
            };

            // Act
            manager.OnRowInserted(primaryKey, indexKeys);

            // Assert
            Assert.That(emailIndex.Find(GetBytes("test@example.com")).Any(), Is.True);
            Assert.That(categoryIndex.Find(GetBytes("premium")).Any(), Is.True);
        }

        [Test]
        public void OnRowInsertedIgnoresNonExistentIndexesTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_email", isUnique: true);

            var primaryKey = GetBytes("user-123");
            var indexKeys = new Dictionary<string, byte[]>
            {
                ["idx_nonexistent"] = GetBytes("value")
            };

            // Act & Assert - should not throw
            Assert.DoesNotThrow(() => manager.OnRowInserted(primaryKey, indexKeys));
        }

        #endregion

        #region OnRowDeleted Tests

        [Test]
        public void OnRowDeletedRemovesFromIndexTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            var index = manager.CreateIndex("idx_email", isUnique: true);

            var primaryKey = GetBytes("user-123");
            var indexKeys = new Dictionary<string, byte[]>
            {
                ["idx_email"] = GetBytes("test@example.com")
            };

            manager.OnRowInserted(primaryKey, indexKeys);

            // Act
            manager.OnRowDeleted(primaryKey, indexKeys);

            // Assert
            Assert.That(index.Find(GetBytes("test@example.com")).Any(), Is.False);
        }

        #endregion

        #region OnRowUpdated Tests

        [Test]
        public void OnRowUpdatedChangesIndexKeyTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            var index = manager.CreateIndex("idx_email", isUnique: true);

            var primaryKey = GetBytes("user-123");
            var oldEmail = GetBytes("old@example.com");
            var newEmail = GetBytes("new@example.com");

            manager.OnRowInserted(primaryKey, new Dictionary<string, byte[]> 
            { 
                ["idx_email"] = oldEmail 
            });

            // Act
            manager.OnRowUpdated(
                primaryKey,
                new Dictionary<string, byte[]> { ["idx_email"] = oldEmail },
                new Dictionary<string, byte[]> { ["idx_email"] = newEmail }
            );

            // Assert
            Assert.That(index.Find(oldEmail).Any(), Is.False);
            Assert.That(index.Find(newEmail).Any(), Is.True);
        }

        [Test]
        public void OnRowUpdatedSkipsUnchangedKeysTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            var index = manager.CreateIndex("idx_email", isUnique: true);

            var primaryKey = GetBytes("user-123");
            var email = GetBytes("test@example.com");

            manager.OnRowInserted(primaryKey, new Dictionary<string, byte[]> 
            { 
                ["idx_email"] = email 
            });

            // Act - same key in old and new
            manager.OnRowUpdated(
                primaryKey,
                new Dictionary<string, byte[]> { ["idx_email"] = email },
                new Dictionary<string, byte[]> { ["idx_email"] = email }
            );

            // Assert - should still exist
            Assert.That(index.Find(email).Any(), Is.True);
            Assert.That(index.Count, Is.EqualTo(1));
        }

        #endregion

        #region IndexNames and IndexCount Tests

        [Test]
        public void IndexNamesReturnsAllIndexNamesTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);
            manager.CreateIndex("idx_a", isUnique: true);
            manager.CreateIndex("idx_b", isUnique: false);
            manager.CreateIndex("idx_c", isUnique: true);

            // Act
            var names = manager.IndexNames;

            // Assert
            Assert.That(names, Has.Count.EqualTo(3));
            Assert.That(names, Contains.Item("idx_a"));
            Assert.That(names, Contains.Item("idx_b"));
            Assert.That(names, Contains.Item("idx_c"));
        }

        [Test]
        public void IndexCountReturnsCorrectCountTest()
        {
            // Arrange
            using var manager = new IndexManager(m_factory);

            // Act & Assert
            Assert.That(manager.IndexCount, Is.EqualTo(0));

            manager.CreateIndex("idx_a", isUnique: true);
            Assert.That(manager.IndexCount, Is.EqualTo(1));

            manager.CreateIndex("idx_b", isUnique: false);
            Assert.That(manager.IndexCount, Is.EqualTo(2));

            manager.DropIndex("idx_a");
            Assert.That(manager.IndexCount, Is.EqualTo(1));
        }

        #endregion

        #region Interface Tests

        [Test]
        public void ImplementsIIndexManagerTest()
        {
            // Arrange & Act
            using var manager = new IndexManager(m_factory);

            // Assert
            Assert.That(manager, Is.InstanceOf<IIndexManager>());
        }

        #endregion

        #region Helpers

        private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        #endregion
    }
}
