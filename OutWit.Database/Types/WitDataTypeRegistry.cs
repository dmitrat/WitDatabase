namespace OutWit.Database.Types;

/// <summary>
/// Maps .NET types to WitDB data types.
/// </summary>
public static class WitDataTypeRegistry
{
    #region Constants

    private static readonly Dictionary<Type, WitDataType> CLR_TO_WIT_MAP = new()
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
        
        // Unique ID
        [typeof(Guid)] = WitDataType.Guid,
        
        // Strings and Binary
        [typeof(string)] = WitDataType.StringVariable,
        [typeof(byte[])] = WitDataType.BinaryVariable,
    };

    private static readonly Dictionary<WitDataType, Type> WIT_TO_CLR_MAP;

    #endregion

    #region Constructors

    static WitDataTypeRegistry()
    {
        WIT_TO_CLR_MAP = new Dictionary<WitDataType, Type>();
        foreach (var (clrType, witDataType) in CLR_TO_WIT_MAP)
        {
            WIT_TO_CLR_MAP.TryAdd(witDataType, clrType);
        }
    }

    #endregion

    /// <summary>
    /// Gets the WitDB data type for a .NET type.
    /// </summary>
    public static WitDataType GetWitDataType(Type clrType)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(clrType);
        if (underlyingType != null)
        {
            clrType = underlyingType;
        }

        // Handle enums - store as underlying type
        if (clrType.IsEnum)
        {
            clrType = Enum.GetUnderlyingType(clrType);
        }

        if (CLR_TO_WIT_MAP.TryGetValue(clrType, out var witDataType))
        {
            return witDataType;
        }

        throw new NotSupportedException($"Type {clrType.FullName} is not supported by WitDB");
    }

    /// <summary>
    /// Gets the WitDB data type for a generic type.
    /// </summary>
    public static WitDataType GetWitDataType<T>()
    {
        return GetWitDataType(typeof(T));
    }

    /// <summary>
    /// Gets the .NET type for a WitDB data type.
    /// </summary>
    public static Type GetClrType(WitDataType witDataType)
    {
        if (WIT_TO_CLR_MAP.TryGetValue(witDataType, out var clrType))
        {
            return clrType;
        }

        return witDataType switch
        {
            WitDataType.Null => typeof(DBNull),
            WitDataType.StringFixed => typeof(string),
            WitDataType.BinaryFixed => typeof(byte[]),
            _ => throw new NotSupportedException($"WitDataType {witDataType} is not supported")
        };
    }

    /// <summary>
    /// Checks if a .NET type is supported.
    /// </summary>
    public static bool IsSupported(Type clrType)
    {
        var underlyingType = Nullable.GetUnderlyingType(clrType);
        if (underlyingType != null)
        {
            clrType = underlyingType;
        }
        
        // Enums are supported (stored as underlying type)
        if (clrType.IsEnum)
        {
            return true;
        }
        
        return CLR_TO_WIT_MAP.ContainsKey(clrType);
    }

    /// <summary>
    /// Checks if a type is an enum.
    /// </summary>
    public static bool IsEnum(Type clrType)
    {
        var underlyingType = Nullable.GetUnderlyingType(clrType);
        if (underlyingType != null)
        {
            clrType = underlyingType;
        }
        return clrType.IsEnum;
    }

    /// <summary>
    /// Gets the underlying type for an enum, or the type itself if not an enum.
    /// </summary>
    public static Type GetEnumUnderlyingType(Type clrType)
    {
        var underlyingType = Nullable.GetUnderlyingType(clrType);
        if (underlyingType != null)
        {
            clrType = underlyingType;
        }
        
        return clrType.IsEnum ? Enum.GetUnderlyingType(clrType) : clrType;
    }

    /// <summary>
    /// Checks if a data type is a fixed-size type.
    /// </summary>
    public static bool IsFixedSize(WitDataType witDataType)
    {
        return witDataType switch
        {
            WitDataType.Null => true,
            WitDataType.Int8 or WitDataType.UInt8 => true,
            WitDataType.Int16 or WitDataType.UInt16 => true,
            WitDataType.Float16 or WitDataType.Float32 or WitDataType.Float64 => true,
            WitDataType.Decimal => true,
            WitDataType.Boolean => true,
            WitDataType.DateOnly or WitDataType.TimeOnly or WitDataType.DateTime or WitDataType.DateTimeOffset
                or WitDataType.TimeSpan => true,
            WitDataType.Guid => true,
            WitDataType.StringFixed or WitDataType.BinaryFixed => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a data type is a variable-size type (VarInt or length-prefixed).
    /// </summary>
    public static bool IsVariableSize(WitDataType witDataType)
    {
        return witDataType switch
        {
            WitDataType.Int32 or WitDataType.UInt32 or WitDataType.Int64 or WitDataType.UInt64 => true,
            WitDataType.StringVariable or WitDataType.BinaryVariable => true,
            _ => false
        };
    }

    /// <summary>
    /// Checks if a data type is nullable.
    /// </summary>
    public static bool IsNullable(Type clrType)
    {
        return Nullable.GetUnderlyingType(clrType) != null || !clrType.IsValueType;
    }
}
