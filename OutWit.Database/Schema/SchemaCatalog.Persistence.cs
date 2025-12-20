using System.Text;
using System.Text.Json;
using OutWit.Common.Json;
using OutWit.Database.Definitions;

namespace OutWit.Database.Schema;

/// <summary>
/// Persistence part of SchemaCatalog.
/// </summary>
public sealed partial class SchemaCatalog
{
    #region Persistence

    private void LoadSchema()
    {
        // Load tables
        var tablesData = m_store.Get(Encoding.UTF8.GetBytes(TABLES_KEY).AsSpan());
        if (tablesData != null)
        {
            var tableList = JsonSerializer.Deserialize<List<DefinitionTable>>(tablesData);
            if (tableList != null)
            {
                foreach (var table in tableList)
                {
                    m_tables[table.Name] = table;
                }
            }
        }

        // Load indexes
        var indexesData = m_store.Get(Encoding.UTF8.GetBytes(INDEXES_KEY).AsSpan());
        if (indexesData != null)
        {
            var indexList = JsonSerializer.Deserialize<List<DefinitionIndex>>(indexesData);
            if (indexList != null)
            {
                foreach (var index in indexList)
                {
                    m_indexes[index.Name] = index;
                }
            }
        }

        // Load max row ID
        var rowIdData = m_store.Get(Encoding.UTF8.GetBytes(ROWID_KEY).AsSpan());
        if (rowIdData != null)
        {
            m_nextRowId = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(rowIdData);
        }

        // Load views
        LoadViews();

        // Load triggers
        LoadTriggers();

        // Load sequences
        LoadSequences();
    }

    private void SaveSchema()
    {
        // Save tables
        var tableList = m_tables.Values.ToList();
        m_store.Put(Encoding.UTF8.GetBytes(TABLES_KEY).AsSpan(), tableList.ToJsonBytes());

        // Save indexes
        var indexList = m_indexes.Values.ToList();
        m_store.Put(Encoding.UTF8.GetBytes(INDEXES_KEY).AsSpan(), indexList.ToJsonBytes());

        // Save max row ID
        var rowIdBytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(rowIdBytes, m_nextRowId);
        m_store.Put(Encoding.UTF8.GetBytes(ROWID_KEY).AsSpan(), rowIdBytes);
    }

    #endregion
}
