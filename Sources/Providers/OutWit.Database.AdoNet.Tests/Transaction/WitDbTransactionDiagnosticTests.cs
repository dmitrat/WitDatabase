using NUnit.Framework;

namespace OutWit.Database.AdoNet.Tests.Transaction;

/// <summary>
/// Diagnostic tests to understand transaction behavior.
/// </summary>
[TestFixture]
public class WitDbTransactionDiagnosticTests
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
        cmd.CommandText = "CREATE TABLE Test (Id INT PRIMARY KEY, Name VARCHAR(100))";
        cmd.ExecuteNonQuery();
    }

    [TearDown]
    public void TearDown()
    {
        m_connection?.Dispose();
    }

    #endregion

    #region Diagnostic Tests

    [Test]
    public void DiagnoseTransactionStateTest()
    {
        // Check initial state
        TestContext.WriteLine($"Initial - Engine.CurrentTransaction is null: {m_connection.Engine?.CurrentTransaction == null}");
        
        // Begin transaction
        using var transaction = (WitDbTransaction)m_connection.BeginTransaction();
        TestContext.WriteLine($"After BeginTransaction - Engine.CurrentTransaction is null: {m_connection.Engine?.CurrentTransaction == null}");
        TestContext.WriteLine($"After BeginTransaction - Engine.CurrentTransaction type: {m_connection.Engine?.CurrentTransaction?.GetType().Name ?? "null"}");

        // Execute INSERT within transaction
        using var cmd = m_connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO Test VALUES (1, 'ToBeRolledBack')";
        cmd.ExecuteNonQuery();
        TestContext.WriteLine($"After INSERT - Engine.CurrentTransaction is null: {m_connection.Engine?.CurrentTransaction == null}");

        // Check count within transaction
        cmd.CommandText = "SELECT COUNT(*) FROM Test";
        var countInTx = cmd.ExecuteScalar();
        TestContext.WriteLine($"Count within transaction: {countInTx}");

        // Rollback
        transaction.Rollback();
        TestContext.WriteLine($"After Rollback - Engine.CurrentTransaction is null: {m_connection.Engine?.CurrentTransaction == null}");

        // Check count after rollback
        cmd.Transaction = null;
        cmd.CommandText = "SELECT COUNT(*) FROM Test";
        var countAfterRollback = cmd.ExecuteScalar();
        TestContext.WriteLine($"Count after rollback: {countAfterRollback}");

        // The actual assertion
        Assert.That(countAfterRollback, Is.EqualTo(0L), "Count should be 0 after rollback");
    }

    [Test]
    public void DiagnoseEngineDirectTransactionTest()
    {
        // Test using engine directly (bypass ADO.NET)
        var engine = m_connection.Engine!;
        
        TestContext.WriteLine($"Initial - CurrentTransaction is null: {engine.CurrentTransaction == null}");
        
        // Begin transaction via SQL
        engine.Execute("BEGIN TRANSACTION");
        TestContext.WriteLine($"After BEGIN TRANSACTION SQL - CurrentTransaction is null: {engine.CurrentTransaction == null}");
        TestContext.WriteLine($"After BEGIN TRANSACTION SQL - CurrentTransaction type: {engine.CurrentTransaction?.GetType().Name ?? "null"}");

        // Insert
        engine.Execute("INSERT INTO Test VALUES (1, 'Test')");
        
        // Check count
        var countInTx = engine.ExecuteScalar("SELECT COUNT(*) FROM Test");
        TestContext.WriteLine($"Count within transaction: {countInTx}");

        // Rollback via SQL
        engine.Execute("ROLLBACK");
        TestContext.WriteLine($"After ROLLBACK SQL - CurrentTransaction is null: {engine.CurrentTransaction == null}");

        // Check count after rollback
        var countAfterRollback = engine.ExecuteScalar("SELECT COUNT(*) FROM Test");
        TestContext.WriteLine($"Count after rollback: {countAfterRollback}");

        Assert.That(countAfterRollback.AsInt64(), Is.EqualTo(0L), "Count should be 0 after rollback");
    }

    [Test]
    public void DiagnoseEngineDirectMethodTransactionTest()
    {
        // Test using engine's direct transaction methods
        var engine = m_connection.Engine!;
        
        TestContext.WriteLine($"Initial - CurrentTransaction is null: {engine.CurrentTransaction == null}");
        
        // Begin transaction via direct method
        using var handle = engine.BeginTransaction();
        TestContext.WriteLine($"After BeginTransaction() - CurrentTransaction is null: {engine.CurrentTransaction == null}");
        TestContext.WriteLine($"After BeginTransaction() - CurrentTransaction type: {engine.CurrentTransaction?.GetType().Name ?? "null"}");

        // Insert
        engine.Execute("INSERT INTO Test VALUES (1, 'Test')");
        
        // Check count
        var countInTx = engine.ExecuteScalar("SELECT COUNT(*) FROM Test");
        TestContext.WriteLine($"Count within transaction: {countInTx}");

        // Rollback via direct method
        engine.Rollback();
        TestContext.WriteLine($"After Rollback() - CurrentTransaction is null: {engine.CurrentTransaction == null}");

        // Check count after rollback
        var countAfterRollback = engine.ExecuteScalar("SELECT COUNT(*) FROM Test");
        TestContext.WriteLine($"Count after rollback: {countAfterRollback}");

        Assert.That(countAfterRollback.AsInt64(), Is.EqualTo(0L), "Count should be 0 after rollback");
    }

    [Test]
    public void DiagnoseConnectionStringDefaultsTest()
    {
        var builder = new WitDbConnectionStringBuilder("Data Source=:memory:");
        
        TestContext.WriteLine($"MVCC default: {builder.Mvcc}");
        TestContext.WriteLine($"Transactions default: {builder.Transactions}");
        TestContext.WriteLine($"IsolationLevel default: {builder.IsolationLevel}");
        
        Assert.Pass("Defaults logged above");
    }

    [Test]
    public void DiagnoseWithMvccFalseTest()
    {
        // Test with MVCC explicitly disabled
        using var conn = new WitDbConnection("Data Source=:memory:;MVCC=false");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE Test2 (Id INT PRIMARY KEY, Name VARCHAR(100))";
        cmd.ExecuteNonQuery();

        TestContext.WriteLine($"Engine.CurrentTransaction type before TX: {conn.Engine?.CurrentTransaction?.GetType().Name ?? "null"}");

        using var tx = (WitDbTransaction)conn.BeginTransaction();
        TestContext.WriteLine($"Engine.CurrentTransaction type after BeginTX: {conn.Engine?.CurrentTransaction?.GetType().Name ?? "null"}");

        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Test2 VALUES (1, 'Test')";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "SELECT COUNT(*) FROM Test2";
        var countInTx = cmd.ExecuteScalar();
        TestContext.WriteLine($"Count within transaction: {countInTx}");

        tx.Rollback();
        TestContext.WriteLine($"Engine.CurrentTransaction type after Rollback: {conn.Engine?.CurrentTransaction?.GetType().Name ?? "null"}");

        cmd.Transaction = null;
        cmd.CommandText = "SELECT COUNT(*) FROM Test2";
        var countAfterRollback = cmd.ExecuteScalar();
        TestContext.WriteLine($"Count after rollback: {countAfterRollback}");

        Assert.That(countAfterRollback, Is.EqualTo(0L), "Count should be 0 after rollback with MVCC=false");
    }

    [Test]
    public void DiagnoseRawStoreTransactionTest()
    {
        // Test raw store transaction without any SQL
        var engine = m_connection.Engine!;
        
        // Access the underlying database directly
        var database = typeof(Engine.WitSqlEngine)
            .GetField("m_database", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine) as OutWit.Database.Core.Builder.WitDatabase;
        
        TestContext.WriteLine($"Database type: {database!.GetType().Name}");
        TestContext.WriteLine($"Store type: {database.Store.GetType().Name}");
        TestContext.WriteLine($"SupportsTransactions: {database.SupportsTransactions}");
        TestContext.WriteLine($"SupportsMvcc: {database.SupportsMvcc}");
        
        // Test key
        var testKey = System.Text.Encoding.UTF8.GetBytes("test:raw:1");
        var testValue = System.Text.Encoding.UTF8.GetBytes("value1");
        
        // Check initial state
        var initialValue = database.Get(testKey);
        TestContext.WriteLine($"Initial value: {(initialValue == null ? "null" : System.Text.Encoding.UTF8.GetString(initialValue))}");
        
        // Begin transaction
        using var tx = database.BeginTransaction();
        TestContext.WriteLine($"Transaction type: {tx.GetType().Name}");
        
        // Write via transaction
        tx.Put(testKey, testValue);
        
        // Read via transaction (should see buffered value)
        var valueInTx = tx.Get(testKey);
        TestContext.WriteLine($"Value in transaction: {(valueInTx == null ? "null" : System.Text.Encoding.UTF8.GetString(valueInTx))}");
        
        // Rollback
        tx.Rollback();
        TestContext.WriteLine($"After rollback - transaction state: {tx.State}");
        
        // Read via store (should be null after rollback)
        var valueAfterRollback = database.Get(testKey);
        TestContext.WriteLine($"Value after rollback: {(valueAfterRollback == null ? "null" : System.Text.Encoding.UTF8.GetString(valueAfterRollback))}");
        
        Assert.That(valueAfterRollback, Is.Null, "Value should be null after rollback");
    }

    [Test]
    public void DiagnoseEngineTransactionWithInsertRowTest()
    {
        // This test uses engine methods directly, similar to what SQL statements do
        var engine = m_connection.Engine!;
        
        TestContext.WriteLine($"CurrentTransaction before BEGIN: {engine.CurrentTransaction?.GetType().Name ?? "null"}");
        
        // Begin transaction via engine method
        using var handle = engine.BeginTransaction();
        TestContext.WriteLine($"CurrentTransaction after BEGIN: {engine.CurrentTransaction?.GetType().Name ?? "null"}");
        
        // Create a row like INSERT does
        var row = new OutWit.Database.Sql.WitSqlRow(
            new OutWit.Database.Values.WitSqlValue[] {
                OutWit.Database.Values.WitSqlValue.FromInt(999),
                OutWit.Database.Values.WitSqlValue.FromText("TransactionTest")
            },
            new string[] { "Id", "Name" }
        );
        
        // Insert using engine's InsertRow method
        engine.InsertRow("Test", row);
        TestContext.WriteLine($"After InsertRow - CurrentTransaction: {engine.CurrentTransaction?.GetType().Name ?? "null"}");
        
        // Check count within transaction using ExecuteScalar
        var countInTx = engine.ExecuteScalar("SELECT COUNT(*) FROM Test");
        TestContext.WriteLine($"Count within transaction (via ExecuteScalar): {countInTx}");
        
        // Rollback
        engine.Rollback();
        TestContext.WriteLine($"After Rollback - CurrentTransaction: {engine.CurrentTransaction?.GetType().Name ?? "null"}");
        
        // Check count after rollback
        var countAfterRollback = engine.ExecuteScalar("SELECT COUNT(*) FROM Test");
        TestContext.WriteLine($"Count after rollback: {countAfterRollback}");
        
        Assert.That(countAfterRollback.AsInt64(), Is.EqualTo(0L), "Count should be 0 after rollback");
    }

    [Test]
    public void DiagnoseTableScanAfterRollbackTest()
    {
        // This test checks what CreateTableScan returns after rollback
        var engine = m_connection.Engine!;
        
        // Insert and rollback
        using var handle = engine.BeginTransaction();
        engine.Execute("INSERT INTO Test VALUES (1, 'Test')");
        engine.Rollback();
        
        // Now check what the table scan returns
        using var iterator = engine.CreateTableScan("Test");
        iterator.Open();
        
        int rowCount = 0;
        while (iterator.MoveNext())
        {
            TestContext.WriteLine($"Found row: {iterator.Current}");
            rowCount++;
        }
        
        TestContext.WriteLine($"Total rows from CreateTableScan: {rowCount}");
        Assert.That(rowCount, Is.EqualTo(0), "Table scan should return 0 rows after rollback");
    }

    #endregion
}
