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
        var tablesData = m_store.Get(TABLES_KEY_BYTES.AsSpan());
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
        var indexesData = m_store.Get(INDEXES_KEY_BYTES.AsSpan());
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

    private void SaveSchema()
    {
        // Save tables
        var tableList = m_tables.Values.ToList();
        m_store.Put(TABLES_KEY_BYTES.AsSpan(), tableList.ToMemoryPackBytes());

        // Save indexes
        var indexList = m_indexes.Values.ToList();
        m_store.Put(INDEXES_KEY_BYTES.AsSpan(), indexList.ToMemoryPackBytes());
    }

    #endregion
}
