using OutWit.Database.Core.Stores;
using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Schema;

[TestFixture]
public class SchemaCatalogTriggersTests
{
    private StoreInMemory m_store = null!;
    private SchemaCatalog m_catalog = null!;

    [SetUp]
    public void SetUp()
    {
        m_store = new StoreInMemory();
        m_catalog = new SchemaCatalog(m_store);

        // Create a table for triggers to reference
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Name", Type = WitDataType.StringVariable, Ordinal = 1 }
            }
        };
        m_catalog.CreateTable(table);
    }

    [TearDown]
    public void TearDown()
    {
        m_store?.Dispose();
    }

    #region Trigger Tests

    [Test]
    public void CreateTriggerTest()
    {
        var trigger = new DefinitionTrigger
        {
            Name = "trg_users_insert",
            TableName = "Users",
            Time = TriggerTime.Before,
            Event = TriggerEvent.Insert,
            Body = "BEGIN SELECT 1; END"
        };

        m_catalog.CreateTrigger(trigger);

        var retrieved = m_catalog.GetTrigger("trg_users_insert");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("trg_users_insert"));
        Assert.That(retrieved.TableName, Is.EqualTo("Users"));
        Assert.That(retrieved.Time, Is.EqualTo(TriggerTime.Before));
        Assert.That(retrieved.Event, Is.EqualTo(TriggerEvent.Insert));
    }

    [Test]
    public void CreateTriggerAlreadyExistsThrowsTest()
    {
        var trigger = new DefinitionTrigger
        {
            Name = "trg_test",
            TableName = "Users",
            Time = TriggerTime.After,
            Event = TriggerEvent.Update,
            Body = "BEGIN END"
        };

        m_catalog.CreateTrigger(trigger);

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateTrigger(trigger));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void CreateTriggerTableNotFoundThrowsTest()
    {
        var trigger = new DefinitionTrigger
        {
            Name = "trg_test",
            TableName = "NonExistent",
            Time = TriggerTime.After,
            Event = TriggerEvent.Insert,
            Body = "BEGIN END"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateTrigger(trigger));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void CreateTriggerOnViewTest()
    {
        var view = new DefinitionView
        {
            Name = "UserView",
            SelectSql = "SELECT * FROM Users"
        };

        m_catalog.CreateView(view);

        var trigger = new DefinitionTrigger
        {
            Name = "trg_view",
            TableName = "UserView",
            Time = TriggerTime.InsteadOf,
            Event = TriggerEvent.Insert,
            Body = "BEGIN END"
        };

        m_catalog.CreateTrigger(trigger);

        var retrieved = m_catalog.GetTrigger("trg_view");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.TableName, Is.EqualTo("UserView"));
    }

    [Test]
    public void DropTriggerTest()
    {
        var trigger = new DefinitionTrigger
        {
            Name = "trg_test",
            TableName = "Users",
            Time = TriggerTime.After,
            Event = TriggerEvent.Delete,
            Body = "BEGIN END"
        };

        m_catalog.CreateTrigger(trigger);
        Assert.That(m_catalog.GetTrigger("trg_test"), Is.Not.Null);

        bool dropped = m_catalog.DropTrigger("trg_test");
        Assert.That(dropped, Is.True);
        Assert.That(m_catalog.GetTrigger("trg_test"), Is.Null);
    }

    [Test]
    public void DropTriggerNotFoundReturnsFalseTest()
    {
        bool dropped = m_catalog.DropTrigger("NonExistent");
        Assert.That(dropped, Is.False);
    }

    [Test]
    public void GetTriggersForTableTest()
    {
        var trigger1 = new DefinitionTrigger
        {
            Name = "trg_insert",
            TableName = "Users",
            Time = TriggerTime.Before,
            Event = TriggerEvent.Insert,
            Body = "BEGIN END"
        };

        var trigger2 = new DefinitionTrigger
        {
            Name = "trg_update",
            TableName = "Users",
            Time = TriggerTime.After,
            Event = TriggerEvent.Update,
            Body = "BEGIN END"
        };

        var trigger3 = new DefinitionTrigger
        {
            Name = "trg_delete",
            TableName = "Users",
            Time = TriggerTime.Before,
            Event = TriggerEvent.Delete,
            Body = "BEGIN END"
        };

        m_catalog.CreateTrigger(trigger1);
        m_catalog.CreateTrigger(trigger2);
        m_catalog.CreateTrigger(trigger3);

        var allTriggers = m_catalog.GetTriggersForTable("Users").ToList();
        Assert.That(allTriggers, Has.Count.EqualTo(3));

        var insertTriggers = m_catalog.GetTriggersForTable("Users", TriggerEvent.Insert).ToList();
        Assert.That(insertTriggers, Has.Count.EqualTo(1));
        Assert.That(insertTriggers[0].Name, Is.EqualTo("trg_insert"));

        var beforeTriggers = m_catalog.GetTriggersForTable("Users", time: TriggerTime.Before).ToList();
        Assert.That(beforeTriggers, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetTriggersForTableWithBothFiltersTest()
    {
        var trigger1 = new DefinitionTrigger
        {
            Name = "trg_before_insert",
            TableName = "Users",
            Time = TriggerTime.Before,
            Event = TriggerEvent.Insert,
            Body = "BEGIN END"
        };

        var trigger2 = new DefinitionTrigger
        {
            Name = "trg_after_insert",
            TableName = "Users",
            Time = TriggerTime.After,
            Event = TriggerEvent.Insert,
            Body = "BEGIN END"
        };

        m_catalog.CreateTrigger(trigger1);
        m_catalog.CreateTrigger(trigger2);

        var triggers = m_catalog.GetTriggersForTable("Users", TriggerEvent.Insert, TriggerTime.Before).ToList();
        Assert.That(triggers, Has.Count.EqualTo(1));
        Assert.That(triggers[0].Name, Is.EqualTo("trg_before_insert"));
    }

    [Test]
    public void TriggersPersistedTest()
    {
        var trigger = new DefinitionTrigger
        {
            Name = "trg_test",
            TableName = "Users",
            Time = TriggerTime.After,
            Event = TriggerEvent.Update,
            UpdateColumns = new[] { "Name" },
            ForEachRow = true,
            WhenCondition = "NEW.Name <> OLD.Name",
            Body = "BEGIN SELECT 1; END"
        };

        m_catalog.CreateTrigger(trigger);

        // Create new catalog with same store
        var catalog2 = new SchemaCatalog(m_store);

        var retrieved = catalog2.GetTrigger("trg_test");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("trg_test"));
        Assert.That(retrieved.TableName, Is.EqualTo("Users"));
        Assert.That(retrieved.UpdateColumns, Is.Not.Null);
        Assert.That(retrieved.UpdateColumns!.Count, Is.EqualTo(1));
        Assert.That(retrieved.ForEachRow, Is.True);
        Assert.That(retrieved.WhenCondition, Is.EqualTo("NEW.Name <> OLD.Name"));
    }

    #endregion
}
