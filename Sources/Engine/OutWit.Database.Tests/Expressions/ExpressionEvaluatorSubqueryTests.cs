using OutWit.Database.Parser;

namespace OutWit.Database.Tests.Expressions;

/// <summary>
/// Tests for subquery expression evaluation: scalar subqueries, EXISTS, IN (subquery), ANY/SOME/ALL.
/// </summary>
[TestFixture]
public class ExpressionEvaluatorSubqueryTests : WitSqlEngineTestsBase
{
    #region Scalar Subquery Tests

    [Test]
    public void ScalarSubqueryReturnsValueTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var result = m_engine.ExecuteScalar("SELECT (SELECT COUNT(*) FROM Users)");
        
        Assert.That(result.AsInt64(), Is.EqualTo(3));
    }

    [Test]
    public void ScalarSubqueryInSelectListTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT NOT NULL,
                Amount DECIMAL NOT NULL
            )");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (1, 100.00)");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (1, 200.00)");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (2, 150.00)");
        
        var rows = m_engine.Query(@"
            SELECT 
                Name,
                (SELECT SUM(Amount) FROM Orders WHERE Orders.UserId = Users.Id) AS TotalOrders
            FROM Users
            ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[0]["TotalOrders"].AsDecimal(), Is.EqualTo(300.00m));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
        Assert.That(rows[1]["TotalOrders"].AsDecimal(), Is.EqualTo(150.00m));
        Assert.That(rows[2]["Name"].AsString(), Is.EqualTo("Charlie"));
        Assert.That(rows[2]["TotalOrders"].IsNull, Is.True); // No orders
    }

    [Test]
    public void ScalarSubqueryReturnsNullForEmptyResultTest()
    {
        CreateUsersTable();
        
        var result = m_engine.ExecuteScalar("SELECT (SELECT Name FROM Users WHERE Id = 999)");
        
        Assert.That(result.IsNull, Is.True);
    }

    [Test]
    public void ScalarSubqueryWithMultipleRowsThrowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        Assert.Throws<InvalidOperationException>(() => 
            m_engine.ExecuteScalar("SELECT (SELECT Name FROM Users)"));
    }

    #endregion

    #region EXISTS Tests

    [Test]
    public void ExistsReturnsTrueWhenRowsExistTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var result = m_engine.ExecuteScalar("SELECT EXISTS (SELECT 1 FROM Users WHERE Name = 'Alice')");
        
        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void ExistsReturnsFalseWhenNoRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var result = m_engine.ExecuteScalar("SELECT EXISTS (SELECT 1 FROM Users WHERE Name = 'NonExistent')");
        
        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void NotExistsReturnsTrueWhenNoRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        var result = m_engine.ExecuteScalar("SELECT NOT EXISTS (SELECT 1 FROM Users WHERE Name = 'NonExistent')");
        
        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void ExistsInWhereClauseFiltersRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT NOT NULL,
                Amount DECIMAL NOT NULL
            )");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (1, 100.00)");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (2, 150.00)");
        
        // Get users who have orders
        var rows = m_engine.Query(@"
            SELECT Name FROM Users 
            WHERE EXISTS (SELECT 1 FROM Orders WHERE Orders.UserId = Users.Id)
            ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
    }

    [Test]
    public void NotExistsInWhereClauseFiltersRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT NOT NULL,
                Amount DECIMAL NOT NULL
            )");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (1, 100.00)");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (2, 150.00)");
        
        // Get users who have NO orders
        var rows = m_engine.Query(@"
            SELECT Name FROM Users 
            WHERE NOT EXISTS (SELECT 1 FROM Orders WHERE Orders.UserId = Users.Id)
            ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    #endregion

    #region IN (Subquery) Tests

    [Test]
    public void InSubqueryReturnsTrueWhenMatchTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE ActiveUserIds (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO ActiveUserIds (Id) VALUES (1)");
        m_engine.Execute("INSERT INTO ActiveUserIds (Id) VALUES (2)");
        
        var rows = m_engine.Query(@"
            SELECT Name FROM Users 
            WHERE Id IN (SELECT Id FROM ActiveUserIds)
            ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
    }

    [Test]
    public void NotInSubqueryFiltersMatchingRowsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE ActiveUserIds (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO ActiveUserIds (Id) VALUES (1)");
        m_engine.Execute("INSERT INTO ActiveUserIds (Id) VALUES (2)");
        
        var rows = m_engine.Query(@"
            SELECT Name FROM Users 
            WHERE Id NOT IN (SELECT Id FROM ActiveUserIds)
            ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Charlie"));
    }

    [Test]
    public void InSubqueryWithNullsHandledCorrectlyTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE TestIds (Id BIGINT)");
        m_engine.Execute("INSERT INTO TestIds (Id) VALUES (1)");
        m_engine.Execute("INSERT INTO TestIds (Id) VALUES (NULL)");
        
        // When subquery contains NULL, IN returns NULL for non-matching values
        var rows = m_engine.Query(@"
            SELECT Name FROM Users 
            WHERE Id IN (SELECT Id FROM TestIds)
            ORDER BY Name");
        
        // Only Id=1 matches (Alice)
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    #endregion

    #region ANY/SOME Tests

    [Test]
    public void AnyWithEqualReturnsTrueWhenMatchExistsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE TargetIds (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO TargetIds (Id) VALUES (1)");
        m_engine.Execute("INSERT INTO TargetIds (Id) VALUES (2)");
        
        var result = m_engine.ExecuteScalar("SELECT 1 = ANY (SELECT Id FROM TargetIds)");
        
        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void AnyWithEqualReturnsFalseWhenNoMatchTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE TargetIds (Id BIGINT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO TargetIds (Id) VALUES (1)");
        m_engine.Execute("INSERT INTO TargetIds (Id) VALUES (2)");
        
        var result = m_engine.ExecuteScalar("SELECT 99 = ANY (SELECT Id FROM TargetIds)");
        
        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void AnyWithGreaterThanTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (Value INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (10)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (20)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (30)");
        
        // 15 > ANY (10, 20, 30) = true because 15 > 10
        var result = m_engine.ExecuteScalar("SELECT 15 > ANY (SELECT Value FROM Numbers)");
        
        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void SomeIsAliasForAnyTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (Value INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (10)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (20)");
        
        var result = m_engine.ExecuteScalar("SELECT 10 = SOME (SELECT Value FROM Numbers)");
        
        Assert.That(result.AsBool(), Is.True);
    }

    #endregion

    #region ALL Tests

    [Test]
    public void AllReturnsTrueWhenAllMatchTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (Value INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (5)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (10)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (15)");
        
        // 20 > ALL (5, 10, 15) = true
        var result = m_engine.ExecuteScalar("SELECT 20 > ALL (SELECT Value FROM Numbers)");
        
        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void AllReturnsFalseWhenNotAllMatchTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (Value INT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (5)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (10)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (15)");
        
        // 10 > ALL (5, 10, 15) = false because 10 is not > 10 and not > 15
        var result = m_engine.ExecuteScalar("SELECT 10 > ALL (SELECT Value FROM Numbers)");
        
        Assert.That(result.AsBool(), Is.False);
    }

    [Test]
    public void AllWithEmptySubqueryReturnsTrueTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (Value INT PRIMARY KEY)");
        
        // x > ALL (empty) = true (vacuously true)
        var result = m_engine.ExecuteScalar("SELECT 5 > ALL (SELECT Value FROM Numbers)");
        
        Assert.That(result.AsBool(), Is.True);
    }

    [Test]
    public void AllWithNullsReturnsNullTest()
    {
        m_engine.Execute(@"
            CREATE TABLE Numbers (Value INT)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (5)");
        m_engine.Execute("INSERT INTO Numbers (Value) VALUES (NULL)");
        
        // 10 > ALL (5, NULL) = NULL (can't be certain about NULL)
        var result = m_engine.ExecuteScalar("SELECT 10 > ALL (SELECT Value FROM Numbers)");
        
        Assert.That(result.IsNull, Is.True);
    }

    #endregion

    #region Correlated Subquery Tests

    [Test]
    public void CorrelatedSubqueryInSelectTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE Orders (
                Id BIGINT PRIMARY KEY AUTOINCREMENT,
                UserId BIGINT NOT NULL,
                Amount DECIMAL NOT NULL
            )");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (1, 100.00)");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (1, 50.00)");
        m_engine.Execute("INSERT INTO Orders (UserId, Amount) VALUES (2, 200.00)");
        
        var rows = m_engine.Query(@"
            SELECT 
                u.Name,
                (SELECT COUNT(*) FROM Orders o WHERE o.UserId = u.Id) AS OrderCount
            FROM Users u
            ORDER BY u.Name");
        
        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0]["OrderCount"].AsInt64(), Is.EqualTo(2)); // Alice: 2 orders
        Assert.That(rows[1]["OrderCount"].AsInt64(), Is.EqualTo(1)); // Bob: 1 order
        Assert.That(rows[2]["OrderCount"].AsInt64(), Is.EqualTo(0)); // Charlie: 0 orders
    }

    [Test]
    public void CorrelatedExistsSubqueryTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        m_engine.Execute(@"
            CREATE TABLE VipUsers (UserId BIGINT PRIMARY KEY)");
        m_engine.Execute("INSERT INTO VipUsers (UserId) VALUES (1)");
        
        var rows = m_engine.Query(@"
            SELECT Name FROM Users u
            WHERE EXISTS (SELECT 1 FROM VipUsers v WHERE v.UserId = u.Id)
            ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
    }

    #endregion
}
