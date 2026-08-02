using System.Text;
using OutWit.Common.MemoryPack;
using OutWit.Database.Definitions;
using OutWit.Database.Exceptions;

namespace OutWit.Database.Schema;

/// <summary>
/// Persistence part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Persistence

    /// <summary>
    /// Deserializes a stored schema record, failing loudly rather than yielding an empty catalog.
    /// </summary>
    /// <remarks>
    /// <c>FromMemoryPackBytes</c> catches every exception and returns <c>default</c>. Passing no logger
    /// - which every call site here did - made that silent. A record that is present but unreadable is
    /// corruption, not an empty schema, and must not be mistaken for one: the next
    /// <see cref="SaveSchema"/> would overwrite it. See <see cref="WitSchemaCorruptException"/>.
    /// </remarks>
    internal static TRecord ReadSchemaRecord<TRecord>(byte[] data, string recordName)
        where TRecord : class
    {
        var record = data.FromMemoryPackBytes<TRecord>();
        if (record == null)
            throw new WitSchemaCorruptException(recordName, data.Length);

        return record;
    }

    private void LoadSchema()
    {
        // Load tables
        var tablesData = GetSchemaRecord(TABLES_KEY_BYTES.AsSpan());
        if (tablesData != null)
        {
            var tableList = ReadSchemaRecord<List<DefinitionTable>>(tablesData, "tables");
            foreach (var table in tableList)
            {
                m_tables[table.Name] = table;
                LoadTableRowId(table.Name);
                LoadTableRowCount(table.Name);
            }
        }

        // Load indexes
        var indexesData = GetSchemaRecord(INDEXES_KEY_BYTES.AsSpan());
        if (indexesData != null)
        {
            var indexList = ReadSchemaRecord<List<DefinitionIndex>>(indexesData, "indexes");
            foreach (var index in indexList)
            {
                m_indexes[index.Name] = index;
            }
        }

        // Load views
        LoadViews();

        // Load triggers
        LoadTriggers();

        // Load sequences
        LoadSequences();

        // Load global row version counter
        LoadRowVersion();
    }

    /// <summary>
    /// Throws away the whole in-memory catalog and reads it back from the store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called after a rollback. Until schema writes went through the transaction they were
    /// autocommit, so a rolled-back <c>CREATE TABLE</c> left the table and a rolled-back
    /// <c>DROP TABLE</c> lost it for good; now the store no longer has the change, and this is what
    /// makes the dictionaries agree with it again.
    /// </para>
    /// <para>
    /// <c>ReloadMetadataFromStore</c> is deliberately not this: it reloads the row counters only, and
    /// it is called on paths where the schema itself cannot have moved. Reloading everything is the
    /// right answer after a rollback and the wrong one after a savepoint release.
    /// </para>
    /// </remarks>
    public void ReloadFromStore()
    {
        m_lock.EnterWriteLock();
        try
        {
            m_tables.Clear();
            m_indexes.Clear();
            m_views.Clear();
            m_triggers.Clear();
            m_sequences.Clear();
            m_tableRowIds.Clear();
            m_tableRowCounts.Clear();

            LoadSchema();
        }
        finally
        {
            m_lock.ExitWriteLock();
        }
    }

    private void SaveSchema()
    {
        // Save tables
        var tableList = m_tables.Values.ToList();
        PutSchemaRecord(TABLES_KEY_BYTES.AsSpan(), tableList.ToMemoryPackBytes());

        // Save indexes
        var indexList = m_indexes.Values.ToList();
        PutSchemaRecord(INDEXES_KEY_BYTES.AsSpan(), indexList.ToMemoryPackBytes());
    }

    #endregion
}
