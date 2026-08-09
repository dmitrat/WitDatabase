using NUnit.Framework;
using OutWit.Database.Core.Exceptions;
using OutWit.Database.Core.IndexedDb.Indexes;
using OutWit.Database.Core.IndexedDb.Tests.Mocks;
using OutWit.Database.Core.Interfaces;

namespace OutWit.Database.Core.IndexedDb.Tests.Indexes;

[TestFixture]
public class SecondaryIndexIndexedDbTests
{
    #region Fields

    private MockJSRuntime m_jsRuntime = null!;
    private IndexedDbIndexInterop m_interop = null!;

    #endregion

    #region Setup

    [SetUp]
    public async Task SetupAsync()
    {
        m_jsRuntime = new MockJSRuntime();
        m_interop = new IndexedDbIndexInterop(m_jsRuntime, "TestDb", "test_store");
        await m_interop.OpenAsync();
    }

    [TearDown]
    public async Task TearDownAsync()
    {
        if (m_interop != null)
        {
            await m_interop.DisposeAsync();
        }
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void ConstructorWithValidParametersCreatesIndexTest()
    {
        // Act
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Assert
        Assert.That(index, Is.Not.Null);
        Assert.That(index.Name, Is.EqualTo("test_index"));
        Assert.That(index.IsUnique, Is.False);
        Assert.That(index.Count, Is.EqualTo(0));
    }

    [Test]
    public void ConstructorWithNullNameThrowsTest()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SecondaryIndexIndexedDb(null!, m_interop, isUnique: false, ownsInterop: false));
    }

    [Test]
    public void ConstructorWithEmptyNameThrowsTest()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SecondaryIndexIndexedDb("", m_interop, isUnique: false, ownsInterop: false));
    }

    [Test]
    public void ConstructorWithNullInteropThrowsTest()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SecondaryIndexIndexedDb("test_index", null!, isUnique: false, ownsInterop: false));
    }

    #endregion

    #region Add Tests

    [Test]
    public void AddNonUniqueIndexSucceedsTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Act
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Assert
        Assert.That(index.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddMultipleEntriesWithSameKeyNonUniqueSucceedsTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        var indexKey = GetBytes("category");

        // Act
        index.Add(indexKey, GetBytes("pk1"));
        index.Add(indexKey, GetBytes("pk2"));
        index.Add(indexKey, GetBytes("pk3"));

        // Assert
        Assert.That(index.Count, Is.EqualTo(3));
        var results = index.Find(indexKey).ToList();
        Assert.That(results, Has.Count.EqualTo(3));
    }

    [Test]
    public void AddUniqueIndexSucceedsTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);

        // Act
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Assert
        Assert.That(index.Count, Is.EqualTo(1));
    }

    [Test]
    public void AddDuplicateKeyUniqueIndexThrowsTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act & Assert. The specific type now, and it still IS an InvalidOperationException - the
        // second assertion is what says a caller written against the old type keeps working, which
        // is the whole reason the new one derives from it.
        var violation = Assert.Throws<UniqueIndexViolationException>(() =>
            index.Add(GetBytes("key1"), GetBytes("pk2")));

        Assert.That(violation, Is.InstanceOf<InvalidOperationException>());
    }

    #endregion

    #region Find Tests

    [Test]
    public void FindExistingKeyReturnsValueTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act
        var results = index.Find(GetBytes("key1")).ToList();

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(GetBytes("pk1")));
    }

    [Test]
    public void FindNonExistingKeyReturnsEmptyTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);

        // Act
        var results = index.Find(GetBytes("key1")).ToList();

        // Assert
        Assert.That(results, Is.Empty);
    }

    [Test]
    public void FindNonUniqueReturnsAllValuesTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        var indexKey = GetBytes("category");
        index.Add(indexKey, GetBytes("pk1"));
        index.Add(indexKey, GetBytes("pk2"));
        index.Add(indexKey, GetBytes("pk3"));

        // Act
        var results = index.Find(indexKey).ToList();

        // Assert
        Assert.That(results, Has.Count.EqualTo(3));
    }

    #endregion

    #region FindRange Tests

    [Test]
    public void FindRangeReturnsEntriesInRangeTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("a"), GetBytes("pk1"));
        index.Add(GetBytes("b"), GetBytes("pk2"));
        index.Add(GetBytes("c"), GetBytes("pk3"));
        index.Add(GetBytes("d"), GetBytes("pk4"));

        // Act
        var results = index.FindRange(GetBytes("b"), GetBytes("d")).ToList();

        // Assert
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void FindRangeWithNullStartReturnsFromBeginningTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("a"), GetBytes("pk1"));
        index.Add(GetBytes("b"), GetBytes("pk2"));
        index.Add(GetBytes("c"), GetBytes("pk3"));

        // Act
        var results = index.FindRange(null, GetBytes("c")).ToList();

        // Assert
        Assert.That(results, Has.Count.EqualTo(2));
    }

    [Test]
    public void FindRangeWithNullEndReturnsToEndTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("a"), GetBytes("pk1"));
        index.Add(GetBytes("b"), GetBytes("pk2"));
        index.Add(GetBytes("c"), GetBytes("pk3"));

        // Act
        var results = index.FindRange(GetBytes("b"), null).ToList();

        // Assert
        Assert.That(results, Has.Count.EqualTo(2));
    }

    #endregion

    #region Contains Tests

    [Test]
    public void ContainsExistingKeyReturnsTrueTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act & Assert
        Assert.That(index.Contains(GetBytes("key1")), Is.True);
    }

    [Test]
    public void ContainsNonExistingKeyReturnsFalseTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);

        // Act & Assert
        Assert.That(index.Contains(GetBytes("key1")), Is.False);
    }

    #endregion

    #region ContainsEntry Tests

    [Test]
    public void ContainsEntryExistingReturnsTrueTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act & Assert
        Assert.That(index.ContainsEntry(GetBytes("key1"), GetBytes("pk1")), Is.True);
    }

    [Test]
    public void ContainsEntryWrongValueReturnsFalseTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act & Assert
        Assert.That(index.ContainsEntry(GetBytes("key1"), GetBytes("pk2")), Is.False);
    }

    [Test]
    public void ContainsEntryNonUniqueReturnsTrueTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));
        index.Add(GetBytes("key1"), GetBytes("pk2"));

        // Act & Assert
        Assert.That(index.ContainsEntry(GetBytes("key1"), GetBytes("pk1")), Is.True);
        Assert.That(index.ContainsEntry(GetBytes("key1"), GetBytes("pk2")), Is.True);
        Assert.That(index.ContainsEntry(GetBytes("key1"), GetBytes("pk3")), Is.False);
    }

    #endregion

    #region Remove Tests

    [Test]
    public void RemoveExistingEntryReturnsTrueTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act
        var result = index.Remove(GetBytes("key1"), GetBytes("pk1"));

        // Assert
        Assert.That(result, Is.True);
        Assert.That(index.Count, Is.EqualTo(0));
    }

    [Test]
    public void RemoveNonExistingEntryReturnsFalseTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);

        // Act
        var result = index.Remove(GetBytes("key1"), GetBytes("pk1"));

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RemoveWithWrongValueReturnsFalseTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: true, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));

        // Act
        var result = index.Remove(GetBytes("key1"), GetBytes("pk2"));

        // Assert
        Assert.That(result, Is.False);
        Assert.That(index.Count, Is.EqualTo(1));
    }

    [Test]
    public void RemoveNonUniqueOnlyRemovesSpecificEntryTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));
        index.Add(GetBytes("key1"), GetBytes("pk2"));

        // Act
        index.Remove(GetBytes("key1"), GetBytes("pk1"));

        // Assert
        Assert.That(index.Count, Is.EqualTo(1));
        Assert.That(index.ContainsEntry(GetBytes("key1"), GetBytes("pk2")), Is.True);
    }

    #endregion

    #region RemoveAll Tests

    [Test]
    public void RemoveAllRemovesAllEntriesWithKeyTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));
        index.Add(GetBytes("key1"), GetBytes("pk2"));
        index.Add(GetBytes("key2"), GetBytes("pk3"));

        // Act
        var removed = index.RemoveAll(GetBytes("key1"));

        // Assert
        Assert.That(removed, Is.EqualTo(2));
        Assert.That(index.Count, Is.EqualTo(1));
    }

    [Test]
    public void RemoveAllNonExistingKeyReturnsZeroTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Act
        var removed = index.RemoveAll(GetBytes("key1"));

        // Assert
        Assert.That(removed, Is.EqualTo(0));
    }

    #endregion

    #region Clear Tests

    [Test]
    public void ClearRemovesAllEntriesTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        index.Add(GetBytes("key1"), GetBytes("pk1"));
        index.Add(GetBytes("key2"), GetBytes("pk2"));
        index.Add(GetBytes("key3"), GetBytes("pk3"));

        // Act
        index.Clear();

        // Assert
        Assert.That(index.Count, Is.EqualTo(0));
    }

    #endregion

    #region Flush Tests

    [Test]
    public void FlushDoesNotThrowTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Act & Assert
        Assert.DoesNotThrow(() => index.Flush());
    }

    [Test]
    public async Task FlushAsyncDoesNotThrowTest()
    {
        // Arrange
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Act & Assert
        await index.FlushAsync();
    }

    #endregion

    #region ISecondaryIndex Interface Tests

    [Test]
    public void ImplementsISecondaryIndexTest()
    {
        // Act
        using var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Assert
        Assert.That(index, Is.InstanceOf<ISecondaryIndex>());
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void DisposeDoesNotThrowTest()
    {
        // Arrange
        var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);

        // Act & Assert
        Assert.DoesNotThrow(() => index.Dispose());
    }

    [Test]
    public void DoubleDisposeDoesNotThrowTest()
    {
        // Arrange
        var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        index.Dispose();

        // Act & Assert
        Assert.DoesNotThrow(() => index.Dispose());
    }

    [Test]
    public void AccessAfterDisposeThrowsTest()
    {
        // Arrange
        var index = new SecondaryIndexIndexedDb("test_index", m_interop, isUnique: false, ownsInterop: false);
        index.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => index.Add(GetBytes("key"), GetBytes("value")));
    }

    #endregion

    #region Helpers

    private static byte[] GetBytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    #endregion
}
