using System.Linq;
using OutWit.Database.Core.Builder;
using OutWit.Database.Definitions;
using OutWit.Database.Engine;
using NUnit.Framework;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for cascading FK actions (ON DELETE CASCADE, SET NULL, SET DEFAULT).
/// </summary>
public class WitSqlEngineCascadeTests : WitSqlEngineTestsBase
{
    #region Helper

    private WitSqlEngine CreateEngine()
    {
        var database = WitDatabase.CreateInMemory();
        return new WitSqlEngine(database, ownsStore: true);
    }

    #endregion

    #region ON DELETE CASCADE Tests

    [Test]
    public void OnDeleteCascade_DeletesChildRows()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Departments (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100) NOT NULL
            )");
        
        engine.Execute(@"
            CREATE TABLE Employees (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100) NOT NULL,
                DepartmentId INTEGER REFERENCES Departments(Id) ON DELETE CASCADE
            )");

        engine.Execute("INSERT INTO Departments (Id, Name) VALUES (1, 'Engineering'), (2, 'Sales')");
        engine.Execute("INSERT INTO Employees (Id, Name, DepartmentId) VALUES (1, 'Alice', 1), (2, 'Bob', 1), (3, 'Charlie', 2)");

        // Act - Delete department 1
        var result = engine.Execute("DELETE FROM Departments WHERE Id = 1");

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(1));
        
        // Check that employees from department 1 were also deleted
        var employees = engine.Query("SELECT * FROM Employees");
        Assert.That(employees.Count, Is.EqualTo(1));
        Assert.That(employees[0]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void OnDeleteCascade_MultipleLevels()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Categories (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Products (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100),
                CategoryId INTEGER REFERENCES Categories(Id) ON DELETE CASCADE
            )");
        
        engine.Execute(@"
            CREATE TABLE OrderItems (
                Id INTEGER PRIMARY KEY,
                ProductId INTEGER REFERENCES Products(Id) ON DELETE CASCADE,
                Quantity INTEGER
            )");

        engine.Execute("INSERT INTO Categories VALUES (1, 'Electronics')");
        engine.Execute("INSERT INTO Products VALUES (1, 'Phone', 1), (2, 'Laptop', 1)");
        engine.Execute("INSERT INTO OrderItems VALUES (1, 1, 2), (2, 1, 1), (3, 2, 3)");

        // Act - Delete the category (should cascade through products to order items)
        engine.Execute("DELETE FROM Categories WHERE Id = 1");

        // Assert
        var categories = engine.Query("SELECT COUNT(*) as cnt FROM Categories");
        Assert.That(categories[0]["cnt"].AsInt64(), Is.EqualTo(0));
        
        var products = engine.Query("SELECT COUNT(*) as cnt FROM Products");
        Assert.That(products[0]["cnt"].AsInt64(), Is.EqualTo(0));
        
        var orderItems = engine.Query("SELECT COUNT(*) as cnt FROM OrderItems");
        Assert.That(orderItems[0]["cnt"].AsInt64(), Is.EqualTo(0));
    }

    #endregion

    #region ON DELETE SET NULL Tests

    [Test]
    public void OnDeleteSetNull_SetsChildFKToNull()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Managers (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Employees (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100),
                ManagerId INTEGER REFERENCES Managers(Id) ON DELETE SET NULL
            )");

        engine.Execute("INSERT INTO Managers VALUES (1, 'John')");
        engine.Execute("INSERT INTO Employees VALUES (1, 'Alice', 1), (2, 'Bob', 1), (3, 'Charlie', NULL)");

        // Act
        engine.Execute("DELETE FROM Managers WHERE Id = 1");

        // Assert
        var employees = engine.Query("SELECT * FROM Employees ORDER BY Id");
        
        Assert.That(employees.Count, Is.EqualTo(3));
        Assert.That(employees[0]["ManagerId"].IsNull, Is.True);
        Assert.That(employees[1]["ManagerId"].IsNull, Is.True);
        Assert.That(employees[2]["ManagerId"].IsNull, Is.True);
    }

    #endregion

    #region ON DELETE SET DEFAULT Tests

    [Test]
    public void OnDeleteSetDefault_ColumnHasDefaultValueStored()
    {
        // Diagnostic test: verify that DEFAULT value is properly stored in column definition
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Departments (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Employees (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100),
                DepartmentId INTEGER DEFAULT 0 REFERENCES Departments(Id) ON DELETE SET DEFAULT
            )");

        // Check that column has DefaultValue stored
        var empTable = engine.GetTable("Employees");
        Assert.That(empTable, Is.Not.Null);
        
        var deptIdCol = empTable!.GetColumn("DepartmentId");
        Assert.That(deptIdCol, Is.Not.Null, "DepartmentId column should exist");
        Assert.That(deptIdCol!.DefaultValue, Is.Not.Null, "DepartmentId should have DefaultValue set");
        Assert.That(deptIdCol.DefaultValue, Is.EqualTo("0"), "DefaultValue should be '0'");
        Assert.That(deptIdCol.ForeignKey, Is.Not.Null, "DepartmentId should have ForeignKey set");
        Assert.That(deptIdCol.ForeignKey!.ForeignTable, Is.EqualTo("Departments"), "FK should reference Departments table");
        Assert.That(deptIdCol.ForeignKey.OnDelete, Is.EqualTo(ReferenceAction.SetDefault), 
            "FK OnDelete should be SetDefault");
        
        // Also verify columns list
        Assert.That(deptIdCol.ForeignKey.Columns, Is.Not.Null);
        Assert.That(deptIdCol.ForeignKey.Columns.Count, Is.EqualTo(1));
        Assert.That(deptIdCol.ForeignKey.Columns[0], Is.EqualTo("DepartmentId"));
    }

    [Test]
    public void OnDeleteSetDefault_SetsChildFKToDefault()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Departments (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Employees (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100),
                DepartmentId INTEGER DEFAULT 0 REFERENCES Departments(Id) ON DELETE SET DEFAULT
            )");

        // Insert a "default" department with Id = 0
        engine.Execute("INSERT INTO Departments VALUES (0, 'Unassigned'), (1, 'Engineering')");
        engine.Execute("INSERT INTO Employees VALUES (1, 'Alice', 1), (2, 'Bob', 1)");

        // Verify setup - check employees before delete
        var employeesBefore = engine.Query("SELECT * FROM Employees ORDER BY Id");
        Assert.That(employeesBefore.Count, Is.EqualTo(2), "Should have 2 employees before");
        Assert.That(employeesBefore[0]["DepartmentId"].AsInt64(), Is.EqualTo(1), "Alice should be in dept 1");
        Assert.That(employeesBefore[1]["DepartmentId"].AsInt64(), Is.EqualTo(1), "Bob should be in dept 1");

        // Act
        var deleteResult = engine.Execute("DELETE FROM Departments WHERE Id = 1");
        Assert.That(deleteResult.RowsAffected, Is.EqualTo(1), "Should delete 1 department");

        // Assert
        var employees = engine.Query("SELECT * FROM Employees ORDER BY Id");
        
        Assert.That(employees.Count, Is.EqualTo(2), "Should still have 2 employees");
        Assert.That(employees[0]["DepartmentId"].AsInt64(), Is.EqualTo(0), "Alice's DepartmentId should be 0 (default)");
        Assert.That(employees[1]["DepartmentId"].AsInt64(), Is.EqualTo(0), "Bob's DepartmentId should be 0 (default)");
    }

    #endregion

    #region ON DELETE RESTRICT / NO ACTION Tests

    [Test]
    public void OnDeleteRestrict_ThrowsWhenChildRowsExist()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Parents (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Children (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parents(Id) ON DELETE RESTRICT
            )");

        engine.Execute("INSERT INTO Parents VALUES (1, 'Parent1')");
        engine.Execute("INSERT INTO Children VALUES (1, 1)");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.Execute("DELETE FROM Parents WHERE Id = 1"));
        
        Assert.That(ex!.Message, Does.Contain("foreign key").IgnoreCase);
    }

    [Test]
    public void OnDeleteNoAction_AllowsDeleteWhenNoChildRows()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Parents (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Children (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parents(Id) ON DELETE NO ACTION
            )");

        engine.Execute("INSERT INTO Parents VALUES (1, 'Parent1'), (2, 'Parent2')");
        engine.Execute("INSERT INTO Children VALUES (1, 1)");

        // Act - Delete parent 2 which has no children
        var result = engine.Execute("DELETE FROM Parents WHERE Id = 2");

        // Assert
        Assert.That(result.RowsAffected, Is.EqualTo(1));
    }

    #endregion

    #region Table-Level FK Constraint Tests

    [Test]
    public void OnDeleteCascade_TableLevelFK()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute(@"
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                CustomerName VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE OrderItems (
                Id INTEGER PRIMARY KEY,
                OrderId INTEGER,
                Product VARCHAR(100),
                FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE
            )");

        engine.Execute("INSERT INTO Orders VALUES (1, 'Alice'), (2, 'Bob')");
        engine.Execute("INSERT INTO OrderItems VALUES (1, 1, 'Phone'), (2, 1, 'Case'), (3, 2, 'Laptop')");

        // Act
        engine.Execute("DELETE FROM Orders WHERE Id = 1");

        // Assert
        var items = engine.Query("SELECT * FROM OrderItems");
        Assert.That(items.Count, Is.EqualTo(1));
        Assert.That(items[0]["Product"].AsString(), Is.EqualTo("Laptop"));
    }

    #endregion

    #region Named Constraint Tests

    [Test]
    public void OnDeleteCascade_ViaAlterTableAddConstraint()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute("CREATE TABLE Projects (Id INTEGER PRIMARY KEY, Name VARCHAR(100))");
        engine.Execute("CREATE TABLE Tasks (Id INTEGER PRIMARY KEY, ProjectId INTEGER, Title VARCHAR(100))");
        
        engine.Execute(@"
            ALTER TABLE Tasks 
            ADD CONSTRAINT fk_project 
            FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE");

        engine.Execute("INSERT INTO Projects VALUES (1, 'Project Alpha')");
        engine.Execute("INSERT INTO Tasks VALUES (1, 1, 'Task 1'), (2, 1, 'Task 2')");

        // Act
        engine.Execute("DELETE FROM Projects WHERE Id = 1");

        // Assert
        var tasks = engine.Query("SELECT COUNT(*) as cnt FROM Tasks");
        Assert.That(tasks[0]["cnt"].AsInt64(), Is.EqualTo(0));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void CascadeDelete_WithNullFKValues_DoesNotAffectNullRows()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute("CREATE TABLE Parents (Id INTEGER PRIMARY KEY)");
        engine.Execute(@"
            CREATE TABLE Children (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parents(Id) ON DELETE CASCADE
            )");

        engine.Execute("INSERT INTO Parents VALUES (1)");
        engine.Execute("INSERT INTO Children VALUES (1, 1), (2, NULL), (3, 1)");

        // Act
        engine.Execute("DELETE FROM Parents WHERE Id = 1");

        // Assert - child with NULL ParentId should remain
        var children = engine.Query("SELECT * FROM Children");
        Assert.That(children.Count, Is.EqualTo(1));
        Assert.That(children[0]["ParentId"].IsNull, Is.True);
    }

    [Test]
    public void CascadeDelete_MultipleChildTables()
    {
        // Arrange
        var engine = CreateEngine();
        engine.Execute("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Name VARCHAR(100))");
        
        engine.Execute(@"
            CREATE TABLE Posts (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER REFERENCES Users(Id) ON DELETE CASCADE,
                Title VARCHAR(100)
            )");
        
        engine.Execute(@"
            CREATE TABLE Comments (
                Id INTEGER PRIMARY KEY,
                UserId INTEGER REFERENCES Users(Id) ON DELETE CASCADE,
                Content VARCHAR(100)
            )");

        engine.Execute("INSERT INTO Users VALUES (1, 'Alice')");
        engine.Execute("INSERT INTO Posts VALUES (1, 1, 'Post 1'), (2, 1, 'Post 2')");
        engine.Execute("INSERT INTO Comments VALUES (1, 1, 'Comment 1')");

        // Act
        engine.Execute("DELETE FROM Users WHERE Id = 1");

        // Assert
        var posts = engine.Query("SELECT COUNT(*) as cnt FROM Posts");
        Assert.That(posts[0]["cnt"].AsInt64(), Is.EqualTo(0));
        
        var comments = engine.Query("SELECT COUNT(*) as cnt FROM Comments");
        Assert.That(comments[0]["cnt"].AsInt64(), Is.EqualTo(0));
    }

    #endregion

    #region Debug Tests

    [Test]
    public void OnDeleteSetDefault_DebugCascadeChain()
    {
        // Test: verify SET DEFAULT cascade chain correctly
        var engine = CreateEngine();
        
        // Create parent table
        engine.Execute(@"
            CREATE TABLE Departments (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100)
            )");
        
        // Create child table with DEFAULT and FK
        engine.Execute(@"
            CREATE TABLE Employees (
                Id INTEGER PRIMARY KEY,
                Name VARCHAR(100),
                DepartmentId INTEGER DEFAULT 0 REFERENCES Departments(Id) ON DELETE SET DEFAULT
            )");

        // Verify table definition
        var empTable = engine.GetTable("Employees");
        Assert.That(empTable, Is.Not.Null);
        
        var deptIdCol = empTable!.GetColumn("DepartmentId");
        Assert.That(deptIdCol, Is.Not.Null, "DepartmentId column should exist");
        Assert.That(deptIdCol!.DefaultValue, Is.EqualTo("0"), $"DefaultValue should be '0', was: '{deptIdCol.DefaultValue}'");
        Assert.That(deptIdCol.ForeignKey, Is.Not.Null, "FK should be set");
        Assert.That(deptIdCol.ForeignKey!.OnDelete, Is.EqualTo(ReferenceAction.SetDefault), 
            $"OnDelete should be SetDefault, was: {deptIdCol.ForeignKey.OnDelete}");
        Assert.That(deptIdCol.ForeignKey.Columns[0], Is.EqualTo("DepartmentId"),
            $"FK column should be DepartmentId, was: {deptIdCol.ForeignKey.Columns[0]}");

        // Insert data 
        engine.Execute("INSERT INTO Departments VALUES (0, 'Unassigned'), (1, 'Engineering')");
        engine.Execute("INSERT INTO Employees VALUES (1, 'Alice', 1)");

        // Verify value before delete
        var childBefore = engine.Query("SELECT Id, Name, DepartmentId FROM Employees");
        Assert.That(childBefore[0]["DepartmentId"].AsInt64(), Is.EqualTo(1), "Should have DeptId=1 before");
        
        // Delete department 1 - should SET DEFAULT on child
        engine.Execute("DELETE FROM Departments WHERE Id = 1");
        
        // Verify child has default value
        var childAfter = engine.Query("SELECT Id, Name, DepartmentId FROM Employees");
        
        Assert.That(childAfter[0]["DepartmentId"].AsInt64(), Is.EqualTo(0), 
            $"Should have DeptId=0 (default) after DELETE, but was: {childAfter[0]["DepartmentId"].AsInt64()}");
    }

    #endregion
}
