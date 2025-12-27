using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OutWit.Database.Model;
using OutWit.Database.Values;

namespace OutWit.Database.Types;

/// <summary>
/// Centralized type conversion and metadata for WitDB types.
/// Handles all conversions between WitDataType (storage), WitSqlType (runtime), and CLR types.
/// </summary>
/// <remarks>
/// This is the single source of truth for type-related operations.
/// When adding new types, update this class and the corresponding tests.
/// </remarks>
public static class WitTypeConverter
{
    #region WitDataType <-> WitSqlType

    /// <summary>
    /// Maps a storage data type to a SQL type for expression evaluation.
    /// </summary>
    public static WitSqlType ToSqlType(this WitDataType me) => me switch
    {
        // Integers
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
        WitDataType.BinaryFixed or WitDataType.BinaryVariable => WitSqlType.Blob,

        // RowVersion
        WitDataType.RowVersion => WitSqlType.RowVersion,

        // JSON
        WitDataType.Json => WitSqlType.Json,

        _ => WitSqlType.Null
    };

    /// <summary>
    /// Converts a runtime SQL type to a default storage data type.
    /// </summary>
    public static WitDataType ToDataType(this WitSqlType me)
    {
        return me switch
        {
            WitSqlType.Null => WitDataType.Null,
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
            WitSqlType.DateTimeOffset => WitDataType.DateTimeOffset,
            WitSqlType.Json => WitDataType.Json,
            WitSqlType.RowVersion => WitDataType.RowVersion,
            _ => WitDataType.StringVariable
        };
    }

    #endregion

    #region CLR Type <-> WitDataType

    private static readonly Dictionary<Type, WitDataType> s_clrToDataType = new()
    {
        // Integers
        [typeof(sbyte)] = WitDataType.Int8,
        [typeof(byte)] = WitDataType.UInt8,
        [typeof(short)] = WitDataType.Int16,
        [typeof(ushort)] = WitDataType.UInt16,
        [typeof(int)] = WitDataType.Int32,
        [typeof(uint)] = WitDataType.UInt32,
        [typeof(long)] = WitDataType.Int64,
        [typeof(ulong)] = WitDataType.UInt64,

        // Floating point
        [typeof(Half)] = WitDataType.Float16,
        [typeof(float)] = WitDataType.Float32,
        [typeof(double)] = WitDataType.Float64,
        [typeof(decimal)] = WitDataType.Decimal,

        // Boolean
        [typeof(bool)] = WitDataType.Boolean,

        // Date/Time
        [typeof(DateOnly)] = WitDataType.DateOnly,
        [typeof(TimeOnly)] = WitDataType.TimeOnly,
        [typeof(DateTime)] = WitDataType.DateTime,
        [typeof(DateTimeOffset)] = WitDataType.DateTimeOffset,
        [typeof(TimeSpan)] = WitDataType.TimeSpan,

        // Identifiers
        [typeof(Guid)] = WitDataType.Guid,

        // Strings and Binary
        [typeof(string)] = WitDataType.StringVariable,
        [typeof(byte[])] = WitDataType.BinaryVariable,

        // JSON
        [typeof(JsonDocument)] = WitDataType.Json,
        [typeof(JsonElement)] = WitDataType.Json,
    };

    private static readonly Dictionary<WitDataType, Type> s_dataTypeToClr;

    static WitTypeConverter()
    {
        s_dataTypeToClr = new Dictionary<WitDataType, Type>();
        foreach (var (clrType, witDataType) in s_clrToDataType)
        {
            s_dataTypeToClr.TryAdd(witDataType, clrType);
        }
        s_dataTypeToClr[WitDataType.Null] = typeof(DBNull);
        s_dataTypeToClr[WitDataType.StringFixed] = typeof(string);
        s_dataTypeToClr[WitDataType.BinaryFixed] = typeof(byte[]);
        s_dataTypeToClr[WitDataType.RowVersion] = typeof(ulong);
    }

    /// <summary>
    /// Gets the WitDataType for a CLR type.
    /// </summary>
    public static WitDataType GetDataType(Type clrType)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (underlying.IsEnum)
            underlying = Enum.GetUnderlyingType(underlying);

        return s_clrToDataType.TryGetValue(underlying, out var result)
            ? result
            : throw new NotSupportedException($"Type {clrType.FullName} is not supported");
    }

    /// <summary>
    /// Gets the WitDataType for a CLR type.
    /// </summary>
    public static WitDataType GetDataType<T>() => GetDataType(typeof(T));

    /// <summary>
    /// Gets the CLR type for a WitDataType.
    /// </summary>
    public static Type GetClrType(WitDataType dataType)
    {
        return s_dataTypeToClr.TryGetValue(dataType, out var result)
            ? result
            : typeof(object);
    }

    /// <summary>
    /// Checks if a CLR type is supported.
    /// </summary>
    public static bool IsSupported(Type clrType)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        return underlying.IsEnum || s_clrToDataType.ContainsKey(underlying);
    }

    #endregion

    #region CLR Type <-> WitSqlType

    /// <summary>
    /// Gets the WitSqlType for a CLR type.
    /// </summary>
    public static WitSqlType GetSqlType(Type clrType)
    {
        if (clrType == typeof(DBNull) || clrType == typeof(void))
            return WitSqlType.Null;

        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;

        return underlying switch
        {
            _ when underlying == typeof(bool) => WitSqlType.Boolean,
            _ when underlying == typeof(sbyte) => WitSqlType.Integer,
            _ when underlying == typeof(byte) => WitSqlType.Integer,
            _ when underlying == typeof(short) => WitSqlType.Integer,
            _ when underlying == typeof(ushort) => WitSqlType.Integer,
            _ when underlying == typeof(int) => WitSqlType.Integer,
            _ when underlying == typeof(uint) => WitSqlType.Integer,
            _ when underlying == typeof(long) => WitSqlType.Integer,
            _ when underlying == typeof(ulong) => WitSqlType.Integer,
            _ when underlying == typeof(Half) => WitSqlType.Real,
            _ when underlying == typeof(float) => WitSqlType.Real,
            _ when underlying == typeof(double) => WitSqlType.Real,
            _ when underlying == typeof(decimal) => WitSqlType.Decimal,
            _ when underlying == typeof(string) => WitSqlType.Text,
            _ when underlying == typeof(byte[]) => WitSqlType.Blob,
            _ when underlying == typeof(DateTime) => WitSqlType.DateTime,
            _ when underlying == typeof(DateOnly) => WitSqlType.DateOnly,
            _ when underlying == typeof(TimeOnly) => WitSqlType.TimeOnly,
            _ when underlying == typeof(TimeSpan) => WitSqlType.TimeSpan,
            _ when underlying == typeof(Guid) => WitSqlType.Guid,
            _ when underlying == typeof(DateTimeOffset) => WitSqlType.DateTimeOffset,
            _ when underlying == typeof(JsonDocument) => WitSqlType.Json,
            _ when underlying == typeof(JsonElement) => WitSqlType.Json,
            _ => WitSqlType.Text
        };
    }

    /// <summary>
    /// Gets the CLR type for a WitSqlType.
    /// </summary>
    public static Type GetClrType(WitSqlType sqlType) => sqlType switch
    {
        WitSqlType.Null => typeof(DBNull),
        WitSqlType.Integer => typeof(long),
        WitSqlType.Real => typeof(double),
        WitSqlType.Text => typeof(string),
        WitSqlType.Blob => typeof(byte[]),
        WitSqlType.Boolean => typeof(bool),
        WitSqlType.Decimal => typeof(decimal),
        WitSqlType.DateTime => typeof(DateTime),
        WitSqlType.DateOnly => typeof(DateOnly),
        WitSqlType.TimeOnly => typeof(TimeOnly),
        WitSqlType.TimeSpan => typeof(TimeSpan),
        WitSqlType.Guid => typeof(Guid),
        WitSqlType.DateTimeOffset => typeof(DateTimeOffset),
        WitSqlType.Json => typeof(JsonDocument),
        WitSqlType.RowVersion => typeof(ulong),
        _ => typeof(object)
    };

    #endregion

    #region SQL Type Name Parsing

    /// <summary>
    /// Parses a SQL type name string to WitSqlType.
    /// </summary>
    public static WitSqlType ParseSqlTypeName(string typeName)
    {
        return typeName.ToUpperInvariant() switch
        {
            // Integer types
            "TINYINT" or "INT8" or "UTINYINT" or "UINT8" or "BYTE" => WitSqlType.Integer,
            "SMALLINT" or "INT16" or "SHORT" or "USMALLINT" or "UINT16" or "USHORT" => WitSqlType.Integer,
            "INT" or "INT32" or "INTEGER" or "UINT" or "UINT32" => WitSqlType.Integer,
            "BIGINT" or "INT64" or "LONG" or "UBIGINT" or "UINT64" or "ULONG" => WitSqlType.Integer,

            // Real types
            "FLOAT16" or "HALF" => WitSqlType.Real,
            "FLOAT" or "FLOAT32" or "REAL" => WitSqlType.Real,
            "DOUBLE" or "FLOAT64" => WitSqlType.Real,

            // Decimal
            "DECIMAL" or "NUMERIC" or "MONEY" => WitSqlType.Decimal,

            // Boolean
            "BOOLEAN" or "BOOL" or "BIT" => WitSqlType.Boolean,

            // Date/Time
            "DATE" or "DATEONLY" => WitSqlType.DateOnly,
            "TIME" or "TIMEONLY" => WitSqlType.TimeOnly,
            "DATETIME" or "TIMESTAMP" or "DATETIME2" => WitSqlType.DateTime,
            "DATETIMEOFFSET" => WitSqlType.DateTimeOffset,
            "TIMESPAN" or "DURATION" or "INTERVAL" => WitSqlType.TimeSpan,

            // Identifiers
            "GUID" or "UUID" or "UNIQUEIDENTIFIER" => WitSqlType.Guid,

            // Strings
            "CHAR" or "NCHAR" or "VARCHAR" or "NVARCHAR" or "TEXT" or "NTEXT" or "STRING" => WitSqlType.Text,

            // Binary
            "BINARY" or "VARBINARY" or "BLOB" => WitSqlType.Blob,

            // JSON
            "JSON" or "JSONB" => WitSqlType.Json,

            _ => WitSqlType.Text
        };
    }

    /// <summary>
    /// Gets the SQL type name for display purposes.
    /// </summary>
    public static string GetSqlTypeName(this WitSqlType type) => type switch
    {
        WitSqlType.Null => "NULL",
        WitSqlType.Integer => "INTEGER",
        WitSqlType.Real => "REAL",
        WitSqlType.Text => "TEXT",
        WitSqlType.Blob => "BLOB",
        WitSqlType.Boolean => "BOOLEAN",
        WitSqlType.Decimal => "DECIMAL",
        WitSqlType.DateTime => "DATETIME",
        WitSqlType.DateOnly => "DATE",
        WitSqlType.TimeOnly => "TIME",
        WitSqlType.TimeSpan => "INTERVAL",
        WitSqlType.Guid => "GUID",
        WitSqlType.DateTimeOffset => "DATETIMEOFFSET",
        WitSqlType.Json => "JSON",
        WitSqlType.RowVersion => "ROWVERSION",
        _ => "UNKNOWN"
    };

    #endregion

    #region Value Conversion

    /// <summary>
    /// Converts a WitSqlValue to the specified target WitSqlType.
    /// This is the central conversion method.
    /// </summary>
    public static WitSqlValue Convert(WitSqlValue value, WitSqlType targetType)
    {
        if (value.IsNull)
            return WitSqlValue.Null;

        if (value.Type == targetType)
            return value;

        return targetType switch
        {
            WitSqlType.Null => WitSqlValue.Null,
            WitSqlType.Integer => WitSqlValue.FromInt(value.AsInt64()),
            WitSqlType.Real => WitSqlValue.FromReal(value.AsDouble()),
            WitSqlType.Text => WitSqlValue.FromText(value.AsString()),
            WitSqlType.Blob => WitSqlValue.FromBlob(value.AsBlob()),
            WitSqlType.Boolean => WitSqlValue.FromBool(value.AsBool()),
            WitSqlType.Decimal => WitSqlValue.FromDecimal(value.AsDecimal()),
            WitSqlType.DateTime => WitSqlValue.FromDateTime(value.AsDateTime()),
            WitSqlType.DateOnly => WitSqlValue.FromDateOnly(value.AsDateOnly()),
            WitSqlType.TimeOnly => WitSqlValue.FromTimeOnly(value.AsTimeOnly()),
            WitSqlType.TimeSpan => WitSqlValue.FromTimeSpan(value.AsTimeSpan()),
            WitSqlType.Guid => WitSqlValue.FromGuid(value.AsGuid()),
            WitSqlType.DateTimeOffset => WitSqlValue.FromDateTimeOffset(value.AsDateTimeOffset()),
            WitSqlType.Json => WitSqlValue.FromJson(value.AsJsonElement()),
            _ => throw new InvalidCastException($"Cannot convert to {targetType}")
        };
    }

    /// <summary>
    /// Converts a WitSqlValue to the specified target WitDataType.
    /// </summary>
    public static WitSqlValue Convert(WitSqlValue value, WitDataType targetDataType)
    {
        return Convert(value, targetDataType.ToSqlType());
    }

    /// <summary>
    /// Checks if conversion from source to target type is possible.
    /// </summary>
    public static bool CanConvert(WitSqlType sourceType, WitSqlType targetType)
    {
        if (sourceType == targetType || sourceType == WitSqlType.Null)
            return true;

        return targetType switch
        {
            WitSqlType.Integer => sourceType is WitSqlType.Boolean or WitSqlType.Real or WitSqlType.Text or WitSqlType.Decimal,
            WitSqlType.Real => sourceType is WitSqlType.Integer or WitSqlType.Boolean or WitSqlType.Text or WitSqlType.Decimal,
            WitSqlType.Text => true,
            WitSqlType.Blob => sourceType is WitSqlType.Text or WitSqlType.Guid,
            WitSqlType.Boolean => sourceType is WitSqlType.Integer or WitSqlType.Real or WitSqlType.Text,
            WitSqlType.Decimal => sourceType is WitSqlType.Integer or WitSqlType.Real or WitSqlType.Text,
            WitSqlType.DateTime => sourceType is WitSqlType.Text or WitSqlType.Integer or WitSqlType.DateTimeOffset or WitSqlType.DateOnly,
            WitSqlType.DateOnly => sourceType is WitSqlType.DateTime or WitSqlType.Text or WitSqlType.DateTimeOffset,
            WitSqlType.TimeOnly => sourceType is WitSqlType.DateTime or WitSqlType.TimeSpan or WitSqlType.Text,
            WitSqlType.TimeSpan => sourceType is WitSqlType.TimeOnly or WitSqlType.Integer or WitSqlType.Text,
            WitSqlType.Guid => sourceType is WitSqlType.Text or WitSqlType.Blob,
            WitSqlType.DateTimeOffset => sourceType is WitSqlType.DateTime or WitSqlType.Text,
            WitSqlType.Json => sourceType is WitSqlType.Text,
            WitSqlType.Null => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the default value for a WitSqlType.
    /// </summary>
    public static WitSqlValue GetDefaultValue(WitSqlType type) => type switch
    {
        WitSqlType.Null => WitSqlValue.Null,
        WitSqlType.Integer => WitSqlValue.FromInt(0),
        WitSqlType.Real => WitSqlValue.FromReal(0.0),
        WitSqlType.Text => WitSqlValue.FromText(string.Empty),
        WitSqlType.Blob => WitSqlValue.FromBlob([]),
        WitSqlType.Boolean => WitSqlValue.False,
        WitSqlType.Decimal => WitSqlValue.FromDecimal(0m),
        WitSqlType.DateTime => WitSqlValue.FromDateTime(DateTime.MinValue),
        WitSqlType.DateOnly => WitSqlValue.FromDateOnly(DateOnly.MinValue),
        WitSqlType.TimeOnly => WitSqlValue.FromTimeOnly(TimeOnly.MinValue),
        WitSqlType.TimeSpan => WitSqlValue.FromTimeSpan(TimeSpan.Zero),
        WitSqlType.Guid => WitSqlValue.FromGuid(Guid.Empty),
        WitSqlType.DateTimeOffset => WitSqlValue.FromDateTimeOffset(DateTimeOffset.MinValue),
        WitSqlType.Json => WitSqlValue.Null,
        WitSqlType.RowVersion => WitSqlValue.FromRowVersion(0UL),
        _ => WitSqlValue.Null
    };

    #endregion

    #region Type Categories

    /// <summary>
    /// Checks if the type is numeric (can be used in arithmetic).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNumeric(this WitSqlType type) => type is
        WitSqlType.Integer or WitSqlType.Real or WitSqlType.Decimal or WitSqlType.Boolean;

    /// <summary>
    /// Checks if the type is a date/time type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDateTimeType(this WitSqlType type) => type is
        WitSqlType.DateTime or WitSqlType.DateOnly or WitSqlType.TimeOnly or
        WitSqlType.TimeSpan or WitSqlType.DateTimeOffset;

    /// <summary>
    /// Checks if the type is a string-like type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsStringType(this WitSqlType type) => type is WitSqlType.Text or WitSqlType.Json;

    /// <summary>
    /// Checks if the type stores value in int field (m_intValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UsesIntStorage(this WitSqlType type) => type is
        WitSqlType.Integer or WitSqlType.Boolean or WitSqlType.DateTime or
        WitSqlType.DateOnly or WitSqlType.TimeOnly or WitSqlType.TimeSpan;

    /// <summary>
    /// Checks if the type stores value in real field (m_realValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UsesRealStorage(this WitSqlType type) => type is WitSqlType.Real;

    /// <summary>
    /// Checks if the type stores value in ulong field (m_ulongValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UsesUlongStorage(this WitSqlType type) => type is WitSqlType.RowVersion;

    /// <summary>
    /// Checks if the type stores value in object field (m_objectValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UsesObjectStorage(this WitSqlType type) => type is
        WitSqlType.Text or WitSqlType.Blob or WitSqlType.Decimal or
        WitSqlType.Guid or WitSqlType.DateTimeOffset or WitSqlType.Json;

    /// <summary>
    /// Checks if a WitDataType is fixed-size.
    /// </summary>
    public static bool IsFixedSize(this WitDataType type) => type switch
    {
        WitDataType.Null or WitDataType.Int8 or WitDataType.UInt8 or WitDataType.Boolean => true,
        WitDataType.Int16 or WitDataType.UInt16 or WitDataType.Float16 => true,
        WitDataType.Float32 or WitDataType.DateOnly => true,
        WitDataType.Float64 or WitDataType.DateTime or WitDataType.TimeOnly or WitDataType.TimeSpan => true,
        WitDataType.DateTimeOffset or WitDataType.Decimal or WitDataType.Guid => true,
        WitDataType.StringFixed or WitDataType.BinaryFixed or WitDataType.RowVersion => true,
        _ => false
    };

    /// <summary>
    /// Checks if a WitDataType is variable-size.
    /// </summary>
    public static bool IsVariableSize(this WitDataType type) => type switch
    {
        WitDataType.Int32 or WitDataType.UInt32 or WitDataType.Int64 or WitDataType.UInt64 => true,
        WitDataType.StringVariable or WitDataType.BinaryVariable or WitDataType.Json => true,
        _ => false
    };

    #endregion

    #region Read/Write Values

    /// <summary>
    /// Reads a WitSqlValue from a SpanReader based on the storage data type.
    /// </summary>
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
            
            // RowVersion - stored as 8-byte ulong
            WitDataType.RowVersion => WitSqlValue.FromRowVersion((ulong)reader.ReadInt64()),

            // JSON
            WitDataType.Json => WitSqlValue.FromText(reader.ReadString()),

            _ => WitSqlValue.FromText(reader.ReadString())
        };
    }

    private static WitSqlValue ReadDateTimeOffset(ref SpanReader reader)
    {
        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt16();
        return WitSqlValue.FromDateTimeOffset(new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes)));
    }

    /// <summary>
    /// Writes a WitSqlValue to a BinaryWriter based on the storage data type.
    /// </summary>
    public static void WriteValue(BinaryWriter writer, WitDataType type, WitSqlValue value)
    {
        switch (type)
        {
            case WitDataType.Int8:
                writer.Write((sbyte)value.AsInt64());
                break;
            case WitDataType.UInt8:
                writer.Write((byte)value.AsInt64());
                break;
            case WitDataType.Int16:
                writer.Write((short)value.AsInt64());
                break;
            case WitDataType.UInt16:
                writer.Write((ushort)value.AsInt64());
                break;
            case WitDataType.Int32:
                writer.Write((int)value.AsInt64());
                break;
            case WitDataType.UInt32:
                writer.Write((uint)value.AsInt64());
                break;
            case WitDataType.Int64:
                writer.Write(value.AsInt64());
                break;
            case WitDataType.UInt64:
                writer.Write((ulong)value.AsInt64());
                break;
            case WitDataType.Float16:
                writer.Write((Half)value.AsDouble());
                break;
            case WitDataType.Float32:
                writer.Write((float)value.AsDouble());
                break;
            case WitDataType.Float64:
                writer.Write(value.AsDouble());
                break;
            case WitDataType.Decimal:
                var bits = decimal.GetBits(value.AsDecimal());
                foreach (var b in bits)
                    writer.Write(b);
                break;
            case WitDataType.Boolean:
                writer.Write(value.AsBool());
                break;
            case WitDataType.DateOnly:
                writer.Write(value.AsDateOnly().DayNumber);
                break;
            case WitDataType.TimeOnly:
                writer.Write(value.AsTimeOnly().Ticks);
                break;
            case WitDataType.DateTime:
                writer.Write(value.AsDateTime().Ticks);
                break;
            case WitDataType.DateTimeOffset:
                var dto = value.AsDateTimeOffset();
                writer.Write(dto.Ticks);
                writer.Write((short)dto.Offset.TotalMinutes);
                break;
            case WitDataType.TimeSpan:
                writer.Write(value.AsTimeSpan().Ticks);
                break;
            case WitDataType.Guid:
                writer.Write(value.AsGuid().ToByteArray());
                break;
            case WitDataType.StringVariable:
            case WitDataType.StringFixed:
            case WitDataType.Json:
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
            case WitDataType.RowVersion:
                // RowVersion is stored as 8-byte ulong
                var rvValue = value.AsRowVersion();
                writer.Write(rvValue);
                break;
            default:
                var s = value.AsString();
                var sBytes = Encoding.UTF8.GetBytes(s);
                writer.Write(sBytes.Length);
                writer.Write(sBytes);
                break;
        }
    }

    #endregion

    #region All Types Enumeration

    /// <summary>
    /// All defined WitSqlType values. Update when adding new types!
    /// </summary>
    public static IReadOnlyList<WitSqlType> AllSqlTypes { get; } =
    [
        WitSqlType.Null, WitSqlType.Integer, WitSqlType.Real, WitSqlType.Text,
        WitSqlType.Blob, WitSqlType.Boolean, WitSqlType.Decimal, WitSqlType.DateTime,
        WitSqlType.DateOnly, WitSqlType.TimeOnly, WitSqlType.TimeSpan, WitSqlType.Guid,
        WitSqlType.DateTimeOffset, WitSqlType.Json, WitSqlType.RowVersion
    ];

    /// <summary>
    /// Number of WitSqlType values. Update when adding new types!
    /// </summary>
    public const int SqlTypeCount = 15;

    #endregion

    #region Index Key Serialization

    /// <summary>
    /// Serializes a WitSqlValue for index storage in a sort-order preserving format.
    /// </summary>
    /// <param name="writer">The binary writer to write to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="columnType">The storage data type of the column.</param>
    /// <remarks>
    /// The serialization format ensures that byte comparison yields the same order
    /// as comparing the original values. This is critical for index range scans.
    /// 
    /// Format:
    /// - NULL values: 0x00 (sorts first)
    /// - Non-null values: 0x01 + type-specific encoding
    /// </remarks>
    public static void SerializeValueForIndex(BinaryWriter writer, WitSqlValue value, WitDataType columnType)
    {
        if (value.IsNull)
        {
            // Null values are represented as 0x00 (sorts first)
            writer.Write((byte)0x00);
            return;
        }

        // Non-null values start with 0x01
        writer.Write((byte)0x01);

        // Serialize based on type
        switch (columnType)
        {
            case WitDataType.Int8:
            case WitDataType.Int16:
            case WitDataType.Int32:
            case WitDataType.Int64:
                // For signed integers, flip sign bit for correct ordering
                var longVal = value.AsLong();
                var unsignedVal = (ulong)(longVal ^ long.MinValue);
                writer.Write(BinaryPrimitives.ReverseEndianness(unsignedVal));
                break;

            case WitDataType.UInt8:
            case WitDataType.UInt16:
            case WitDataType.UInt32:
            case WitDataType.UInt64:
                // Unsigned integers in big-endian for correct ordering
                var ulongVal = value.AsULong();
                writer.Write(BinaryPrimitives.ReverseEndianness(ulongVal));
                break;

            case WitDataType.Float16:
            case WitDataType.Float32:
            case WitDataType.Float64:
                // Floats need special encoding for ordering
                SerializeFloatForIndex(writer, value.AsDouble());
                break;

            case WitDataType.Decimal:
                // Decimal is complex - use string representation for now
                SerializeStringForIndex(writer, value.AsDecimal().ToString(CultureInfo.InvariantCulture));
                break;

            case WitDataType.StringFixed:
            case WitDataType.StringVariable:
                SerializeStringForIndex(writer, value.AsString() ?? "");
                break;

            case WitDataType.Boolean:
                writer.Write(value.AsBool() ? (byte)1 : (byte)0);
                break;

            case WitDataType.Guid:
                // GUID as raw bytes (not sort-friendly but works for equality)
                writer.Write(value.AsGuid().ToByteArray());
                break;

            case WitDataType.DateTime:
                // DateTime ticks in big-endian for correct ordering
                writer.Write(BinaryPrimitives.ReverseEndianness(value.AsDateTime().Ticks));
                break;

            case WitDataType.DateTimeOffset:
                var dto = value.AsDateTimeOffset();
                // Use UTC ticks for consistent ordering
                writer.Write(BinaryPrimitives.ReverseEndianness(dto.UtcTicks));
                break;

            case WitDataType.DateOnly:
                // DayNumber in big-endian for correct ordering
                writer.Write(BinaryPrimitives.ReverseEndianness(value.AsDateOnly().DayNumber));
                break;

            case WitDataType.TimeOnly:
                // Ticks in big-endian for correct ordering
                writer.Write(BinaryPrimitives.ReverseEndianness(value.AsTimeOnly().Ticks));
                break;

            case WitDataType.TimeSpan:
                // TimeSpan ticks - flip sign bit like signed integers for negative durations
                var tsTicks = value.AsTimeSpan().Ticks;
                var tsUnsigned = (ulong)(tsTicks ^ long.MinValue);
                writer.Write(BinaryPrimitives.ReverseEndianness(tsUnsigned));
                break;

            case WitDataType.BinaryFixed:
            case WitDataType.BinaryVariable:
                SerializeBlobForIndex(writer, value.AsBlob() ?? []);
                break;

            case WitDataType.Json:
                // JSON as string for indexing (not ideal but works)
                SerializeStringForIndex(writer, value.AsString() ?? "");
                break;

            case WitDataType.RowVersion:
                // RowVersion as 8-byte unsigned big-endian
                var rvValue = value.AsRowVersion();
                writer.Write(BinaryPrimitives.ReverseEndianness(rvValue));
                break;

            default:
                // Fallback: serialize as string
                SerializeStringForIndex(writer, value.ToString() ?? "");
                break;
        }
    }

    /// <summary>
    /// Serializes a double value for index storage with proper ordering.
    /// </summary>
    /// <remarks>
    /// IEEE 754 doubles don't sort correctly when compared as bytes.
    /// This encoding flips bits to ensure correct byte ordering:
    /// - Positive numbers (including +0): flip sign bit only
    /// - Negative numbers: flip all bits
    /// This ensures: -? &lt; -1 &lt; -0 &lt; +0 &lt; 1 &lt; +?
    /// </remarks>
    private static void SerializeFloatForIndex(BinaryWriter writer, double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        
        // Check if negative (sign bit is set)
        if (bits < 0)
        {
            // Negative: flip all bits (XOR with all 1s)
            bits = ~bits;
        }
        else
        {
            // Positive (including +0): flip sign bit only to make it sort after negative
            bits ^= long.MinValue;
        }
        
        writer.Write(BinaryPrimitives.ReverseEndianness(bits));
    }

    /// <summary>
    /// Serializes a string value for index storage in sort-order preserving format.
    /// Uses raw UTF-8 bytes without length prefix to preserve lexicographic ordering.
    /// </summary>
    private static void SerializeStringForIndex(BinaryWriter writer, string value)
    {
        // Write raw UTF-8 bytes without length prefix to preserve lexicographic order
        // UTF-8 naturally preserves Unicode code point order for ASCII characters
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes);
    }

    /// <summary>
    /// Serializes a blob value for index storage.
    /// Uses raw bytes without length prefix to preserve lexicographic ordering.
    /// </summary>
    private static void SerializeBlobForIndex(BinaryWriter writer, byte[] value)
    {
        // Write raw bytes without length prefix
        writer.Write(value);
    }

    /// <summary>
    /// Serializes multiple key values to a byte array for index lookup.
    /// </summary>
    /// <param name="keyValues">The values to serialize.</param>
    /// <param name="columnTypes">The storage types for each column.</param>
    /// <returns>The serialized index key.</returns>
    public static byte[] SerializeIndexKey(WitSqlValue[] keyValues, WitDataType[] columnTypes)
    {
        if (keyValues.Length > columnTypes.Length)
            throw new ArgumentException($"Too many key values: expected at most {columnTypes.Length}, got {keyValues.Length}");

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        for (int i = 0; i < keyValues.Length; i++)
        {
            SerializeValueForIndex(writer, keyValues[i], columnTypes[i]);
        }

        return ms.ToArray();
    }

    #endregion
}
