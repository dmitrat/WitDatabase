using OutWit.Database.Values;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for WitSqlStatementPrepared.
/// </summary>
[TestFixture]
public sealed class WitSqlEngineStatementTests : WitSqlEngineTestsBase
{
    #region Basic Tests

    [Test]
    public void PrepareReturnsStatementTest()
    {
        using var prepared = m_engine.Prepare("SELECT 1");
        
        Assert.That(prepared, Is.Not.Null);
    }

    [Test]
    public void ExecutePreparedStatementReturnsResultTest()
    {
        using var prepared = m_engine.Prepare("SELECT 1 AS Value");
        using var result = prepared.Execute();
        
        Assert.That(result.Read(), Is.True);
        Assert.That(result.CurrentRow["Value"].AsInt64(), Is.EqualTo(1));
    }

    #endregion

    #region Parameter Tests

    [Test]
    public void SetParameterBindsValueTest()
    {
        CreateUsersTable();
        
        using var prepared = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        
        prepared.SetParameter("name", "Test");
        prepared.SetParameter("email", "test@test.com");
        
        using var result = prepared.Execute();
        
        var rows = m_engine.Query("SELECT * FROM Users");
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Test"));
    }

    [Test]
    public void SetParameterWithoutAtPrefixBindsValueTest()
    {
        CreateTestTable();
        
        using var prepared = m_engine.Prepare("INSERT INTO TestTable (Name, Value) VALUES (@name, @value)");
        
        prepared.SetParameter("name", "Test");
        prepared.SetParameter("value", 42);
        
        using var result = prepared.Execute();
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM TestTable");
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Value"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void SetParameterWithAtPrefixBindsValueTest()
    {
        CreateTestTable();
        
        using var prepared = m_engine.Prepare("INSERT INTO TestTable (Name, Value) VALUES (@name, @value)");
        
        prepared.SetParameter("@name", "Test");
        prepared.SetParameter("@value", 42);
        
        using var result = prepared.Execute();
        
        var row = m_engine.QueryFirstOrDefault("SELECT * FROM TestTable");
        Assert.That(row, Is.Not.Null);
        Assert.That(row.Value["Value"].AsInt64(), Is.EqualTo(42));
    }

    [Test]
    public void ClearParametersClearsAllParametersTest()
    {
        CreateTestTable();
        
        using var prepared = m_engine.Prepare("INSERT INTO TestTable (Name, Value) VALUES (@name, 1)");
        
        prepared.SetParameter("name", "First");
        using var _ = prepared.Execute();
        
        prepared.ClearParameters();
        prepared.SetParameter("name", "Second");
        using var __ = prepared.Execute();
        
        var rows = m_engine.Query("SELECT Name FROM TestTable ORDER BY Id");
        
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("First"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Second"));
    }

    [Test]
    public void SetParameterReturnsFluentInterfaceTest()
    {
        using var prepared = m_engine.Prepare("SELECT @a, @b, @c");
        
        var result = prepared
            .SetParameter("a", 1)
            .SetParameter("b", 2)
            .SetParameter("c", 3);
        
        Assert.That(result, Is.SameAs(prepared));
    }

    [Test]
    public void ClearParametersReturnsFluentInterfaceTest()
    {
        using var prepared = m_engine.Prepare("SELECT 1");
        
        var result = prepared.ClearParameters();
        
        Assert.That(result, Is.SameAs(prepared));
    }

    #endregion

    #region Reuse Tests

    [Test]
    public void PreparedStatementCanBeReusedTest()
    {
        CreateUsersTable();
        
        using var prepared = m_engine.Prepare("INSERT INTO Users (Name, Email) VALUES (@name, @email)");
        
        prepared.SetParameter("name", "Alice");
        prepared.SetParameter("email", "alice@test.com");
        using var _ = prepared.Execute();
        
        prepared.SetParameter("name", "Bob");
        prepared.SetParameter("email", "bob@test.com");
        using var __ = prepared.Execute();
        
        var rows = m_engine.Query("SELECT * FROM Users ORDER BY Name");
        
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0]["Name"].AsString(), Is.EqualTo("Alice"));
        Assert.That(rows[1]["Name"].AsString(), Is.EqualTo("Bob"));
    }

    [Test]
    public void PreparedStatementSelectCanBeReusedWithDifferentParametersTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        using var prepared = m_engine.Prepare("SELECT * FROM Users WHERE Name = @name");
        
        prepared.SetParameter("name", "Alice");
        using var result1 = prepared.Execute();
        Assert.That(result1.Read(), Is.True);
        Assert.That(result1.CurrentRow["Name"].AsString(), Is.EqualTo("Alice"));
        
        prepared.SetParameter("name", "Bob");
        using var result2 = prepared.Execute();
        Assert.That(result2.Read(), Is.True);
        Assert.That(result2.CurrentRow["Name"].AsString(), Is.EqualTo("Bob"));
    }

    #endregion

    #region Cancellation Tests

    [Test]
    public void ExecuteWithCancellationTokenCancelsTest()
    {
        CreateUsersTable();
        InsertTestUsers();
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        using var prepared = m_engine.Prepare("SELECT * FROM Users; SELECT * FROM Users");
        
        Assert.Throws<OperationCanceledException>(() => prepared.Execute(cts.Token));
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void DisposeCanBeCalledMultipleTimesTest()
    {
        var prepared = m_engine.Prepare("SELECT 1");
        
        prepared.Dispose();
        
        Assert.DoesNotThrow(() => prepared.Dispose());
    }

    #endregion
}
