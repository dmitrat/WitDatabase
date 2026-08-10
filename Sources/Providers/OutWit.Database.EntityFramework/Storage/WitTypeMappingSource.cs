using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Storage;

/// <summary>
/// The type mapping source for WitDatabase, mapping CLR types to WitSQL types.
/// </summary>
public sealed class WitTypeMappingSource : RelationalTypeMappingSource
{
    #region Constants

    // Integer types (signed)
    private const string TYPE_TINYINT = "TINYINT";
    private const string TYPE_SMALLINT = "SMALLINT";
    private const string TYPE_INT = "INT";
    private const string TYPE_BIGINT = "BIGINT";
    
    // Integer types (unsigned)
    private const string TYPE_UTINYINT = "UTINYINT";
    private const string TYPE_USMALLINT = "USMALLINT";
    private const string TYPE_UINT = "UINT";
    private const string TYPE_UBIGINT = "UBIGINT";

    // Floating-point types
    private const string TYPE_FLOAT = "FLOAT";
    private const string TYPE_DOUBLE = "DOUBLE";
    private const string TYPE_DECIMAL = "DECIMAL";

    // Boolean
    private const string TYPE_BOOLEAN = "BOOLEAN";

    // Date/Time types
    private const string TYPE_DATE = "DATE";
    private const string TYPE_TIME = "TIME";
    private const string TYPE_DATETIME = "DATETIME";
    private const string TYPE_DATETIMEOFFSET = "DATETIMEOFFSET";
    private const string TYPE_INTERVAL = "INTERVAL";

    // String types
    private const string TYPE_TEXT = "TEXT";
    private const string TYPE_JSON = "JSON";

    // Binary types
    private const string TYPE_BLOB = "BLOB";

    // Other types
    private const string TYPE_GUID = "GUID";

    #endregion

    #region Fields

    // Signed integer mappings
    private readonly SByteTypeMapping m_sbyteMapping = new(TYPE_TINYINT, DbType.SByte);
    private readonly ShortTypeMapping m_shortMapping = new(TYPE_SMALLINT, DbType.Int16);
    private readonly IntTypeMapping m_intMapping = new(TYPE_INT, DbType.Int32);
    private readonly LongTypeMapping m_longMapping = new(TYPE_BIGINT, DbType.Int64);
    
    // Unsigned integer mappings
    private readonly ByteTypeMapping m_byteMapping = new(TYPE_UTINYINT, DbType.Byte);
    private readonly UShortTypeMapping m_ushortMapping = new(TYPE_USMALLINT, DbType.UInt16);
    private readonly UIntTypeMapping m_uintMapping = new(TYPE_UINT, DbType.UInt32);
    private readonly ULongTypeMapping m_ulongMapping = new(TYPE_UBIGINT, DbType.UInt64);

    // Floating-point mappings
    private readonly FloatTypeMapping m_floatMapping = new(TYPE_FLOAT);
    private readonly DoubleTypeMapping m_doubleMapping = new(TYPE_DOUBLE);
    private readonly DecimalTypeMapping m_decimalMapping = new(TYPE_DECIMAL);

    // Boolean mapping
    private readonly BoolTypeMapping m_boolMapping = new(TYPE_BOOLEAN);

    // Date/Time mappings. They emit WitSQL's TYPED literals - DATE '...', TIME '...',
    // TIMESTAMP '...', DATETIMEOFFSET '...' - see WitTemporalTypeMappings. EF Core's own mappings
    // are close but not the same: theirs write a DateTimeOffset as a TIMESTAMP with an offset
    // inside, which this engine refuses by name rather than silently dropping the offset.
    private readonly DateOnlyTypeMapping m_dateOnlyMapping = new WitDateOnlyTypeMapping(TYPE_DATE);
    private readonly TimeOnlyTypeMapping m_timeOnlyMapping = new WitTimeOnlyTypeMapping(TYPE_TIME);
    private readonly DateTimeTypeMapping m_dateTimeMapping = new WitDateTimeTypeMapping(TYPE_DATETIME);
    private readonly DateTimeOffsetTypeMapping m_dateTimeOffsetMapping = new WitDateTimeOffsetTypeMapping(TYPE_DATETIMEOFFSET);
    private readonly TimeSpanTypeMapping m_timeSpanMapping = new(TYPE_INTERVAL);

    // String mapping
    private readonly StringTypeMapping m_textMapping = new(TYPE_TEXT, DbType.String);
    private readonly StringTypeMapping m_jsonMapping = new(TYPE_JSON, DbType.String);

    // A char was mapped to the string mapping, whose converter cannot take one: any SaveChanges on
    // an entity with a char property failed with "No coercion operator is defined between types
    // 'System.Char' and 'System.String'". The property was unusable outright, not merely in
    // inlined constants.
    private readonly CharTypeMapping m_charMapping = new(TYPE_TEXT, DbType.String);

    // Binary mapping
    private readonly ByteArrayTypeMapping m_blobMapping = new(TYPE_BLOB, DbType.Binary);

    // GUID mapping
    private readonly GuidTypeMapping m_guidMapping = new(TYPE_GUID);

    // Mappings by CLR type
    private readonly Dictionary<Type, RelationalTypeMapping> m_clrTypeMappings;

    // Mappings by store type
    private readonly Dictionary<string, RelationalTypeMapping> m_storeTypeMappings;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="WitTypeMappingSource"/> class.
    /// </summary>
    /// <param name="dependencies">The type mapping source dependencies.</param>
    /// <param name="relationalDependencies">The relational type mapping source dependencies.</param>
    public WitTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
        m_clrTypeMappings = new Dictionary<Type, RelationalTypeMapping>
        {
            // Signed integers
            { typeof(sbyte), m_sbyteMapping },
            { typeof(short), m_shortMapping },
            { typeof(int), m_intMapping },
            { typeof(long), m_longMapping },
            
            // Unsigned integers
            { typeof(byte), m_byteMapping },
            { typeof(ushort), m_ushortMapping },
            { typeof(uint), m_uintMapping },
            { typeof(ulong), m_ulongMapping },

            // Floating-point
            { typeof(float), m_floatMapping },
            { typeof(double), m_doubleMapping },
            { typeof(decimal), m_decimalMapping },

            // Boolean
            { typeof(bool), m_boolMapping },

            // Date/Time
            { typeof(DateOnly), m_dateOnlyMapping },
            { typeof(TimeOnly), m_timeOnlyMapping },
            { typeof(DateTime), m_dateTimeMapping },
            { typeof(DateTimeOffset), m_dateTimeOffsetMapping },
            { typeof(TimeSpan), m_timeSpanMapping },

            // String
            { typeof(string), m_textMapping },

            // Binary
            { typeof(byte[]), m_blobMapping },

            // GUID
            { typeof(Guid), m_guidMapping },

            // Char
            { typeof(char), m_charMapping }
        };

        m_storeTypeMappings = new Dictionary<string, RelationalTypeMapping>(StringComparer.OrdinalIgnoreCase)
        {
            // Signed integer types
            { TYPE_TINYINT, m_sbyteMapping },
            { "INT8", m_sbyteMapping },
            { TYPE_SMALLINT, m_shortMapping },
            { "INT16", m_shortMapping },
            { TYPE_INT, m_intMapping },
            { "INT32", m_intMapping },
            { "INTEGER", m_intMapping },
            { TYPE_BIGINT, m_longMapping },
            { "INT64", m_longMapping },
            { "LONG", m_longMapping },
            
            // Unsigned integer types
            { TYPE_UTINYINT, m_byteMapping },
            { "UINT8", m_byteMapping },
            { TYPE_USMALLINT, m_ushortMapping },
            { "UINT16", m_ushortMapping },
            { TYPE_UINT, m_uintMapping },
            { "UINT32", m_uintMapping },
            { TYPE_UBIGINT, m_ulongMapping },
            { "UINT64", m_ulongMapping },
            { "ULONG", m_ulongMapping },

            // Floating-point types
            { TYPE_FLOAT, m_floatMapping },
            { "FLOAT32", m_floatMapping },
            { "REAL", m_floatMapping },
            { TYPE_DOUBLE, m_doubleMapping },
            { "FLOAT64", m_doubleMapping },
            { TYPE_DECIMAL, m_decimalMapping },
            { "NUMERIC", m_decimalMapping },
            { "MONEY", m_decimalMapping },

            // Boolean
            { TYPE_BOOLEAN, m_boolMapping },
            { "BOOL", m_boolMapping },

            // Date/Time
            { TYPE_DATE, m_dateOnlyMapping },
            { "DATEONLY", m_dateOnlyMapping },
            { TYPE_TIME, m_timeOnlyMapping },
            { "TIMEONLY", m_timeOnlyMapping },
            { TYPE_DATETIME, m_dateTimeMapping },
            { "TIMESTAMP", m_dateTimeMapping },
            { TYPE_DATETIMEOFFSET, m_dateTimeOffsetMapping },
            { TYPE_INTERVAL, m_timeSpanMapping },
            { "TIMESPAN", m_timeSpanMapping },

            // String
            { TYPE_TEXT, m_textMapping },
            { "VARCHAR", m_textMapping },
            { "NVARCHAR", m_textMapping },
            { "CHAR", m_textMapping },
            { "NCHAR", m_textMapping },
            { "NTEXT", m_textMapping },
            
            // JSON
            { TYPE_JSON, m_jsonMapping },
            { "JSONB", m_jsonMapping },

            // Binary
            { TYPE_BLOB, m_blobMapping },
            { "BINARY", m_blobMapping },
            { "VARBINARY", m_blobMapping },

            // GUID
            { TYPE_GUID, m_guidMapping },
            { "UUID", m_guidMapping },
            { "UNIQUEIDENTIFIER", m_guidMapping }
        };
    }

    #endregion

    #region Type Mapping

    /// <inheritdoc/>
    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;
        var storeTypeName = mappingInfo.StoreTypeName;

        // First try exact store type match
        if (!string.IsNullOrEmpty(storeTypeName))
        {
            var baseTypeName = GetBaseTypeName(storeTypeName);
            if (m_storeTypeMappings.TryGetValue(baseTypeName, out var storeMapping))
            {
                // A NAME that carries its size must answer with that size, not with the unsized
                // mapping the base name happens to share.
                //
                // `VARCHAR(100)` used to resolve here to the plain TEXT mapping, because the size was
                // cut off to look the name up and then thrown away - so the same column described two
                // ways answered two different store types: `HasMaxLength(100)` gave VARCHAR(100) and
                // `HasColumnType("VARCHAR(100)")` gave TEXT. A model snapshot writes BOTH, and EF's
                // differ compares the resolved types, so every sized column looked altered on the
                // next `migrations add` - one spurious AlterColumn per column, in both directions,
                // and the Down() half narrowed the column to TEXT. Reported from WitAnalytics
                // against 12.3.0, reproduced on 12.5.0 by generating two migrations.
                //
                // DECIMAL(p,s) and VARBINARY(n) had the identical fault; the report named only
                // VARCHAR.
                return FindMappingForStoreTypeName(storeTypeName, baseTypeName) ?? storeMapping;
            }
        }

        // Then try CLR type match
        if (clrType != null)
        {
            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(clrType) ?? clrType;

            // The size the model declared, kept. Returning the plain mapping here dropped it: a property
            // with HasMaxLength(5) got the same TEXT mapping as any other string, so ColumnType was
            // "TEXT" by the time the migrations generator saw it, and the generator's own size handling
            // never got a chance to run. That is the deepest of the places this size was being lost.
            var sized = FindSizedMapping(underlyingType, mappingInfo);
            if (sized != null)
                return sized;

            if (m_clrTypeMappings.TryGetValue(underlyingType, out var clrMapping))
            {
                return clrMapping;
            }

            // Handle enums as integers
            if (underlyingType.IsEnum)
            {
                return m_intMapping;
            }
        }

        // Fall back to base implementation
        return base.FindMapping(mappingInfo);
    }

    /// <summary>
    /// The mapping for a type whose declared size changes its store type - a length for text and binary,
    /// a precision and scale for decimals.
    /// </summary>
    /// <remarks>
    /// Returns null when the model declared no size, which is the ordinary case and leaves the plain
    /// mapping in charge.
    /// </remarks>
    private RelationalTypeMapping? FindSizedMapping(Type underlyingType, in RelationalTypeMappingInfo mappingInfo)
    {
        if (underlyingType == typeof(string) && mappingInfo.Size is > 0)
        {
            var size = mappingInfo.Size.Value;

            return new StringTypeMapping(
                $"VARCHAR({size.ToString(CultureInfo.InvariantCulture)})",
                DbType.String,
                unicode: false,
                size: size);
        }

        if (underlyingType == typeof(byte[]) && mappingInfo.Size is > 0)
        {
            var size = mappingInfo.Size.Value;

            return new ByteArrayTypeMapping(
                $"VARBINARY({size.ToString(CultureInfo.InvariantCulture)})",
                DbType.Binary,
                size: size);
        }

        if (underlyingType == typeof(decimal) && mappingInfo.Precision is > 0)
        {
            var precision = mappingInfo.Precision.Value;
            var scale = mappingInfo.Scale;

            var storeType = scale is > 0
                ? $"DECIMAL({precision.ToString(CultureInfo.InvariantCulture)},{scale.Value.ToString(CultureInfo.InvariantCulture)})"
                : $"DECIMAL({precision.ToString(CultureInfo.InvariantCulture)})";

            return new DecimalTypeMapping(storeType, DbType.Decimal, precision, scale);
        }

        return null;
    }

    /// <summary>
    /// The mapping for a store type NAME that carries its own facets - <c>VARCHAR(100)</c>,
    /// <c>VARBINARY(16)</c>, <c>DECIMAL(18,2)</c> - built the same way the CLR path builds it, so
    /// that a column described by its length and the same column described by its type name resolve
    /// to one mapping rather than two.
    /// </summary>
    /// <remarks>
    /// Returns null when the name carries no facets, or carries ones this type has no use for, which
    /// leaves the plain mapping in charge - the ordinary case.
    /// </remarks>
    private RelationalTypeMapping? FindMappingForStoreTypeName(string storeTypeName, string baseTypeName)
    {
        var facets = GetFacets(storeTypeName);

        if (facets.Length == 0)
            return null;

        return baseTypeName.ToUpperInvariant() switch
        {
            "VARCHAR" or "NVARCHAR" or "CHAR" or "NCHAR" =>
                new StringTypeMapping(storeTypeName, DbType.String, unicode: false, size: facets[0]),

            "VARBINARY" or "BINARY" =>
                new ByteArrayTypeMapping(storeTypeName, DbType.Binary, size: facets[0]),

            TYPE_DECIMAL or "NUMERIC" or "MONEY" =>
                new DecimalTypeMapping(
                    storeTypeName, DbType.Decimal, facets[0], facets.Length > 1 ? facets[1] : null),

            _ => null
        };
    }

    /// <summary>
    /// The numbers inside a store type name's parentheses, or an empty array when there are none or
    /// any of them is not a number.
    /// </summary>
    private static int[] GetFacets(string storeTypeName)
    {
        var open = storeTypeName.IndexOf('(');
        var close = storeTypeName.LastIndexOf(')');

        if (open < 0 || close < open)
            return [];

        var parts = storeTypeName[(open + 1)..close].Split(',');
        var facets = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            // A name this method cannot read - MAX, a blank, anything else - is not a failure: the
            // caller falls back to the plain mapping, which is what happened before this existed.
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var facet)
                || facet <= 0)
            {
                return [];
            }

            facets[i] = facet;
        }

        return facets;
    }

    private static string GetBaseTypeName(string storeTypeName)
    {
        var parenIndex = storeTypeName.IndexOf('(');
        return parenIndex > 0 ? storeTypeName[..parenIndex].Trim() : storeTypeName.Trim();
    }

    #endregion
}
