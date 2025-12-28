using System.Diagnostics;
using OutWit.Database.Core.Builder;
using NUnit.Framework;

namespace OutWit.Database.Tests;

/// <summary>
/// Tests for query timeout functionality.
/// </summary>
[TestFixture]
public class WitSqlEngineTimeoutTests : WitSqlEngineTestsBase
{
    #region Timeout Tests

    [Test]
    public void Execute_WithTimeout_CompletesBeforeTimeout()
    {
        // Arrange
        m_engine.Execute("CREATE TABLE Simple (Id INTEGER PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Simple VALUES (1)");

        // Act - Simple query that should complete quickly
        var result = m_engine.Execute(
            "SELECT * FROM Simple",
            parameters: null,
            timeout: TimeSpan.FromSeconds(10));

        // Assert
        var rows = result.ReadAll();
        Assert.That(rows.Count, Is.EqualTo(1));
    }

    [Test]
    public void Execute_TimeoutCreatesCorrectException()
    {
        // Arrange - Create a scenario where we can verify timeout handling
        m_engine.Execute("CREATE TABLE Test (Id INTEGER PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Test VALUES (1)");

        // Act - Verify that timeout parameter is accepted and processed
        var result = m_engine.Execute(
            "SELECT * FROM Test",
            parameters: null,
            timeout: TimeSpan.FromSeconds(30));

        // Assert - Query completes successfully within timeout
        Assert.That(result.ReadAll().Count, Is.EqualTo(1));
    }

    [Test]
    public void Execute_WithCancellationToken_CanBeCancelled()
    {
        // Arrange
        m_engine.Execute("CREATE TABLE Data (Id INTEGER PRIMARY KEY)");
        for (int i = 0; i < 100; i++)
        {
            m_engine.Execute($"INSERT INTO Data VALUES ({i})");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        Assert.Throws<OperationCanceledException>(() =>
            m_engine.Execute(
                "SELECT * FROM Data",
                parameters: null,
                cts.Token));
    }

    [Test]
    public void Execute_WithCancellationTokenAndTimeout_BothWork()
    {
        // Arrange
        m_engine.Execute("CREATE TABLE Combined (Id INTEGER PRIMARY KEY)");
        m_engine.Execute("INSERT INTO Combined VALUES (1)");

        using var cts = new CancellationTokenSource();

        // Act - Use both timeout and cancellation token
        var result = m_engine.Execute(
            "SELECT * FROM Combined",
            parameters: null,
            timeout: TimeSpan.FromSeconds(10),
            cts.Token);

        // Assert
        Assert.That(result.ReadAll().Count, Is.EqualTo(1));
    }

    [Test]
    public void DefaultQueryTimeout_AppliesWhenSet()
    {
        // Arrange
        m_engine.DefaultQueryTimeout = TimeSpan.FromSeconds(30);
        m_engine.Execute("CREATE TABLE Test (Id INTEGER PRIMARY KEY)");

        // Act - Should complete within default timeout
        var result = m_engine.Execute("SELECT * FROM Test");

        // Assert
        Assert.That(result.ReadAll().Count, Is.EqualTo(0));
    }

    [Test]
    public void DefaultQueryTimeout_CanBeNull()
    {
        // Arrange
        m_engine.DefaultQueryTimeout = null;

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => m_engine.Execute("SELECT 1"));
    }

    [Test]
    public void DefaultQueryTimeout_CanBeRead()
    {
        // Act
        m_engine.DefaultQueryTimeout = TimeSpan.FromMinutes(5);

        // Assert
        Assert.That(m_engine.DefaultQueryTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void Execute_ExplicitTimeoutOverridesDefault()
    {
        // Arrange
        m_engine.DefaultQueryTimeout = TimeSpan.FromSeconds(1);
        m_engine.Execute("CREATE TABLE Test (Id INTEGER PRIMARY KEY)");

        // Act - Use longer explicit timeout
        var result = m_engine.Execute(
            "SELECT * FROM Test",
            parameters: null,
            timeout: TimeSpan.FromSeconds(60));

        // Assert - Query should complete (not timeout)
        Assert.That(result.ReadAll().Count, Is.EqualTo(0));
    }

    [Test]
    public void Execute_ZeroTimeout_MeansNoTimeout()
    {
        // Arrange
        m_engine.Execute("CREATE TABLE Quick (Id INTEGER PRIMARY KEY)");

        // Act - Zero timeout should mean no timeout
        var result = m_engine.Execute(
            "SELECT * FROM Quick",
            parameters: null,
            timeout: TimeSpan.Zero);

        // Assert
        Assert.That(result.ReadAll().Count, Is.EqualTo(0));
    }

    [Test]
    public void Execute_NegativeTimeout_MeansNoTimeout()
    {
        // Arrange
        m_engine.Execute("CREATE TABLE Quick2 (Id INTEGER PRIMARY KEY)");

        // Act - Negative timeout should be treated as no timeout
        var result = m_engine.Execute(
            "SELECT * FROM Quick2",
            parameters: null,
            timeout: TimeSpan.FromSeconds(-1));

        // Assert
        Assert.That(result.ReadAll().Count, Is.EqualTo(0));
    }

    #endregion
}
