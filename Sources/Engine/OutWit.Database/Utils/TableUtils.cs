using OutWit.Database.Definitions;
using OutWit.Database.Model;
using OutWit.Database.Sql;
using OutWit.Database.Types;
using OutWit.Database.Values;

namespace OutWit.Database.Utils;

/// <summary>
/// Extension methods for table operations.
/// </summary>
public static class TableUtils
{
    #region Deserialization

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

    /// <summary>
    /// Deserializes row values from binary data according to the table schema.
    /// This overload allows reusing column names array for better performance.
    /// </summary>
    /// <param name="table">The table definition.</param>
    /// <param name="data">The binary data to deserialize.</param>
    /// <param name="columnNames">Pre-allocated column names array to reuse.</param>
    /// <returns>Array of deserialized values.</returns>
    public static WitSqlValue[] DeserializeRowValues(this DefinitionTable table, ReadOnlySpan<byte> data, string[] columnNames)
    {
        var reader = new SpanReader(data);

        var columnCount = reader.ReadUInt16();
        var values = new WitSqlValue[table.Columns.Count];

        for (int i = 0; i < table.Columns.Count; i++)
        {
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

        return values;
    }

    #endregion

    #region Serialization

    /// <summary>
    /// Serializes a row to binary data according to the table schema.
    /// </summary>
    public static byte[] SerializeRow(this DefinitionTable me, WitSqlRow row)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8);

        // Write number of columns
        writer.Write((ushort)me.Columns.Count);

        foreach (var col in me.Columns)
        {
            WitSqlValue value = WitSqlValue.Null;
            if (row.TryGetValue(col.Name, out var v))
            {
                value = v;
            }

            // Write null flag
            writer.Write(value.IsNull);

            if (!value.IsNull)
            {
                WitTypeConverter.WriteValue(writer, col.Type, value);
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Serializes values array to binary data according to the table schema.
    /// </summary>
    public static byte[] SerializeValuesArray(this DefinitionTable me, WitSqlValue[] values)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8);

        // Write number of columns
        writer.Write((ushort)values.Length);

        for (int i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var colType = i < me.Columns.Count ? me.Columns[i].Type : WitDataType.StringVariable;

            // Write null flag
            writer.Write(value.IsNull);

            if (!value.IsNull)
            {
                WitTypeConverter.WriteValue(writer, colType, value);
            }
        }

        return ms.ToArray();
    }

    #endregion
}
