using OutWit.Database.Definitions;
using OutWit.Database.Model;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Utils;

/// <summary>
/// Extension methods for table operations.
/// </summary>
public static class TableUtils
{
    /// <summary>
    /// Deserializes a row from binary data according to the table schema.
    /// </summary>
    /// <param name="table">The table definition.</param>
    /// <param name="data">The binary data to deserialize.</param>
    /// <returns>A row containing the deserialized values.</returns>
    public static WitSqlRow DeserializeRow(this DefinitionTable table, ReadOnlySpan<byte> data)
    {
        var reader = new SpanReader(data);

        var columnCount = reader.ReadUInt16();
        var values = new WitSqlValue[table.Columns.Count];
        var names = new string[table.Columns.Count];

        for (int i = 0; i < table.Columns.Count; i++)
        {
            names[i] = table.Columns[i].Name;

            if (i < columnCount)
            {
                var isNull = reader.ReadBool();
                values[i] = isNull ? WitSqlValue.Null : WitTypeConverter.ReadValue(ref reader, table.Columns[i].Type);
            }
            else
            {
                values[i] = WitSqlValue.Null;
            }
        }

        return new WitSqlRow(values, names);
    }
}
