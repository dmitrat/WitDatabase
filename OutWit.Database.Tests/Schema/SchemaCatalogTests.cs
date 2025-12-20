using OutWit.Database.Core.Stores;
using OutWit.Database.Definitions;
using OutWit.Database.Schema;
using OutWit.Database.Types;

namespace OutWit.Database.Tests.Schema;

[TestFixture]
public class SchemaCatalogTests
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

    #region Table Tests

    [Test]
    public void CreateTableTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn
                {
                    Name = "Id",
                    Type = WitDataType.Int32,
                    IsPrimaryKey = true,
                    Ordinal = 0
                },
                new DefinitionColumn
                {
                    Name = "Name",
                    Type = WitDataType.StringVariable,
                    Ordinal = 1
                }
            }
        };

        m_catalog.CreateTable(table);

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("Users"));
        Assert.That(retrieved.Columns.Count, Is.EqualTo(2));
    }

    [Test]
    public void CreateTableAlreadyExistsThrowsTest()
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

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateTable(table));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void DropTableTest()
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
        Assert.That(m_catalog.GetTable("Users"), Is.Not.Null);

        bool dropped = m_catalog.DropTable("Users");
        Assert.That(dropped, Is.True);
        Assert.That(m_catalog.GetTable("Users"), Is.Null);
    }

    [Test]
    public void DropTableRemovesAssociatedIndexesTest()
    {
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

        var index = new DefinitionIndex
        {
            Name = "IX_Users_Name",
            TableName = "Users",
            Columns = new[] { "Name" }
        };

        m_catalog.CreateIndex(index);

        m_catalog.DropTable("Users");

        Assert.That(m_catalog.GetTable("Users"), Is.Null);
        Assert.That(m_catalog.GetIndex("IX_Users_Name"), Is.Null);
    }

    [Test]
    public void RenameTableTest()
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
        m_catalog.RenameTable("Users", "Accounts");

        Assert.That(m_catalog.GetTable("Users"), Is.Null);
        Assert.That(m_catalog.GetTable("Accounts"), Is.Not.Null);
        Assert.That(m_catalog.GetTable("Accounts")!.Name, Is.EqualTo("Accounts"));
    }

    [Test]
    public void RenameTableNotFoundThrowsTest()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.RenameTable("NonExistent", "NewName"));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void RenameTableTargetExistsThrowsTest()
    {
        var table1 = new DefinitionTable
        {
            Name = "Users",
            Columns = new[] { new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 } }
        };

        var table2 = new DefinitionTable
        {
            Name = "Accounts",
            Columns = new[] { new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 } }
        };

        m_catalog.CreateTable(table1);
        m_catalog.CreateTable(table2);

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.RenameTable("Users", "Accounts"));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void RenameTableUpdatesIndexReferencesTest()
    {
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

        var index = new DefinitionIndex
        {
            Name = "IX_Users_Name",
            TableName = "Users",
            Columns = new[] { "Name" }
        };

        m_catalog.CreateIndex(index);

        m_catalog.RenameTable("Users", "Accounts");

        var retrievedIndex = m_catalog.GetIndex("IX_Users_Name");
        Assert.That(retrievedIndex, Is.Not.Null);
        Assert.That(retrievedIndex.TableName, Is.EqualTo("Accounts"));
    }

    [Test]
    public void GetTableNamesTest()
    {
        var table1 = new DefinitionTable
        {
            Name = "Users",
            Columns = new[] { new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 } }
        };

        var table2 = new DefinitionTable
        {
            Name = "Orders",
            Columns = new[] { new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 } }
        };

        m_catalog.CreateTable(table1);
        m_catalog.CreateTable(table2);

        var tableNames = m_catalog.TableNames.ToList();
        Assert.That(tableNames, Has.Count.EqualTo(2));
        Assert.That(tableNames, Does.Contain("Users"));
        Assert.That(tableNames, Does.Contain("Orders"));
    }

    #endregion

    #region Column Tests

    [Test]
    public void AddColumnTest()
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

        var newColumn = new DefinitionColumn
        {
            Name = "Email",
            Type = WitDataType.StringVariable,
            Ordinal = 0 // Will be reset
        };

        m_catalog.AddColumn("Users", newColumn);

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Columns.Count, Is.EqualTo(2));
        Assert.That(retrieved.Columns[1].Name, Is.EqualTo("Email"));
        Assert.That(retrieved.Columns[1].Ordinal, Is.EqualTo(1));
    }

    [Test]
    public void AddColumnTableNotFoundThrowsTest()
    {
        var column = new DefinitionColumn
        {
            Name = "Email",
            Type = WitDataType.StringVariable,
            Ordinal = 0
        };

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.AddColumn("NonExistent", column));
        Assert.That(ex!.Message, Does.Contain("not found"));
    }

    [Test]
    public void DropColumnTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Name", Type = WitDataType.StringVariable, Ordinal = 1 },
                new DefinitionColumn { Name = "Email", Type = WitDataType.StringVariable, Ordinal = 2 }
            }
        };

        m_catalog.CreateTable(table);
        m_catalog.DropColumn("Users", "Name");

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Columns.Count, Is.EqualTo(2));
        Assert.That(retrieved.Columns[0].Name, Is.EqualTo("Id"));
        Assert.That(retrieved.Columns[1].Name, Is.EqualTo("Email"));
        Assert.That(retrieved.Columns[1].Ordinal, Is.EqualTo(1)); // Re-indexed
    }

    [Test]
    public void RenameColumnTest()
    {
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
        m_catalog.RenameColumn("Users", "Name", "FullName");

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Columns[1].Name, Is.EqualTo("FullName"));
    }

    [Test]
    public void AlterColumnTypeTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Age", Type = WitDataType.Int32, Ordinal = 1 }
            }
        };

        m_catalog.CreateTable(table);
        m_catalog.AlterColumnType("Users", "Age", WitDataType.Int16);

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Columns[1].Type, Is.EqualTo(WitDataType.Int16));
    }

    [Test]
    public void SetColumnDefaultTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Status", Type = WitDataType.StringVariable, Ordinal = 1 }
            }
        };

        m_catalog.CreateTable(table);
        m_catalog.SetColumnDefault("Users", "Status", "'active'");

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Columns[1].DefaultValue, Is.EqualTo("'active'"));
    }

    [Test]
    public void SetColumnNotNullTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Email", Type = WitDataType.StringVariable, Nullable = true, Ordinal = 1 }
            }
        };

        m_catalog.CreateTable(table);
        m_catalog.SetColumnNotNull("Users", "Email", true);

        var retrieved = m_catalog.GetTable("Users");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Columns[1].Nullable, Is.False);
    }

    #endregion

    #region Index Tests

    [Test]
    public void CreateIndexTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Email", Type = WitDataType.StringVariable, Ordinal = 1 }
            }
        };

        m_catalog.CreateTable(table);

        var index = new DefinitionIndex
        {
            Name = "IX_Users_Email",
            TableName = "Users",
            Columns = new[] { "Email" },
            IsUnique = true
        };

        m_catalog.CreateIndex(index);

        var retrieved = m_catalog.GetIndex("IX_Users_Email");
        Assert.That(retrieved, Is.Not.Null);
        Assert.That(retrieved.Name, Is.EqualTo("IX_Users_Email"));
        Assert.That(retrieved.IsUnique, Is.True);
    }

    [Test]
    public void CreateIndexAlreadyExistsThrowsTest()
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

        var index = new DefinitionIndex
        {
            Name = "IX_Test",
            TableName = "Users",
            Columns = new[] { "Id" }
        };

        m_catalog.CreateIndex(index);

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateIndex(index));
        Assert.That(ex!.Message, Does.Contain("already exists"));
    }

    [Test]
    public void CreateIndexTableNotFoundThrowsTest()
    {
        var index = new DefinitionIndex
        {
            Name = "IX_Test",
            TableName = "NonExistent",
            Columns = new[] { "Id" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => m_catalog.CreateIndex(index));
        Assert.That(ex!.Message, Does.Contain("does not exist"));
    }

    [Test]
    public void DropIndexTest()
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

        var index = new DefinitionIndex
        {
            Name = "IX_Test",
            TableName = "Users",
            Columns = new[] { "Id" }
        };

        m_catalog.CreateIndex(index);
        Assert.That(m_catalog.GetIndex("IX_Test"), Is.Not.Null);

        bool dropped = m_catalog.DropIndex("IX_Test");
        Assert.That(dropped, Is.True);
        Assert.That(m_catalog.GetIndex("IX_Test"), Is.Null);
    }

    [Test]
    public void GetTableIndexesTest()
    {
        var table = new DefinitionTable
        {
            Name = "Users",
            Columns = new[]
            {
                new DefinitionColumn { Name = "Id", Type = WitDataType.Int32, Ordinal = 0 },
                new DefinitionColumn { Name = "Email", Type = WitDataType.StringVariable, Ordinal = 1 },
                new DefinitionColumn { Name = "Name", Type = WitDataType.StringVariable, Ordinal = 2 }
            }
        };

        m_catalog.CreateTable(table);

        m_catalog.CreateIndex(new DefinitionIndex
        {
            Name = "IX_Email",
            TableName = "Users",
            Columns = new[] { "Email" }
        });

        m_catalog.CreateIndex(new DefinitionIndex
        {
            Name = "IX_Name",
            TableName = "Users",
            Columns = new[] { "Name" }
        });

        var indexes = m_catalog.GetTableIndexes("Users").ToList();
        Assert.That(indexes, Has.Count.EqualTo(2));
    }

    #endregion

    #region Persistence Tests

    [Test]
    public void PersistenceRoundtripTest()
    {
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

        var index = new DefinitionIndex
        {
            Name = "IX_Users_Name",
            TableName = "Users",
            Columns = new[] { "Name" }
        };

        m_catalog.CreateIndex(index);

        // Create new catalog with same store
        var catalog2 = new SchemaCatalog(m_store);

        var retrievedTable = catalog2.GetTable("Users");
        Assert.That(retrievedTable, Is.Not.Null);
        Assert.That(retrievedTable.Name, Is.EqualTo("Users"));
        Assert.That(retrievedTable.Columns.Count, Is.EqualTo(2));

        var retrievedIndex = catalog2.GetIndex("IX_Users_Name");
        Assert.That(retrievedIndex, Is.Not.Null);
        Assert.That(retrievedIndex.TableName, Is.EqualTo("Users"));
    }

    #endregion

    #region Row ID Tests

    [Test]
    public void GetNextRowIdTest()
    {
        var id1 = m_catalog.GetNextRowId("Users");
        var id2 = m_catalog.GetNextRowId("Users");
        var id3 = m_catalog.GetNextRowId("Orders");

        Assert.That(id2, Is.GreaterThan(id1));
        Assert.That(id3, Is.GreaterThan(id2));
    }

    [Test]
    public void CreateRowKeyTest()
    {
        var key = SchemaCatalog.CreateRowKey("Users", 123);

        Assert.That(key, Is.Not.Null);
        Assert.That(key.Length, Is.GreaterThan(0));

        var parsedId = SchemaCatalog.ParseRowId(key, "Users");
        Assert.That(parsedId, Is.EqualTo(123));
    }

    [Test]
    public void GetTableDataPrefixTest()
    {
        var prefix = SchemaCatalog.GetTableDataPrefix("Users");

        Assert.That(prefix, Is.Not.Null);
        Assert.That(System.Text.Encoding.UTF8.GetString(prefix), Is.EqualTo("t:Users:"));
    }

    #endregion
}
