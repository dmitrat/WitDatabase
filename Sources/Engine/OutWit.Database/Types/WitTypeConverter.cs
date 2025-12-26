using OutWit.Database.Model;
using OutWit.Database.Values;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace OutWit.Database.Types;

/// <summary>
/// Provides conversion between WitDataType (storage types) and WitSqlType (runtime types).
/// Centralizes all type mapping logic to ensure consistency across the codebase.
/// </summary>
public static class WitTypeConverter
{
    #region WitDataType -> WitSqlType

    /// <summary>
    /// Converts a storage data type to a runtime SQL type.
    /// </summary>
    /// <param name="me">The storage data type.</param>
    /// <returns>The corresponding runtime SQL type.</returns>
    public static WitSqlType ToSqlType(this WitDataType me)
    {
        return me switch
        {
            WitDataType.Null => WitSqlType.Null,

            // Integers -> Integer
            WitDataType.Int8 or WitDataType.UInt8 or
            WitDataType.Int16 or WitDataType.UInt16 or
            WitDataType.Int32 or WitDataType.UInt32 or
            WitDataType.Int64 or WitDataType.UInt64 => WitSqlType.Integer,

            // Floating point
            WitDataType.Float16 or WitDataType.Float32 or WitDataType.Float64 => WitSqlType.Real,
            WitDataType.Decimal => WitSqlType.Decimal,

            // Boolean
            WitDataType.Boolean => WitSqlType.Boolean,

            // Date/Time
            WitDataType.DateOnly => WitSqlType.DateOnly,
            WitDataType.TimeOnly => WitSqlType.TimeOnly,
            WitDataType.DateTime => WitSqlType.DateTime,
            WitDataType.DateTimeOffset => WitSqlType.DateTimeOffset,
            WitDataType.TimeSpan => WitSqlType.TimeSpan,

            // Identifiers
            WitDataType.Guid => WitSqlType.Guid,

            // Strings
            WitDataType.StringFixed or WitDataType.StringVariable => WitSqlType.Text,

            // Binary
            WitDataType.BinaryFixed or WitDataType.BinaryVariable or WitDataType.RowVersion => WitSqlType.Blob,

            // JSON
            WitDataType.Json => WitSqlType.Json,

            // Default fallback
            _ => WitSqlType.Text
        };
    }

    public static WitDataType ToDataType(this WitSqlType me)
    {
        return me switch
        {
            WitSqlType.Integer => WitDataType.Int64,
            WitSqlType.Real => WitDataType.Float64,
            WitSqlType.Text => WitDataType.StringVariable,
            WitSqlType.Blob => WitDataType.BinaryVariable,
            WitSqlType.Boolean => WitDataType.Boolean,
            WitSqlType.Decimal => WitDataType.Decimal,
            WitSqlType.DateTime => WitDataType.DateTime,
            WitSqlType.DateOnly => WitDataType.DateOnly,
            WitSqlType.TimeOnly => WitDataType.TimeOnly,
            WitSqlType.TimeSpan => WitDataType.TimeSpan,
            WitSqlType.Guid => WitDataType.Guid,
            WitSqlType.Json => WitDataType.Json,
            _ => WitDataType.StringVariable
        };
    }

    #endregion

    #region Read WitSqlValue from SpanReader

    /// <summary>
    /// Reads a WitSqlValue from a SpanReader based on the storage data type.
    /// </summary>
    /// <param name="reader">The span reader positioned at the value data.</param>
    /// <param name="dataType">The storage data type.</param>
    /// <returns>The read SQL value.</returns>
    public static WitSqlValue ReadValue(ref SpanReader reader, WitDataType dataType)
    {
        return dataType switch
        {
            WitDataType.Null => WitSqlValue.Null,

            // Integers
            WitDataType.Int8 => WitSqlValue.FromInt(reader.ReadSByte()),
            WitDataType.UInt8 => WitSqlValue.FromInt(reader.ReadByte()),
            WitDataType.Int16 => WitSqlValue.FromInt(reader.ReadInt16()),
            WitDataType.UInt16 => WitSqlValue.FromInt(reader.ReadUInt16()),
            WitDataType.Int32 => WitSqlValue.FromInt(reader.ReadInt32()),
            WitDataType.UInt32 => WitSqlValue.FromInt((uint)reader.ReadInt32()),
            WitDataType.Int64 => WitSqlValue.FromInt(reader.ReadInt64()),
            WitDataType.UInt64 => WitSqlValue.FromInt(reader.ReadInt64()),

            // Floating point
            WitDataType.Float16 => WitSqlValue.FromReal((double)reader.ReadHalf()),
            WitDataType.Float32 => WitSqlValue.FromReal(reader.ReadSingle()),
            WitDataType.Float64 => WitSqlValue.FromReal(reader.ReadDouble()),
            WitDataType.Decimal => WitSqlValue.FromDecimal(reader.ReadDecimal()),

            // Boolean
            WitDataType.Boolean => WitSqlValue.FromBool(reader.ReadBool()),

            // Date/Time
            WitDataType.DateOnly => WitSqlValue.FromDateOnly(DateOnly.FromDayNumber(reader.ReadInt32())),
            WitDataType.TimeOnly => WitSqlValue.FromTimeOnly(new TimeOnly(reader.ReadInt64())),
            WitDataType.DateTime => WitSqlValue.FromDateTime(new DateTime(reader.ReadInt64())),
            WitDataType.DateTimeOffset => ReadDateTimeOffset(ref reader),
            WitDataType.TimeSpan => WitSqlValue.FromTimeSpan(new TimeSpan(reader.ReadInt64())),

            // Identifiers
            WitDataType.Guid => WitSqlValue.FromGuid(new Guid(reader.ReadBytes(16))),

            // Strings
            WitDataType.StringFixed or WitDataType.StringVariable => WitSqlValue.FromText(reader.ReadString()),

            // Binary
            WitDataType.BinaryFixed or WitDataType.BinaryVariable => WitSqlValue.FromBlob(reader.ReadBlob()),
            WitDataType.RowVersion => WitSqlValue.FromBlob(reader.ReadBytes(8).ToArray()),

            // JSON - stored as string, parsed on demand
            WitDataType.Json => WitSqlValue.FromText(reader.ReadString()),

            // Default fallback
            _ => WitSqlValue.FromText(reader.ReadString())
        };
    }

    private static WitSqlValue ReadDateTimeOffset(ref SpanReader reader)
    {
        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt16();
        return WitSqlValue.FromDateTimeOffset(new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes)));
    }

    #endregion


    #region Write Value

    public static void WriteValue(BinaryWriter writer, WitDataType type, WitSqlValue value)
    {
        switch (type)
        {
            case WitDataType.Int32:
                writer.Write((int)value.AsInt64());
                break;
            case WitDataType.Int64:
                writer.Write(value.AsInt64());
                break;
            case WitDataType.Float64:
                writer.Write(value.AsDouble());
                break;
            case WitDataType.Boolean:
                writer.Write(value.AsBool());
                break;
            case WitDataType.StringVariable:
            case WitDataType.StringFixed:
                var str = value.AsString();
                var strBytes = Encoding.UTF8.GetBytes(str);
                writer.Write(strBytes.Length);
                writer.Write(strBytes);
                break;
            case WitDataType.BinaryVariable:
            case WitDataType.BinaryFixed:
                var blob = value.AsBlob();
                writer.Write(blob.Length);
                writer.Write(blob);
                break;
            case WitDataType.Guid:
                writer.Write(value.AsGuid().ToByteArray());
                break;
            case WitDataType.DateTime:
                writer.Write(value.AsDateTime().Ticks);
                break;
            case WitDataType.DateOnly:
                writer.Write((int)value.AsInt64());
                break;
            case WitDataType.TimeOnly:
                writer.Write(value.AsInt64());
                break;
            case WitDataType.TimeSpan:
                writer.Write(value.AsInt64());
                break;
            case WitDataType.Decimal:
                var bits = decimal.GetBits(value.AsDecimal());
                foreach (var b in bits)
                    writer.Write(b);
                break;
            default:
                // Default to string
                var s = value.AsString();
                var sBytes = Encoding.UTF8.GetBytes(s);
                writer.Write(sBytes.Length);
                writer.Write(sBytes);
                break;
        }
    }

    #endregion
}
