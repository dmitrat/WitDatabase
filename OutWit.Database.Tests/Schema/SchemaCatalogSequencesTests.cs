using OutWit.Database.Core.Stores;
using OutWit.Database.Definitions;
using OutWit.Database.Schema;

namespace OutWit.Database.Tests.Schema;

[TestFixture]
public class SchemaCatalogSequencesTests
{
    private StoreInMemory m_store = null!;
    private SchemaCatalog m_catalog = null!;

    [SetUp]
    public void SetUp()
    {
        m_store = new StoreInMemory();
        m_catalog = new SchemaCatalog(m_store);
    }

    [TearDown]
    public void TearDown()
    {
        m_store?.Dispose();
    }

    #region Sequence Tests

    [Test]
    public void CreateSequenceTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_order_id",
            StartWith = 1000,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);

        var retrieved = m_catalog.GetSequence("seq_order_id");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("seq_order_id"));
        Assert.That(retrieved.StartWith, Is.EqualTo(1000));
        Assert.That(retrieved.IncrementBy, Is.EqualTo(1));
    }

    [Test]
    public void CreateSequenceAlreadyExistsThrowsTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 1,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateSequence(sequence));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void DropSequenceTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 1,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);
        Assert.That(m_catalog.GetSequence("seq_test"), Is.Not.Null);

        m_catalog.DropSequence("seq_test");
        Assert.That(m_catalog.GetSequence("seq_test"), Is.Null);
    }

    [Test]
    public void DropSequenceNotFoundThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.DropSequence("NonExistent"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void NextValTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 100,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);

        var val1 = m_catalog.NextVal("seq_test");
        var val2 = m_catalog.NextVal("seq_test");
        var val3 = m_catalog.NextVal("seq_test");

        Assert.That(val1, Is.EqualTo(100));
        Assert.That(val2, Is.EqualTo(101));
        Assert.That(val3, Is.EqualTo(102));
    }

    [Test]
    public void NextValWithIncrementTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 10,
            IncrementBy = 5
        };

        m_catalog.CreateSequence(sequence);

        var val1 = m_catalog.NextVal("seq_test");
        var val2 = m_catalog.NextVal("seq_test");

        Assert.That(val1, Is.EqualTo(10));
        Assert.That(val2, Is.EqualTo(15));
    }

    [Test]
    public void NextValNegativeIncrementTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 100,
            IncrementBy = -10
        };

        m_catalog.CreateSequence(sequence);

        var val1 = m_catalog.NextVal("seq_test");
        var val2 = m_catalog.NextVal("seq_test");

        Assert.That(val1, Is.EqualTo(100));
        Assert.That(val2, Is.EqualTo(90));
    }

    [Test]
    public void NextValNotFoundThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.NextVal("NonExistent"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void CurrValTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 100,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);

        m_catalog.NextVal("seq_test");
        m_catalog.NextVal("seq_test");

        var currentValue = m_catalog.CurrVal("seq_test");
        Assert.That(currentValue, Is.EqualTo(101));
    }

    [Test]
    public void CurrValNotFoundThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CurrVal("NonExistent"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void RestartSequenceTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 100,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);

        m_catalog.NextVal("seq_test"); // 100
        m_catalog.NextVal("seq_test"); // 101
        m_catalog.NextVal("seq_test"); // 102

        m_catalog.RestartSequence("seq_test");

        var nextVal = m_catalog.NextVal("seq_test");
        Assert.That(nextVal, Is.EqualTo(100)); // Restarted from StartWith
    }

    [Test]
    public void RestartSequenceWithValueTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 100,
            IncrementBy = 1
        };

        m_catalog.CreateSequence(sequence);

        m_catalog.NextVal("seq_test"); // 100
        m_catalog.NextVal("seq_test"); // 101

        m_catalog.RestartSequence("seq_test", 500);

        var nextVal = m_catalog.NextVal("seq_test");
        Assert.That(nextVal, Is.EqualTo(500));
    }

    [Test]
    public void RestartSequenceNotFoundThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.RestartSequence("NonExistent"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void SequenceWithMinMaxTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 1,
            IncrementBy = 1,
            MinValue = 1,
            MaxValue = 1000
        };

        m_catalog.CreateSequence(sequence);

        var retrieved = m_catalog.GetSequence("seq_test");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.MinValue, Is.EqualTo(1));
        Assert.That(retrieved.MaxValue, Is.EqualTo(1000));
    }

    [Test]
    public void SequenceWithCycleTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 1,
            IncrementBy = 1,
            Cycle = true
        };

        m_catalog.CreateSequence(sequence);

        var retrieved = m_catalog.GetSequence("seq_test");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Cycle, Is.True);
    }

    [Test]
    public void SequencesPersistedTest()
    {
        var sequence = new DefinitionSequence
        {
            Name = "seq_test",
            StartWith = 1000,
            IncrementBy = 5,
            MinValue = 100,
            MaxValue = 10000,
            Cycle = true
        };

        m_catalog.CreateSequence(sequence);
        m_catalog.NextVal("seq_test"); // 1000
        m_catalog.NextVal("seq_test"); // 1005

        // Create new catalog with same store
        var catalog2 = new SchemaCatalog(m_store);

        var retrieved = catalog2.GetSequence("seq_test");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("seq_test"));
        Assert.That(retrieved.StartWith, Is.EqualTo(1000));
        Assert.That(retrieved.IncrementBy, Is.EqualTo(5));
        Assert.That(retrieved.CurrentValue, Is.EqualTo(1005));
        Assert.That(retrieved.MinValue, Is.EqualTo(100));
        Assert.That(retrieved.MaxValue, Is.EqualTo(10000));
        Assert.That(retrieved.Cycle, Is.True);

        // Next value should continue from persisted state
        var nextVal = catalog2.NextVal("seq_test");
        Assert.That(nextVal, Is.EqualTo(1010));
    }

    #endregion
}
