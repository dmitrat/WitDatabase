using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage;

namespace OutWit.Database.EntityFramework.Storage;

/// <summary>
/// Temporal literals in the form WitSQL can read.
///
/// EF Core's own temporal mappings emit ANSI typed literals - <c>TIMESTAMP '1970-01-01 …'</c>,
/// <c>DATE '…'</c>, <c>TIME '…'</c> - and the WitSQL grammar has no such form, so any query that
/// compared against a constant date failed to parse before it ever reached the engine
/// (<c>no viable alternative at input '&gt;TIMESTAMP'</c>). A plain quoted string is what the
/// grammar accepts and what EF Core's SQLite provider emits for the same values.
/// </summary>
internal static class WitTemporalLiteral
{
    /// <summary>
    /// Round-trip format, so a literal read back yields the value that was written.
    /// </summary>
    public const string DATETIME_FORMAT = "yyyy-MM-dd HH:mm:ss.fffffff";

    /// <summary>
    /// Quotes a formatted temporal value as a plain string literal.
    /// </summary>
    /// <param name="text">The formatted value.</param>
    /// <returns>The value as a SQL string literal.</returns>
    public static string Quote(string text) => $"'{text}'";
}

/// <inheritdoc />
public sealed class WitDateTimeTypeMapping : DateTimeTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateTimeTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitDateTimeTypeMapping(string storeType)
        : base(storeType, System.Data.DbType.DateTime)
    {
    }

    private WitDateTimeTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitDateTimeTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Quote(
            ((DateTime)value).ToString(WitTemporalLiteral.DATETIME_FORMAT, CultureInfo.InvariantCulture));
}

/// <inheritdoc />
public sealed class WitDateTimeOffsetTypeMapping : DateTimeOffsetTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateTimeOffsetTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitDateTimeOffsetTypeMapping(string storeType)
        : base(storeType)
    {
    }

    private WitDateTimeOffsetTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitDateTimeOffsetTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Quote(
            ((DateTimeOffset)value).ToString(
                WitTemporalLiteral.DATETIME_FORMAT + "zzz", CultureInfo.InvariantCulture));
}

/// <inheritdoc />
public sealed class WitDateOnlyTypeMapping : DateOnlyTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitDateOnlyTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitDateOnlyTypeMapping(string storeType)
        : base(storeType)
    {
    }

    private WitDateOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitDateOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Quote(((DateOnly)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}

/// <inheritdoc />
public sealed class WitTimeOnlyTypeMapping : TimeOnlyTypeMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WitTimeOnlyTypeMapping"/> class.
    /// </summary>
    /// <param name="storeType">The store type name.</param>
    public WitTimeOnlyTypeMapping(string storeType)
        : base(storeType)
    {
    }

    private WitTimeOnlyTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <inheritdoc />
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new WitTimeOnlyTypeMapping(parameters);

    /// <inheritdoc />
    protected override string GenerateNonNullSqlLiteral(object value)
        => WitTemporalLiteral.Quote(((TimeOnly)value).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
}
