using OutWit.Database.Core.Stores;
using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Schema;

[TestFixture]
public class SchemaCatalogViewsTests
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

    #region View Tests

    [Test]
    public void CreateViewTest()
    {
        var view = new DefinitionView
        {
            Name = "ActiveUsers",
            SelectSql = "SELECT * FROM Users WHERE IsActive = 1"
        };

        m_catalog.CreateView(view);

        var retrieved = m_catalog.GetView("ActiveUsers");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("ActiveUsers"));
        Assert.That(retrieved.SelectSql, Is.EqualTo("SELECT * FROM Users WHERE IsActive = 1"));
    }

    [Test]
    public void CreateViewAlreadyExistsThrowsTest()
    {
        var view = new DefinitionView
        {
            Name = "ActiveUsers",
            SelectSql = "SELECT * FROM Users"
        };

        m_catalog.CreateView(view);

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateView(view));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void CreateViewConflictWithTableThrowsTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 }
            }
        };

        m_catalog.CreateTable(table);

        var view = new DefinitionView
        {
            Name = "Users",
            SelectSql = "SELECT 1"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateView(view));
        Assert.That(ex!.Message, Does.Contain("table with name"));
    }

    [Test]
    public void DropViewTest()
    {
        var view = new DefinitionView
        {
            Name = "ActiveUsers",
            SelectSql = "SELECT * FROM Users"
        };

        m_catalog.CreateView(view);
        Assert.That(m_catalog.GetView("ActiveUsers"), Is.Not.Null);

        bool dropped = m_catalog.DropView("ActiveUsers");
        Assert.That(dropped, Is.True);
        Assert.That(m_catalog.GetView("ActiveUsers"), Is.Null);
    }

    [Test]
    public void DropViewNotFoundReturnsFalseTest()
    {
        bool dropped = m_catalog.DropView("NonExistent");
        Assert.That(dropped, Is.False);
    }

    [Test]
    public void GetViewsTest()
    {
        var view1 = new DefinitionView
        {
            Name = "View1",
            SelectSql = "SELECT 1"
        };

        var view2 = new DefinitionView
        {
            Name = "View2",
            SelectSql = "SELECT 2"
        };

        m_catalog.CreateView(view1);
        m_catalog.CreateView(view2);

        var views = m_catalog.GetViews().ToList();
        Assert.That(views, Has.Count.EqualTo(2));
        Assert.That(views.Select(v => v.Name), Does.Contain("View1"));
        Assert.That(views.Select(v => v.Name), Does.Contain("View2"));
    }

    [Test]
    public void ViewsPersistedTest()
    {
        var view = new DefinitionView
        {
            Name = "TestView",
            SelectSql = "SELECT * FROM Users",
            ColumnAliases = new[] { "UserId", "UserName" }
        };

        m_catalog.CreateView(view);

        // Create new catalog with same store
        var catalog2 = new SchemaCatalog(m_store);

        var retrieved = catalog2.GetView("TestView");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("TestView"));
        Assert.That(retrieved.SelectSql, Is.EqualTo("SELECT * FROM Users"));
        Assert.That(retrieved.ColumnAliases, Is.Not.Null);
        Assert.That(retrieved.ColumnAliases!.Count, Is.EqualTo(2));
    }

    #endregion
}
