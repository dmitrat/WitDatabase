using NUnit.Framework;
using System.Data;

namespace OutWit.Database.AdoNet.Tests.Command;

/// <summary>
/// Tests for WitDbCommand.
/// </summary>
[TestFixture]
public class WitDbCommandTests
{
    #region Fields

    private WitDbConnection m_connection = null!;

    #endregion

    #region Setup/TearDown

    [SetUp]
    public void Setup()
    {
        m_connection = new WitDbConnection("Data Source=:memory:");
        m_connection.Open();

        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Name VARCHAR(100), Value DECIMAL(10,2))";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        m_connection?.Dispose();
    }

    #endregion

    #region Constructor Tests

    [Test]
    public void DefaultConstructorCreatesCommandTest()
    {
        using var cmd = new WitDbCommand();

        Assert.That(cmd.CommandText, Is.Empty);
        Assert.That(cmd.Connection, Is.Null);
        Assert.That(cmd.CommandType, Is.EqualTo(CommandType.Text));
        Assert.That(cmd.CommandTimeout, Is.EqualTo(30));
    }

    [Test]
    public void ConstructorWithCommandTextSetsPropertyTest()
    {
        using var cmd = new WitDbCommand("SELECT 1");

        Assert.That(cmd.CommandText, Is.EqualTo("SELECT 1"));
    }

    [Test]
    public void ConstructorWithConnectionSetsPropertyTest()
    {
        using var cmd = new WitDbCommand("SELECT 1", m_connection);

        Assert.That(cmd.CommandText, Is.EqualTo("SELECT 1"));
        Assert.That(cmd.Connection, Is.SameAs(m_connection));
    }

    [Test]
    public void ConstructorWithTransactionSetsPropertyTest()
    {
        using var transaction = (WitDbTransaction)m_connection.BeginTransaction();
        using var cmd = new WitDbCommand("SELECT 1", m_connection, transaction);

        Assert.That(cmd.Transaction, Is.SameAs(transaction));
    }

    #endregion

    #region ExecuteNonQuery Tests

    [Test]
    public void ExecuteNonQueryInsertReturnsRowsAffectedTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test')";

        var result = cmd.ExecuteNonQuery();

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteNonQueryUpdateReturnsRowsAffectedTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "UPDATE Test SET Name = 'Updated' WHERE Id = 1";
        var result = cmd.ExecuteNonQuery();

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteNonQueryDeleteReturnsRowsAffectedTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DELETE FROM Test WHERE Id = 1";
        var result = cmd.ExecuteNonQuery();

        Assert.That(result, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteNonQueryWithoutConnectionThrowsTest()
    {
        using var cmd = new WitDbCommand("SELECT 1");

        Assert.Throws<InvalidOperationException>(() => cmd.ExecuteNonQuery());
    }

    [Test]
    public void ExecuteNonQueryWithClosedConnectionThrowsTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1";

        Assert.Throws<InvalidOperationException>(() => cmd.ExecuteNonQuery());
    }

    [Test]
    public void ExecuteNonQueryWithEmptyCommandTextThrowsTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "";

        Assert.Throws<InvalidOperationException>(() => cmd.ExecuteNonQuery());
    }

    [Test]
    public async Task ExecuteNonQueryAsyncReturnsRowsAffectedTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test')";

        var result = await cmd.ExecuteNonQueryAsync();

        Assert.That(result, Is.EqualTo(1));
    }

    #endregion

    #region ExecuteScalar Tests

    [Test]
    public void ExecuteScalarReturnsFirstColumnFirstRowTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Name FROM Test WHERE Id = 1";
        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.EqualTo("Test"));
    }

    [Test]
    public void ExecuteScalarReturnsNullForEmptyResultTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Test WHERE Id = 999";

        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ExecuteScalarReturnsIntegerValueTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (42, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Id FROM Test WHERE Name = 'Test'";
        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.EqualTo(42L));
    }

    [Test]
    public void ExecuteScalarReturnsDecimalValueTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name, Value) VALUES (1, 'Test', 123.45)";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Value FROM Test WHERE Id = 1";
        var result = cmd.ExecuteScalar();

        Assert.That(result, Is.EqualTo(123.45m));
    }

    [Test]
    public async Task ExecuteScalarAsyncReturnsValueTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT 42";

        var result = await cmd.ExecuteScalarAsync();

        Assert.That(result, Is.EqualTo(42L));
    }

    #endregion

    #region ExecuteReader Tests

    [Test]
    public void ExecuteReaderReturnsDataReaderTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT 1 AS Value";

        using var reader = cmd.ExecuteReader();

        Assert.That(reader, Is.Not.Null);
        Assert.That(reader, Is.InstanceOf<WitDbDataReader>());
    }

    [Test]
    public void ExecuteReaderCanReadDataTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT Id, Name FROM Test";
        using var reader = cmd.ExecuteReader();

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt64(0), Is.EqualTo(1));
        Assert.That(reader.GetString(1), Is.EqualTo("Test"));
    }

    [Test]
    public async Task ExecuteReaderAsyncReturnsDataReaderTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT 1 AS Value";

        using var reader = await cmd.ExecuteReaderAsync();

        Assert.That(reader, Is.Not.Null);
    }

    [Test]
    public void ExecuteReaderWithCloseConnectionBehaviorClosesConnectionTest()
    {
        using var connection = new WitDbConnection("Data Source=:memory:");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1";
        
        using var reader = cmd.ExecuteReader(CommandBehavior.CloseConnection);
        reader.Close();

        Assert.That(connection.State, Is.EqualTo(ConnectionState.Closed));
    }

    #endregion

    #region Parameters Tests

    [Test]
    public void CreateParameterReturnsWitDbParameterTest()
    {
        using var cmd = m_connection.CreateCommand();

        var param = cmd.CreateParameter();

        Assert.That(param, Is.InstanceOf<WitDbParameter>());
    }

    [Test]
    public void ParametersCollectionIsNotNullTest()
    {
        using var cmd = m_connection.CreateCommand();

        Assert.That(cmd.Parameters, Is.Not.Null);
        Assert.That(cmd.Parameters, Is.InstanceOf<WitDbParameterCollection>());
    }

    [Test]
    public void ParameterizedQueryWorksCorrectlyTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (@id, @name)";
        cmd.Parameters.AddWithValue("@id", 1);
        cmd.Parameters.AddWithValue("@name", "Parametrized");

        var result = cmd.ExecuteNonQuery();

        Assert.That(result, Is.EqualTo(1));

        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT Name FROM Test WHERE Id = 1";
        var name = cmd.ExecuteScalar();
        Assert.That(name, Is.EqualTo("Parametrized"));
    }

    [Test]
    public void ParameterWithoutPrefixWorksTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (@id, @name)";
        cmd.Parameters.AddWithValue("id", 2);
        cmd.Parameters.AddWithValue("name", "NoPrefixParam");

        var result = cmd.ExecuteNonQuery();

        Assert.That(result, Is.EqualTo(1));
    }

    #endregion

    #region CommandTimeout Tests

    [Test]
    public void CommandTimeoutDefaultIs30SecondsTest()
    {
        using var cmd = m_connection.CreateCommand();

        Assert.That(cmd.CommandTimeout, Is.EqualTo(30));
    }

    [Test]
    public void CommandTimeoutCanBeSetTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandTimeout = 60;

        Assert.That(cmd.CommandTimeout, Is.EqualTo(60));
    }

    [Test]
    public void CommandTimeoutNegativeThrowsTest()
    {
        using var cmd = m_connection.CreateCommand();

        Assert.Throws<ArgumentOutOfRangeException>(() => cmd.CommandTimeout = -1);
    }

    #endregion

    #region CommandType Tests

    [Test]
    public void CommandTypeDefaultIsTextTest()
    {
        using var cmd = m_connection.CreateCommand();

        Assert.That(cmd.CommandType, Is.EqualTo(CommandType.Text));
    }

    /// <summary>
    /// <c>StoredProcedure</c> is accepted since phase 9d; <c>TableDirect</c> is still refused.
    /// </summary>
    /// <remarks>
    /// This test asserted that <c>StoredProcedure</c> threw, and it failed the moment procedures
    /// became reachable - which is the right way for a pin to stop being true. It is kept rather than
    /// deleted because the other half is still worth pinning: <c>TableDirect</c> means "the
    /// CommandText is a table name", which this provider has no translation for, and answering
    /// something approximate would be worse than refusing.
    /// <c>StoredProcedureCommandTests</c> covers what the accepted one now does.
    /// </remarks>
    [Test]
    public void CommandTypeAcceptsStoredProcedureAndRefusesTableDirectTest()
    {
        using var cmd = m_connection.CreateCommand();

        Assert.Multiple(() =>
        {
            Assert.That(() => cmd.CommandType = CommandType.StoredProcedure, Throws.Nothing);
            Assert.Throws<NotSupportedException>(() => cmd.CommandType = CommandType.TableDirect);
        });
    }

    #endregion

    #region Prepare Tests

    [Test]
    public void PrepareDoesNotThrowTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test WHERE Id = @id";

        Assert.DoesNotThrow(() => cmd.Prepare());
    }

    [Test]
    public void PrepareWithEmptyCommandTextThrowsTest()
    {
        using var cmd = m_connection.CreateCommand();

        Assert.Throws<InvalidOperationException>(() => cmd.Prepare());
    }

    [Test]
    public async Task PrepareAsyncDoesNotThrowTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test";

        await cmd.PrepareAsync();

        Assert.Pass();
    }

    [Test]
    public void IsPreparedReturnsFalseBeforePrepareTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test";
        
        Assert.That(cmd.IsPrepared, Is.False);
    }

    [Test]
    public void IsPreparedReturnsTrueAfterPrepareTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test";
        
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
    }

    [Test]
    public void IsPreparedReturnsFalseAfterCommandTextChangesTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test";
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
        
        cmd.CommandText = "SELECT Id FROM Test";
        
        Assert.That(cmd.IsPrepared, Is.False);
    }

    [Test]
    public void UnprepareDisposePreparedStatementTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test";
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
        
        cmd.Unprepare();
        
        Assert.That(cmd.IsPrepared, Is.False);
    }

    [Test]
    public void PreparedStatementExecutesCorrectlyTest()
    {
        // Insert test data
        using (var insertCmd = m_connection.CreateCommand())
        {
            insertCmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Alice'), (2, 'Bob')";
            insertCmd.ExecuteNonQuery();
        }

        // Create and prepare select command
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT Name FROM Test WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", 1);
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
        
        // Execute prepared statement
        var result = cmd.ExecuteScalar();
        Assert.That(result, Is.EqualTo("Alice"));
        
        // Change parameter and execute again
        cmd.Parameters["@id"].Value = 2;
        result = cmd.ExecuteScalar();
        Assert.That(result, Is.EqualTo("Bob"));
    }

    [Test]
    public void PreparedStatementRemainsValidAfterReprepareSameTextTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
        
        // Re-prepare same command text should succeed without re-parsing
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
    }

    [Test]
    public void PreparedInsertStatementWorksCorrectlyTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (@id, @name)";
        cmd.Parameters.AddWithValue("@id", 0);
        cmd.Parameters.AddWithValue("@name", "");
        cmd.Prepare();

        // Insert multiple rows using prepared statement
        for (int i = 1; i <= 5; i++)
        {
            cmd.Parameters["@id"].Value = i;
            cmd.Parameters["@name"].Value = $"User{i}";
            var result = cmd.ExecuteNonQuery();
            Assert.That(result, Is.EqualTo(1));
        }

        // Verify data
        using var selectCmd = m_connection.CreateCommand();
        selectCmd.CommandText = "SELECT COUNT(*) FROM Test";
        var count = selectCmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(5L));
    }

    [Test]
    public void PreparedUpdateStatementWorksCorrectlyTest()
    {
        // Setup
        using var insertCmd = m_connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'Before')";
        insertCmd.ExecuteNonQuery();

        // Prepare update statement
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "UPDATE Test SET Name = @name WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", 1);
        cmd.Parameters.AddWithValue("@name", "After");
        cmd.Prepare();

        var result = cmd.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));

        // Verify
        using var selectCmd = m_connection.CreateCommand();
        selectCmd.CommandText = "SELECT Name FROM Test WHERE Id = 1";
        var name = selectCmd.ExecuteScalar();
        Assert.That(name, Is.EqualTo("After"));
    }

    [Test]
    public void PreparedDeleteStatementWorksCorrectlyTest()
    {
        // Setup
        using var insertCmd = m_connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Test (Id, Name) VALUES (1, 'ToDelete')";
        insertCmd.ExecuteNonQuery();

        // Prepare delete statement
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Test WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", 1);
        cmd.Prepare();

        var result = cmd.ExecuteNonQuery();
        Assert.That(result, Is.EqualTo(1));

        // Verify
        using var selectCmd = m_connection.CreateCommand();
        selectCmd.CommandText = "SELECT COUNT(*) FROM Test WHERE Id = 1";
        var count = selectCmd.ExecuteScalar();
        Assert.That(count, Is.EqualTo(0L));
    }

    [Test]
    public void DisposeCleansPreparedStatementTest()
    {
        var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Test";
        cmd.Prepare();
        
        Assert.That(cmd.IsPrepared, Is.True);
        
        cmd.Dispose();
        
        Assert.That(cmd.IsPrepared, Is.False);
    }

    #endregion

    #region Cancel Tests

    [Test]
    public void CancelDoesNotThrowTest()
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT 1";

        Assert.DoesNotThrow(() => cmd.Cancel());
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void DisposeDoesNotThrowTest()
    {
        var cmd = m_connection.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.Parameters.AddWithValue("@p", 1);

        Assert.DoesNotThrow(() => cmd.Dispose());
    }

    [Test]
    public void DisposeClearsParametersTest()
    {
        var cmd = m_connection.CreateCommand();
        cmd.Parameters.AddWithValue("@p", 1);
        
        cmd.Dispose();

        Assert.That(cmd.Parameters.Count, Is.EqualTo(0));
    }

    #endregion
}
