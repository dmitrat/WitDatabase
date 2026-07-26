using System.Text;
using OutWit.Database.Core.Builder;
using OutWit.Database.Engine;
using OutWit.Database.Exceptions;

namespace OutWit.Database.Tests.Schema;

/// <summary>
/// Regression tests for schema-record corruption handling.
/// </summary>
/// <remarks>
/// The catalog deserialized its records through a helper that catches everything and returns
/// <c>default</c>, and passed it no logger. So a torn write, a definition shape changed between
/// versions, or a wrong decryption key produced an *empty* catalog instead of an error: every
/// statement then failed with "Table 'X' not found" while the rows sat intact on disk, and the next
/// DDL statement called SaveSchema() and overwrote the record - turning a recoverable file into a
/// permanently lost schema, with no diagnostics anywhere.
/// </remarks>
[TestFixture]
public sealed class SchemaCatalogCorruptionTests
{
    #region Fields

    private const string TABLES_KEY = "$schema:_tables";

    #endregion

    #region Corrupt Record Tests

    [Test]
    public void CorruptTablesRecordThrowsInsteadOfYieldingAnEmptyCatalogTest()
    {
        var database = WitDatabase.CreateInMemory();
        using (var engine = new WitSqlEngine(database, ownsStore: false))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
            engine.Execute("INSERT INTO Users (Id, Name) VALUES (1, 'a')");
        }

        Corrupt(database, TABLES_KEY);

        var exception = Assert.Throws<WitSchemaCorruptException>(() =>
        {
            using var reopened = new WitSqlEngine(database, ownsStore: false);
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception!.RecordName, Is.EqualTo("tables"));
            Assert.That(exception.ByteCount, Is.GreaterThan(0));
            Assert.That(exception.Message, Does.Contain("has NOT been modified"),
                "The message must tell the operator not to run DDL, which would overwrite the record");
        });

        database.Dispose();
    }

    [Test]
    public void CorruptRecordDoesNotPresentAsATableNotFoundErrorTest()
    {
        var database = WitDatabase.CreateInMemory();
        using (var engine = new WitSqlEngine(database, ownsStore: false))
        {
            engine.Execute("CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(50) NOT NULL)");
        }

        Corrupt(database, TABLES_KEY);

        try
        {
            using var reopened = new WitSqlEngine(database, ownsStore: false);
            Assert.Fail("Opening a database with a corrupt schema record must not succeed");
        }
        catch (WitSchemaCorruptException)
        {
            // Expected: named, actionable, and distinguishable from a missing table.
        }
        finally
        {
            database.Dispose();
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Overwrites a schema record with bytes that are not a valid MemoryPack payload, standing in for
    /// a torn write or a definition shape change between library versions.
    /// </summary>
    private static void Corrupt(Core.Builder.WitDatabase store, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var existing = store.Get(keyBytes.AsSpan());

        Assert.That(existing, Is.Not.Null,
            $"Expected a stored schema record under '{key}' - the key layout may have changed");

        store.Put(keyBytes.AsSpan(), new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA });
    }

    #endregion
}
